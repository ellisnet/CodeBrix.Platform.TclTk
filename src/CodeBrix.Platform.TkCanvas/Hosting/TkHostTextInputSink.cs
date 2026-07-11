using System;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Text;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CodeBrix.Platform.TkCanvas.Hosting;

/// <summary>
/// The hidden-input-element sink (the plan's §3.13 model): a reused,
/// visually transparent <see cref="TextBox"/> layered over the Skia surface
/// that carries ALL keyboard input for the host. While a text-entry widget
/// holds the Tk focus (<see cref="Attach"/>), committed text — including
/// completed IME composition — flows into that widget's buffer
/// (<see cref="ITextInputTarget.CommitText"/>), live pre-edit into its
/// composition state, and the element is repositioned to the caret so the
/// OS places any IME candidate window correctly. With NO text widget
/// focused, the element turns read-only and its key events are forwarded
/// as toolkit key events to whatever window holds the Tk focus (canvas and
/// toplevel key bindings). Special keys (arrows, Home/End,
/// BackSpace/Delete, Return/Tab, Control combinations) are always forwarded
/// so the widgets' class bindings do the editing.
/// </summary>
public sealed class TkHostTextInputSink : ITextInputSink
{
    private readonly TextBox _textBox;
    private ITextInputTarget _target;
    private bool _composing;
    private bool _muteTextChanged;

    /// <summary>
    /// Creates the sink over its hidden input element. The host adds
    /// <see cref="InputElement"/> to an overlay canvas above the Skia
    /// surface (done by <see cref="TkHostView"/>).
    /// </summary>
    public TkHostTextInputSink()
    {
        _textBox = new TextBox
        {
            Opacity = 0,
            Width = 8,
            Height = 16,
            // IsTabStop must be TRUE: programmatic Focus() fails on a
            // non-tab-stop control (X11-head-verified), and the whole model
            // depends on the hidden element holding keyboard focus.
            IsTabStop = true,
            AcceptsReturn = false,
            // Read-only until a text widget attaches: key events still
            // arrive (and are forwarded), but no text accumulates.
            IsReadOnly = true,
        };
        _textBox.TextChanged += OnTextChanged;
        _textBox.KeyDown += OnKeyDown;
        _textBox.TextCompositionStarted += OnCompositionStarted;
        _textBox.TextCompositionChanged += OnCompositionChanged;
        _textBox.TextCompositionEnded += OnCompositionEnded;
    }

    /// <summary>The hidden input element to layer above the Skia surface.</summary>
    public FrameworkElement InputElement
    {
        get { return _textBox; }
    }

    /// <summary>
    /// The window tree receiving forwarded key events (assigned by the
    /// host when it creates the sink).
    /// </summary>
    public WindowTree Tree { get; set; }

    /// <summary>The currently attached widget, or null.</summary>
    public ITextInputTarget Target
    {
        get { return _target; }
    }

    /// <summary>
    /// Takes keyboard focus onto the hidden element, one dispatch later (so
    /// a pointer press being processed cannot immediately steal it back).
    /// </summary>
    public void RequestFocus()
    {
        _textBox.DispatcherQueue.TryEnqueue(() =>
        {
            _textBox.Focus(FocusState.Programmatic);
        });
    }

    /// <inheritdoc/>
    public void Attach(ITextInputTarget target)
    {
        _target = target;
        _composing = false;
        ClearTextBox();
        _textBox.IsReadOnly = false;
        RequestFocus();
    }

    /// <inheritdoc/>
    public void Detach()
    {
        _target = null;
        _composing = false;
        ClearTextBox();
        _textBox.IsReadOnly = true;
    }

    /// <inheritdoc/>
    public void UpdateCaret(int x, int y, int height)
    {
        ITextInputTarget target = _target;
        if (target == null) { return; }

        // Widget-window coordinates -> root/canvas coordinates.
        int originX = 0, originY = 0;
        for (TkWindow w = target.Window; w != null && w.Parent != null; w = w.Parent)
        {
            originX += w.X;
            originY += w.Y;
        }
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(_textBox, originX + x);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(_textBox, originY + y);
        _textBox.Height = (height > 0) ? height : 16;
    }

    private void ClearTextBox()
    {
        _muteTextChanged = true;
        _textBox.Text = "";
        _muteTextChanged = false;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs args)
    {
        if (_muteTextChanged || _composing) { return; }
        string text = _textBox.Text;
        if (text.Length == 0) { return; }

        ITextInputTarget target = _target;
        ClearTextBox();
        if (target != null) { target.CommitText(text); }
    }

    private void OnCompositionStarted(TextBox sender, TextCompositionStartedEventArgs args)
    {
        _composing = true;
    }

    private void OnCompositionChanged(TextBox sender, TextCompositionChangedEventArgs args)
    {
        if (!_composing) { return; }
        ITextInputTarget target = _target;
        if (target != null) { target.SetComposition(_textBox.Text); }
    }

    private void OnCompositionEnded(TextBox sender, TextCompositionEndedEventArgs args)
    {
        _composing = false;
        ITextInputTarget target = _target;
        string text = _textBox.Text;
        ClearTextBox();
        if (target != null)
        {
            target.SetComposition(null);
            if (text.Length > 0) { target.CommitText(text); }
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_composing) { return; }
        WindowTree tree = Tree;
        if (tree == null) { return; }

        if (_target != null)
        {
            string keySym;
            EventModifiers state;
            if (!TkKeyMapper.TryMapSpecialOrControl(args.Key, out keySym, out state))
            {
                return; // plain character keys arrive through TextChanged
            }

            // Control-v is NOT forwarded: the element's native paste inserts
            // the clipboard text, which then commits through TextChanged —
            // the single paste path. (Forwarding too would paste twice, and
            // the element's Paste event ignores Handled on the X11 head —
            // both X11-head-verified.)
            if (keySym == "v" && (state & EventModifiers.Control) != 0)
            {
                return;
            }

            tree.KeyEvent(TkEventType.KeyPress, keySym, "", state);
            args.Handled = true;
            return;
        }

        // No text widget focused: every key becomes a toolkit key event for
        // whatever window holds the Tk focus (canvas/toplevel key bindings).
        string viewKeySym;
        string character;
        EventModifiers viewState;
        if (TkKeyMapper.TryMapViewKey(args.Key, out viewKeySym, out character, out viewState))
        {
            tree.KeyEvent(TkEventType.KeyPress, viewKeySym, character, viewState);
            args.Handled = true;
        }
    }
}

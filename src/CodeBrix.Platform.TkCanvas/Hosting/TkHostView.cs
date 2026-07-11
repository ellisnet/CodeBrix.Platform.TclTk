using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using SkiaSharp.Views.Windows;

using Windows.System;

using XamlCanvas = Microsoft.UI.Xaml.Controls.Canvas;

namespace CodeBrix.Platform.TkCanvas.Hosting;

/// <summary>
/// The ready-made CodeBrix.Platform host control for a TkCanvas window tree:
/// one <see cref="SKXamlCanvas"/> owning the whole Tk UI (the plan's §3.1
/// single-control model). It creates the root <see cref="TkWindow"/>, wires
/// the scheduler to the UI dispatcher (<see cref="TkHostDispatcher"/>), the
/// clipboard to the OS (<see cref="TkHostClipboard"/>), and the hidden IME
/// input element (<see cref="TkHostTextInputSink"/>, which also carries ALL
/// keyboard input); routes live pointer input into the toolkit's event
/// dispatch (with double-click detection and wheel support); and runs the
/// §11.4 host-resize pipeline (root geometry → relayout → repaint).
/// Declare the UI in XAML by nesting <see cref="Xaml.TkElement"/> children
/// (frames, buttons, entries, menus, photos, ...) — the host materializes
/// them when it loads — or build widgets in code under <see cref="Root"/>;
/// the two styles mix freely.
/// </summary>
public sealed class TkHostView : Grid
{
    private const int DoubleClickMilliseconds = 500;
    private const int DoubleClickSlopPixels = 3;

    private readonly SKXamlCanvas _surface;
    private readonly XamlCanvas _overlay;
    private readonly TkHostTextInputSink _sink;
    private readonly TkWindow _root;
    private readonly Dictionary<string, ToggleVariable> _groupVariables =
            new Dictionary<string, ToggleVariable>(StringComparer.Ordinal);

    private long _lastPressTicks;
    private int _lastPressButton;
    private int _lastPressX;
    private int _lastPressY;
    private int _clickCount = 1;
    private int _autoNameSerial;
    private bool _materialized;

    /// <summary>Creates the host view and its Tk window tree.</summary>
    public TkHostView()
    {
        _root = TkWindow.CreateRoot();

        TkScheduler scheduler = _root.Tree.Scheduler;
        scheduler.Host = new TkHostDispatcher(DispatcherQueue);
        scheduler.RepaintRequested += OnRepaintRequested;

        _root.Tree.Clipboard.Host = new TkHostClipboard();

        _sink = new TkHostTextInputSink { Tree = _root.Tree };
        _root.Tree.InputSink = _sink;

        _surface = new SKXamlCanvas();
        _surface.PaintSurface += OnPaintSurface;

        _overlay = new XamlCanvas { IsHitTestVisible = false };
        _overlay.Children.Add(_sink.InputElement);

        Children.Add(_surface);
        Children.Add(_overlay);

        Loaded += OnLoaded;
        SizeChanged += OnHostSizeChanged;

        _surface.PointerPressed += OnPointerPressed;
        _surface.PointerMoved += OnPointerMoved;
        _surface.PointerReleased += OnPointerReleased;
        _surface.PointerWheelChanged += OnPointerWheel;
    }

    /// <summary>The Tk root window (<c>.</c>) — code-built widgets go under this.</summary>
    public TkWindow Root
    {
        get { return _root; }
    }

    /// <summary>
    /// The color scheme name for the whole Tk tree — <c>Classic</c> (the
    /// default; <c>Default</c> is an alias), <c>Bisque</c>, or any name in
    /// <see cref="Theming.TkThemeRegistry"/> (DarkNew, LightNew, DarkPlus,
    /// LightPlus, DarkModern, LightModern, Monokai, DimmedMonokai,
    /// SolarizedDark, SolarizedLight, Abyss, QuietLight, Red,
    /// TomorrowNightBlue, KimbieDark, ...). Unknown names are ignored.
    /// Setting after load recolors the running UI on the next repaint.
    /// </summary>
    public string Theme
    {
        get { return (string)GetValue(ThemeProperty); }
        set { SetValue(ThemeProperty, value); }
    }

    /// <summary>Identifies the <see cref="Theme"/> property.</summary>
    public static readonly DependencyProperty ThemeProperty =
            DependencyProperty.Register(nameof(Theme), typeof(string), typeof(TkHostView),
                    new PropertyMetadata(null, OnThemeChanged));

    private static void OnThemeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var host = (TkHostView)sender;
        string name = args.NewValue as string;
        if (string.IsNullOrEmpty(name)) { name = "Classic"; }
        Theming.TkTheme theme = Theming.TkThemeRegistry.TryCreate(name);
        if (theme != null)
        {
            host._root.Tree.Theme = theme;
            host._surface.Invalidate();
        }
    }

    /// <summary>
    /// The tree's theme instance — assign a custom
    /// <see cref="Theming.TkTheme"/> built in code (the string
    /// <see cref="Theme"/> property is the XAML-friendly path).
    /// </summary>
    public Theming.TkTheme ThemePalette
    {
        get { return _root.Tree.Theme; }
        set
        {
            _root.Tree.Theme = value;
            _surface.Invalidate();
        }
    }

    /// <summary>
    /// Option-database entries applied before the declared UI materializes —
    /// one <c>pattern value ?priority?</c> Tcl list per line (the XAML face
    /// of <c>option add</c>, the plan's B.12b). Entries added after load
    /// follow Tk's rule: they affect widgets created later, never existing
    /// ones.
    /// </summary>
    public string OptionsDatabase
    {
        get { return (string)GetValue(OptionsDatabaseProperty); }
        set { SetValue(OptionsDatabaseProperty, value); }
    }

    /// <summary>Identifies the <see cref="OptionsDatabase"/> property.</summary>
    public static readonly DependencyProperty OptionsDatabaseProperty =
            DependencyProperty.Register(nameof(OptionsDatabase), typeof(string), typeof(TkHostView),
                    new PropertyMetadata(null, OnOptionsDatabaseChanged));

    private static void OnOptionsDatabaseChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var host = (TkHostView)sender;
        // Before load the entries are (re)applied by OnLoaded; afterwards new
        // text adds entries immediately (existing widgets stay as they are).
        if (host._materialized)
        {
            host.ApplyOptionsDatabase(args.NewValue as string);
        }
    }

    private void ApplyOptionsDatabase(string text)
    {
        if (string.IsNullOrEmpty(text)) { return; }
        Theming.OptionDatabase database = _root.Tree.OptionDatabase;
        foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '!' || line[0] == '#') { continue; }
            List<string> words = Canvas.TclString.SplitList(line);
            if (words.Count == 2) { database.Add(words[0], words[1]); }
            else if (words.Count >= 3) { database.Add(words[0], words[1], words[2]); }
        }
    }

    /// <summary>The tree's event system (bindings, focus, managers).</summary>
    public WindowTree Tree
    {
        get { return _root.Tree; }
    }

    /// <summary>Forces a repaint of the Skia surface.</summary>
    public void Invalidate()
    {
        _surface.Invalidate();
    }

    /// <summary>
    /// Synchronously flushes pending toolkit layout work and repaints — what
    /// XAML-declared property changes call after reconfiguring a widget.
    /// </summary>
    public void RequestUpdate()
    {
        _root.Tree.Scheduler.UpdateIdleTasks();
        _surface.Invalidate();
    }

    /// <summary>
    /// The shared toggle variable for a radiobutton group name (created on
    /// first use) — the XAML analogue of Tk's <c>-variable</c>.
    /// </summary>
    /// <param name="group">The group name.</param>
    /// <returns>The group's shared variable.</returns>
    public ToggleVariable GetGroupVariable(string group)
    {
        ToggleVariable variable;
        if (!_groupVariables.TryGetValue(group, out variable))
        {
            variable = new ToggleVariable();
            _groupVariables[group] = variable;
        }
        return variable;
    }

    /// <summary>Allocates a Tk window name for a nameless XAML declaration.</summary>
    /// <returns>A unique leaf name.</returns>
    public string NextAutoName()
    {
        _autoNameSerial++;
        return "x" + _autoNameSerial.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Finds a declared element by its XAML <c>Name</c> anywhere under this
    /// host (used by <c>For</c>-style cross-references).
    /// </summary>
    /// <param name="name">The element name.</param>
    /// <returns>The element, or null.</returns>
    public Xaml.TkElement FindTkElement(string name)
    {
        return FindTkElement(this, name);
    }

    private static Xaml.TkElement FindTkElement(Panel panel, string name)
    {
        foreach (UIElement child in panel.Children)
        {
            var element = child as Xaml.TkElement;
            if (element != null && element.Name == name) { return element; }
            var nested = child as Panel;
            if (nested != null)
            {
                Xaml.TkElement found = FindTkElement(nested, name);
                if (found != null) { return found; }
            }
        }
        return null;
    }

    private void OnRepaintRequested()
    {
        _surface.Invalidate();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        // The option database must be populated before the declared widgets
        // materialize — creation-time lookup is the Tk contract (B.12b).
        if (!_materialized)
        {
            ApplyOptionsDatabase(OptionsDatabase);
        }
        MaterializeDeclarations();

        if (ActualWidth >= 1 && ActualHeight >= 1)
        {
            _root.SetForcedSize((int)ActualWidth, (int)ActualHeight);
            _root.Tree.Scheduler.UpdateIdleTasks();
        }
        _surface.Invalidate();

        // The hidden input element carries ALL keyboard input; give it focus
        // once the tree is up so keys work before the first click.
        _sink.RequestFocus();
    }

    /// <summary>
    /// Materializes the XAML-declared Tk tree: photos first (so widget
    /// <c>Image</c> properties resolve), then the widget declarations in
    /// document order. Runs once; safe to call again (new declarations are
    /// not supported after load).
    /// </summary>
    private void MaterializeDeclarations()
    {
        if (_materialized) { return; }
        _materialized = true;

        var photos = new List<Xaml.TkPhoto>();
        CollectPhotos(this, photos);
        foreach (Xaml.TkPhoto photo in photos)
        {
            photo.Materialize(this, _root);
        }

        foreach (UIElement child in Children)
        {
            var element = child as Xaml.TkElement;
            if (element != null && !(element is Xaml.TkPhoto))
            {
                element.Materialize(this, _root);
            }
        }
    }

    private static void CollectPhotos(Panel panel, List<Xaml.TkPhoto> photos)
    {
        foreach (UIElement child in panel.Children)
        {
            var photo = child as Xaml.TkPhoto;
            if (photo != null) { photos.Add(photo); }
            var nested = child as Panel;
            if (nested != null) { CollectPhotos(nested, photos); }
        }
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs args)
    {
        int width = (int)args.NewSize.Width;
        int height = (int)args.NewSize.Height;
        if (width < 1 || height < 1) { return; }

        // The §11.4 pipeline: root geometry -> <Configure>/relayout (the
        // layout pass also clamps overlay toplevels) -> repaint.
        _root.SetForcedSize(width, height);
        _root.Tree.Scheduler.UpdateIdleTasks();
        _surface.Invalidate();
    }

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs args)
    {
        TkRenderer.Render(_root, args.Surface.Canvas);
    }

    // ------------------------------------------------------------------
    // Pointer routing (keyboard lives on the hidden input element)
    // ------------------------------------------------------------------

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(_surface);
        int x = (int)point.Position.X;
        int y = (int)point.Position.Y;
        int button = ButtonOf(point.Properties.PointerUpdateKind);
        if (button == 0) { return; }

        // Multi-click detection (Tk's <Double-ButtonPress> and beyond).
        long now = Environment.TickCount64;
        bool linked = (now - _lastPressTicks) <= DoubleClickMilliseconds
                && button == _lastPressButton
                && Math.Abs(x - _lastPressX) <= DoubleClickSlopPixels
                && Math.Abs(y - _lastPressY) <= DoubleClickSlopPixels;
        _clickCount = linked ? _clickCount + 1 : 1;
        _lastPressTicks = now;
        _lastPressButton = button;
        _lastPressX = x;
        _lastPressY = y;

        _root.Tree.PointerEvent(TkEventType.ButtonPress, x, y, button,
                Modifiers(args), 0, _clickCount);
        _root.Tree.Scheduler.UpdateIdleTasks();

        // Keyboard always routes through the hidden input element; (re)take
        // focus one dispatch after the press so the press cannot steal it.
        _sink.RequestFocus();
        args.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(_surface);
        _root.Tree.PointerEvent(TkEventType.Motion,
                (int)point.Position.X, (int)point.Position.Y, 0, Modifiers(args));
        _root.Tree.Scheduler.UpdateIdleTasks();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(_surface);
        int button = ButtonOf(point.Properties.PointerUpdateKind);
        if (button == 0) { button = _lastPressButton; }
        if (button == 0) { button = 1; }
        _root.Tree.PointerEvent(TkEventType.ButtonRelease,
                (int)point.Position.X, (int)point.Position.Y, button, Modifiers(args));
        _root.Tree.Scheduler.UpdateIdleTasks();
        args.Handled = true;
    }

    private void OnPointerWheel(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(_surface);
        int delta = point.Properties.MouseWheelDelta;
        _root.Tree.PointerEvent(TkEventType.MouseWheel,
                (int)point.Position.X, (int)point.Position.Y, 0, Modifiers(args), delta);
        _root.Tree.Scheduler.UpdateIdleTasks();
        args.Handled = true;
    }

    private static int ButtonOf(Microsoft.UI.Input.PointerUpdateKind kind)
    {
        switch (kind)
        {
            case Microsoft.UI.Input.PointerUpdateKind.LeftButtonPressed:
            case Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased:
                return 1;
            case Microsoft.UI.Input.PointerUpdateKind.MiddleButtonPressed:
            case Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased:
                return 2;
            case Microsoft.UI.Input.PointerUpdateKind.RightButtonPressed:
            case Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased:
                return 3;
            default:
                return 0;
        }
    }

    private static EventModifiers Modifiers(PointerRoutedEventArgs args)
    {
        EventModifiers state = EventModifiers.None;
        VirtualKeyModifiers mods = args.KeyModifiers;
        if ((mods & VirtualKeyModifiers.Shift) != 0) { state |= EventModifiers.Shift; }
        if ((mods & VirtualKeyModifiers.Control) != 0) { state |= EventModifiers.Control; }
        if ((mods & VirtualKeyModifiers.Menu) != 0) { state |= EventModifiers.Alt; }
        return state;
    }
}

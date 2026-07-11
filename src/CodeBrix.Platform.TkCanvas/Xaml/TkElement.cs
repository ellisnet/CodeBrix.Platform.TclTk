using System;
using System.Collections.Generic;
using System.Globalization;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// The base of the XAML declaration elements: invisible XAML panels that
/// DESCRIBE a Tk widget so a whole TkCanvas UI can be declared in markup
/// inside a <see cref="TkHostView"/>, with nesting expressed as XAML
/// nesting. When the host loads, it walks these elements and materializes
/// the real <see cref="TkWindow"/>/widget tree; setting a property after
/// materialization reconfigures the live widget. The elements render
/// nothing and take no space — but because they sit in the visual tree,
/// <c>DataContext</c> flows into them, so command properties bind to a
/// view-model's commands the normal XAML way. This layer covers
/// declaration-time setup and one-shot command wiring; it does NOT
/// implement live two-way synchronization between widget state and
/// view-model properties (read widget state through the element's typed
/// widget property, e.g. via a view-model bridge interface).
/// </summary>
public abstract class TkElement : Panel
{
    private readonly Dictionary<string, string> _pendingOptions =
            new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The materialized Tk window, or null before the host loads.</summary>
    public TkWindow TkWindow { get; private set; }

    /// <summary>The materialized widget, or null before the host loads.</summary>
    public IWidget TkWidget { get; private set; }

    /// <summary>The owning host view, or null before materialization.</summary>
    public TkHostView Host { get; private set; }

    // ------------------------------------------------------------------
    // Option infrastructure
    // ------------------------------------------------------------------

    /// <summary>
    /// Registers a dependency property that maps straight onto a Tk option:
    /// values set in XAML are collected and applied at materialization;
    /// values set afterwards reconfigure the live widget.
    /// </summary>
    /// <param name="propertyName">The CLR property name.</param>
    /// <param name="tkOption">The Tk option (e.g. <c>-text</c>).</param>
    /// <param name="ownerType">The declaring element type.</param>
    /// <param name="propertyType">The property type (string by default).</param>
    /// <param name="defaultValue">The unset default.</param>
    /// <returns>The registered property.</returns>
    private protected static DependencyProperty RegisterOption(string propertyName,
            string tkOption, Type ownerType, Type propertyType = null, object defaultValue = null)
    {
        return DependencyProperty.Register(propertyName, propertyType ?? typeof(string),
                ownerType, new PropertyMetadata(defaultValue ?? "",
                        (d, e) => ((TkElement)d).OnOptionChanged(tkOption, e.NewValue)));
    }

    private void OnOptionChanged(string tkOption, object newValue)
    {
        string formatted = FormatOptionValue(newValue);
        if (formatted == null) { return; }

        if (TkWidget == null)
        {
            _pendingOptions[tkOption] = formatted;
            return;
        }
        TkWidget.Configure(new Dictionary<string, string> { { tkOption, formatted } });
        Host?.RequestUpdate();
    }

    /// <summary>
    /// Formats a property value as Tk option text: booleans become
    /// <c>1</c>/<c>0</c>, enums their lowercase Tk name, numbers invariant
    /// text. Null, empty strings, and negative integers (the "unset"
    /// sentinel for numeric options) are skipped.
    /// </summary>
    /// <param name="value">The property value.</param>
    /// <returns>The option text, or null to skip.</returns>
    private protected static string FormatOptionValue(object value)
    {
        if (value == null) { return null; }
        if (value is string text) { return text.Length > 0 ? text : null; }
        if (value is bool flag) { return flag ? "1" : "0"; }
        if (value is int number) { return number < 0 ? null : number.ToString(CultureInfo.InvariantCulture); }
        if (value is double real)
        {
            return double.IsNaN(real) ? null : real.ToString(CultureInfo.InvariantCulture);
        }
        if (value is Enum) { return value.ToString().ToLowerInvariant(); }
        return value.ToString();
    }

    /// <summary>
    /// Extra Tk options as a Tcl-style list of pairs (e.g.
    /// <c>-highlightthickness 0 -takefocus 1</c>) — the catch-all that keeps
    /// every widget option reachable from XAML without a dedicated property.
    /// </summary>
    public string Options
    {
        get { return (string)GetValue(OptionsProperty); }
        set { SetValue(OptionsProperty, value); }
    }

    /// <summary>Identifies the <see cref="Options"/> property.</summary>
    public static readonly DependencyProperty OptionsProperty =
            DependencyProperty.Register(nameof(Options), typeof(string), typeof(TkElement),
                    new PropertyMetadata("", (d, e) => ((TkElement)d).OnOptionsChanged((string)e.NewValue)));

    private void OnOptionsChanged(string value)
    {
        Dictionary<string, string> parsed = ParseOptionList(value);
        if (parsed.Count == 0) { return; }
        if (TkWidget == null)
        {
            foreach (KeyValuePair<string, string> pair in parsed) { _pendingOptions[pair.Key] = pair.Value; }
            return;
        }
        TkWidget.Configure(parsed);
        Host?.RequestUpdate();
    }

    private static Dictionary<string, string> ParseOptionList(string text)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) { return options; }
        List<string> words = TclString.SplitList(text);
        for (int i = 0; i + 1 < words.Count; i += 2)
        {
            options[words[i]] = words[i + 1];
        }
        return options;
    }

    // ------------------------------------------------------------------
    // Shared appearance options
    // ------------------------------------------------------------------

    /// <summary>The Tk background color (<c>-background</c>; name or #rrggbb).</summary>
    public string TkBackground
    {
        get { return (string)GetValue(TkBackgroundProperty); }
        set { SetValue(TkBackgroundProperty, value); }
    }

    /// <summary>Identifies the <see cref="TkBackground"/> property.</summary>
    public static readonly DependencyProperty TkBackgroundProperty =
            RegisterOption(nameof(TkBackground), "-background", typeof(TkElement));

    /// <summary>The Tk foreground/text color (<c>-foreground</c>).</summary>
    public string TkForeground
    {
        get { return (string)GetValue(TkForegroundProperty); }
        set { SetValue(TkForegroundProperty, value); }
    }

    /// <summary>Identifies the <see cref="TkForeground"/> property.</summary>
    public static readonly DependencyProperty TkForegroundProperty =
            RegisterOption(nameof(TkForeground), "-foreground", typeof(TkElement));

    /// <summary>The Tk 3D relief style (<c>-relief</c>: flat/raised/sunken/groove/ridge/solid).</summary>
    public string Relief
    {
        get { return (string)GetValue(ReliefProperty); }
        set { SetValue(ReliefProperty, value); }
    }

    /// <summary>Identifies the <see cref="Relief"/> property.</summary>
    public static readonly DependencyProperty ReliefProperty =
            RegisterOption(nameof(Relief), "-relief", typeof(TkElement));

    /// <summary>The Tk border width in pixels (<c>-borderwidth</c>; negative = widget default).</summary>
    public int BorderWidth
    {
        get { return (int)GetValue(BorderWidthProperty); }
        set { SetValue(BorderWidthProperty, value); }
    }

    /// <summary>Identifies the <see cref="BorderWidth"/> property.</summary>
    public static readonly DependencyProperty BorderWidthProperty =
            RegisterOption(nameof(BorderWidth), "-borderwidth", typeof(TkElement), typeof(int), -1);

    /// <summary>The Tk font specification (<c>-font</c>, e.g. <c>Helvetica 12 bold</c>).</summary>
    public string TkFont
    {
        get { return (string)GetValue(TkFontProperty); }
        set { SetValue(TkFontProperty, value); }
    }

    /// <summary>Identifies the <see cref="TkFont"/> property.</summary>
    public static readonly DependencyProperty TkFontProperty =
            RegisterOption(nameof(TkFont), "-font", typeof(TkElement));

    // ------------------------------------------------------------------
    // Layout (pack by default; grid when a grid row/column is set)
    // ------------------------------------------------------------------

    /// <summary>The pack side (<c>pack -side</c>).</summary>
    public Side Side
    {
        get { return (Side)GetValue(SideProperty); }
        set { SetValue(SideProperty, value); }
    }

    /// <summary>Identifies the <see cref="Side"/> property.</summary>
    public static readonly DependencyProperty SideProperty =
            RegisterLayout(nameof(Side), typeof(Side), Layout.Side.Top);

    /// <summary>The pack/grid fill direction (<c>pack -fill</c>).</summary>
    public Fill Fill
    {
        get { return (Fill)GetValue(FillProperty); }
        set { SetValue(FillProperty, value); }
    }

    /// <summary>Identifies the <see cref="Fill"/> property.</summary>
    public static readonly DependencyProperty FillProperty =
            RegisterLayout(nameof(Fill), typeof(Fill), Layout.Fill.None);

    /// <summary>Whether the packed window takes extra cavity space (<c>pack -expand</c>).</summary>
    public bool Expand
    {
        get { return (bool)GetValue(ExpandProperty); }
        set { SetValue(ExpandProperty, value); }
    }

    /// <summary>Identifies the <see cref="Expand"/> property.</summary>
    public static readonly DependencyProperty ExpandProperty =
            RegisterLayout(nameof(Expand), typeof(bool), false);

    /// <summary>The pack anchor within the allocated frame (<c>pack -anchor</c>).</summary>
    public Anchor PackAnchor
    {
        get { return (Anchor)GetValue(PackAnchorProperty); }
        set { SetValue(PackAnchorProperty, value); }
    }

    /// <summary>Identifies the <see cref="PackAnchor"/> property.</summary>
    public static readonly DependencyProperty PackAnchorProperty =
            RegisterLayout(nameof(PackAnchor), typeof(Anchor), Layout.Anchor.Center);

    /// <summary>External horizontal padding in pixels (<c>-padx</c>).</summary>
    public int PadX
    {
        get { return (int)GetValue(PadXProperty); }
        set { SetValue(PadXProperty, value); }
    }

    /// <summary>Identifies the <see cref="PadX"/> property.</summary>
    public static readonly DependencyProperty PadXProperty =
            RegisterLayout(nameof(PadX), typeof(int), 0);

    /// <summary>External vertical padding in pixels (<c>-pady</c>).</summary>
    public int PadY
    {
        get { return (int)GetValue(PadYProperty); }
        set { SetValue(PadYProperty, value); }
    }

    /// <summary>Identifies the <see cref="PadY"/> property.</summary>
    public static readonly DependencyProperty PadYProperty =
            RegisterLayout(nameof(PadY), typeof(int), 0);

    /// <summary>Internal horizontal padding in pixels (<c>-ipadx</c>).</summary>
    public int IPadX
    {
        get { return (int)GetValue(IPadXProperty); }
        set { SetValue(IPadXProperty, value); }
    }

    /// <summary>Identifies the <see cref="IPadX"/> property.</summary>
    public static readonly DependencyProperty IPadXProperty =
            RegisterLayout(nameof(IPadX), typeof(int), 0);

    /// <summary>Internal vertical padding in pixels (<c>-ipady</c>).</summary>
    public int IPadY
    {
        get { return (int)GetValue(IPadYProperty); }
        set { SetValue(IPadYProperty, value); }
    }

    /// <summary>Identifies the <see cref="IPadY"/> property.</summary>
    public static readonly DependencyProperty IPadYProperty =
            RegisterLayout(nameof(IPadY), typeof(int), 0);

    /// <summary>The grid row (<c>grid -row</c>); setting a row or column selects grid layout.</summary>
    public int GridRow
    {
        get { return (int)GetValue(GridRowProperty); }
        set { SetValue(GridRowProperty, value); }
    }

    /// <summary>Identifies the <see cref="GridRow"/> property.</summary>
    public static readonly DependencyProperty GridRowProperty =
            RegisterLayout(nameof(GridRow), typeof(int), -1);

    /// <summary>The grid column (<c>grid -column</c>); setting a row or column selects grid layout.</summary>
    public int GridColumn
    {
        get { return (int)GetValue(GridColumnProperty); }
        set { SetValue(GridColumnProperty, value); }
    }

    /// <summary>Identifies the <see cref="GridColumn"/> property.</summary>
    public static readonly DependencyProperty GridColumnProperty =
            RegisterLayout(nameof(GridColumn), typeof(int), -1);

    /// <summary>The grid row span (<c>grid -rowspan</c>).</summary>
    public int GridRowSpan
    {
        get { return (int)GetValue(GridRowSpanProperty); }
        set { SetValue(GridRowSpanProperty, value); }
    }

    /// <summary>Identifies the <see cref="GridRowSpan"/> property.</summary>
    public static readonly DependencyProperty GridRowSpanProperty =
            RegisterLayout(nameof(GridRowSpan), typeof(int), 1);

    /// <summary>The grid column span (<c>grid -columnspan</c>).</summary>
    public int GridColumnSpan
    {
        get { return (int)GetValue(GridColumnSpanProperty); }
        set { SetValue(GridColumnSpanProperty, value); }
    }

    /// <summary>Identifies the <see cref="GridColumnSpan"/> property.</summary>
    public static readonly DependencyProperty GridColumnSpanProperty =
            RegisterLayout(nameof(GridColumnSpan), typeof(int), 1);

    /// <summary>The grid stickiness (<c>grid -sticky</c>).</summary>
    public Sticky Sticky
    {
        get { return (Sticky)GetValue(StickyProperty); }
        set { SetValue(StickyProperty, value); }
    }

    /// <summary>Identifies the <see cref="Sticky"/> property.</summary>
    public static readonly DependencyProperty StickyProperty =
            RegisterLayout(nameof(Sticky), typeof(Sticky), Layout.Sticky.None);

    private static DependencyProperty RegisterLayout(string propertyName, Type propertyType,
            object defaultValue)
    {
        return DependencyProperty.Register(propertyName, propertyType, typeof(TkElement),
                new PropertyMetadata(defaultValue, (d, e) => ((TkElement)d).OnLayoutChanged()));
    }

    private void OnLayoutChanged()
    {
        if (TkWindow == null || Host == null) { return; }
        ApplyLayout();
        Host.RequestUpdate();
    }

    // ------------------------------------------------------------------
    // Materialization
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates the real Tk window/widget for this declaration under
    /// <paramref name="parentWindow"/>, applies the declared options and
    /// layout, and recurses into child declarations. Called by the host
    /// when it loads.
    /// </summary>
    /// <param name="host">The owning host view.</param>
    /// <param name="parentWindow">The Tk parent window.</param>
    internal virtual void Materialize(TkHostView host, TkWindow parentWindow)
    {
        if (TkWidget != null) { return; }
        Host = host;

        string name = !string.IsNullOrEmpty(Name) ? Name : host.NextAutoName();
        TkWindow = parentWindow.CreateChild(name);
        TkWidget = CreateWidget(TkWindow);

        if (_pendingOptions.Count > 0)
        {
            TkWidget.Configure(_pendingOptions);
            _pendingOptions.Clear();
        }

        TkElement parentElement = Parent as TkElement;
        if (parentElement == null || parentElement.ArrangesOwnChildren == false)
        {
            ApplyLayout();
        }
        parentElement?.OnChildMaterialized(this);

        MaterializeChildren(host);
        OnMaterialized(host);
    }

    /// <summary>Creates the widget on the freshly created window.</summary>
    /// <param name="window">The Tk window this element owns.</param>
    /// <returns>The widget.</returns>
    private protected abstract IWidget CreateWidget(TkWindow window);

    /// <summary>Materializes the nested child declarations (overridable for containers with special child handling).</summary>
    /// <param name="host">The owning host view.</param>
    private protected virtual void MaterializeChildren(TkHostView host)
    {
        foreach (UIElement child in Children)
        {
            var element = child as TkElement;
            if (element != null) { element.Materialize(host, TkWindow); }
        }
    }

    /// <summary>Hook run after this element's widget, options, layout, and children exist.</summary>
    /// <param name="host">The owning host view.</param>
    private protected virtual void OnMaterialized(TkHostView host)
    {
    }

    /// <summary>
    /// Whether this element arranges its materialized children itself (e.g.
    /// a paned window adding panes) so they must NOT be pack/grid-managed.
    /// </summary>
    private protected virtual bool ArrangesOwnChildren
    {
        get { return false; }
    }

    /// <summary>Hook run when a nested child declaration has materialized.</summary>
    /// <param name="child">The materialized child.</param>
    private protected virtual void OnChildMaterialized(TkElement child)
    {
    }

    private void ApplyLayout()
    {
        if (GridRow >= 0 || GridColumn >= 0)
        {
            var grid = new GridOptions
            {
                Row = Math.Max(0, GridRow),
                Column = Math.Max(0, GridColumn),
                RowSpan = Math.Max(1, GridRowSpan),
                ColumnSpan = Math.Max(1, GridColumnSpan),
                Sticky = Sticky,
                IPadX = IPadX,
                IPadY = IPadY,
            };
            grid.SetPadX(PadX);
            grid.SetPadY(PadY);
            GridLayout.Configure(TkWindow, grid);
            return;
        }

        var pack = new PackOptions
        {
            Side = Side,
            Fill = Fill,
            Expand = Expand,
            Anchor = PackAnchor,
            IPadX = IPadX,
            IPadY = IPadY,
        };
        pack.SetPadX(PadX);
        pack.SetPadY(PadY);
        PackLayout.Configure(TkWindow, pack);
    }

    // ------------------------------------------------------------------
    // Command helper + invisible-panel behavior
    // ------------------------------------------------------------------

    /// <summary>Executes a bound command if it exists and can execute.</summary>
    /// <param name="command">The command (may be null).</param>
    /// <param name="parameter">The command parameter.</param>
    private protected static void ExecuteCommand(System.Windows.Input.ICommand command, object parameter)
    {
        if (command != null && command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        // Declaration elements occupy no space; children are measured only
        // to satisfy the XAML layout contract.
        foreach (UIElement child in Children)
        {
            child.Measure(new Size(0, 0));
        }
        return new Size(0, 0);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in Children)
        {
            child.Arrange(new Windows.Foundation.Rect(0, 0, 0, 0));
        }
        return finalSize;
    }
}

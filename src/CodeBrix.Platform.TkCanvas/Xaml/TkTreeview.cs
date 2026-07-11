using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>ttk::treeview</c>. Columns/headings declare in markup;
/// the item hierarchy is dynamic — build it through
/// <see cref="TreeviewWidget"/> (e.g. via a view-model bridge).
/// </summary>
public sealed class TkTreeview : TkElement
{
    /// <summary>The data-column ids as a Tcl-style list (<c>-columns</c>).</summary>
    public string Columns
    {
        get { return (string)GetValue(ColumnsProperty); }
        set { SetValue(ColumnsProperty, value); }
    }

    /// <summary>Identifies the <see cref="Columns"/> property.</summary>
    public static readonly DependencyProperty ColumnsProperty =
            RegisterOption(nameof(Columns), "-columns", typeof(TkTreeview));

    /// <summary>The materialized treeview widget, or null before the host loads.</summary>
    public TreeviewWidget TreeviewWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        TreeviewWidget = new TreeviewWidget(window);
        return TreeviewWidget;
    }
}

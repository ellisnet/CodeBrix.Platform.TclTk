using System;
using System.Windows.Input;

using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Menus;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares the menubar: a Tk menu of <c>-type menubar</c> registered with
/// the tree's menu system. Its nested <see cref="TkMenu"/> children become
/// cascades (drop-down menus), <see cref="TkMenuItem"/> children become
/// direct command entries, and <see cref="TkMenuSeparator"/> children become
/// separators.
/// </summary>
public sealed class TkMenubar : TkElement
{
    /// <summary>The materialized menubar widget, or null before the host loads.</summary>
    public MenuWidget MenuWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        MenuWidget = new MenuWidget(window);
        MenuWidget.Configure(new System.Collections.Generic.Dictionary<string, string>
        {
            { "-type", "menubar" },
        });
        return MenuWidget;
    }

    private protected override void MaterializeChildren(TkHostView host)
    {
        // Menu children are declarations consumed here, not widget windows.
    }

    private protected override void OnMaterialized(TkHostView host)
    {
        MenuManager menus = host.Tree.Menus;
        BuildEntries(menus, MenuWidget, Children);
        menus.SetMenubar(MenuWidget);
    }

    internal static void BuildEntries(MenuManager menus, MenuWidget menu,
            Microsoft.UI.Xaml.Controls.UIElementCollection children)
    {
        foreach (UIElement child in children)
        {
            switch (child)
            {
                case TkMenu cascade:
                {
                    MenuWidget submenu = menus.CreateMenu(
                            !string.IsNullOrEmpty(cascade.Name) ? cascade.Name : "menu");
                    BuildEntries(menus, submenu, cascade.Children);
                    MenuEntry entry = menu.AddCascade(cascade.Label, submenu, cascade.Underline);
                    ApplyImage(entry, cascade.Image, cascade.Compound);
                    break;
                }
                case TkMenuItem item:
                {
                    TkMenuItem captured = item;
                    MenuEntry entry = menu.AddCommand(item.Label,
                            () => ExecuteCommand(captured.Command, captured.CommandParameter),
                            string.IsNullOrEmpty(item.Accelerator) ? null : item.Accelerator,
                            item.Underline);
                    ApplyImage(entry, item.Image, item.Compound);
                    break;
                }
                case TkMenuSeparator:
                    menu.AddSeparator();
                    break;
                default:
                    break;
            }
        }
    }

    private static void ApplyImage(MenuEntry entry, string image, string compound)
    {
        if (!string.IsNullOrEmpty(image)) { entry.Image = image; }
        if (!string.IsNullOrEmpty(compound)) { entry.Compound = compound; }
    }
}

/// <summary>
/// Declares a cascade (drop-down) menu inside a <see cref="TkMenubar"/> or
/// another <see cref="TkMenu"/>. Its children are <see cref="TkMenuItem"/>,
/// <see cref="TkMenuSeparator"/>, and nested <see cref="TkMenu"/> cascades.
/// </summary>
public sealed class TkMenu : TkElement
{
    /// <summary>The cascade's label in its parent menu.</summary>
    public string Label
    {
        get { return (string)GetValue(LabelProperty); }
        set { SetValue(LabelProperty, value); }
    }

    /// <summary>Identifies the <see cref="Label"/> property.</summary>
    public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(TkMenu),
                    new PropertyMetadata(""));

    /// <summary>The 0-based mnemonic character index to underline, or -1.</summary>
    public int Underline
    {
        get { return (int)GetValue(UnderlineProperty); }
        set { SetValue(UnderlineProperty, value); }
    }

    /// <summary>Identifies the <see cref="Underline"/> property.</summary>
    public static readonly DependencyProperty UnderlineProperty =
            DependencyProperty.Register(nameof(Underline), typeof(int), typeof(TkMenu),
                    new PropertyMetadata(-1));

    /// <summary>The photo-image name drawn with the entry (<c>-image</c>).</summary>
    public string Image
    {
        get { return (string)GetValue(ImageProperty); }
        set { SetValue(ImageProperty, value); }
    }

    /// <summary>Identifies the <see cref="Image"/> property.</summary>
    public static readonly DependencyProperty ImageProperty =
            DependencyProperty.Register(nameof(Image), typeof(string), typeof(TkMenu),
                    new PropertyMetadata(""));

    /// <summary>How image and label combine (<c>-compound</c>; only <c>left</c> affects drawing).</summary>
    public string Compound
    {
        get { return (string)GetValue(CompoundProperty); }
        set { SetValue(CompoundProperty, value); }
    }

    /// <summary>Identifies the <see cref="Compound"/> property.</summary>
    public static readonly DependencyProperty CompoundProperty =
            DependencyProperty.Register(nameof(Compound), typeof(string), typeof(TkMenu),
                    new PropertyMetadata(""));

    private protected override IWidget CreateWidget(TkWindow window)
    {
        throw new NotSupportedException("TkMenu declares a cascade inside a TkMenubar");
    }
}

/// <summary>
/// Declares a command entry inside a <see cref="TkMenubar"/> or
/// <see cref="TkMenu"/>. Bind <see cref="Command"/> to a view-model command.
/// </summary>
public sealed class TkMenuItem : TkElement
{
    /// <summary>The entry label.</summary>
    public string Label
    {
        get { return (string)GetValue(LabelProperty); }
        set { SetValue(LabelProperty, value); }
    }

    /// <summary>Identifies the <see cref="Label"/> property.</summary>
    public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(TkMenuItem),
                    new PropertyMetadata(""));

    /// <summary>The accelerator hint drawn right-aligned (e.g. <c>Ctrl+S</c>).</summary>
    public string Accelerator
    {
        get { return (string)GetValue(AcceleratorProperty); }
        set { SetValue(AcceleratorProperty, value); }
    }

    /// <summary>Identifies the <see cref="Accelerator"/> property.</summary>
    public static readonly DependencyProperty AcceleratorProperty =
            DependencyProperty.Register(nameof(Accelerator), typeof(string), typeof(TkMenuItem),
                    new PropertyMetadata(""));

    /// <summary>The 0-based mnemonic character index to underline, or -1.</summary>
    public int Underline
    {
        get { return (int)GetValue(UnderlineProperty); }
        set { SetValue(UnderlineProperty, value); }
    }

    /// <summary>Identifies the <see cref="Underline"/> property.</summary>
    public static readonly DependencyProperty UnderlineProperty =
            DependencyProperty.Register(nameof(Underline), typeof(int), typeof(TkMenuItem),
                    new PropertyMetadata(-1));

    /// <summary>The photo-image name drawn with the entry (<c>-image</c>).</summary>
    public string Image
    {
        get { return (string)GetValue(ImageProperty); }
        set { SetValue(ImageProperty, value); }
    }

    /// <summary>Identifies the <see cref="Image"/> property.</summary>
    public static readonly DependencyProperty ImageProperty =
            DependencyProperty.Register(nameof(Image), typeof(string), typeof(TkMenuItem),
                    new PropertyMetadata(""));

    /// <summary>How image and label combine (<c>-compound</c>; only <c>left</c> affects drawing).</summary>
    public string Compound
    {
        get { return (string)GetValue(CompoundProperty); }
        set { SetValue(CompoundProperty, value); }
    }

    /// <summary>Identifies the <see cref="Compound"/> property.</summary>
    public static readonly DependencyProperty CompoundProperty =
            DependencyProperty.Register(nameof(Compound), typeof(string), typeof(TkMenuItem),
                    new PropertyMetadata(""));

    /// <summary>The command invoked when the entry fires.</summary>
    public ICommand Command
    {
        get { return (ICommand)GetValue(CommandProperty); }
        set { SetValue(CommandProperty, value); }
    }

    /// <summary>Identifies the <see cref="Command"/> property.</summary>
    public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(TkMenuItem),
                    new PropertyMetadata(null));

    /// <summary>The parameter passed to <see cref="Command"/>.</summary>
    public object CommandParameter
    {
        get { return GetValue(CommandParameterProperty); }
        set { SetValue(CommandParameterProperty, value); }
    }

    /// <summary>Identifies the <see cref="CommandParameter"/> property.</summary>
    public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(TkMenuItem),
                    new PropertyMetadata(null));

    private protected override IWidget CreateWidget(TkWindow window)
    {
        throw new NotSupportedException("TkMenuItem declares an entry inside a TkMenubar/TkMenu");
    }
}

/// <summary>Declares a separator entry inside a <see cref="TkMenubar"/> or <see cref="TkMenu"/>.</summary>
public sealed class TkMenuSeparator : TkElement
{
    private protected override IWidget CreateWidget(TkWindow window)
    {
        throw new NotSupportedException("TkMenuSeparator declares an entry inside a TkMenubar/TkMenu");
    }
}

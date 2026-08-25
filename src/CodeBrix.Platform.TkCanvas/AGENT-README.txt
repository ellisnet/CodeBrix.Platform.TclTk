================================================================================
AGENT-README: CodeBrix.Platform.TkCanvas
A Guide for AI Coding Agents — CONSUMING the
CodeBrix.Platform.TkCanvas.BsdLicenseForever NuGet package
================================================================================

OVERVIEW
========
A retained-mode reimplementation of the classic Tk widget toolkit, drawn
entirely onto a SkiaSharp surface, for CodeBrix.Platform applications and for
headless use. It provides:

  * the Tk geometry managers (pack, grid) and the Tk window tree;
  * the classic widget set — frame, labelframe, label, button, entry, text,
    listbox, treeview, combobox, checkbutton, radiobutton, panedwindow,
    scrollbar, separator, menus — as typed C# classes;
  * the canvas widget with its scene-graph item model (arc, bitmap, image,
    line, oval, polygon, rectangle, text, window) and the full search/
    geometry surface (find, bbox, coords, tags, scroll, scan, item bindings);
  * the Tk event/bind/focus/grab system, "after"/"update" scheduling, photo
    images, fonts, clipboard, overlay toplevels with a mini window-manager,
    message dialogs, color theming, the option database and ttk::style;
  * TkHostView — a ready-made CodeBrix.Platform control hosting a whole Tk
    tree, plus XAML declaration elements for every widget;
  * TkTclBridge — the Tcl command bridge that registers the classic Tk
    command surface on a CodeBrix.Platform.TclTk interpreter, so an
    UNMODIFIED Tcl/Tk program presents its UI through this toolkit.

Every class here is original CodeBrix code (not a port of Tk's C sources);
layout results, canvas coords/bbox/find and GIF decoding are validated
byte-for-byte against real Tk 8.6.16 (wish) fixtures. Target: .NET 10 or
later. The result looks and behaves like classic Tk identically on every
platform because nothing native is involved.

INSTALLATION
============
    dotnet add package CodeBrix.Platform.TkCanvas.BsdLicenseForever

PackageId: CodeBrix.Platform.TkCanvas.BsdLicenseForever
License:   BSD-2-Clause (the interpreter dependency carries its own
           "TCL AND BSD-2-Clause" metadata)

NuGet dependencies you get transitively:
  * CodeBrix.Platform.TclTk.BsdLicenseForever          (the Tcl interpreter;
                                                        used by TkBootstrap
                                                        and TkTclBridge)
  * SkiaSharp                                          (all drawing)
  * CodeBrix.Imaging.ApacheLicenseForever              (GIF/PNG decode +
                                                        encode for photos)
  * CodeBrix.Platform.ApacheLicenseForever             (the UI stack for
                                                        TkHostView and the
                                                        XAML elements)
  * CodeBrix.Platform.SkiaSharp.Views.MitLicenseForever (the SKXamlCanvas
                                                        TkHostView paints on)

Requirements: to show a TkHostView you need a CodeBrix.Platform application
with exactly one platform head package (for example
CodeBrix.Platform.Runtime.Skia.X11.ApacheLicenseForever for Linux X11) — see
MINIMUM VIABLE PROJECT. Headless/offscreen use (render into any SKCanvas,
drive input synthetically) never touches the UI stack at runtime. Do not add
a second SkiaSharp package reference; this package pins the SkiaSharp line
the CodeBrix.Platform Skia heads use.

The PackageId carries a license suffix; the code namespace does not.

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Platform.TkCanvas;            // TkBootstrap
    using CodeBrix.Platform.TkCanvas.Hosting;    // TkHostView, TkHostDispatcher,
                                                 // TkHostClipboard,
                                                 // TkHostFileDialogs,
                                                 // TkHostTextInputSink
    using CodeBrix.Platform.TkCanvas.Xaml;       // TkElement + Tk* XAML elements
    using CodeBrix.Platform.TkCanvas.Windowing;  // TkWindow, TkLayout
    using CodeBrix.Platform.TkCanvas.Layout;     // PackLayout, PackOptions,
                                                 // GridLayout, GridOptions,
                                                 // Side, Fill, Anchor, Sticky
    using CodeBrix.Platform.TkCanvas.Widgets;    // IWidget, WidgetBase,
                                                 // WidgetOptions, ToggleVariable,
                                                 // the widget classes, TreeItem
    using CodeBrix.Platform.TkCanvas.Canvas;     // CanvasWidget, ICanvasItem,
                                                 // CanvasItem + item classes,
                                                 // CanvasAnchor, ArcStyle,
                                                 // ArrowStyle, CapStyle,
                                                 // JoinStyle, CanvasItemState,
                                                 // TkColor
    using CodeBrix.Platform.TkCanvas.Events;     // WindowTree, BindingTable,
                                                 // TkEvent, TkEventType,
                                                 // EventModifiers, EventPattern,
                                                 // DispatchResult, TkEventHandler,
                                                 // TkScheduler, AfterHandle,
                                                 // ITkDispatcher, ITkTimeSource
    using CodeBrix.Platform.TkCanvas.Text;       // TextWidget, TextPosition,
                                                 // TextTag, ITextInputSink,
                                                 // ITextInputTarget
    using CodeBrix.Platform.TkCanvas.Fonts;      // TkFont, FontManager,
                                                 // FontMetrics
    using CodeBrix.Platform.TkCanvas.Images;     // PhotoImage, ImageManager,
                                                 // PhotoCopyOptions
    using CodeBrix.Platform.TkCanvas.Menus;      // MenuWidget, MenuEntry,
                                                 // MenuEntryType, MenuManager
    using CodeBrix.Platform.TkCanvas.Overlay;    // WindowManager, OverlayState
    using CodeBrix.Platform.TkCanvas.Dialogs;    // MessageDialog,
                                                 // MessageDialogOptions
    using CodeBrix.Platform.TkCanvas.Theming;    // TkTheme, TkThemeRegistry,
                                                 // BuiltinThemes, OptionDatabase,
                                                 // TtkStyleEngine
    using CodeBrix.Platform.TkCanvas.Clipboard;  // ClipboardManager, ITkClipboard
    using CodeBrix.Platform.TkCanvas.Rendering;  // TkRenderer, ReliefPainter,
                                                 // Relief
    using CodeBrix.Platform.TkCanvas.Tcl;        // TkTclBridge, TkTclError,
                                                 // ITkFileDialogProvider
    using CodeBrix.Platform.TclTk._Components.Public;  // Interpreter, Result,
                                                       // ReturnCode (bridge)
    using SkiaSharp;                             // SKCanvas, SKColor, SKRectI

TWO WAYS TO BUILD THE UI (both fully supported; they mix freely)
================================================================
PATH 1 — DECLARE THE UI IN XAML (preferred for CodeBrix.Platform apps).
Nest Tk* declaration elements inside a TkHostView; the host materializes the
real widget tree when it loads. Every element exposes its materialized widget
(NameEntry.EntryWidget, Output.TextWidget, ...), null until the host loads.
Setting a declared property after load reconfigures the live widget.

PATH 2 — BUILD THE UI IN CODE (generated/dynamic UIs, headless hosts, and
what the Tcl bridge itself does). Create TkWindows under a root, attach a
widget class to each, and lay them out with PackLayout/GridLayout.

Shared concepts:
  * A widget OWNS a TkWindow (composition, mirroring Tk's window/widget
    split). Every widget class takes its TkWindow in the constructor.
  * Options are Tk option names: widget.Configure(dictionary) with "-text",
    "-background", ... Unknown-but-valid options are accepted and STORED,
    never thrown (the toolkit-wide accept-and-no-op discipline).
  * window.Tree is the WindowTree — bindings, focus, grab, scheduler, fonts,
    images, clipboard, theme, option database, styles, window manager.
  * Geometry reads are final right after tree.Scheduler.UpdateIdleTasks()
    (or TkLayout.Update(root) headless) — Tk's synchronous "update".

CORE API REFERENCE
==================
THE HOST CONTROL — TkHostView (Hosting)
---------------------------------------
    public sealed class TkHostView : Grid
        TkHostView()                       creates the root window and tree,
                                           wires the dispatcher, clipboard and
                                           IME sink bridges, the Skia surface
                                           and pointer routing
        TkWindow Root                      the Tk root window (".")
        WindowTree Tree                    the tree's event system/managers
        string Theme                       XAML-friendly scheme name (DP):
                                           "Classic" (default; "Default",
                                           "clam", "alt" are aliases),
                                           "Bisque", or any TkThemeRegistry
                                           name; unknown names are ignored
        TkTheme ThemePalette               the live TkTheme; assign a custom
                                           one built in code
        string OptionsDatabase             one "pattern value ?priority?"
                                           Tcl list per line (DP); applied
                                           before the declared UI materializes
        void Invalidate()                  repaint the Skia surface
        void RequestUpdate()               UpdateIdleTasks() + repaint —
                                           call after reconfiguring widgets
        ToggleVariable GetGroupVariable(string group)
                                           shared radiobutton-group variable
        string NextAutoName()              a unique leaf name for nameless
                                           declarations
        TkElement FindTkElement(string name)
                                           a declared element by XAML Name

The host owns: rendering (TkRenderer over an SKXamlCanvas), pointer routing
with double-click and wheel, ALL keyboard routing through a hidden input
element (entry/text typing and IME), the OS clipboard bridge, the dispatcher
bridge for "after" timers and the synchronous "update" flush, and the
host-resize pipeline (root geometry follows the control; pack/grid re-run;
overlays re-clamp).

WINDOWS — TkWindow, TkLayout (Windowing)
----------------------------------------
    public sealed class TkWindow
        static TkWindow CreateRoot()       a new tree; root.Tree is its WindowTree
        TkWindow CreateChild(string name)  ".name" under this window
        WindowTree Tree
        string Name; string PathName; TkWindow Parent
        IReadOnlyList<TkWindow> Children; bool IsRoot; bool IsDestroyed
        string ClassName { get; set; }     the class bind tag ("Button", ...)
        bool Focusable { get; set; }
        IList<string> BindTags { get; set; }
                                           null = Tk default: path, class,
                                           root path, "all"
        IReadOnlyList<string> EffectiveBindTags()
        IWidget Widget { get; set; }       the widget owning this window
        int RequestedWidth; int RequestedHeight
        void SetRequestedSize(int width, int height)   (Tk_GeometryRequest)
        int MinimumRequestedWidth; int MinimumRequestedHeight
        void SetMinimumRequestedSize(int width, int height)
        int X; int Y; int Width; int Height            allocated geometry
        int InternalBorderLeft/Right/Top/Bottom
        void SetInternalBorder(int uniform)
        void SetInternalBorders(int left, int top, int right, int bottom)
        bool IsDisplayed
        int? ForcedWidth; int? ForcedHeight
        void SetForcedSize(int width, int height)      root size when headless
        void ClearForcedSize()
        void Destroy()
        TkWindow FindDescendant(string pathName)

    public static class TkLayout
        static void Update(TkWindow root)  synchronous propagate + arrange to
                                           a fixed point (root only)

GEOMETRY MANAGERS — pack and grid (Layout)
------------------------------------------
    public enum Side   { Top, Bottom, Left, Right }
    public enum Fill   { None, X, Y, Both }
    public enum Anchor { Center, N, NE, E, SE, S, SW, W, NW }
    [Flags] public enum Sticky { None = 0, N = 1, E = 2, S = 4, W = 8,
                                 All = N | E | S | W }

    public sealed class PackOptions
        Side Side = Side.Top; Anchor Anchor = Anchor.Center;
        Fill Fill = Fill.None; bool Expand;
        int PadLeft, PadRight, PadTop, PadBottom; int IPadX, IPadY;
        TkWindow In; TkWindow Before; TkWindow After;
        void SetPadX(int pad); void SetPadY(int pad)    (both sides at once)

    public static class PackLayout
        static void Configure(TkWindow window, PackOptions options)
        static void Forget(TkWindow window)
        static PackOptions Info(TkWindow window)
        static IReadOnlyList<TkWindow> Content(TkWindow container)
        static bool GetPropagate(TkWindow container)
        static void SetPropagate(TkWindow container, bool propagate)

    public sealed class GridOptions
        int Row; int Column; int RowSpan = 1; int ColumnSpan = 1;
        Sticky Sticky = Sticky.None;
        int PadLeft, PadRight, PadTop, PadBottom; int IPadX, IPadY;
        TkWindow In;
        void SetPadX(int pad); void SetPadY(int pad)

    public static class GridLayout
        static void Configure(TkWindow window, GridOptions options)
        static void Forget(TkWindow window)
        static GridOptions Info(TkWindow window)
        static IReadOnlyList<TkWindow> Content(TkWindow container)
        static void ColumnConfigure(TkWindow container, int index,
                int? minSize = null, int? weight = null, int? pad = null,
                string uniform = null)
        static void RowConfigure(TkWindow container, int index,
                int? minSize = null, int? weight = null, int? pad = null,
                string uniform = null)
        static void Size(TkWindow container, out int columns, out int rows)
        static bool GetPropagate(TkWindow container)
        static void SetPropagate(TkWindow container, bool propagate)
        static Anchor GetAnchor(TkWindow container)
        static void SetAnchor(TkWindow container, Anchor anchor)

Pack is Tk's cavity model, grid is Tk's constraint solver; both honour the
per-container pack/grid exclusivity rule and propagate.

WIDGETS — the shared contract (Widgets)
---------------------------------------
    public interface IWidget
        TkWindow Window { get; }
        string ClassName { get; }
        WidgetOptions Options { get; }
        void Measure()                     natural size -> Window.SetRequestedSize
        void Paint(SKCanvas canvas)        (0,0) = the window's top-left
        bool HitTest(SKPoint point)        window-relative refinement
        void Configure(IReadOnlyDictionary<string, string> options)

    public abstract class WidgetBase : IWidget
        The base of every classic widget below (constructor is not public:
        derive your own widgets from IWidget, not WidgetBase). Adds the
        option-database application at creation, configure -> Measure ->
        repaint, the font seam, ttk style resolution (-style, class styles
        TButton/TLabel/...), and the shared 3D border painting. Options every
        WidgetBase widget understands: -background/-bg, -borderwidth/-bd,
        -relief, -highlightthickness, -highlightbackground, -image, -style.

    public sealed class WidgetOptions           (accept-and-store option bag)
        void Set(string name, string value)
        string Get(string name, string defaultValue = "")
        bool IsSet(string name)
        int GetInt(string name, int defaultValue = 0)
        double GetDouble(string name, double defaultValue = 0)
        bool GetBool(string name, bool defaultValue = false)
        IReadOnlyCollection<string> Names

    public sealed class ToggleVariable          (Tk's -variable for toggles)
        ToggleVariable(string initial = "")
        event Action Changed
        string Value
        void Set(string value)

Color resolution for every widget: explicit option > option database (at
creation) > ttk style map (state-matched) > style configure > theme default.

WIDGETS — the classes (all constructors take the owning TkWindow)
-----------------------------------------------------------------
    FrameWidget(TkWindow)                  ClassName "Frame"
        options: -width -height (+ shared)

    LabelWidget(TkWindow)                  "Label"
        options: -text -font -foreground/-fg -anchor -justify -padx -pady
                 -width -height -state -image

    LabelframeWidget(TkWindow)             "Labelframe"
        options: -text -labelanchor -font -foreground/-fg -width -height

    ButtonWidget(TkWindow)                 "Button"
        event Action Invoked
        bool IsActive; bool IsPressed
        void Invoke()
        options: -text -font -foreground/-fg -anchor -justify -padx -pady
                 -width -height -state -image

    CheckbuttonWidget(TkWindow)            "Checkbutton"
        event Action Invoked; Action Command { get; set; }
        ToggleVariable Variable { get; set; }   (null = internal state)
        bool IsSelected
        void Select(); void Deselect(); void Invoke()
        options: -text -font -foreground/-fg -onvalue -offvalue -state

    RadiobuttonWidget(TkWindow)            "Radiobutton"
        event Action Invoked; Action Command { get; set; }
        ToggleVariable Variable { get; set; }   (share one per group)
        bool IsSelected
        void Select(); void Invoke()
        options: -text -font -foreground/-fg -value -state

    EntryWidget(TkWindow) : WidgetBase, ITextInputTarget    "Entry"
        string Text; int Cursor; string Composition
        void SetText(string text)
        void Insert(int index, string text)
        void Delete(int first, int last = -1)
        void SetCursor(int index); void MoveCursor(int index, bool extend)
        void SelectRange(int first, int last); void ClearSelection()
        string SelectedText; bool DeleteSelectionIfAny()
        int IndexAt(int x)
        void CommitText(string text); void SetComposition(string preedit)
        void CopySelectionToClipboard(bool cut); void PasteFromClipboard()
        options: -width -font -foreground/-fg -show -state
                 -disabledbackground

    ListboxWidget(TkWindow)                "Listbox"
        event Action<double, double> YScrollChanged
        int Size; IReadOnlyList<string> Items; int Active
        void Insert(int index, params string[] values)
        void Delete(int first, int last = -1)
        string Get(int index)
        IReadOnlyList<int> CurSelection()
        void SelectionSet(int index)
        void SelectionClear(int first, int last = -1)
        bool SelectionIncludes(int index)
        int Nearest(int y); void See(int index)
        void YViewMoveTo(double fraction); void YViewScroll(int count, bool pages)
        fires the <<ListboxSelect>> virtual event on selection change
        options: -height -width -font -selectmode

    TreeviewWidget(TkWindow)               "Treeview"
        event Action<double, double> YScrollChanged
        IReadOnlyList<string> Columns; IReadOnlyList<string> Selection
        void SetColumns(params string[] columns)
        void SetHeading(string column, string text)
        string Insert(string parent, int index, string text,
                      string[] values = null, string id = null)   -> item id
        void Delete(params string[] ids)
        TreeItem Item(string id)
        IReadOnlyList<string> ChildrenOf(string id)
        void SelectionSet(params string[] ids)      fires <<TreeviewSelect>>
        void SetOpen(string id, bool open)
        IReadOnlyList<string> VisibleItems()
        string ItemAt(int y)
        void YViewMoveTo(double fraction); void YViewScroll(int count, bool pages)
        options: -columns -height -font
    public sealed class TreeItem
        string Id; string Text; List<string> Values; bool Open; string Image;
        string Parent; List<string> Children

    ComboboxWidget(TkWindow)               "TCombobox"
        event Action Selected                 (<<ComboboxSelected>>)
        string Value; bool IsDropDownOpen
        void SetValues(params string[] values); IReadOnlyList<string> Values
        void SetValue(string value)           (no Selected event)
        void ToggleDropDown(); void OpenDropDown(); void CloseDropDown()
        options: -values -width -font -state

    ScrollbarWidget(TkWindow)              "Scrollbar"
        event Action<string[]> Command        the words Tk appends to -command:
                                              ["moveto", frac] or
                                              ["scroll", n, "units"|"pages"]
        bool IsVertical; double First; double Last
        void Set(double first, double last)
        options: -orient

    SeparatorWidget(TkWindow)              "TSeparator"
        options: -orient

    PanedWindowWidget(TkWindow)            "Panedwindow"
        bool IsHorizontal; IReadOnlyList<TkWindow> Panes
        void Add(TkWindow pane); void Forget(TkWindow pane)
        int SashCoord(int index); void MoveSash(int index, int delta)
        options: -orient -sashwidth -width -height

    MenuWidget(TkWindow)                   "Menu"   (see MENUS below)

    CanvasWidget(TkWindow) : IWidget       "Canvas" (see THE CANVAS below)

    TextWidget(TkWindow) : IWidget, ITextInputTarget   "Text" (see TEXT below)

Pairing a scrollbar with a scrollable widget in code:
    listbox.YScrollChanged += (first, last) => scrollbar.Set(first, last);
    scrollbar.Command += words =>
    {
        if (words[0] == "moveto") listbox.YViewMoveTo(double.Parse(words[1],
                System.Globalization.CultureInfo.InvariantCulture));
        else listbox.YViewScroll(int.Parse(words[1]), words[2] == "pages");
    };

THE CANVAS — CanvasWidget and the item model (Canvas)
-----------------------------------------------------
    public sealed class CanvasWidget : IWidget
        CanvasWidget(TkWindow window)
        ICanvasItem CurrentItem; ICanvasItem FocusItem
        IReadOnlyList<ICanvasItem> Items          display-list order
        CanvasItemState CanvasState; int XOrigin; int YOrigin
        BindingTable ItemBindings
        event Action<double, double> XScrollChanged, YScrollChanged
        static void RegisterItemType(string name, Func<CanvasItem> factory)
        int Create(string type, IReadOnlyList<double> coords,
                   IReadOnlyDictionary<string, string> options = null)
                                                  type may be a unique prefix
        void Delete(string tagOrId)
        IEnumerable<ICanvasItem> FindWithTag(string tagOrId)
        IReadOnlyList<int> FindAll()
        ICanvasItem FindAbove(string tagOrId); ICanvasItem FindBelow(string tagOrId)
        ICanvasItem FindClosest(double x, double y, double halo = 0.0,
                                string startTagOrId = null)
        IReadOnlyList<int> FindArea(double x1, double y1, double x2, double y2,
                                    bool enclosedOnly)
        IReadOnlyList<string> GetTags(string tagOrId)
        void DeleteTag(string tagOrId, string tagToDelete = null)
        void AddTagToItems(string tag, IEnumerable<ICanvasItem> items)
        void RaiseItems(string tagOrId, string aboveThis = null)
        void LowerItems(string tagOrId, string belowThis = null)
        SKRectI? BBox(params string[] tagOrIds)
        void Move(string tagOrId, double dx, double dy)
        void MoveTo(string tagOrId, double? x, double? y)
        void ScaleItems(string tagOrId, double originX, double originY,
                        double scaleX, double scaleY)
        double CanvasX(int screenX, double gridSpacing = 0.0)
        double CanvasY(int screenY, double gridSpacing = 0.0)
        void XViewFractions(out double first, out double last)   (+ YView...)
        void XViewMoveTo(double fraction); void XViewScroll(int count, bool pages)
        void YViewMoveTo(double fraction); void YViewScroll(int count, bool pages)
        void ScanMark(int x, int y); void ScanDragTo(int x, int y, int gain = 10)
        void SetOrigin(int xOrigin, int yOrigin)
        void BindItem(string tagOrId, string pattern, TkEventHandler handler)
                                                  null handler unbinds
        void SetFocusItem(string tagOrId)
        string Execute(IReadOnlyList<string> words)
                                                  the Tcl "canvas" command's
                                                  words verbatim; errors throw
                                                  InvalidOperationException
                                                  carrying the Tk message
        options: -width -height -background -borderwidth -highlightthickness
                 -scrollregion -confine -closeenough -xscrollincrement
                 -yscrollincrement -state

Tag specs accept Tk's forms: a decimal id, a tag, "all", "current", and tag
expressions (&& || ^ ! with parentheses). Item bindings dispatch in Tk's
order: "all", then the item's tags in order, then the item id.

    public interface ICanvasItem
        int Id; string TypeName; IReadOnlyList<string> Tags
        WidgetOptions Options; SKRectI Bounds
        bool HitTest(SKPoint point, double halo)
        void Paint(SKCanvas canvas)
        void Configure(IReadOnlyDictionary<string, string> options)
        IReadOnlyList<double> GetCoords()
        void SetCoords(IReadOnlyList<double> coords)

    public abstract class CanvasItem : ICanvasItem      (the item base)
        CanvasWidget Canvas; CanvasItemState State; CanvasItemState EffectiveState
        int X1, Y1, X2, Y2                              the header bbox
        void AddTag(string tag); void RemoveTag(string tag); bool HasTag(string tag)
        abstract double DistanceTo(double x, double y)
        abstract int AreaTest(double[] rect)            -1 outside / 0 overlaps /
                                                        1 enclosed
    public abstract class AnchoredCanvasItem : CanvasItem   (bitmap/image/window
                                                             base; -anchor)
    public enum CanvasItemState { Default, Normal, Hidden, Disabled }
    public enum CanvasAnchor { Center, N, NE, E, SE, S, SW, W, NW }
    public enum ArcStyle   { PieSlice, Chord, Arc }
    public enum ArrowStyle { None, First, Last, Both }
    public enum CapStyle   { Butt, Round, Projecting }
    public enum JoinStyle  { Miter, Bevel, Round }

Item classes (created through CanvasWidget.Create by type name; read back
through FindWithTag/Items and cast):
    ArcItem        "arc"        ArcStyle Style
                   options -start -extent -style -fill -outline -width -dash
                   -activefill -activeoutline -activewidth -disabledfill
                   -disabledoutline -disabledwidth
    LineItem       "line"       ArrowStyle Arrow; bool Smooth
                   options -fill -width -arrow -arrowshape -capstyle
                   -joinstyle -smooth -splinesteps -dash -activefill
                   -disabledfill
    OvalItem       "oval"       -fill -outline -width -dash + active/disabled
    PolygonItem    "polygon"    -fill -outline -width -smooth -splinesteps
                                -joinstyle -dash + active/disabled
    RectangleItem  "rectangle"  -fill -outline -width -dash + active/disabled
    TextItem       "text"       IReadOnlyList<string> Lines (after wrapping)
                                options -text -font -anchor -justify -width
                                -fill -activefill -disabledfill
    ImageItem      "image"      -image NAME -anchor
    BitmapItem     "bitmap"     options accepted and stored; the item has no
                                size and paints nothing (deferred)
    WindowItem     "window"     -width -height -anchor size its bbox so
                                geometry code works; painting the embedded
                                control is deferred (no-op)

    public static class TkColor
        static bool TryParse(string text, out SKColor color)
            #RGB/#RRGGBB/#RRRRGGGGBBBB, the X11/Tk color names, gray0..gray100.
            Returns false for ""/null (Tk's "no color" = not drawn); unknown
            names resolve to black and return true.

EVENTS — bind, dispatch and synthetic input (Events)
----------------------------------------------------
    public enum TkEventType { ButtonPress, ButtonRelease, Motion, KeyPress,
        KeyRelease, Enter, Leave, FocusIn, FocusOut, Configure, Destroy, Map,
        Unmap, MouseWheel, Virtual }
    [Flags] public enum EventModifiers { None, Shift, Lock, Control, Alt,
        Meta, Command, Button1, Button2, Button3, Button4, Button5, Double,
        Triple, Quadruple }
    public enum DispatchResult { Continue, Break }
    public delegate DispatchResult TkEventHandler(TkEvent tkEvent);

    public sealed class TkEvent                (all properties get/set)
        TkEventType Type; TkWindow Window
        int X, Y                               window-relative
        int RootX, RootY                       root-relative
        int Button; string KeySym; string Character; EventModifiers State
        int Delta; int ClickCount = 1; string VirtualName; int Width, Height

    public sealed class EventPattern : IEquatable<EventPattern>
        static EventPattern Parse(string text)
            forms: <Type>, <Modifier-...-Type-Detail>, <1>/<Double-1>
            (button shorthand), Button/Key aliases, virtual <<Name>>
        TkEventType Type; EventModifiers Modifiers; int Button; string KeySym
        string VirtualName; string Text
        bool Matches(TkEvent tkEvent); int Specificity()

    public sealed class BindingTable
        void Bind(string tag, string pattern, TkEventHandler handler)
        void Unbind(string tag, string pattern)
        IReadOnlyList<string> BoundPatterns(string tag)
        void RemoveTag(string tag)

    public sealed class WindowTree              (one per root; root.Tree)
        TkWindow Root; BindingTable Bindings; TkScheduler Scheduler
        FontManager Fonts; WindowManager WindowManager; MenuManager Menus
        ImageManager Images; ClipboardManager Clipboard
        ITextInputSink InputSink { get; set; }
        TkTheme Theme { get; set; }; OptionDatabase OptionDatabase
        TtkStyleEngine Styles
        void SetPalette(IReadOnlyList<string> args)      (tk_setPalette)
        TkWindow FocusWindow; TkWindow GrabWindow { get; set; }
        TkWindow PointerWindow
        void DispatchEvent(TkWindow window, TkEvent tkEvent)
            walks the window's bind tags; per tag the most specific matching
            binding fires; a handler returning Break stops the walk
        void PointerEvent(TkEventType type, int rootX, int rootY,
                int button = 0, EventModifiers state = EventModifiers.None,
                int delta = 0, int clickCount = 1)
            ButtonPress/ButtonRelease/Motion/MouseWheel only; hit-tests,
            honours implicit and explicit grabs, synthesizes Enter/Leave
        void KeyEvent(TkEventType type, string keySym, string character = "",
                EventModifiers state = EventModifiers.None)
            KeyPress/KeyRelease to the focus window (root when none)
        void VirtualEvent(TkWindow window, string virtualName)
        void SetFocus(TkWindow window)          FocusOut old, FocusIn new
        TkWindow FocusNext(TkWindow window); TkWindow FocusPrev(TkWindow window)
        TkWindow HitTest(int rootX, int rootY)

Binding in code — the tag is a window path, a class name, "all", or any
string you put in window.BindTags:
    tree.Bindings.Bind(buttonWindow.PathName, "<ButtonPress-1>", e =>
    {
        Console.WriteLine($"pressed at {e.X},{e.Y}");
        return DispatchResult.Continue;
    });
    tree.Bindings.Bind("Entry", "<Key-Return>", e => DispatchResult.Break);
    tree.Bindings.Bind(listWindow.PathName, "<<ListboxSelect>>", e => ...);

SCHEDULING — after, update, idle (Events)
-----------------------------------------
    public sealed class TkScheduler             (tree.Scheduler)
        ITkDispatcher Host { get; set; }        UI-thread bridge (null headless)
        ITkTimeSource TimeSource { get; set; }  swappable clock
        event Action RepaintRequested
        bool IsRelayoutPending
        void ScheduleIdle(Action callback); void ScheduleRelayout()
        void ScheduleRepaint()
        AfterHandle After(int milliseconds, Action callback)
        AfterHandle AfterIdle(Action callback)
        void CancelAfter(AfterHandle handle)
        void UpdateIdleTasks()                  synchronous flush of relayout/
                                                repaint/idle work, now
        void Update()                           + due timers + host pump
    public sealed class AfterHandle             opaque token for CancelAfter
    public interface ITkDispatcher
        void Post(Action action)
        object StartTimer(int milliseconds, Action callback)
        void CancelTimer(object handle)
        void PumpPendingWork()                  may be a no-op
    public interface ITkTimeSource
        long NowMilliseconds { get; }

FONTS (Fonts)
-------------
    public sealed class TkFont
        string Family = "TkDefault"; int Size; bool Bold, Italic, Underline,
        Overstrike; string Name (set for named fonts)
        void CopyAttributesFrom(TkFont other); string ToString()
    public readonly struct FontMetrics
        FontMetrics(int ascent, int descent, bool isFixed)
        int Ascent; int Descent; int LineSpace; bool IsFixed
    public sealed class FontManager             (tree.Fonts — the single
                                                 measurement seam)
        double PixelsPerPoint { get; set; }
        TkFont CreateNamed(string name, TkFont template = null)
        TkFont GetNamed(string name); void DeleteNamed(string name)
        IReadOnlyCollection<string> Names
        TkFont Parse(string descriptor)         named font, "{family size
                                                ?styles?}", "-family ... -size
                                                ...", or an X core font name
                                                (mapped to the default)
        SKFont GetSkFont(TkFont font)
        int Measure(TkFont font, string text)   ("font measure", pixels)
        FontMetrics Metrics(TkFont font)        ("font metrics")
        float PixelSize(TkFont font)

IMAGES (Images)
---------------
    public sealed class ImageManager            (tree.Images — the "image"
                                                 command model)
        event Action ImagesChanged
        PhotoImage Find(string name); IReadOnlyList<string> Names
        PhotoImage CreatePhoto(string name, IReadOnlyDictionary<string, string> options)
                                                null/empty name auto-names
        void Delete(string name)
        void SnapshotWindow(PhotoImage image, string pathName)
                                                ("-format window -data .path")
        string Execute(IReadOnlyList<string> words)
                                                create delete names width
                                                height type types inuse
    public sealed class PhotoImage
        string Name; string TypeName; int Width; int Height
        event Action Changed
        void SetUserSize(int userWidth, int userHeight)
        bool IsTransparent(int x, int y); void Blank()
        void CopyFrom(PhotoImage source, PhotoCopyOptions options)
        void LoadEncoded(byte[] encoded, string sourceLabel)   GIF/PNG bytes
        void LoadPixels(byte[] rgba, int width, int height)
        void WriteFile(string path, string format, int fromX1, int fromY1,
                       int fromX2, int fromY2)              "png"/"gif"
        string GetPixelText(int x, int y); string Data()
        void Draw(SKCanvas canvas, float x, float y)
        string Execute(IReadOnlyList<string> words)
                                                blank cget configure copy data
                                                get put read redither
                                                transparency write
    public sealed class PhotoCopyOptions        (the "copy" option set)
        static PhotoCopyOptions Parse(IReadOnlyList<string> words, int start)
        bool HasFrom, HasFromCorner; int FromX1, FromY1, FromX2, FromY2
        bool HasTo, HasToCorner; int ToX1, ToY1, ToX2, ToY2
        int ZoomX = 1, ZoomY = 1, SubsampleX = 1, SubsampleY = 1
        bool Shrink; bool RuleSet

Photo images are referenced by NAME through "-image NAME" on labels,
buttons, canvas image items, menu entries and treeview items.

MENUS (Menus)
-------------
    public enum MenuEntryType { Command, Cascade, Separator, Checkbutton,
                                Radiobutton }
    public sealed class MenuEntry
        MenuEntryType Type; string Label; string Accelerator; int Underline = -1
        bool Disabled; Action Command; MenuWidget Submenu; string Image
        string Compound; bool Selected
    public sealed class MenuWidget : WidgetBase
        MenuWidget(TkWindow window)
        bool IsMenubar; IReadOnlyList<MenuEntry> Entries; int ActiveIndex
        MenuEntry AddCommand(string label, Action command = null,
                             string accelerator = null, int underline = -1)
        MenuEntry AddCascade(string label, MenuWidget submenu, int underline = -1)
        MenuEntry AddSeparator()
        MenuEntry AddCheckbutton(string label, Action command = null)
        MenuEntry AddRadiobutton(string label, Action command = null)
        void RemoveEntries(int first, int last)
        int EntryIndexAt(int x, int y); SKRectI EntryRect(int index)
        void Invoke(int index)
        options: -type -font (+ shared)
    public sealed class MenuManager             (tree.Menus)
        bool IsPosted; IReadOnlyList<MenuWidget> Posted
        MenuWidget CreateMenu(string name)
        void SetMenubar(MenuWidget menubar)     the root menubar
        void Popup(MenuWidget menu, int x, int y)   (tk_popup)
        void Unpost()

A menubar with -underline cascades answers Alt+<letter> traversal.

OVERLAY TOPLEVELS AND DIALOGS (Overlay, Dialogs)
------------------------------------------------
    public sealed class WindowManager           (tree.WindowManager — "wm")
        IReadOnlyList<OverlayState> Overlays
        string RootTitle; event Action<string> RootTitleChanged
        event Action<TkWindow> CloseRequested   the overlay close box
        TkWindow CreateToplevel(string name)    "toplevel .name": excluded
                                                from the base layout, drawn
                                                with chrome above everything
        OverlayState GetOverlay(TkWindow window)
        void SetTitle(TkWindow window, string title)
        void SetGeometry(TkWindow window, int? width, int? height, int? x, int? y)
        void Withdraw(TkWindow window); void Deiconify(TkWindow window)
        void Raise(TkWindow window)
        void SetTransient(TkWindow window, TkWindow master)
        void SetOverrideRedirect(TkWindow window, bool overrideRedirect)
        void SetResizable(TkWindow window, bool width, bool height)
        void Grab(TkWindow window); void ReleaseGrab()
    public sealed class OverlayState
        TkWindow Window; string Title; bool Withdrawn; bool OverrideRedirect
        bool ResizableWidth, ResizableHeight; TkWindow TransientFor
        int? GeometryWidth, GeometryHeight, GeometryX, GeometryY
        int BorderWidth; int TitleBarHeight
        SKRectI FrameRect; SKRectI TitleBarRect; SKRectI CloseBoxRect
    public sealed class MessageDialogOptions
        string Type = "ok"        ok okcancel yesno yesnocancel retrycancel
                                  abortretryignore
        string Message; string Detail; string Title; string Icon = "info"
        string Default; IReadOnlyList<string> CustomButtons
    public static class MessageDialog
        static TkWindow Show(WindowTree tree, MessageDialogOptions options,
                             Action<string> onResult)
            a Skia overlay toplevel; onResult receives "ok"/"cancel"/"yes"/...

THEMING, OPTION DATABASE, ttk::style (Theming)
----------------------------------------------
    public sealed class TkTheme                 (tree.Theme / host.ThemePalette)
        string Name = "Classic" and one string Tk-color-spec property per
        default the toolkit paints: Background, Foreground, ActiveBackground,
        ActiveForeground, DisabledForeground, HighlightBackground,
        HighlightColor, InsertBackground, SelectBackground, SelectForeground,
        SelectColor, IndicatorForeground, TroughColor, FieldBackground,
        FieldForeground, ListSelectBackground, ListSelectForeground,
        HeadingBackground, HeadingForeground, MenuBackground, MenuForeground,
        MenuActiveBackground, MenuActiveForeground, StageBackground,
        TitleBarBackground, TitleBarForeground, ButtonBackground,
        ButtonForeground, ScrollbarBackground, CanvasBackground,
        DialogInfoAccent, DialogWarningAccent, DialogErrorAccent.
        Unset properties keep their classic values.
        static SKColor Color(string spec)
        static TkTheme CreateClassic(); static TkTheme CreateBisque()
        static TkTheme FromPalette(IReadOnlyList<string> args)   (tk_setPalette)
        static Dictionary<string, string> DerivePalette(IReadOnlyList<string> args)
    public static class TkThemeRegistry
        static void Register(string name, Func<TkTheme> factory)
        static TkTheme TryCreate(string name)   null when unknown
        static IReadOnlyList<string> Names
    public static class BuiltinThemes           (all registered by name)
        CreateDarkNew, CreateLightNew, CreateDarkPlus, CreateLightPlus,
        CreateDarkModern, CreateLightModern, CreateMonokai,
        CreateDimmedMonokai, CreateSolarizedDark, CreateSolarizedLight,
        CreateAbyss, CreateQuietLight, CreateRed, CreateTomorrowNightBlue,
        CreateKimbieDark   — each: static TkTheme Create...()
    public sealed class OptionDatabase          (tree.OptionDatabase — "option")
        string ApplicationName = "tk"; string ApplicationClass = "Tk"
        void Add(string pattern, string value, string priority = "interactive")
        void Clear()
        void ReadContent(string content, string priority = "interactive")
        string Get(TkWindow window, string name, string className)
        void ApplyTo(WidgetOptions options, TkWindow window)
        bool IsEmpty
    public sealed class TtkStyleEngine          (tree.Styles — "ttk::style")
        string CurrentTheme; IReadOnlyList<string> ThemeNames
        void ThemeUse(string name); void ThemeCreate(string name, string parent = null)
        void Configure(string style, string option, string value)
        string ConfigureGet(string style, string option)
        void Map(string style, string option, string tclPairs)
        string MapGet(string style, string option)
        string Lookup(string style, string option,
                      IReadOnlyCollection<string> states = null,
                      string defaultValue = null)
        static IEnumerable<string> StyleChain(string style)
        string Execute(IReadOnlyList<string> words)

Switching tree.Theme repaints the running UI with no per-widget
reconfiguration; explicitly configured widget colors are never touched.
Option-database entries apply when a widget is CREATED (Tk's rule); later
additions never restyle existing widgets. The standard ttk theme names
default/clam/alt/classic exist; the ttk element/layout engine is deferred
(accept-and-no-op).

TEXT WIDGET AND TEXT INPUT (Text)
---------------------------------
    public readonly struct TextPosition : IComparable<TextPosition>,
                                          IEquatable<TextPosition>
        TextPosition(int line, int charIndex); int Line; int Char
        (comparison operators; Line is 1-based)
    public sealed class TextTag
        string Name; WidgetOptions Options
        IReadOnlyList<TextPosition> Boundaries; bool Covers(TextPosition position)
    public sealed class TextWidget : IWidget, ITextInputTarget
        TextWidget(TkWindow window)
        ITextInputSink InputSink; string Composition
        event Action<double, double> XScrollChanged, YScrollChanged
        TextPosition EndPosition
        string Index(string indexExpr); TextPosition ParseIndex(string indexExpr)
        TextPosition Clamp(TextPosition position)
        void Insert(string index, string text, IReadOnlyList<string> tags = null)
        void Delete(string start, string end = null)
        string Get(string start, string end = null)
        void InsertAtCaret(string text)
        void MarkSet(string name, string index); void MarkUnset(string name)
        string MarkGravity(string name, string gravity = null)
        IReadOnlyCollection<string> MarkNames()
        void TagAdd(string name, string start, string end = null)
        void TagRemove(string name, string start, string end = null)
        void TagDelete(string name)
        void TagConfigure(string name, IReadOnlyDictionary<string, string> options)
        IReadOnlyList<string> TagRanges(string name); IReadOnlyList<string> TagNames()
        TextTag GetTag(string name)
        void See(string index)
        void YViewFractions(out double first, out double last)
        void YViewMoveTo(double fraction); void YViewScroll(int count, bool pages)
        TextPosition PositionAt(string atExpr)
        TextPosition PositionAtPoint(int x, int y)
        void MoveCaret(string indexExpr, bool extend); bool DeleteSelectionIfAny()
        void CopySelectionToClipboard(bool cut); void PasteFromClipboard()
        void CommitText(string text); void SetComposition(string preedit)
        options: -width -height -font -foreground -background -wrap -padx
                 -insertbackground -underline -overstrike -borderwidth
                 -relief -highlightthickness
    Index expressions are Tk's: "1.0", "end", "insert", "end - 1 chars",
    "3.0 lineend", mark and tag forms.

    public interface ITextInputTarget
        TkWindow Window { get; }
        void CommitText(string text); void SetComposition(string preedit)
    public interface ITextInputSink              (tree.InputSink)
        void Attach(ITextInputTarget target); void Detach()
        void UpdateCaret(int x, int y, int height)

CLIPBOARD (Clipboard)
---------------------
    public sealed class ClipboardManager        (tree.Clipboard — "clipboard")
        ITkClipboard Host { get; set; }         null = in-process only
        void Clear(); void Append(string text); string Get()
        string Execute(IReadOnlyList<string> words)
    public interface ITkClipboard
        void SetText(string text); string GetText()

RENDERING (Rendering)
---------------------
    public static class TkRenderer
        static void Render(TkWindow root, SKCanvas canvas)
            clears to Theme.StageBackground, paints the whole tree with
            (0,0) at the root's top-left
        static void RenderWindow(TkWindow window, SKCanvas canvas)
            one subtree with (0,0) at that window's top-left
    public enum Relief { Flat, Raised, Sunken, Groove, Ridge, Solid }
    public static class ReliefPainter           (the shared 3D border primitive)
        static Relief Parse(string text)
        static SKColor LightShadow(SKColor background)
        static SKColor DarkShadow(SKColor background)
        static void DrawBorder(SKCanvas canvas, SKRect rect, int borderWidth,
                               Relief relief, SKColor background)

THE Tcl COMMAND BRIDGE (TkBootstrap, Tcl)
-----------------------------------------
    public static class TkBootstrap
        static readonly Version TkVersion; const string TkPatchLevel
        static readonly Version ImgVersion
        static ReturnCode Register(Interpreter interpreter, ref Result error)
            sets ::tk_version / ::tk_patchLevel and provides the Tk and Img
            packages so "package require Tk"/"Img" and version gates pass

    public sealed class TkTclBridge : IDisposable
        static TkTclBridge Register(Interpreter interpreter, WindowTree tree)
            DIRECT mode: everything runs on the calling thread (headless,
            tests; bind and -command scripts run inline like real Tk)
        static TkTclBridge RegisterHosted(Interpreter interpreter, WindowTree tree)
            HOSTED mode: the interpreter moves to a dedicated Tcl worker
            thread; Tk command bodies marshal synchronously to the UI thread
            through tree.Scheduler.Host (must be set — TkHostView sets it;
            otherwise InvalidOperationException); UI callbacks post their
            scripts back to the Tcl thread. Modal commands (tk_messageBox,
            tk_dialog, the file pickers) block the Tcl thread while the UI
            stays live.
        event Action<string> BackgroundError    bgerror analogue: the Tcl
                                                error text of a failed
                                                callback script
        ITkFileDialogProvider FileDialogs { get; set; }
                                                null = picker commands raise
                                                Tcl errors
        Func<string, string[], string> ModalAutoResponder { get; set; }
                                                optional auto-answer for the
                                                modal dialog commands in
                                                scripted/headless runs:
                                                receives the command name
                                                ("tk_messageBox") and its
                                                argument words, returns the
                                                result string, or null to
                                                decline and show the dialog
        void PostScript(string script)          fire-and-forget evaluation on
                                                the Tcl thread (inline in
                                                DIRECT mode)
        void Post(Action<Interpreter> work)     arbitrary interpreter work on
                                                the Tcl thread — the ONLY
                                                correct way to touch a hosted
                                                interpreter
        void Dispose()                          stops the Tcl thread

    public sealed class TkTclError : Exception
        TkTclError(string message)              throw from code running inside
                                                a bridge command to produce a
                                                Tcl error with that message

    public interface ITkFileDialogProvider      (the only native-escape seam)
        void GetOpenFile(IReadOnlyDictionary<string, string> options,
                         Action<string> completion)
        void GetSaveFile(IReadOnlyDictionary<string, string> options,
                         Action<string> completion)
        void ChooseDirectory(IReadOnlyDictionary<string, string> options,
                             Action<string> completion)
            completion receives the path, or "" when cancelled
    public sealed class TkHostFileDialogs : ITkFileDialogProvider
            the CodeBrix.Platform pickers (Hosting)

Registered command surface (all drive the widget classes above):
  * Widget creation: frame labelframe label button entry text listbox
    checkbutton radiobutton scrollbar panedwindow canvas combobox treeview
    separator menu toplevel, and the ttk:: forms — each returns its path and
    registers a ".path subcommand" instance command.
  * Geometry: pack (+ forget/propagate/info/slaves) and grid (+ forget/
    row|columnconfigure/size/slaves/info).
  * Events: bind (Tk %-substitution; X11 <Button-4/5> mirror to MouseWheel),
    bindtags; -command / -textvariable / -variable wired through the
    interpreter's variable traces (a check/radio group over one variable
    shares one ToggleVariable).
  * Windowing: wm (title/geometry/withdraw/deiconify/transient/
    overrideredirect/resizable), winfo, destroy, focus, grab, raise/lower.
    ". configure -menu" builds the root menubar. "tkwait visibility" flushes
    and returns.
  * Resources: image (photo names become instance commands), font (measure/
    metrics), clipboard, option, ttk::style, and the tk_setPalette /
    tk_bisque / tk_classic / tk_<theme> appliers.
  * Dialogs: tk_messageBox / tk_dialog as Skia overlays; tk_popup;
    tk_getOpenFile / tk_getSaveFile / tk_chooseDirectory via FileDialogs.
  * Event loop: after / after idle / after cancel, update, update idletasks.

HOSTING SEAMS (Hosting) — what TkHostView wires, and the templates for
custom hosts
-----------------------------------------------------------------------
    TkHostDispatcher(DispatcherQueue queue) : ITkDispatcher
    TkHostClipboard : ITkClipboard
    TkHostTextInputSink : ITextInputSink
        FrameworkElement InputElement; WindowTree Tree; ITextInputTarget Target
        void RequestFocus()
    TkHostFileDialogs : ITkFileDialogProvider   (async CodeBrix.Platform pickers)

XAML DECLARATION ELEMENTS (Xaml)
--------------------------------
    public abstract class TkElement : Panel     invisible; DataContext flows in
        TkWindow TkWindow; IWidget TkWidget; TkHostView Host   (null until load)
        string Options            Tcl-style pair list catch-all, e.g.
                                  Options="-highlightthickness 0 -takefocus 1"
        string TkBackground; string TkForeground; string Relief
        int BorderWidth; string TkFont
        pack:  Side Side; Fill Fill; bool Expand; Anchor PackAnchor
               int PadX, PadY, IPadX, IPadY
        grid:  int GridRow, GridColumn, GridRowSpan, GridColumnSpan
               Sticky Sticky      (set GridRow/GridColumn to use grid)
    Elements and their typed properties (each also exposes its materialized
    widget):
        TkFrame        FrameWidget FrameWidget
        TkLabelframe   Text; LabelframeWidget
        TkLabel        Text, Image, ContentAnchor, Justify; LabelWidget
        TkButton       Text, Image, ICommand Command, object CommandParameter;
                       ButtonWidget
        TkCheckbutton  Text, bool Checked, ICommand Command; CheckbuttonWidget
        TkRadiobutton  Text, Value, Group, bool Checked, ICommand Command;
                       RadiobuttonWidget
        TkEntry        int WidthChars, Show, Text; EntryWidget
        TkText         int WidthChars, int HeightLines, Wrap, Text; TextWidget
        TkListbox      int HeightRows, Items, IEnumerable ItemsSource;
                       ListboxWidget
        TkTreeview     Columns; TreeviewWidget
        TkCombobox     Items, Value, ICommand SelectionCommand; ComboboxWidget
        TkCanvasView   int PixelWidth, int PixelHeight, ScrollRegion;
                       CanvasWidget
        TkScrollbar    Orient, For (the Name of the scrolled element);
                       ScrollbarWidget
        TkSeparator    Orient; SeparatorWidget
        TkPanedwindow  Orient; PanedWindowWidget
        TkMenubar      MenuWidget
        TkMenu         Label, int Underline, Image, Compound
        TkMenuItem     Label, Accelerator, int Underline, Image, Compound,
                       ICommand Command, object CommandParameter
        TkMenuSeparator
        TkPhoto        File, Data, int PixelWidth, int PixelHeight;
                       PhotoImage Photo   (a named photo, by XAML Name)
    Booleans become 1/0, enums their lowercase Tk name, negative ints mean
    "unset". Command properties bind to view-model ICommands the normal XAML
    way (for example a CodeBrix.Platform.Simple SimpleCommand).

COMPLETE EXAMPLES
=================
1. Declare the UI in XAML, wire widget access in a thin code-behind
--------------------------------------------------------------------
    <Page x:Class="MyApp.Views.MainPage"
          xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
          xmlns:vm="using:MyApp.ViewModels"
          xmlns:tkhost="using:CodeBrix.Platform.TkCanvas.Hosting"
          xmlns:tk="using:CodeBrix.Platform.TkCanvas.Xaml">
      <Page.DataContext><vm:MainViewModel /></Page.DataContext>

      <tkhost:TkHostView x:Name="TkHost" Theme="DarkNew">
        <tk:TkPhoto Name="backIcon" File="Assets/tk-back.gif" />

        <tk:TkMenubar Side="Top" Fill="X">
          <tk:TkMenu Label="File" Underline="0">
            <tk:TkMenuItem Label="New" Accelerator="Ctrl+N"
                           Command="{Binding NewCommand}" />
            <tk:TkMenuSeparator />
            <tk:TkMenuItem Label="About" Command="{Binding AboutCommand}" />
          </tk:TkMenu>
        </tk:TkMenubar>

        <tk:TkFrame Side="Top" Fill="X" Relief="raised" BorderWidth="1">
          <tk:TkButton Side="Left" Image="backIcon" Relief="flat"
                       BorderWidth="0" Command="{Binding BackCommand}" />
          <tk:TkButton Side="Left" Text="Greet"
                       Command="{Binding GreetCommand}" />
        </tk:TkFrame>

        <tk:TkFrame Side="Top" Fill="X">
          <tk:TkLabel Side="Left" Text="Name:" PadX="4" />
          <tk:TkEntry x:Name="NameEntry" Side="Left" Fill="X" Expand="True" />
        </tk:TkFrame>

        <tk:TkFrame Side="Top" Fill="X" PadY="2">
          <tk:TkCheckbutton Side="Left" Text="Verbose"
                            Command="{Binding VerboseCommand}" />
          <tk:TkRadiobutton Side="Left" Text="Edit" Group="mode" Value="edit"
                            Checked="True" Command="{Binding ModeCommand}" />
          <tk:TkRadiobutton Side="Left" Text="View" Group="mode" Value="view"
                            Command="{Binding ModeCommand}" />
        </tk:TkFrame>

        <tk:TkScrollbar Side="Right" Fill="Y" Orient="vertical" For="Output" />
        <tk:TkText x:Name="Output" Fill="Both" Expand="True"
                   WidthChars="60" HeightLines="12" />
      </tkhost:TkHostView>
    </Page>

There is deliberately no live two-way binding between widget state and
view-model properties. When the view model needs widget state, expose
Func/Action properties behind a small interface and wire them in
DataContextChanged — the whole code-behind stays this size:

    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            DataContextChanged += (s, e) =>
            {
                if (DataContext is ITkWidgetBridge bridge)
                {
                    bridge.GetEntryText = () => NameEntry.EntryWidget?.Text ?? "";
                    bridge.AppendOutputLine = line =>
                    {
                        Output.TextWidget?.Insert("end - 1 chars", line + "\n");
                        TkHost.RequestUpdate();
                    };
                }
            };
            InitializeComponent();
        }
    }

2. Build the same kind of UI in code (works headless too)
----------------------------------------------------------
    using System;
    using System.Collections.Generic;
    using CodeBrix.Platform.TkCanvas.Canvas;
    using CodeBrix.Platform.TkCanvas.Hosting;
    using CodeBrix.Platform.TkCanvas.Layout;
    using CodeBrix.Platform.TkCanvas.Widgets;
    using CodeBrix.Platform.TkCanvas.Windowing;

    var host = new TkHostView();          // or: TkWindow root = TkWindow.CreateRoot();
    containerGrid.Children.Add(host);     //     root.SetForcedSize(640, 480);
    TkWindow root = host.Root;

    TkWindow buttonWindow = root.CreateChild("hello");
    var button = new ButtonWidget(buttonWindow);
    button.Configure(new Dictionary<string, string> { { "-text", "Hello" } });
    button.Invoked += () => Console.WriteLine("clicked");
    PackLayout.Configure(buttonWindow, new PackOptions { Side = Side.Top });

    TkWindow listWindow = root.CreateChild("list");
    var listbox = new ListboxWidget(listWindow);
    listbox.Insert(0, "alpha", "beta", "gamma");
    PackLayout.Configure(listWindow, new PackOptions
    {
        Side = Side.Left, Fill = Fill.Y,
    });
    TkWindow barWindow = root.CreateChild("bar");
    var bar = new ScrollbarWidget(barWindow);
    bar.Configure(new Dictionary<string, string> { { "-orient", "vertical" } });
    PackLayout.Configure(barWindow, new PackOptions { Side = Side.Left, Fill = Fill.Y });
    listbox.YScrollChanged += (first, last) => bar.Set(first, last);
    bar.Command += words =>
    {
        if (words[0] == "moveto")
            listbox.YViewMoveTo(double.Parse(words[1],
                    System.Globalization.CultureInfo.InvariantCulture));
        else
            listbox.YViewScroll(int.Parse(words[1]), words[2] == "pages");
    };

    TkWindow gridWindow = root.CreateChild("form");
    new FrameWidget(gridWindow);
    PackLayout.Configure(gridWindow, new PackOptions { Fill = Fill.Both, Expand = true });
    TkWindow labelWindow = gridWindow.CreateChild("l");
    new LabelWidget(labelWindow).Configure(
        new Dictionary<string, string> { { "-text", "Name:" } });
    TkWindow entryWindow = gridWindow.CreateChild("e");
    var entry = new EntryWidget(entryWindow);
    GridLayout.Configure(labelWindow, new GridOptions { Row = 0, Column = 0, Sticky = Sticky.W });
    GridLayout.Configure(entryWindow, new GridOptions
    {
        Row = 0, Column = 1, Sticky = Sticky.W | Sticky.E,
    });
    GridLayout.ColumnConfigure(gridWindow, 1, weight: 1);

    root.Tree.Scheduler.UpdateIdleTasks();   // geometry is final after this
    Console.WriteLine(entryWindow.Width);

3. The typed canvas API
-----------------------
    TkWindow canvasWindow = root.CreateChild("c");
    var canvas = new CanvasWidget(canvasWindow);
    canvas.Configure(new Dictionary<string, string>
    {
        { "-width", "300" }, { "-height", "200" }, { "-background", "white" },
    });
    PackLayout.Configure(canvasWindow, new PackOptions { Fill = Fill.Both, Expand = true });

    int box = canvas.Create("rectangle", new double[] { 20, 20, 120, 80 },
        new Dictionary<string, string> { { "-fill", "red" }, { "-tags", "box" } });
    int arrow = canvas.Create("line", new double[] { 20, 150, 200, 150 },
        new Dictionary<string, string> { { "-arrow", "last" }, { "-width", "3" } });
    canvas.Create("text", new double[] { 70, 100 },
        new Dictionary<string, string> { { "-text", "hello" }, { "-anchor", "n" } });
    canvas.Create("arc", new double[] { 150, 20, 250, 120 },
        new Dictionary<string, string>
        {
            { "-start", "0" }, { "-extent", "120" }, { "-style", "pieslice" },
        });

    foreach (ICanvasItem item in canvas.FindWithTag("box"))
    {
        item.Configure(new Dictionary<string, string> { { "-outline", "blue" } });
    }
    var line = (LineItem)canvas.Items[1];
    Console.WriteLine(line.Arrow);            // ArrowStyle.Last
    Console.WriteLine(canvas.BBox("all"));    // SKRectI? — Tk's bbox math
    canvas.Move("box", 10, 5);
    canvas.BindItem("box", "<ButtonPress-1>", e =>
    {
        Console.WriteLine("box pressed at " + e.X + "," + e.Y);
        return DispatchResult.Continue;
    });

    // The string layer takes the Tcl command's words verbatim and returns
    // exactly what Tk returns — what a Tcl bridge calls:
    string ids = canvas.Execute(new[] { "find", "withtag", "box" });
    canvas.Execute(new[] { "itemconfigure", "box", "-fill", "green" });

4. Run an unmodified Tcl/Tk program through the bridge (hosted)
---------------------------------------------------------------
    using System;
    using System.Threading.Tasks;
    using CodeBrix.Platform.TclTk._Components.Public;
    using CodeBrix.Platform.TkCanvas;
    using CodeBrix.Platform.TkCanvas.Hosting;
    using CodeBrix.Platform.TkCanvas.Tcl;

    // In the page: Loaded += (s, e) => StartTcl(TkHost);  Unloaded => Dispose
    private Interpreter _interpreter;
    private TkTclBridge _bridge;

    private void StartTcl(TkHostView host)       // UI thread, host loaded
    {
        var tree = host.Tree;
        Task.Run(() =>
        {
            Result r = null;
            _interpreter = Interpreter.Create(ref r);
            if (_interpreter == null) { Console.Error.WriteLine(r); return; }

            Result error = null;
            if (TkBootstrap.Register(_interpreter, ref error) != ReturnCode.Ok)
            {
                Console.Error.WriteLine("TkBootstrap: " + error); return;
            }
            // (optional) TclTkExtras.RegisterAll(_interpreter, ref error);

            _bridge = TkTclBridge.RegisterHosted(_interpreter, tree);
            _bridge.FileDialogs = new TkHostFileDialogs();
            _bridge.BackgroundError += message =>
                    Console.Error.WriteLine("bgerror: " + message);

            // From here on, touch the interpreter ONLY through Post:
            _bridge.Post(interp =>
            {
                Result result = null;
                if (interp.EvaluateScript("source {app.tcl}", ref result) != ReturnCode.Ok)
                {
                    Console.Error.WriteLine("app.tcl: " + result);
                }
            });
        });
    }

    private void StopTcl()
    {
        _bridge?.Dispose();
        _interpreter?.Dispose();
    }

Headless / same-thread (tests, generators): replace RegisterHosted with
TkTclBridge.Register(interpreter, root.Tree) on a TkWindow.CreateRoot() root
with SetForcedSize, and call interpreter.EvaluateScript directly.

5. Headless: build, lay out, render to a PNG
--------------------------------------------
    using SkiaSharp;
    using CodeBrix.Platform.TkCanvas.Rendering;

    TkWindow root = TkWindow.CreateRoot();
    root.SetForcedSize(320, 200);
    TkWindow w = root.CreateChild("b");
    new ButtonWidget(w).Configure(new Dictionary<string, string> { { "-text", "Hi" } });
    PackLayout.Configure(w, new PackOptions { Side = Side.Top });
    TkLayout.Update(root);

    using (var surface = SKSurface.Create(new SKImageInfo(320, 200)))
    {
        TkRenderer.Render(root, surface.Canvas);
        using (SKImage image = surface.Snapshot())
        using (SKData png = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            System.IO.File.WriteAllBytes("tk.png", png.ToArray());
        }
    }

    // synthetic input, same tree:
    root.Tree.PointerEvent(TkEventType.ButtonPress, 20, 10, button: 1);
    root.Tree.PointerEvent(TkEventType.ButtonRelease, 20, 10, button: 1);
    root.Tree.KeyEvent(TkEventType.KeyPress, "Return", "\r");

MINIMUM VIABLE PROJECT
======================
A CodeBrix.Platform app hosting one TkHostView. Layout (the shape the
CodeBrix.Develop "new application" template and samples/DRAKON.Brix use):

    MyApp.Core/       class library: view models + this package
    MyApp.UI/         shared project (.shproj/.projitems): App.xaml, Views/
    MyApp.LinuxX11/   one head project per platform (exactly ONE head
                      package each)

    <!-- MyApp.Core/MyApp.Core.csproj -->
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <RootNamespace>MyApp</RootNamespace>
        <!-- CodeBrix.Platform needs these for conditional compilation -->
        <DefineConstants>$(DefineConstants);HAS_CODEBRIX;HAS_CODEBRIX_WINUI</DefineConstants>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Platform.ApacheLicenseForever" Version="..." />
        <PackageReference Include="CodeBrix.Platform.Fonts.OpenSans.ApacheLicenseForever" Version="..." />
        <PackageReference Include="CodeBrix.Platform.TkCanvas.BsdLicenseForever" Version="..." />
      </ItemGroup>
    </Project>

    <!-- MyApp.LinuxX11/MyApp.LinuxX11.csproj -->
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <OutputType>Exe</OutputType>
        <DefineConstants>$(DefineConstants);HAS_CODEBRIX;HAS_CODEBRIX_WINUI</DefineConstants>
      </PropertyGroup>
      <ItemGroup>
        <Page Include="**\*.xaml" Exclude="bin\**\*.xaml;obj\**\*.xaml" />
        <None Remove="**\*.xaml" />
      </ItemGroup>
      <Import Project="..\MyApp.UI\MyApp.UI.projitems" Label="Shared" />
      <ItemGroup>
        <ProjectReference Include="..\MyApp.Core\MyApp.Core.csproj" />
      </ItemGroup>
      <ItemGroup>
        <!-- EXACTLY ONE platform head package per head project -->
        <PackageReference Include="CodeBrix.Platform.Runtime.Skia.X11.ApacheLicenseForever" Version="..." />
      </ItemGroup>
    </Project>

    // MyApp.LinuxX11/Program.cs
    using System;
    using CodeBrix.Platform.UI.Hosting;

    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var host = CodeBrixPlatformHostBuilder.Create()
                .App(() => new App())
                .UseLinuxX11()
                .Build();
            host.Run();
        }
    }

    // MyApp.UI/App.xaml.cs (OnLaunched: a Window with a Frame navigating to
    // Views.MainPage — the standard CodeBrix.Platform App)

    <!-- MyApp.UI/Views/MainPage.xaml -->
    <Page x:Class="MyApp.Views.MainPage"
          xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
          xmlns:tkhost="using:CodeBrix.Platform.TkCanvas.Hosting"
          xmlns:tk="using:CodeBrix.Platform.TkCanvas.Xaml">
      <Grid>
        <tkhost:TkHostView x:Name="TkHost">
          <tk:TkLabel Side="Top" Text="Hello, Tk" />
          <tk:TkButton Side="Top" Text="Quit" Command="{Binding QuitCommand}" />
        </tkhost:TkHostView>
      </Grid>
    </Page>

Other heads swap only the head package and the UseXxx() call (UseLinuxWayland,
UseLinuxFrameBuffer, UseMacOS, UseWin32Skia, UseWpfSkia per the platform's
own AGENT-README). For a Tcl-driven UI the page is just the empty
TkHostView plus example 4.

PERFORMANCE TIPS
================
  * Batch configuration: every Configure call re-measures and schedules a
    repaint; pass all options in one dictionary rather than one call each.
  * Prefer tree.Scheduler.ScheduleRepaint()/RequestUpdate() over repeated
    synchronous UpdateIdleTasks() in loops; flush once when you need final
    geometry.
  * The canvas keeps a display list; FindWithTag/FindArea/FindClosest are
    linear scans like Tk's. Tag a small set of items and query by tag
    rather than "all" in per-pointer-move handlers.
  * Text-item and text-widget measurement goes through one SKFont per TkFont
    (cached in FontManager); use named fonts (CreateNamed) shared across
    widgets instead of parsing descriptors per item.
  * When driving the interpreter through the bridge, hosted mode marshals
    EVERY Tk command body to the UI thread synchronously; keep tight Tcl
    loops that build thousands of widgets/items inside one script rather
    than many PostScript calls, and let the interpreter cache parsed scripts
    (CacheParsedScripts) plus ProductionMode for batch drawing.
  * Photo images decode once on load; re-creating an image by the same name
    re-decodes.

COMMON PITFALLS TO AVOID
========================
  * XAML elements' widget properties (NameEntry.EntryWidget, ...) are null
    until the TkHostView has loaded; wire access in Loaded/DataContextChanged
    and null-check, never in the constructor.
  * There is NO two-way binding of widget state to view-model properties by
    design; read widget state through the typed widget property.
  * A container is either packed or gridded, never both (Tk's rule);
    Configure on the other manager for the same container is an error.
  * Explicitly configured colors always win over themes, styles and the
    option database — "why doesn't my theme recolor this widget" is almost
    always an explicit -background somewhere.
  * The option database applies at widget CREATION only; set
    TkHostView.OptionsDatabase before load, or add entries before creating
    widgets in code.
  * Geometry values (Width/Height/X/Y) are stale until UpdateIdleTasks() /
    TkLayout.Update(root); read them after a flush.
  * Headless trees have no dispatcher: tree.Scheduler.Host is null, "after"
    timers only fire from Update()/UpdateIdleTasks() pumping with the
    scheduler clock, and TkTclBridge.RegisterHosted throws — use Register.
  * In hosted bridge mode never call EvaluateScript from the UI thread;
    always bridge.Post(...). Never block the UI thread waiting on the Tcl
    thread (modal commands park the Tcl thread, not the UI).
  * Tk color names follow Tk 8.6 (green=#008000, gray/grey=#808080,
    maroon=#800000, purple=#800080), not the CSS values.
  * Unknown item/widget options are accepted and stored silently; a typo in
    an option name will not throw — check Options.Names when something is
    ignored.
  * CanvasWidget.Execute throws InvalidOperationException for Tk errors;
    the typed methods mostly throw ArgumentException for malformed input.
  * Text-bearing widget sizes depend on the installed font stack (formula-
    consistent with Tk, not pixel-identical to a given X server's fonts).
  * A widget with -state disabled still receives events; bindings must
    check state themselves, as in Tk.
  * WidgetBase cannot be subclassed from outside the package; custom widgets
    implement IWidget and register a TkWindow via window.Widget.

WHAT THIS PACKAGE DOES NOT DO
=============================
  * Canvas "postscript", in-canvas text-item editing, the bitmap item's
    pixels and the window item's embedded control paint: accepted, no-op.
  * The ttk element/layout engine (custom element layouts): accept-and-no-op;
    ttk::style configure/map/lookup/theme work, and ttk:: widgets are the
    same unified classic widgets.
  * Accessibility / screen-reader bridge: none (a fully Skia-drawn UI has no
    native control tree).
  * Native OS windows: toplevels are Skia overlays inside the host control
    with their own mini window-manager, not OS windows.
  * Native file pickers on its own: only through ITkFileDialogProvider
    (TkHostFileDialogs in a CodeBrix.Platform app).
  * IME pre-edit display beyond what the platform head's composition events
    deliver through the hidden input element.
  * Two-way data binding between widgets and view models.
  * Running Tcl by itself: the interpreter is CodeBrix.Platform.TclTk (see
    its AGENT-README at the repository root); the sqlite3/pdf4tcl command
    shims are CodeBrix.Platform.TclTk.Extras
    (src/CodeBrix.Platform.TclTk.Extras/AGENT-README.txt).

FIDELITY NOTES / KNOWN EDGES
============================
  * Layout, canvas coords/bbox/find and GIF decoding are byte-identical to
    real Tk on the vendored oracle fixtures; text-bearing natural sizes are
    font-stack dependent.
  * GIF writes are lossless up to 255 distinct colors; a full 256-color
    image gets one color remapped (the encoder reserves a transparency
    slot). PNG writes round-trip exactly.
  * The classic look is the default theme and is byte-identical to the
    pre-theming rendering; tk_setPalette derivation uses Tk's exact math.
  * Option-database matching: highest priority wins, ties go to the most
    recently added entry — no pattern-specificity ranking (as in Tk).

HEADLESS / CUSTOM HOSTS
=======================
Everything except Hosting/ runs without a UI: render into any SKCanvas via
TkRenderer.Render(root, canvas) (or RenderWindow for one subtree), drive
input with tree.PointerEvent/KeyEvent/VirtualEvent, pump with
tree.Scheduler.UpdateIdleTasks()/Update(), and swap the clock with
tree.Scheduler.TimeSource (ITkTimeSource) for deterministic "after" tests.
A custom host supplies ITkDispatcher (UI-thread posting + timers; assign to
tree.Scheduler.Host), and optionally ITkClipboard (tree.Clipboard.Host),
ITextInputSink (tree.InputSink) and ITkFileDialogProvider
(bridge.FileDialogs). TkHostDispatcher / TkHostClipboard /
TkHostTextInputSink / TkHostFileDialogs are the CodeBrix.Platform
implementations and the reference for writing new ones.

WORKING EXAMPLES ON GITHUB
==========================
  * A complete application — the unmodified DRAKON Editor Tcl booted on the
    interpreter + Extras + this bridge inside one TkHostView (hosted mode;
    DrakonRuntime.cs is the boot sequence, MainPage.xaml the one-element
    page):
    https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/samples/DRAKON.Brix
  * The XAML declaration path with every element type, menus, toggles,
    a paired scrollbar and the theme list:
    https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/samples/TkCanvas_Testing
    (src/DRAKON.Brix.UI/Views/MainPage.xaml)
  * Headless usage of every subsystem, oracle replays and the bridge in
    DIRECT mode:
    https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/tests/CodeBrix.Platform.TkCanvas.Tests
    (B6WidgetsTests.cs, B7bMenusDialogsTests.cs, B8bContentWidgetsTests.cs,
    B9TogglesSweepTests.cs, CanvasWidgetTests.cs, CanvasItemsB5bTests.cs,
    CanvasOracleTests.cs, TextItemTests.cs, TextWidgetTests.cs,
    KeyEditingTests.cs, EventPatternTests.cs, BindOracleTests.cs,
    PackLayoutTests.cs, PackOracleTests.cs, GridLayoutTests.cs,
    TkSchedulerTests.cs, FontManagerTests.cs, ImageManagerTests.cs,
    PhotoImageTests.cs, ImageWidgetsTests.cs, ClipboardManagerTests.cs,
    WindowManagerTests.cs, WindowTreeTests.cs, TkThemeTests.cs,
    ThemingOracleTests.cs, OptionDatabaseTests.cs, TtkStyleEngineTests.cs,
    ReliefPainterTests.cs, TkBootstrapTests.cs, TkTclBridgeTests.cs)

QUICK REFERENCE CARD
====================
    // host
    var host = new TkHostView();  host.Theme = "Monokai";  host.RequestUpdate();
    TkWindow root = host.Root;    WindowTree tree = host.Tree;
    // headless
    TkWindow root = TkWindow.CreateRoot();  root.SetForcedSize(640, 480);
    TkLayout.Update(root);        TkRenderer.Render(root, skCanvas);
    // windows + widgets
    TkWindow w = root.CreateChild("name");          // ".name"
    var b = new ButtonWidget(w);  b.Configure(new Dictionary<string,string>
            { { "-text", "Go" } });  b.Invoked += () => ...;
    // Frame Label Labelframe Button Checkbutton Radiobutton Entry Listbox
    // Treeview Combobox Scrollbar Separator PanedWindow Menu Canvas Text
    // layout
    PackLayout.Configure(w, new PackOptions { Side = Side.Left,
            Fill = Fill.Both, Expand = true, PadLeft = 4 });
    GridLayout.Configure(w, new GridOptions { Row = 0, Column = 1,
            Sticky = Sticky.All });
    GridLayout.ColumnConfigure(container, 1, weight: 1);
    tree.Scheduler.UpdateIdleTasks();               // geometry final now
    // events
    tree.Bindings.Bind(w.PathName, "<ButtonPress-1>", e => DispatchResult.Continue);
    tree.PointerEvent(TkEventType.Motion, x, y);    tree.SetFocus(w);
    tree.KeyEvent(TkEventType.KeyPress, "a", "a");  tree.VirtualEvent(w, "Name");
    // canvas
    var c = new CanvasWidget(root.CreateChild("c"));
    int id = c.Create("rectangle", new double[] { 0, 0, 50, 50 },
            new Dictionary<string,string> { { "-fill", "red" } });
    c.BindItem("all", "<Enter>", e => DispatchResult.Continue);
    c.Execute(new[] { "coords", id.ToString(), "10", "10", "60", "60" });
    // resources
    TkFont f = tree.Fonts.Parse("{Helvetica 12 bold}");  tree.Fonts.Measure(f, "x");
    PhotoImage p = tree.Images.CreatePhoto("icon",
            new Dictionary<string,string> { { "-file", "icon.gif" } });
    tree.Clipboard.Append("text");  tree.Theme = BuiltinThemes.CreateDarkNew();
    tree.OptionDatabase.Add("*Button.background", "green");
    tree.Styles.Configure("TButton", "-foreground", "blue");
    // menus, dialogs, toplevels
    MenuWidget m = tree.Menus.CreateMenu("file");  m.AddCommand("Open", () => ...);
    tree.Menus.SetMenubar(bar);   tree.Menus.Popup(m, x, y);
    MessageDialog.Show(tree, new MessageDialogOptions { Type = "yesno",
            Message = "Save?" }, answer => ...);
    TkWindow top = tree.WindowManager.CreateToplevel("dlg");
    tree.WindowManager.SetTitle(top, "Dialog");  tree.WindowManager.Grab(top);
    // after / update
    AfterHandle h = tree.Scheduler.After(500, () => ...);  tree.Scheduler.CancelAfter(h);
    // Tcl bridge
    TkBootstrap.Register(interp, ref error);
    TkTclBridge bridge = TkTclBridge.RegisterHosted(interp, host.Tree);  // or Register
    bridge.FileDialogs = new TkHostFileDialogs();
    bridge.Post(i => { Result r = null; i.EvaluateScript("source app.tcl", ref r); });
    bridge.BackgroundError += msg => ...;   bridge.Dispose();

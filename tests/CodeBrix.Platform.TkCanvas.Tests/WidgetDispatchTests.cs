using System;
using System.Globalization;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TkCanvas.Tcl;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Widget instance subcommands through the Tcl bridge in DIRECT mode: the
/// listbox <c>insert</c>/<c>delete</c> index rules and the
/// <c>ttk::treeview bbox</c> geometry query. Expected values and error texts
/// were probed on REAL Tk 8.6.16 (<c>wish</c>, Debian, x11) and are quoted
/// beside the assertions. Treeview pixel values differ from wish (fonts and
/// the column model differ); the asserted rules are the shape of the result
/// (x y width height), how rows and cells tile, and when the result is empty.
/// </summary>
public class WidgetDispatchTests : IDisposable
{
    private readonly Interpreter _interpreter;
    private readonly TkWindow _root;
    private readonly TkTclBridge _bridge;

    public WidgetDispatchTests()
    {
        Result result = null;
        _interpreter = Interpreter.Create(ref result, BooleanModeForTests.Mode);
        _interpreter.Should().NotBeNull();

        _root = TkWindow.CreateRoot();
        _root.SetForcedSize(640, 480);

        Result error = null;
        TkBootstrap.Register(_interpreter, ref error);
        _bridge = TkTclBridge.Register(_interpreter, _root.Tree);
    }

    public void Dispose()
    {
        _bridge.Dispose();
        _interpreter.Dispose();
    }

    private string Eval(string script)
    {
        Result result = null;
        ReturnCode code = _interpreter.EvaluateScript(script, ref result);
        if (code != ReturnCode.Ok)
        {
            throw new InvalidOperationException(
                "script failed: " + (result != null ? result.ToString() : "(null)"));
        }
        return result != null ? result.ToString() : string.Empty;
    }

    private ReturnCode TryEval(string script, out string message)
    {
        Result result = null;
        ReturnCode code = _interpreter.EvaluateScript(script, ref result);
        message = result != null ? result.ToString() : string.Empty;
        return code;
    }

    private static int[] Box(string bbox)
    {
        string[] parts = bbox.Split(' ');
        parts.Length.Should().Be(4);
        var values = new int[4];
        for (int i = 0; i < 4; i++)
        {
            values[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
        }
        return values;
    }

    // ------------------------------------------------------------- listbox

    [Fact]
    public void Listbox_insert_at_end_appends_after_the_last_item()
    {
        //Arrange
        Eval("listbox .l");
        Eval(".l insert end a b");

        //Act (wish: "insert end" appends; for insert, end means the item count)
        Eval(".l insert end c");

        //Assert
        Eval(".l get 0 end").Should().Be("a b c");
        ((ListboxWidget)_root.FindDescendant(".l").Widget).Items.Count.Should().Be(3);
    }

    [Fact]
    public void Listbox_delete_shifts_the_remaining_selection_down()
    {
        //Arrange
        Eval("listbox .l -selectmode extended");
        Eval(".l insert end 0 1 2 3");
        Eval(".l selection set 1");
        Eval(".l selection set 3");

        //Act (wish: sel 1,3 delete 0: {0 2})
        Eval(".l delete 0");

        //Assert
        Eval(".l curselection").Should().Be("0 2");

        //wish: sel 2 delete 2: {} (a deleted item's selection goes with it)
        Eval(".l selection clear 0 end");
        Eval(".l selection set 2");
        Eval(".l delete 2");
        Eval(".l curselection").Should().Be("");
    }

    [Fact]
    public void Listbox_insert_shifts_the_selection_after_the_insertion_point()
    {
        //Arrange
        Eval("listbox .l -selectmode extended");
        Eval(".l insert end a b c");
        Eval(".l selection set 1");

        //Act (wish: selection set 1; insert 0 new -> insert shift: cur={2})
        Eval(".l insert 0 new");

        //Assert
        Eval(".l curselection").Should().Be("2");
    }

    // ------------------------------------------------------------ treeview

    private void BuildTree(int height)
    {
        Eval("frame .f -width 400 -height " + height.ToString(CultureInfo.InvariantCulture));
        Eval("pack propagate .f 0");
        Eval("pack .f");
        Eval("ttk::treeview .f.t -columns {a b}");
        Eval("pack .f.t -fill both -expand 1");
        Eval(".f.t heading #0 -text Tree");
        Eval(".f.t heading a -text A");
        Eval(".f.t heading b -text B");
        Eval("foreach i {1 2 3 4 5 6 7 8} { .f.t insert {} end -id r$i -text \"Row $i\" -values [list a$i b$i] }");
        Eval(".f.t insert r1 end -id c1 -text child");
        Eval("update");
    }

    [Fact]
    public void Treeview_bbox_of_an_item_is_its_row_rectangle()
    {
        //Arrange
        BuildTree(400);

        //Act
        int[] r1 = Box(Eval(".f.t bbox r1"));
        int[] r2 = Box(Eval(".f.t bbox r2"));
        int[] r4 = Box(Eval(".f.t bbox r4"));

        //Assert (wish: bbox r1 = {1 22 398 20}, r2 = {1 42 398 20},
        //r4 = {1 82 398 20}: rows stack by the row height below the heading,
        //every row spans the full column width)
        r1[0].Should().Be(r2[0]);
        r1[2].Should().Be(r2[2]);
        r1[3].Should().BeGreaterThan(0);
        r2[1].Should().Be(r1[1] + r1[3]);
        r4[1].Should().Be(r1[1] + 3 * r1[3]);
    }

    [Fact]
    public void Treeview_bbox_with_a_column_is_the_cell_rectangle()
    {
        //Arrange
        BuildTree(400);
        int[] row = Box(Eval(".f.t bbox r2"));

        //Act
        int[] tree = Box(Eval(".f.t bbox r2 #0"));
        int[] first = Box(Eval(".f.t bbox r2 #1"));
        int[] second = Box(Eval(".f.t bbox r2 #2"));

        //Assert (wish: #0 = {1 42 166 20}, #1 = {167 42 126 20},
        //#2 = {293 42 106 20}; a = #1, b = #2: cells tile the row)
        tree[0].Should().Be(row[0]);
        first[0].Should().Be(tree[0] + tree[2]);
        second[0].Should().Be(first[0] + first[2]);
        (tree[2] + first[2] + second[2]).Should().Be(row[2]);
        tree[1].Should().Be(row[1]);
        tree[3].Should().Be(row[3]);
        Eval(".f.t bbox r2 a").Should().Be(Eval(".f.t bbox r2 #1"));
        Eval(".f.t bbox r2 b").Should().Be(Eval(".f.t bbox r2 #2"));
    }

    [Fact]
    public void Treeview_bbox_is_empty_for_items_that_are_not_visible()
    {
        //Arrange
        BuildTree(400);

        //Act + Assert (wish: bbox c1 = {} while r1 is closed; after
        //.t item r1 -open 1: bbox c1 = {1 42 398 20}, bbox r2 = {1 62 398 20})
        Eval(".f.t bbox c1").Should().Be("");
        int[] r2Closed = Box(Eval(".f.t bbox r2"));
        Eval(".f.t item r1 -open 1");
        int[] c1 = Box(Eval(".f.t bbox c1"));
        int[] r2Open = Box(Eval(".f.t bbox r2"));
        c1[1].Should().Be(r2Closed[1]);
        r2Open[1].Should().Be(r2Closed[1] + r2Closed[3]);
    }

    [Fact]
    public void Treeview_bbox_follows_scrolling_and_counts_partially_visible_rows()
    {
        //Arrange — room for the heading plus three and a half rows
        //  (wish: a 95px-high treeview, rows at y 22/42/62/82, r4 cut off).
        BuildTree(400);
        int[] first = Box(Eval(".f.t bbox r1"));
        int rowHeight = first[3];
        int headingBottom = first[1];
        int height = headingBottom + 3 * rowHeight + rowHeight / 2 + (first[0]);
        Eval(".f configure -height " + height.ToString(CultureInfo.InvariantCulture));
        Eval("update");

        //Act + Assert (wish: r1 = {1 22 ..}, r4 = {1 82 ..}, r5 = {})
        Eval(".f.t bbox r4").Should().NotBe("");
        Eval(".f.t bbox r5").Should().Be("");

        //wish: after scroll 2 units: r1 = {}, r2 = {}, r3 = {1 22 ..},
        //r6 = {1 82 ..}, r7 = {}
        Eval(".f.t yview scroll 2 units");
        Eval(".f.t bbox r1").Should().Be("");
        Eval(".f.t bbox r2").Should().Be("");
        Box(Eval(".f.t bbox r3"))[1].Should().Be(headingBottom);
        Box(Eval(".f.t bbox r6"))[1].Should().Be(headingBottom + 3 * rowHeight);
        Eval(".f.t bbox r7").Should().Be("");
    }

    [Fact]
    public void Treeview_bbox_errors_match_tk()
    {
        //Arrange
        BuildTree(400);
        string message;

        //Act + Assert (wish: bbox r2 #3: Column #3 out of range; bbox r2 zz:
        //Invalid column index zz; bbox nosuch: Item nosuch not found;
        //noargs / toomany: wrong # args: should be "PATH bbox itemid ?column")
        TryEval(".f.t bbox r2 #3", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("Column #3 out of range");
        TryEval(".f.t bbox r2 zz", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("Invalid column index zz");
        TryEval(".f.t bbox nosuch", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("Item nosuch not found");
        TryEval(".f.t bbox", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("wrong # args: should be \".f.t bbox itemid ?column\"");
        TryEval(".f.t bbox r1 a b", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("wrong # args: should be \".f.t bbox itemid ?column\"");
    }
}

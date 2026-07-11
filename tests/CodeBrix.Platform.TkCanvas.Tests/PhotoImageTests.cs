using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Images;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// B.10b coverage: the photo-image pixel model. Every behavior asserted here
/// was probed against real wish 8.6.16 first (blank transparency, put/copy
/// expansion, region tiling, -from defaulting, zoom/subsample shapes, the
/// user-size pin rules, and the exact DRAKON export_png copy shape); the
/// expected strings are the wish outputs verbatim.
/// </summary>
public class PhotoImageTests
{
    private static ImageManager Manager()
    {
        TkWindow root = TkWindow.CreateRoot();
        root.SetForcedSize(300, 200);
        return root.Tree.Images;
    }

    private static Dictionary<string, string> Opts(params string[] pairs)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < pairs.Length; i += 2) { d[pairs[i]] = pairs[i + 1]; }
        return d;
    }

    private static PhotoImage GraySource(ImageManager manager)
    {
        // The 3x2 gray ramp used by the wish copy probes.
        PhotoImage src = manager.CreatePhoto("src", null);
        src.Execute(new[] { "put", "{#010101 #020202 #030303} {#040404 #050505 #060606}" });
        return src;
    }

    // ------------------------------------------------------------------
    // Blank photos
    // ------------------------------------------------------------------

    [Fact]
    public void Blank_photo_is_sized_and_transparent_black()
    {
        PhotoImage blank = Manager().CreatePhoto("b", Opts("-width", "4", "-height", "3"));

        blank.Width.Should().Be(4);
        blank.Height.Should().Be(3);
        blank.GetPixelText(0, 0).Should().Be("0 0 0");
        blank.IsTransparent(0, 0).Should().BeTrue();
        blank.Data().Should().Be(
                "{#000000 #000000 #000000 #000000} {#000000 #000000 #000000 #000000}"
                + " {#000000 #000000 #000000 #000000}");
    }

    [Fact]
    public void Blank_subcommand_keeps_size_and_clears_to_transparent()
    {
        ImageManager manager = Manager();
        PhotoImage img = manager.CreatePhoto(null, null);
        img.Execute(new[] { "put", "red", "-to", "0", "0", "5", "4" });

        img.Execute(new[] { "blank" });

        img.Width.Should().Be(5);
        img.Height.Should().Be(4);
        img.GetPixelText(1, 1).Should().Be("0 0 0");
        img.IsTransparent(1, 1).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // put
    // ------------------------------------------------------------------

    [Fact]
    public void Put_color_rows_sizes_image_and_stores_pixels()
    {
        PhotoImage img = Manager().CreatePhoto(null, null);

        img.Execute(new[] { "put", "{red green} {blue #ffff00}" });

        img.Width.Should().Be(2);
        img.Height.Should().Be(2);
        img.GetPixelText(0, 0).Should().Be("255 0 0");
        img.GetPixelText(1, 0).Should().Be("0 128 0");
        img.GetPixelText(0, 1).Should().Be("0 0 255");
        img.GetPixelText(1, 1).Should().Be("255 255 0");
        img.Data().Should().Be("{#ff0000 #008000} {#0000ff #ffff00}");
    }

    [Fact]
    public void Put_single_color_with_region_fills_and_expands()
    {
        PhotoImage img = Manager().CreatePhoto(null, null);
        img.Execute(new[] { "put", "{red green} {blue #ffff00}" });

        img.Execute(new[] { "put", "red", "-to", "5", "5", "8", "7" });

        img.Width.Should().Be(8);
        img.Height.Should().Be(7);
        img.GetPixelText(5, 5).Should().Be("255 0 0");
        img.GetPixelText(7, 6).Should().Be("255 0 0");
        // The gap between the old content and the filled region stays
        // transparent black.
        img.IsTransparent(3, 3).Should().BeTrue();
        img.GetPixelText(3, 3).Should().Be("0 0 0");
    }

    [Fact]
    public void Put_ragged_rows_error_matches_tk()
    {
        PhotoImage img = Manager().CreatePhoto(null, null);

        Action act = () => img.Execute(new[] { "put", "{red} {green blue}" });

        act.Should().Throw<InvalidOperationException>()
                .WithMessage("all elements of color list must have the same number of elements");
    }

    [Fact]
    public void Put_without_data_error_matches_tk()
    {
        PhotoImage img = Manager().CreatePhoto("img4", null);

        Action act = () => img.Execute(new[] { "put" });

        act.Should().Throw<InvalidOperationException>()
                .WithMessage("wrong # args: should be \"img4 put data ?-option value ...?\"");
    }

    [Fact]
    public void Put_beyond_pinned_size_clips()
    {
        PhotoImage img = Manager().CreatePhoto(null, Opts("-width", "4", "-height", "3"));

        img.Execute(new[] { "put", "red", "-to", "0", "0", "8", "6" });

        img.Width.Should().Be(4);
        img.Height.Should().Be(3);
        img.GetPixelText(3, 2).Should().Be("255 0 0");
    }

    // ------------------------------------------------------------------
    // copy (all shapes probed in wish)
    // ------------------------------------------------------------------

    [Fact]
    public void Copy_whole_source_expands_empty_destination()
    {
        ImageManager manager = Manager();
        GraySource(manager);
        PhotoImage dst = manager.CreatePhoto("dst", null);

        dst.Execute(new[] { "copy", "src" });

        dst.Width.Should().Be(3);
        dst.Height.Should().Be(2);
        dst.GetPixelText(2, 1).Should().Be("6 6 6");
    }

    [Fact]
    public void Copy_from_with_only_corner_runs_to_source_bottom_right()
    {
        ImageManager manager = Manager();
        GraySource(manager);
        PhotoImage dst = manager.CreatePhoto("dst", null);

        dst.Execute(new[] { "copy", "src", "-from", "1", "0" });

        dst.Width.Should().Be(2);
        dst.Height.Should().Be(2);
        dst.GetPixelText(0, 0).Should().Be("2 2 2");
    }

    [Fact]
    public void Copy_to_point_inside_larger_destination_keeps_size()
    {
        ImageManager manager = Manager();
        GraySource(manager);
        PhotoImage dst = manager.CreatePhoto("dst", Opts("-width", "10", "-height", "10"));

        dst.Execute(new[] { "copy", "src", "-to", "4", "4" });

        dst.Width.Should().Be(10);
        dst.Height.Should().Be(10);
        dst.GetPixelText(4, 4).Should().Be("1 1 1");
        dst.GetPixelText(6, 5).Should().Be("6 6 6");
    }

    [Fact]
    public void Copy_to_region_tiles_the_source()
    {
        ImageManager manager = Manager();
        GraySource(manager);
        PhotoImage dst = manager.CreatePhoto("dst", null);

        dst.Execute(new[] { "copy", "src", "-to", "0", "0", "6", "4" });

        dst.Width.Should().Be(6);
        dst.Height.Should().Be(4);
        // (4,1) = source (1,1); (3,2) = source (0,0) — the tiling wraps.
        dst.GetPixelText(4, 1).Should().Be("5 5 5");
        dst.GetPixelText(3, 2).Should().Be("1 1 1");
    }

    [Fact]
    public void Copy_shrink_cannot_shrink_a_pinned_destination()
    {
        ImageManager manager = Manager();
        GraySource(manager);
        PhotoImage dst = manager.CreatePhoto("dst", Opts("-width", "10", "-height", "10"));

        dst.Execute(new[] { "copy", "src", "-shrink" });

        dst.Width.Should().Be(10);
        dst.Height.Should().Be(10);
    }

    [Fact]
    public void Copy_zoom_repeats_pixels()
    {
        ImageManager manager = Manager();
        GraySource(manager);
        PhotoImage dst = manager.CreatePhoto("dst", null);

        dst.Execute(new[] { "copy", "src", "-zoom", "2" });

        dst.Width.Should().Be(6);
        dst.Height.Should().Be(4);
        dst.GetPixelText(1, 1).Should().Be("1 1 1");
        dst.GetPixelText(5, 3).Should().Be("6 6 6");
    }

    [Fact]
    public void Copy_subsample_skips_pixels()
    {
        ImageManager manager = Manager();
        GraySource(manager);
        PhotoImage dst = manager.CreatePhoto("dst", null);

        dst.Execute(new[] { "copy", "src", "-subsample", "2", "1" });

        dst.Width.Should().Be(2);
        dst.Height.Should().Be(2);
        dst.GetPixelText(1, 0).Should().Be("3 3 3");
    }

    [Fact]
    public void Copy_drakon_export_shape_composites_at_offset()
    {
        // export_png.tcl: canvas_all copy canvas_piece -to $dx $dy -from 5 5
        // — copies the piece's (5,5)..bottom-right into the pinned big photo.
        ImageManager manager = Manager();
        PhotoImage piece = manager.CreatePhoto("piece", null);
        var rows = new List<string>();
        for (int y = 0; y < 7; y++)
        {
            var row = new List<string>();
            for (int x = 0; x < 8; x++)
            {
                int v = y * 16 + 10 + x;
                row.Add("#" + v.ToString("x2") + v.ToString("x2") + v.ToString("x2"));
            }
            rows.Add("{" + string.Join(" ", row) + "}");
        }
        piece.Execute(new[] { "put", string.Join(" ", rows) });

        PhotoImage all = manager.CreatePhoto("all", Opts("-width", "12", "-height", "9"));
        all.Execute(new[] { "copy", "piece", "-to", "2", "3", "-from", "5", "5" });

        all.Width.Should().Be(12);
        all.Height.Should().Be(9);
        all.GetPixelText(2, 3).Should().Be("95 95 95");
        all.GetPixelText(4, 4).Should().Be("113 113 113");
        all.IsTransparent(5, 5).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // The user-size pin (configure -width/-height)
    // ------------------------------------------------------------------

    [Fact]
    public void Configure_width_pins_immediately_and_release_keeps_size()
    {
        PhotoImage img = Manager().CreatePhoto(null, null);
        img.Execute(new[] { "put", "red", "-to", "0", "0", "6", "5" });

        img.Execute(new[] { "configure", "-width", "2" });
        img.Width.Should().Be(2);
        img.Height.Should().Be(5);

        img.Execute(new[] { "configure", "-width", "0" });
        img.Width.Should().Be(2);
        img.Height.Should().Be(5);
    }

    // ------------------------------------------------------------------
    // Errors and the verb surface
    // ------------------------------------------------------------------

    [Fact]
    public void Get_out_of_range_error_matches_tk()
    {
        PhotoImage img = Manager().CreatePhoto("image3", Opts("-width", "4", "-height", "3"));

        Action act = () => img.Execute(new[] { "get", "100", "100" });

        act.Should().Throw<InvalidOperationException>()
                .WithMessage("image3 get: coordinates out of range");
    }

    [Fact]
    public void Bad_verb_error_matches_tk()
    {
        PhotoImage img = Manager().CreatePhoto(null, null);

        Action act = () => img.Execute(new[] { "badverb" });

        act.Should().Throw<InvalidOperationException>()
                .WithMessage("bad option \"badverb\": must be blank, cget, configure, copy, "
                        + "data, get, put, read, redither, transparency, or write");
    }

    [Fact]
    public void Transparency_set_changes_alpha_only()
    {
        PhotoImage img = Manager().CreatePhoto(null, null);
        img.Execute(new[] { "put", "red" });

        img.Execute(new[] { "transparency", "get", "0", "0" }).Should().Be("0");
        img.Execute(new[] { "transparency", "set", "0", "0", "1" });

        img.Execute(new[] { "transparency", "get", "0", "0" }).Should().Be("1");
        img.GetPixelText(0, 0).Should().Be("255 0 0");
    }

    [Fact]
    public void Redither_and_unknown_copy_options_accept_and_no_op()
    {
        ImageManager manager = Manager();
        GraySource(manager);
        PhotoImage dst = manager.CreatePhoto("dst", null);

        dst.Execute(new[] { "redither" }).Should().Be("");
        dst.Execute(new[] { "copy", "src", "-compositingrule", "set" }).Should().Be("");
        dst.Width.Should().Be(3);
    }
}

using System;
using System.Collections.Generic;
using System.IO;

using CodeBrix.Platform.TkCanvas.Images;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// B.10b coverage: the image registry (<c>image create/delete/names/...</c>),
/// GIF decoding through CodeBrix.Imaging validated BYTE-IDENTICAL against
/// real wish 8.6.16 (<c>$img data</c> captured as fixtures from genuine
/// DRAKON Editor gifs — the plan's de-risk item #3), base64 <c>-data</c>
/// creation, the PNG write/read round-trip, and the <c>-format window</c>
/// widget snapshot through the render pass.
/// </summary>
public class ImageManagerTests
{
    private static string AssetPath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Images", name);
    }

    private static TkWindow Root()
    {
        TkWindow root = TkWindow.CreateRoot();
        root.SetForcedSize(300, 200);
        return root;
    }

    private static Dictionary<string, string> Opts(params string[] pairs)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < pairs.Length; i += 2) { d[pairs[i]] = pairs[i + 1]; }
        return d;
    }

    // ------------------------------------------------------------------
    // Registry semantics
    // ------------------------------------------------------------------

    [Fact]
    public void Create_auto_names_sequentially()
    {
        ImageManager images = Root().Tree.Images;

        string first = images.Execute(new[] { "create", "photo" });
        string second = images.Execute(new[] { "create", "photo" });

        first.Should().Be("image1");
        second.Should().Be("image2");
    }

    [Fact]
    public void Create_with_name_returns_it_and_names_lists_sorted()
    {
        ImageManager images = Root().Tree.Images;
        images.Execute(new[] { "create", "photo", "zed" });
        images.Execute(new[] { "create", "photo", "alpha" });

        images.Execute(new[] { "names" }).Should().Be("alpha zed");
    }

    [Fact]
    public void Delete_removes_and_use_after_lookup_fails()
    {
        ImageManager images = Root().Tree.Images;
        images.Execute(new[] { "create", "photo", "gone" });

        images.Execute(new[] { "delete", "gone" });

        images.Find("gone").Should().BeNull();
        Action act = () => images.Execute(new[] { "width", "gone" });
        act.Should().Throw<InvalidOperationException>()
                .WithMessage("image \"gone\" doesn't exist");
    }

    [Fact]
    public void Bad_type_and_type_queries_match_tk()
    {
        ImageManager images = Root().Tree.Images;
        images.Execute(new[] { "create", "photo", "p" });

        images.Execute(new[] { "type", "p" }).Should().Be("photo");
        images.Execute(new[] { "types" }).Should().Be("photo");

        Action act = () => images.Execute(new[] { "create", "badtype" });
        act.Should().Throw<InvalidOperationException>()
                .WithMessage("image type \"badtype\" doesn't exist");
    }

    // ------------------------------------------------------------------
    // GIF decode — byte-identical to real wish (de-risk #3)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("back.gif", 16, 16)]
    [InlineData("action.gif", 45, 34)]
    public void Gif_decode_is_byte_identical_to_wish(string gifName, int width, int height)
    {
        ImageManager images = Root().Tree.Images;
        PhotoImage img = images.CreatePhoto(null,
                Opts("-format", "GIF", "-file", AssetPath(gifName)));

        img.Width.Should().Be(width);
        img.Height.Should().Be(height);

        string expected = File.ReadAllText(AssetPath(gifName + ".data.txt")).TrimEnd('\n');
        img.Data().Should().Be(expected);
    }

    [Fact]
    public void Drakon_gifs_decode_fully_opaque()
    {
        ImageManager images = Root().Tree.Images;
        PhotoImage img = images.CreatePhoto(null, Opts("-file", AssetPath("back.gif")));

        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                img.IsTransparent(x, y).Should().BeFalse();
            }
        }
    }

    [Fact]
    public void Undecodable_file_error_matches_tk()
    {
        ImageManager images = Root().Tree.Images;
        string bogus = Path.Combine(Path.GetTempPath(), "tkcanvas_bogus_image.bin");
        File.WriteAllText(bogus, "this is not an image");
        try
        {
            Action act = () => images.CreatePhoto(null, Opts("-file", bogus));
            act.Should().Throw<InvalidOperationException>()
                    .WithMessage("couldn't recognize data in image file \"" + bogus + "\"");
        }
        finally
        {
            File.Delete(bogus);
        }
    }

    // ------------------------------------------------------------------
    // Base64 -data create + write round-trip (probed in wish)
    // ------------------------------------------------------------------

    [Fact]
    public void Base64_data_create_decodes_like_file()
    {
        ImageManager images = Root().Tree.Images;
        string b64 = Convert.ToBase64String(File.ReadAllBytes(AssetPath("back.gif")));

        PhotoImage img = images.CreatePhoto(null, Opts("-data", b64));

        img.Width.Should().Be(16);
        img.Height.Should().Be(16);
        img.GetPixelText(8, 8).Should().Be("0 0 0");
    }

    [Fact]
    public void Png_write_and_read_back_round_trips_identically()
    {
        ImageManager images = Root().Tree.Images;
        PhotoImage original = images.CreatePhoto(null, Opts("-file", AssetPath("back.gif")));
        string tempPng = Path.Combine(Path.GetTempPath(), "tkcanvas_write_test.png");
        try
        {
            original.Execute(new[] { "write", tempPng, "-format", "png" });

            PhotoImage readBack = images.CreatePhoto(null, Opts("-file", tempPng));
            readBack.Width.Should().Be(16);
            readBack.Height.Should().Be(16);
            readBack.Data().Should().Be(original.Data());
        }
        finally
        {
            File.Delete(tempPng);
        }
    }

    [Fact]
    public void Gif_write_and_read_back_round_trips_identically()
    {
        // action.gif has 16 distinct colors, so the exact-palette GIF path
        // is lossless. (back.gif uses a FULL 256-color palette; the encoder
        // reserves a transparency slot there, so a 256-color image is the
        // one documented lossy edge — real Tk keeps all 256.)
        ImageManager images = Root().Tree.Images;
        PhotoImage original = images.CreatePhoto(null, Opts("-file", AssetPath("action.gif")));
        string tempGif = Path.Combine(Path.GetTempPath(), "tkcanvas_write_test.gif");
        try
        {
            original.Execute(new[] { "write", tempGif, "-format", "gif" });

            PhotoImage readBack = images.CreatePhoto(null, Opts("-file", tempGif));
            readBack.Data().Should().Be(original.Data());
        }
        finally
        {
            File.Delete(tempGif);
        }
    }

    // ------------------------------------------------------------------
    // The -format window widget snapshot (DRAKON export_png path)
    // ------------------------------------------------------------------

    [Fact]
    public void Format_window_snapshot_captures_widget_pixels()
    {
        TkWindow root = Root();
        TkWindow frameWindow = root.CreateChild("f");
        var frame = new FrameWidget(frameWindow);
        frame.Configure(Opts("-background", "red", "-width", "60", "-height", "40"));
        PackLayout.Configure(frameWindow, new PackOptions());
        root.Tree.Scheduler.UpdateIdleTasks();

        PhotoImage snap = root.Tree.Images.CreatePhoto("snap",
                Opts("-format", "window", "-data", frameWindow.PathName));

        snap.Width.Should().Be(frameWindow.Width);
        snap.Height.Should().Be(frameWindow.Height);
        snap.GetPixelText(snap.Width / 2, snap.Height / 2).Should().Be("255 0 0");
        snap.IsTransparent(0, 0).Should().BeFalse();
    }

    [Fact]
    public void Format_window_snapshot_of_unknown_path_yields_empty_photo()
    {
        TkWindow root = Root();

        PhotoImage snap = root.Tree.Images.CreatePhoto("snap",
                Opts("-format", "window", "-data", ".nosuch"));

        snap.Width.Should().Be(0);
        snap.Height.Should().Be(0);
    }
}

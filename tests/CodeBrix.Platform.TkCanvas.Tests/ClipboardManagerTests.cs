using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Clipboard;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// B.10b coverage: the toolkit-wide <c>clipboard</c> command model —
/// clear/append/get with DRAKON's exact call shapes
/// (<c>append -type STRING -format UTF8_STRING -- $text</c>,
/// <c>get -type STRING</c>), the empty-clipboard error, and the
/// <see cref="ITkClipboard"/> host seam.
/// </summary>
public class ClipboardManagerTests
{
    private sealed class FakeHostClipboard : ITkClipboard
    {
        public string Stored;

        public void SetText(string text)
        {
            Stored = text;
        }

        public string GetText()
        {
            return Stored;
        }
    }

    [Fact]
    public void Clear_append_get_round_trips_in_process()
    {
        var clipboard = new ClipboardManager();

        clipboard.Execute(new[] { "clear" });
        clipboard.Execute(new[] { "append", "hello " });
        clipboard.Execute(new[] { "append", "world" });

        clipboard.Execute(new[] { "get" }).Should().Be("hello world");
    }

    [Fact]
    public void Drakon_append_shape_with_options_and_separator()
    {
        var clipboard = new ClipboardManager();

        clipboard.Execute(new[] { "clear" });
        clipboard.Execute(new[]
        {
            "append", "-type", "STRING", "-format", "UTF8_STRING", "--", "-payload-",
        });

        clipboard.Execute(new[] { "get", "-type", "STRING" }).Should().Be("-payload-");
    }

    [Fact]
    public void Get_without_content_errors_like_tk()
    {
        var clipboard = new ClipboardManager();

        Action act = () => clipboard.Execute(new[] { "get" });

        act.Should().Throw<InvalidOperationException>()
                .WithMessage("CLIPBOARD selection doesn't exist or form \"STRING\" not defined");
    }

    [Fact]
    public void Clear_then_get_returns_empty_content()
    {
        var clipboard = new ClipboardManager();

        clipboard.Execute(new[] { "clear" });

        clipboard.Execute(new[] { "get" }).Should().Be("");
    }

    [Fact]
    public void Append_publishes_accumulated_content_through_the_host_seam()
    {
        var host = new FakeHostClipboard();
        var clipboard = new ClipboardManager { Host = host };

        clipboard.Clear();
        clipboard.Append("one ");
        clipboard.Append("two");

        host.Stored.Should().Be("one two");
    }

    [Fact]
    public void Get_prefers_the_host_clipboard_content()
    {
        var host = new FakeHostClipboard { Stored = "external copy" };
        var clipboard = new ClipboardManager { Host = host };

        clipboard.Get().Should().Be("external copy");
    }

    [Fact]
    public void Get_with_empty_host_clipboard_errors_like_tk()
    {
        var host = new FakeHostClipboard { Stored = null };
        var clipboard = new ClipboardManager { Host = host };

        Action act = () => clipboard.Get();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Bad_verb_error_matches_tk()
    {
        var clipboard = new ClipboardManager();

        Action act = () => clipboard.Execute(new[] { "badverb" });

        act.Should().Throw<InvalidOperationException>()
                .WithMessage("bad option \"badverb\": must be append, clear, or get");
    }

    [Fact]
    public void Window_tree_exposes_one_lazy_clipboard()
    {
        TkWindow root = TkWindow.CreateRoot();

        ClipboardManager first = root.Tree.Clipboard;
        ClipboardManager second = root.Tree.Clipboard;

        first.Should().BeSameAs(second);
    }
}

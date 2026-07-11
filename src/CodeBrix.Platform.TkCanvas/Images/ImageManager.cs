using System;
using System.Collections.Generic;
using System.IO;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Images;

/// <summary>
/// The tree's image registry — the model behind the Tk <c>image</c> command
/// (<c>image create photo</c>, <c>delete</c>, <c>names</c>, sizes) and the
/// name resolver every <c>-image</c> option goes through. Reached lazily via
/// <see cref="WindowTree.Images"/>. Beyond files and base64 data, creation
/// supports Tk's <c>-format window -data $widget</c> snapshot form by
/// rendering the named window's subtree through <see cref="TkRenderer"/> —
/// the path DRAKON's PNG export uses.
/// </summary>
public sealed class ImageManager
{
    private readonly WindowTree _tree;
    private readonly Dictionary<string, PhotoImage> _images =
            new Dictionary<string, PhotoImage>(StringComparer.Ordinal);
    private int _nameCounter;

    internal ImageManager(WindowTree tree)
    {
        _tree = tree;
    }

    /// <summary>
    /// Raised after any image is created, mutated, or deleted, so displaying
    /// widgets can be repainted.
    /// </summary>
    public event Action ImagesChanged;

    /// <summary>Looks up a photo image by name.</summary>
    /// <param name="name">The image name.</param>
    /// <returns>The image, or null when no such image exists.</returns>
    public PhotoImage Find(string name)
    {
        PhotoImage image;
        return (!string.IsNullOrEmpty(name) && _images.TryGetValue(name, out image))
                ? image : null;
    }

    /// <summary>The registered image names, sorted (<c>image names</c> order is unspecified; we sort for determinism).</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            var names = new List<string>(_images.Keys);
            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }

    /// <summary>
    /// Creates (or, Tk-style, re-configures) a photo image — the
    /// <c>image create photo</c> model. A null/empty name auto-generates
    /// <c>image1</c>, <c>image2</c>, ...
    /// </summary>
    /// <param name="name">The image name, or null to auto-name.</param>
    /// <param name="options">The creation options (accept-and-store).</param>
    /// <returns>The created image.</returns>
    public PhotoImage CreatePhoto(string name, IReadOnlyDictionary<string, string> options)
    {
        if (string.IsNullOrEmpty(name))
        {
            do
            {
                _nameCounter++;
                name = "image" + _nameCounter;
            }
            while (_images.ContainsKey(name));
        }

        PhotoImage image = Find(name);
        if (image == null)
        {
            image = new PhotoImage(this, name);
            _images[name] = image;
        }

        if (options != null)
        {
            foreach (KeyValuePair<string, string> option in options)
            {
                image.Options.Set(option.Key, option.Value);
            }
        }

        // Tk's snapshot form: -format window -data $widgetPath. The pixels
        // come from our own render pass instead of the X server.
        if (string.Equals(image.Options.Get("-format", ""), "window", StringComparison.OrdinalIgnoreCase))
        {
            SnapshotWindow(image, image.Options.Get("-data", ""));
        }
        else
        {
            image.ApplyConfiguredContent();
        }

        NotifyImagesChanged();
        return image;
    }

    /// <summary>
    /// Deletes an image — <c>image delete</c>. Displaying widgets fall back
    /// to their text (the name simply stops resolving).
    /// </summary>
    /// <param name="name">The image name.</param>
    public void Delete(string name)
    {
        PhotoImage image = Find(name);
        if (image == null)
        {
            throw new InvalidOperationException("image \"" + name + "\" doesn't exist");
        }
        image.ReleaseResources();
        _images.Remove(name);
        NotifyImagesChanged();
    }

    /// <summary>
    /// Renders a window's subtree into the photo — the engine of the
    /// <c>-format window</c> snapshot. The window must be laid out (sized);
    /// an unknown path or a zero-size window yields an empty photo rather
    /// than an error (deferral discipline).
    /// </summary>
    /// <param name="image">The target photo.</param>
    /// <param name="pathName">The window's Tk path name.</param>
    public void SnapshotWindow(PhotoImage image, string pathName)
    {
        TkWindow window = _tree.Root.FindDescendant(pathName);
        if (window == null || window.Width < 1 || window.Height < 1)
        {
            image.SetUserSize(0, 0);
            return;
        }

        var info = new SKImageInfo(window.Width, window.Height,
                SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var surface = SKSurface.Create(info))
        {
            TkRenderer.RenderWindow(window, surface.Canvas);
            surface.Canvas.Flush();

            using (SKImage snapshot = surface.Snapshot())
            using (var bitmap = new SKBitmap(info))
            {
                if (snapshot.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0))
                {
                    var pixels = new byte[info.BytesSize];
                    System.Runtime.InteropServices.Marshal.Copy(
                            bitmap.GetPixels(), pixels, 0, pixels.Length);
                    image.LoadPixels(pixels, window.Width, window.Height);
                }
            }
        }
    }

    internal void NotifyImagesChanged()
    {
        Action handler = ImagesChanged;
        if (handler != null) { handler(); }
        if (!_tree.Root.IsDestroyed)
        {
            _tree.Scheduler.ScheduleRepaint();
        }
    }

    // ------------------------------------------------------------------
    // The image command surface (image verb ...), Tk argument shapes
    // ------------------------------------------------------------------

    /// <summary>
    /// Executes an <c>image</c> command with the Tcl argument shapes
    /// verbatim — <c>create delete names width height type types inuse</c> —
    /// returning what Tk returns.
    /// </summary>
    /// <param name="words">The subcommand and its arguments.</param>
    /// <returns>The Tcl result string.</returns>
    public string Execute(IReadOnlyList<string> words)
    {
        if (words == null || words.Count == 0)
        {
            throw new InvalidOperationException("wrong # args: should be \"image option ?args?\"");
        }

        switch (words[0])
        {
            case "create":
                return ExecuteCreate(words);
            case "delete":
                for (int i = 1; i < words.Count; i++) { Delete(words[i]); }
                return "";
            case "names":
                return TclString.JoinList(new List<string>(Names));
            case "width":
                return Require(words, 2).Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case "height":
                return Require(words, 2).Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case "type":
                return Require(words, 2).TypeName;
            case "types":
                return "photo";
            case "inuse":
                // Widget usage isn't tracked; report not-in-use (safe for the
                // introspection callers that exist).
                Require(words, 2);
                return "0";
            default:
                throw new InvalidOperationException("bad option \"" + words[0]
                        + "\": must be create, delete, height, inuse, names, type, types, or width");
        }
    }

    private PhotoImage Require(IReadOnlyList<string> words, int count)
    {
        if (words.Count < count)
        {
            throw new InvalidOperationException(
                    "wrong # args: should be \"image " + words[0] + " name\"");
        }
        PhotoImage image = Find(words[1]);
        if (image == null)
        {
            throw new InvalidOperationException("image \"" + words[1] + "\" doesn't exist");
        }
        return image;
    }

    private string ExecuteCreate(IReadOnlyList<string> words)
    {
        if (words.Count < 2)
        {
            throw new InvalidOperationException(
                    "wrong # args: should be \"image create type ?name? ?-option value ...?\"");
        }
        if (words[1] != "photo")
        {
            throw new InvalidOperationException("image type \"" + words[1] + "\" doesn't exist");
        }

        string name = null;
        int optionsStart = 2;
        if (words.Count > 2 && !words[2].StartsWith("-", StringComparison.Ordinal))
        {
            name = words[2];
            optionsStart = 3;
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = optionsStart; i + 1 < words.Count; i += 2)
        {
            options[words[i]] = words[i + 1];
        }

        return CreatePhoto(name, options).Name;
    }
}

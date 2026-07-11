using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using CodeBrix.Platform.TkCanvas.Canvas;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Images;

/// <summary>
/// A Tk <c>photo</c> image: a named, mutable, full-color pixel buffer with
/// alpha, the model behind <c>image create photo</c> (Tk 8.6.16
/// tkImgPhoto.c). Pixels are stored RGBA, row-major. The image grows
/// automatically when data is written beyond its edges — unless a user size
/// was configured (<c>-width</c>/<c>-height</c>), which pins that axis and
/// clips writes outside it, exactly as real Tk does. Decoding and encoding
/// (GIF in, PNG/GIF out) go through CodeBrix.Imaging; painting goes through
/// a cached Skia image. Instances are created and named by
/// <see cref="ImageManager"/>.
/// </summary>
public sealed class PhotoImage
{
    private byte[] _pixels = Array.Empty<byte>();
    private int _width;
    private int _height;
    private int _userWidth;
    private int _userHeight;
    private SKImage _cachedImage;
    private readonly ImageManager _manager;

    internal PhotoImage(ImageManager manager, string name)
    {
        _manager = manager;
        Name = name;
    }

    /// <summary>The registered image name (<c>image names</c>).</summary>
    public string Name { get; }

    /// <summary>The image type reported by <c>image type</c> (always <c>photo</c>).</summary>
    public string TypeName
    {
        get { return "photo"; }
    }

    /// <summary>The option bag echoed by <c>cget</c>/<c>configure</c> (accept-and-store).</summary>
    internal Widgets.WidgetOptions Options { get; } = new Widgets.WidgetOptions();

    /// <summary>The current pixel width.</summary>
    public int Width
    {
        get { return _width; }
    }

    /// <summary>The current pixel height.</summary>
    public int Height
    {
        get { return _height; }
    }

    /// <summary>
    /// Raised after any mutation of the pixel content or size, so displaying
    /// widgets can repaint.
    /// </summary>
    public event Action Changed;

    // ------------------------------------------------------------------
    // Size management (tkImgPhoto.c ImgPhotoSetSize essentials)
    // ------------------------------------------------------------------

    /// <summary>
    /// Applies a configured user size: a positive value pins that axis
    /// immediately (clipping existing content); zero releases the pin but
    /// keeps the current size.
    /// </summary>
    /// <param name="userWidth">The <c>-width</c> value (0 = automatic).</param>
    /// <param name="userHeight">The <c>-height</c> value (0 = automatic).</param>
    public void SetUserSize(int userWidth, int userHeight)
    {
        _userWidth = (userWidth > 0) ? userWidth : 0;
        _userHeight = (userHeight > 0) ? userHeight : 0;

        int newWidth = (_userWidth > 0) ? _userWidth : _width;
        int newHeight = (_userHeight > 0) ? _userHeight : _height;
        Resize(newWidth, newHeight);
    }

    /// <summary>
    /// Grows the buffer so the exclusive corner (<paramref name="x2"/>,
    /// <paramref name="y2"/>) is inside it, honoring pinned axes (which never
    /// grow). Existing pixels are preserved; new area is transparent.
    /// </summary>
    private void EnsureCovers(int x2, int y2)
    {
        int newWidth = (_userWidth > 0) ? _userWidth : Math.Max(_width, x2);
        int newHeight = (_userHeight > 0) ? _userHeight : Math.Max(_height, y2);
        Resize(newWidth, newHeight);
    }

    /// <summary>
    /// Sets the buffer to exactly the given size (used by <c>-shrink</c>),
    /// still honoring pinned axes. Surviving pixels are preserved.
    /// </summary>
    private void ResizeExact(int width, int height)
    {
        int newWidth = (_userWidth > 0) ? _userWidth : width;
        int newHeight = (_userHeight > 0) ? _userHeight : height;
        Resize(newWidth, newHeight);
    }

    private void Resize(int width, int height)
    {
        if (width < 0) { width = 0; }
        if (height < 0) { height = 0; }
        if (width == _width && height == _height) { return; }

        var pixels = new byte[checked(width * height * 4)];
        int copyWidth = Math.Min(width, _width);
        int copyHeight = Math.Min(height, _height);
        for (int y = 0; y < copyHeight; y++)
        {
            Array.Copy(_pixels, y * _width * 4, pixels, y * width * 4, copyWidth * 4);
        }

        _pixels = pixels;
        _width = width;
        _height = height;
    }

    // ------------------------------------------------------------------
    // Pixel access
    // ------------------------------------------------------------------

    /// <summary>Reads a pixel (no range check — callers validate).</summary>
    private void ReadPixel(int x, int y, out byte r, out byte g, out byte b, out byte a)
    {
        int offset = (y * _width + x) * 4;
        r = _pixels[offset];
        g = _pixels[offset + 1];
        b = _pixels[offset + 2];
        a = _pixels[offset + 3];
    }

    private void WritePixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        int offset = (y * _width + x) * 4;
        _pixels[offset] = r;
        _pixels[offset + 1] = g;
        _pixels[offset + 2] = b;
        _pixels[offset + 3] = a;
    }

    /// <summary>
    /// Whether the pixel at (<paramref name="x"/>, <paramref name="y"/>) is
    /// fully transparent — <c>transparency get</c>.
    /// </summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>True when the pixel's alpha is zero.</returns>
    public bool IsTransparent(int x, int y)
    {
        RequireInside(x, y, "transparency get");
        byte r, g, b, a;
        ReadPixel(x, y, out r, out g, out b, out a);
        return a == 0;
    }

    private void RequireInside(int x, int y, string verb)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height)
        {
            throw new InvalidOperationException(Name + " " + verb + ": coordinates out of range");
        }
    }

    // ------------------------------------------------------------------
    // Mutators: blank / put / copy
    // ------------------------------------------------------------------

    /// <summary>
    /// Clears every pixel to transparent, keeping the current size —
    /// the <c>blank</c> subcommand.
    /// </summary>
    public void Blank()
    {
        Array.Clear(_pixels, 0, _pixels.Length);
        NotifyChanged();
    }

    /// <summary>
    /// Writes a parsed color block into the region, tiling it — the
    /// <c>put</c> model (and the engine under <c>read</c>). The region is
    /// expanded to (or clipped against) the image per the pin rules; alpha
    /// is written as fully opaque.
    /// </summary>
    private void PlaceBlock(PixelBlock block, int x1, int y1, int x2, int y2, bool overlay)
    {
        if (block.Width <= 0 || block.Height <= 0) { return; }

        EnsureCovers(x2, y2);
        int right = Math.Min(x2, _width);
        int bottom = Math.Min(y2, _height);

        for (int y = Math.Max(y1, 0); y < bottom; y++)
        {
            int sy = (y - y1) % block.Height;
            for (int x = Math.Max(x1, 0); x < right; x++)
            {
                int sx = (x - x1) % block.Width;
                int offset = (sy * block.Width + sx) * 4;
                byte a = block.Pixels[offset + 3];
                if (overlay && a == 0) { continue; }
                if (overlay && a < 255)
                {
                    BlendPixel(x, y, block.Pixels[offset], block.Pixels[offset + 1],
                            block.Pixels[offset + 2], a);
                    continue;
                }
                WritePixel(x, y, block.Pixels[offset], block.Pixels[offset + 1],
                        block.Pixels[offset + 2], a);
            }
        }
        NotifyChanged();
    }

    private void BlendPixel(int x, int y, byte sr, byte sg, byte sb, byte sa)
    {
        byte dr, dg, db, da;
        ReadPixel(x, y, out dr, out dg, out db, out da);
        int outA = sa + da * (255 - sa) / 255;
        if (outA <= 0) { WritePixel(x, y, 0, 0, 0, 0); return; }
        int outR = (sr * sa + dr * da * (255 - sa) / 255) / outA;
        int outG = (sg * sa + dg * da * (255 - sa) / 255) / outA;
        int outB = (sb * sa + db * da * (255 - sa) / 255) / outA;
        WritePixel(x, y, (byte)outR, (byte)outG, (byte)outB, (byte)outA);
    }

    /// <summary>
    /// Copies pixels from another photo — the <c>copy</c> subcommand:
    /// <c>-from</c> region, <c>-to</c> point or tiled region,
    /// <c>-zoom</c>/<c>-subsample</c> scaling, <c>-shrink</c>, and the
    /// overlay/set compositing rules.
    /// </summary>
    /// <param name="source">The source photo.</param>
    /// <param name="options">The parsed copy options.</param>
    public void CopyFrom(PhotoImage source, PhotoCopyOptions options)
    {
        int fx1 = options.HasFrom ? options.FromX1 : 0;
        int fy1 = options.HasFrom ? options.FromY1 : 0;
        int fx2 = (options.HasFrom && options.HasFromCorner) ? options.FromX2 : source._width;
        int fy2 = (options.HasFrom && options.HasFromCorner) ? options.FromY2 : source._height;
        if (fx1 > fx2) { int t = fx1; fx1 = fx2; fx2 = t; }
        if (fy1 > fy2) { int t = fy1; fy1 = fy2; fy2 = t; }
        fx1 = Math.Max(0, Math.Min(fx1, source._width));
        fx2 = Math.Max(0, Math.Min(fx2, source._width));
        fy1 = Math.Max(0, Math.Min(fy1, source._height));
        fy2 = Math.Max(0, Math.Min(fy2, source._height));

        int subX = Math.Max(1, Math.Abs(options.SubsampleX));
        int subY = Math.Max(1, Math.Abs(options.SubsampleY));
        int zoomX = Math.Max(1, options.ZoomX);
        int zoomY = Math.Max(1, options.ZoomY);

        int blockWidth = ((fx2 - fx1) + subX - 1) / subX * zoomX;
        int blockHeight = ((fy2 - fy1) + subY - 1) / subY * zoomY;

        int tx1 = options.HasTo ? options.ToX1 : 0;
        int ty1 = options.HasTo ? options.ToY1 : 0;
        int tx2 = (options.HasTo && options.HasToCorner) ? options.ToX2 : tx1 + blockWidth;
        int ty2 = (options.HasTo && options.HasToCorner) ? options.ToY2 : ty1 + blockHeight;

        if (blockWidth <= 0 || blockHeight <= 0)
        {
            if (options.Shrink) { ResizeExact(tx2, ty2); NotifyChanged(); }
            return;
        }

        // Materialize the (subsampled, zoomed) source block first: source and
        // destination may be the same image.
        var block = new PixelBlock(blockWidth, blockHeight);
        for (int by = 0; by < blockHeight; by++)
        {
            int sy = fy1 + (by / zoomY) * subY;
            if (sy >= fy2) { sy = fy2 - 1; }
            for (int bx = 0; bx < blockWidth; bx++)
            {
                int sx = fx1 + (bx / zoomX) * subX;
                if (sx >= fx2) { sx = fx2 - 1; }
                byte r, g, b, a;
                source.ReadPixel(sx, sy, out r, out g, out b, out a);
                int offset = (by * blockWidth + bx) * 4;
                block.Pixels[offset] = r;
                block.Pixels[offset + 1] = g;
                block.Pixels[offset + 2] = b;
                block.Pixels[offset + 3] = a;
            }
        }

        if (options.Shrink) { ResizeExact(tx2, ty2); }
        PlaceBlock(block, tx1, ty1, tx2, ty2, !options.RuleSet);
    }

    // ------------------------------------------------------------------
    // Decode / encode (CodeBrix.Imaging)
    // ------------------------------------------------------------------

    /// <summary>
    /// Replaces the content by decoding an encoded image (GIF, PNG, ...)
    /// via CodeBrix.Imaging — the create-time <c>-file</c>/<c>-data</c> path.
    /// </summary>
    /// <param name="encoded">The encoded image bytes.</param>
    /// <param name="sourceLabel">What to blame in the Tk-style error message.</param>
    public void LoadEncoded(byte[] encoded, string sourceLabel)
    {
        CodeBrix.Imaging.Image<CodeBrix.Imaging.PixelFormats.Rgba32> decoded;
        try
        {
            decoded = CodeBrix.Imaging.Image.Load<CodeBrix.Imaging.PixelFormats.Rgba32>(encoded);
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                    "couldn't recognize data in image file \"" + sourceLabel + "\"");
        }

        using (decoded)
        {
            var pixels = new byte[decoded.Width * decoded.Height * 4];
            decoded.CopyPixelDataTo(pixels);
            LoadPixels(pixels, decoded.Width, decoded.Height);
        }
    }

    /// <summary>
    /// Replaces the content with raw RGBA pixels (row-major) — the widget
    /// snapshot path (<c>-format window</c>) and the decode path.
    /// </summary>
    /// <param name="rgba">The pixel bytes (4 per pixel).</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public void LoadPixels(byte[] rgba, int width, int height)
    {
        if (rgba == null) { throw new ArgumentNullException(nameof(rgba)); }
        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException("pixel buffer smaller than width*height*4", nameof(rgba));
        }

        var block = new PixelBlock(width, height);
        Array.Copy(rgba, block.Pixels, width * height * 4);
        ResizeExact(width, height);
        Blank();
        PlaceBlock(block, 0, 0, width, height, false);
    }

    /// <summary>
    /// Encodes the image (or a region of it) and writes it to a file — the
    /// <c>write</c> subcommand. PNG and GIF are supported via
    /// CodeBrix.Imaging.
    /// </summary>
    /// <param name="path">The output file path.</param>
    /// <param name="format">The format name (<c>png</c>/<c>gif</c>; case-insensitive).</param>
    /// <param name="fromX1">The region's left edge.</param>
    /// <param name="fromY1">The region's top edge.</param>
    /// <param name="fromX2">The region's exclusive right edge.</param>
    /// <param name="fromY2">The region's exclusive bottom edge.</param>
    public void WriteFile(string path, string format, int fromX1, int fromY1, int fromX2, int fromY2)
    {
        fromX1 = Math.Max(0, Math.Min(fromX1, _width));
        fromX2 = Math.Max(0, Math.Min(fromX2, _width));
        fromY1 = Math.Max(0, Math.Min(fromY1, _height));
        fromY2 = Math.Max(0, Math.Min(fromY2, _height));
        int width = Math.Max(0, fromX2 - fromX1);
        int height = Math.Max(0, fromY2 - fromY1);
        if (width == 0 || height == 0)
        {
            throw new InvalidOperationException("image \"" + Name + "\" is empty; nothing to write");
        }

        var region = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(_pixels, ((fromY1 + y) * _width + fromX1) * 4,
                    region, y * width * 4, width * 4);
        }

        string normalized = (format ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            normalized = (extension == "gif") ? "gif" : "png";
        }
        if (normalized != "png" && normalized != "gif")
        {
            throw new InvalidOperationException(
                    "image file format \"" + format + "\" has no file writing capability");
        }

        CodeBrix.Imaging.Formats.IImageFormat imageFormat = (normalized == "gif")
                ? (CodeBrix.Imaging.Formats.IImageFormat)CodeBrix.Imaging.Formats.Gif.GifFormat.Instance
                : CodeBrix.Imaging.Formats.Png.PngFormat.Instance;
        using (CodeBrix.Imaging.Image<CodeBrix.Imaging.PixelFormats.Rgba32> image =
                CodeBrix.Imaging.Image.LoadPixelData<CodeBrix.Imaging.PixelFormats.Rgba32>(
                        region, width, height, imageFormat))
        {
            if (normalized == "png")
            {
                CodeBrix.Imaging.ImageExtensions.SaveAsPng(image, path);
            }
            else
            {
                CodeBrix.Imaging.ImageExtensions.SaveAsGif(image, path, BuildGifEncoder(region));
            }
        }
    }

    /// <summary>
    /// Builds a GIF encoder for the region being written: when the pixels
    /// fit the palette, an exact <c>PaletteQuantizer</c> keeps the write
    /// LOSSLESS (as real Tk's GIF writer is). Known edge: the encoder
    /// reserves a transparency slot, so an image using a FULL 256-color
    /// palette has one color nearest-remapped where real Tk keeps all 256 —
    /// 255 or fewer distinct colors round-trip exactly.
    /// </summary>
    private static CodeBrix.Imaging.Formats.Gif.GifEncoder BuildGifEncoder(byte[] regionRgba)
    {
        var distinct = new HashSet<uint>();
        for (int offset = 0; offset + 3 < regionRgba.Length; offset += 4)
        {
            uint packed = (uint)((regionRgba[offset] << 24) | (regionRgba[offset + 1] << 16)
                    | (regionRgba[offset + 2] << 8) | regionRgba[offset + 3]);
            distinct.Add(packed);
            if (distinct.Count > 256) { break; }
        }

        var encoder = new CodeBrix.Imaging.Formats.Gif.GifEncoder();
        if (distinct.Count <= 256)
        {
            var palette = new CodeBrix.Imaging.Color[distinct.Count];
            int index = 0;
            foreach (uint packed in distinct)
            {
                palette[index++] = CodeBrix.Imaging.Color.FromRgba(
                        (byte)(packed >> 24), (byte)(packed >> 16),
                        (byte)(packed >> 8), (byte)packed);
            }
            encoder.Quantizer =
                    new CodeBrix.Imaging.Processing.Processors.Quantization.PaletteQuantizer(palette);
        }
        return encoder;
    }

    // ------------------------------------------------------------------
    // Textual forms (get / data) — byte-compatible with real Tk
    // ------------------------------------------------------------------

    /// <summary>
    /// The <c>get</c> subcommand: the pixel's decimal <c>R G B</c>.
    /// </summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The space-joined decimal components.</returns>
    public string GetPixelText(int x, int y)
    {
        RequireInside(x, y, "get");
        byte r, g, b, a;
        ReadPixel(x, y, out r, out g, out b, out a);
        return r + " " + g + " " + b;
    }

    /// <summary>
    /// The <c>data</c> subcommand: a Tcl list of rows, each a list of
    /// <c>#rrggbb</c> colors (alpha is not encoded — Tk's default format).
    /// </summary>
    /// <returns>The Tcl-formatted pixel data.</returns>
    public string Data()
    {
        var rows = new List<string>(_height);
        var row = new StringBuilder();
        for (int y = 0; y < _height; y++)
        {
            row.Clear();
            for (int x = 0; x < _width; x++)
            {
                byte r, g, b, a;
                ReadPixel(x, y, out r, out g, out b, out a);
                if (x > 0) { row.Append(' '); }
                row.Append('#');
                row.Append(r.ToString("x2"));
                row.Append(g.ToString("x2"));
                row.Append(b.ToString("x2"));
            }
            rows.Add(row.ToString());
        }
        return TclString.JoinList(rows);
    }

    // ------------------------------------------------------------------
    // Painting
    // ------------------------------------------------------------------

    /// <summary>
    /// Draws the image onto a Skia canvas at (<paramref name="x"/>,
    /// <paramref name="y"/>), through a cached <see cref="SKImage"/> that is
    /// rebuilt after mutations.
    /// </summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    public void Draw(SKCanvas canvas, float x, float y)
    {
        if (_width <= 0 || _height <= 0) { return; }

        if (_cachedImage == null)
        {
            var info = new SKImageInfo(_width, _height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using (var bitmap = new SKBitmap(info))
            {
                System.Runtime.InteropServices.Marshal.Copy(
                        _pixels, 0, bitmap.GetPixels(), _pixels.Length);
                _cachedImage = SKImage.FromBitmap(bitmap);
            }
        }
        canvas.DrawImage(_cachedImage, x, y, new SKSamplingOptions(SKFilterMode.Nearest));
    }

    private void NotifyChanged()
    {
        if (_cachedImage != null)
        {
            _cachedImage.Dispose();
            _cachedImage = null;
        }
        Action handler = Changed;
        if (handler != null) { handler(); }
    }

    internal void ReleaseResources()
    {
        if (_cachedImage != null)
        {
            _cachedImage.Dispose();
            _cachedImage = null;
        }
    }

    // ------------------------------------------------------------------
    // The photo command surface ($img verb ...), Tk argument shapes
    // ------------------------------------------------------------------

    /// <summary>
    /// Executes a photo subcommand with the Tcl argument shapes verbatim —
    /// <c>blank cget configure copy data get put read redither transparency
    /// write</c> — returning what Tk returns. Unknown-but-valid corners
    /// accept-and-no-op; genuinely bad usage raises the Tk error message.
    /// </summary>
    /// <param name="words">The subcommand and its arguments.</param>
    /// <returns>The Tcl result string.</returns>
    public string Execute(IReadOnlyList<string> words)
    {
        if (words == null || words.Count == 0)
        {
            throw new InvalidOperationException(
                    "wrong # args: should be \"" + Name + " option ?arg ...?\"");
        }

        switch (words[0])
        {
            case "blank":
                Blank();
                return "";
            case "cget":
                return (words.Count >= 2) ? CgetOption(words[1]) : "";
            case "configure":
                return ExecuteConfigure(words);
            case "copy":
                return ExecuteCopy(words);
            case "data":
                return Data();
            case "get":
                RequireArgCount(words, 3, "get x y");
                return GetPixelText(ParseInt(words[1]), ParseInt(words[2]));
            case "put":
                return ExecutePut(words);
            case "read":
                return ExecuteRead(words);
            case "redither":
                return "";
            case "transparency":
                return ExecuteTransparency(words);
            case "write":
                return ExecuteWrite(words);
            default:
                throw new InvalidOperationException("bad option \"" + words[0]
                        + "\": must be blank, cget, configure, copy, data, get, put, "
                        + "read, redither, transparency, or write");
        }
    }

    private static readonly string[] ConfigureOptionOrder =
            { "-data", "-format", "-file", "-gamma", "-height", "-palette", "-width" };

    private string CgetOption(string option)
    {
        if (option == "-width") { return Options.Get("-width", "0"); }
        if (option == "-height") { return Options.Get("-height", "0"); }
        if (option == "-gamma") { return Options.Get("-gamma", "1.0"); }
        return Options.Get(option, "");
    }

    private string ExecuteConfigure(IReadOnlyList<string> words)
    {
        if (words.Count == 1)
        {
            var entries = new List<string>(ConfigureOptionOrder.Length);
            foreach (string option in ConfigureOptionOrder)
            {
                string defaultValue = (option == "-gamma") ? "1"
                        : (option == "-height" || option == "-width") ? "0" : "";
                var parts = new List<string> { option, "", "", defaultValue, CgetOption(option) };
                entries.Add(TclString.JoinList(parts));
            }
            return TclString.JoinList(entries);
        }

        for (int i = 1; i + 1 < words.Count; i += 2)
        {
            Options.Set(words[i], words[i + 1]);
        }
        ApplyConfiguredContent();
        return "";
    }

    /// <summary>
    /// Re-applies the content-bearing options after a configure: size pin
    /// first, then <c>-file</c>/<c>-data</c> decoding.
    /// </summary>
    internal void ApplyConfiguredContent()
    {
        SetUserSize(Options.GetInt("-width", 0), Options.GetInt("-height", 0));

        string file = Options.Get("-file", "");
        string data = Options.Get("-data", "");
        if (file.Length > 0)
        {
            LoadEncoded(File.ReadAllBytes(file), file);
        }
        else if (data.Length > 0)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("couldn't recognize image data");
            }
            LoadEncoded(bytes, Name);
        }
        NotifyChanged();
    }

    private string ExecuteCopy(IReadOnlyList<string> words)
    {
        if (words.Count < 2)
        {
            throw new InvalidOperationException(
                    "wrong # args: should be \"" + Name + " copy source-image ?-option value ...?\"");
        }

        PhotoImage source = (_manager != null) ? _manager.Find(words[1]) : null;
        if (source == null)
        {
            throw new InvalidOperationException("image \"" + words[1] + "\" doesn't exist"
                    + " or is not a photo image");
        }

        PhotoCopyOptions options = PhotoCopyOptions.Parse(words, 2);
        CopyFrom(source, options);
        return "";
    }

    private string ExecutePut(IReadOnlyList<string> words)
    {
        if (words.Count < 2)
        {
            throw new InvalidOperationException(
                    "wrong # args: should be \"" + Name + " put data ?-option value ...?\"");
        }

        PixelBlock block = ParseColorBlock(words[1]);

        int toX1 = 0, toY1 = 0;
        int toX2 = block.Width, toY2 = block.Height;
        bool hasCorner = false;
        for (int i = 2; i < words.Count; i++)
        {
            if (words[i] == "-to" && i + 2 < words.Count)
            {
                toX1 = ParseInt(words[i + 1]);
                toY1 = ParseInt(words[i + 2]);
                if (i + 4 < words.Count && IsInteger(words[i + 3]) && IsInteger(words[i + 4]))
                {
                    toX2 = ParseInt(words[i + 3]);
                    toY2 = ParseInt(words[i + 4]);
                    hasCorner = true;
                    i += 4;
                }
                else
                {
                    i += 2;
                }
            }
            else if (words[i] == "-format" && i + 1 < words.Count)
            {
                i++;
            }
        }
        if (!hasCorner)
        {
            toX2 = toX1 + block.Width;
            toY2 = toY1 + block.Height;
        }

        PlaceBlock(block, toX1, toY1, toX2, toY2, false);
        return "";
    }

    private string ExecuteRead(IReadOnlyList<string> words)
    {
        if (words.Count < 2)
        {
            throw new InvalidOperationException(
                    "wrong # args: should be \"" + Name + " read fileName ?-option value ...?\"");
        }

        var temp = new PhotoImage(null, Name);
        temp.LoadEncoded(File.ReadAllBytes(words[1]), words[1]);

        var options = PhotoCopyOptions.Parse(words, 2);
        CopyFrom(temp, options);
        return "";
    }

    private string ExecuteTransparency(IReadOnlyList<string> words)
    {
        if (words.Count >= 4 && words[1] == "get")
        {
            return IsTransparent(ParseInt(words[2]), ParseInt(words[3])) ? "1" : "0";
        }
        if (words.Count >= 5 && words[1] == "set")
        {
            int x = ParseInt(words[2]);
            int y = ParseInt(words[3]);
            RequireInside(x, y, "transparency set");
            byte r, g, b, a;
            ReadPixel(x, y, out r, out g, out b, out a);
            bool transparent = words[4] == "1" || words[4] == "true" || words[4] == "yes";
            WritePixel(x, y, r, g, b, transparent ? (byte)0 : (byte)255);
            NotifyChanged();
            return "";
        }
        throw new InvalidOperationException(
                "wrong # args: should be \"" + Name + " transparency option ?arg ...?\"");
    }

    private string ExecuteWrite(IReadOnlyList<string> words)
    {
        if (words.Count < 2)
        {
            throw new InvalidOperationException(
                    "wrong # args: should be \"" + Name + " write fileName ?-option value ...?\"");
        }

        string format = "";
        int fromX1 = 0, fromY1 = 0, fromX2 = _width, fromY2 = _height;
        for (int i = 2; i < words.Count; i++)
        {
            if (words[i] == "-format" && i + 1 < words.Count)
            {
                format = words[i + 1];
                i++;
            }
            else if (words[i] == "-from" && i + 2 < words.Count)
            {
                fromX1 = ParseInt(words[i + 1]);
                fromY1 = ParseInt(words[i + 2]);
                if (i + 4 < words.Count && IsInteger(words[i + 3]) && IsInteger(words[i + 4]))
                {
                    fromX2 = ParseInt(words[i + 3]);
                    fromY2 = ParseInt(words[i + 4]);
                    i += 4;
                }
                else
                {
                    fromX2 = _width;
                    fromY2 = _height;
                    i += 2;
                }
            }
        }

        WriteFile(words[1], format, fromX1, fromY1, fromX2, fromY2);
        return "";
    }

    // ------------------------------------------------------------------
    // Color-block parsing (the put data form)
    // ------------------------------------------------------------------

    /// <summary>
    /// Parses the <c>put</c> data form — a Tcl list of rows, each a list of
    /// colors — into a pixel block. A single color is a 1×1 block.
    /// </summary>
    /// <param name="data">The Tcl-formatted color rows.</param>
    /// <returns>The parsed block.</returns>
    internal static PixelBlock ParseColorBlock(string data)
    {
        List<string> rows = TclString.SplitList(data);
        if (rows.Count == 0) { return new PixelBlock(0, 0); }

        var parsedRows = new List<List<SKColor>>(rows.Count);
        int width = -1;
        foreach (string rowText in rows)
        {
            List<string> cells = TclString.SplitList(rowText);
            var row = new List<SKColor>(cells.Count);
            foreach (string cell in cells)
            {
                SKColor color;
                if (!TkColor.TryParse(cell, out color))
                {
                    throw new InvalidOperationException("can't parse color \"" + cell + "\"");
                }
                row.Add(color);
            }
            if (width < 0) { width = row.Count; }
            else if (width != row.Count)
            {
                throw new InvalidOperationException(
                        "all elements of color list must have the same number of elements");
            }
            parsedRows.Add(row);
        }

        var block = new PixelBlock(width, parsedRows.Count);
        for (int y = 0; y < parsedRows.Count; y++)
        {
            for (int x = 0; x < width; x++)
            {
                SKColor color = parsedRows[y][x];
                int offset = (y * width + x) * 4;
                block.Pixels[offset] = color.Red;
                block.Pixels[offset + 1] = color.Green;
                block.Pixels[offset + 2] = color.Blue;
                block.Pixels[offset + 3] = 255;
            }
        }
        return block;
    }

    private static void RequireArgCount(IReadOnlyList<string> words, int count, string usage)
    {
        if (words.Count < count)
        {
            throw new InvalidOperationException("wrong # args: should be \"image " + usage + "\"");
        }
    }

    private static bool IsInteger(string text)
    {
        int value;
        return int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static int ParseInt(string text)
    {
        int value;
        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            throw new InvalidOperationException("expected integer but got \"" + text + "\"");
        }
        return value;
    }
}

/// <summary>
/// A rectangular RGBA pixel block — the unit <c>put</c>, <c>read</c>, and
/// <c>copy</c> place into a photo.
/// </summary>
internal sealed class PixelBlock
{
    internal PixelBlock(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[Math.Max(0, width * height * 4)];
    }

    internal int Width { get; }

    internal int Height { get; }

    internal byte[] Pixels { get; }
}

/// <summary>
/// The parsed options of a photo <c>copy</c>/<c>read</c> — <c>-from</c>,
/// <c>-to</c>, <c>-zoom</c>, <c>-subsample</c>, <c>-shrink</c>, and the
/// compositing rule.
/// </summary>
public sealed class PhotoCopyOptions
{
    /// <summary>Whether <c>-from</c> was given.</summary>
    public bool HasFrom { get; private set; }

    /// <summary>Whether <c>-from</c> carried the second corner.</summary>
    public bool HasFromCorner { get; private set; }

    /// <summary>The <c>-from</c> left edge.</summary>
    public int FromX1 { get; private set; }

    /// <summary>The <c>-from</c> top edge.</summary>
    public int FromY1 { get; private set; }

    /// <summary>The <c>-from</c> exclusive right edge.</summary>
    public int FromX2 { get; private set; }

    /// <summary>The <c>-from</c> exclusive bottom edge.</summary>
    public int FromY2 { get; private set; }

    /// <summary>Whether <c>-to</c> was given.</summary>
    public bool HasTo { get; private set; }

    /// <summary>Whether <c>-to</c> carried the second corner (tiling).</summary>
    public bool HasToCorner { get; private set; }

    /// <summary>The <c>-to</c> left edge.</summary>
    public int ToX1 { get; private set; }

    /// <summary>The <c>-to</c> top edge.</summary>
    public int ToY1 { get; private set; }

    /// <summary>The <c>-to</c> exclusive right edge (when tiling).</summary>
    public int ToX2 { get; private set; }

    /// <summary>The <c>-to</c> exclusive bottom edge (when tiling).</summary>
    public int ToY2 { get; private set; }

    /// <summary>The horizontal <c>-zoom</c> factor (default 1).</summary>
    public int ZoomX { get; private set; } = 1;

    /// <summary>The vertical <c>-zoom</c> factor (default 1).</summary>
    public int ZoomY { get; private set; } = 1;

    /// <summary>The horizontal <c>-subsample</c> step (default 1).</summary>
    public int SubsampleX { get; private set; } = 1;

    /// <summary>The vertical <c>-subsample</c> step (default 1).</summary>
    public int SubsampleY { get; private set; } = 1;

    /// <summary>Whether <c>-shrink</c> was given.</summary>
    public bool Shrink { get; private set; }

    /// <summary>Whether <c>-compositingrule set</c> was given (default is overlay).</summary>
    public bool RuleSet { get; private set; }

    /// <summary>
    /// Parses the option words starting at <paramref name="start"/>, with
    /// Tk's optional-second-corner shapes. Unknown options are ignored
    /// (accept-and-no-op).
    /// </summary>
    /// <param name="words">The full argument list.</param>
    /// <param name="start">The index of the first option word.</param>
    /// <returns>The parsed options.</returns>
    public static PhotoCopyOptions Parse(IReadOnlyList<string> words, int start)
    {
        var options = new PhotoCopyOptions();
        for (int i = start; i < words.Count; i++)
        {
            switch (words[i])
            {
                case "-from":
                {
                    int consumed;
                    int x1, y1, x2, y2;
                    bool corner;
                    ReadCoords(words, i, out x1, out y1, out x2, out y2, out corner, out consumed);
                    options.HasFrom = true;
                    options.HasFromCorner = corner;
                    options.FromX1 = x1;
                    options.FromY1 = y1;
                    options.FromX2 = x2;
                    options.FromY2 = y2;
                    i += consumed;
                    break;
                }
                case "-to":
                {
                    int consumed;
                    int x1, y1, x2, y2;
                    bool corner;
                    ReadCoords(words, i, out x1, out y1, out x2, out y2, out corner, out consumed);
                    options.HasTo = true;
                    options.HasToCorner = corner;
                    options.ToX1 = x1;
                    options.ToY1 = y1;
                    options.ToX2 = x2;
                    options.ToY2 = y2;
                    i += consumed;
                    break;
                }
                case "-zoom":
                {
                    int x, y, consumed;
                    ReadFactors(words, i, out x, out y, out consumed);
                    options.ZoomX = x;
                    options.ZoomY = y;
                    i += consumed;
                    break;
                }
                case "-subsample":
                {
                    int x, y, consumed;
                    ReadFactors(words, i, out x, out y, out consumed);
                    options.SubsampleX = x;
                    options.SubsampleY = y;
                    i += consumed;
                    break;
                }
                case "-shrink":
                    options.Shrink = true;
                    break;
                case "-compositingrule":
                    if (i + 1 < words.Count)
                    {
                        options.RuleSet = words[i + 1] == "set";
                        i++;
                    }
                    break;
                default:
                    break;
            }
        }
        return options;
    }

    private static void ReadCoords(IReadOnlyList<string> words, int index,
            out int x1, out int y1, out int x2, out int y2, out bool corner, out int consumed)
    {
        x1 = ReadInt(words, index + 1);
        y1 = ReadInt(words, index + 2);
        x2 = 0;
        y2 = 0;
        corner = false;
        consumed = 2;
        if (index + 4 < words.Count && TryReadInt(words[index + 3], out x2)
                && TryReadInt(words[index + 4], out y2))
        {
            corner = true;
            consumed = 4;
        }
    }

    private static void ReadFactors(IReadOnlyList<string> words, int index,
            out int x, out int y, out int consumed)
    {
        x = ReadInt(words, index + 1);
        y = x;
        consumed = 1;
        int second;
        if (index + 2 < words.Count && TryReadInt(words[index + 2], out second))
        {
            y = second;
            consumed = 2;
        }
    }

    private static int ReadInt(IReadOnlyList<string> words, int index)
    {
        if (index >= words.Count)
        {
            throw new InvalidOperationException("wrong # args: missing coordinate");
        }
        int value;
        if (!TryReadInt(words[index], out value))
        {
            throw new InvalidOperationException("expected integer but got \"" + words[index] + "\"");
        }
        return value;
    }

    private static bool TryReadInt(string text, out int value)
    {
        return int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}

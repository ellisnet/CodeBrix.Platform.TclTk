using System;
using System.Collections.Generic;
using System.IO;

using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Images;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a named photo image (<c>image create photo</c>) in XAML, so
/// widget <c>Image</c> properties can reference it by name. Photos are
/// materialized BEFORE the widget declarations, regardless of document
/// position. A relative <see cref="File"/> path resolves against the
/// application base directory.
/// </summary>
public sealed class TkPhoto : TkElement
{
    /// <summary>The image file to load (GIF/PNG; relative paths resolve against the app base directory).</summary>
    public string File
    {
        get { return (string)GetValue(FileProperty); }
        set { SetValue(FileProperty, value); }
    }

    /// <summary>Identifies the <see cref="File"/> property.</summary>
    public static readonly DependencyProperty FileProperty =
            DependencyProperty.Register(nameof(File), typeof(string), typeof(TkPhoto),
                    new PropertyMetadata(""));

    /// <summary>Base64-encoded image data (<c>-data</c>), as an alternative to <see cref="File"/>.</summary>
    public string Data
    {
        get { return (string)GetValue(DataProperty); }
        set { SetValue(DataProperty, value); }
    }

    /// <summary>Identifies the <see cref="Data"/> property.</summary>
    public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(string), typeof(TkPhoto),
                    new PropertyMetadata(""));

    /// <summary>The blank-photo pixel width (<c>-width</c>; 0 = from the image data).</summary>
    public int PixelWidth
    {
        get { return (int)GetValue(PixelWidthProperty); }
        set { SetValue(PixelWidthProperty, value); }
    }

    /// <summary>Identifies the <see cref="PixelWidth"/> property.</summary>
    public static readonly DependencyProperty PixelWidthProperty =
            DependencyProperty.Register(nameof(PixelWidth), typeof(int), typeof(TkPhoto),
                    new PropertyMetadata(0));

    /// <summary>The blank-photo pixel height (<c>-height</c>; 0 = from the image data).</summary>
    public int PixelHeight
    {
        get { return (int)GetValue(PixelHeightProperty); }
        set { SetValue(PixelHeightProperty, value); }
    }

    /// <summary>Identifies the <see cref="PixelHeight"/> property.</summary>
    public static readonly DependencyProperty PixelHeightProperty =
            DependencyProperty.Register(nameof(PixelHeight), typeof(int), typeof(TkPhoto),
                    new PropertyMetadata(0));

    /// <summary>The created photo, or null before materialization.</summary>
    public PhotoImage Photo { get; private set; }

    internal override void Materialize(TkHostView host, TkWindow parentWindow)
    {
        if (Photo != null) { return; }

        var options = new Dictionary<string, string>();
        string file = File;
        if (!string.IsNullOrEmpty(file))
        {
            if (!Path.IsPathRooted(file))
            {
                file = Path.Combine(AppContext.BaseDirectory, file);
            }
            options["-file"] = file;
        }
        if (!string.IsNullOrEmpty(Data)) { options["-data"] = Data; }
        if (PixelWidth > 0) { options["-width"] = PixelWidth.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        if (PixelHeight > 0) { options["-height"] = PixelHeight.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        Photo = host.Tree.Images.CreatePhoto(
                !string.IsNullOrEmpty(Name) ? Name : null, options);
    }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        throw new NotSupportedException("TkPhoto declares an image, not a widget");
    }
}

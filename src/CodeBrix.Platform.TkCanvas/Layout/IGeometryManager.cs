using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The contract every geometry manager (pack, grid) implements toward the
/// window system: computing the size a container wants for its content
/// (geometry propagation) and arranging the content when the container has
/// its final size. Managers keep their own per-container state; the layout
/// pass in <see cref="TkLayout"/> drives these methods synchronously.
/// </summary>
internal interface IGeometryManager
{
    /// <summary>The Tk name of the manager ("pack" or "grid").</summary>
    string Name { get; }

    /// <summary>
    /// Computes the size <paramref name="container"/> needs to just satisfy
    /// all its content (requested sizes plus padding plus the container's
    /// internal border).
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <param name="width">The needed width in pixels.</param>
    /// <param name="height">The needed height in pixels.</param>
    /// <returns>
    /// False when the container has no content managed by this manager (its
    /// requested size must then be left as-is, matching Tk, which never
    /// relinquishes a container's size when the last content leaves).
    /// </returns>
    bool TryComputeRequestedSize(TkWindow container, out int width, out int height);

    /// <summary>
    /// Assigns geometry (and displayed state) to every content window managed
    /// in <paramref name="container"/>, using the container's current
    /// allocated size.
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <returns>True when any content geometry actually changed.</returns>
    bool Arrange(TkWindow container);

    /// <summary>
    /// Removes <paramref name="content"/> from this manager (the analogue of
    /// <c>pack forget</c> / <c>grid forget</c>): the window becomes unmanaged
    /// and undisplayed but keeps its last geometry.
    /// </summary>
    /// <param name="content">The managed window to release.</param>
    void Forget(TkWindow content);

    /// <summary>
    /// Notifies this manager that a window it manages as content has been
    /// destroyed, so its record must be dropped from the container state.
    /// </summary>
    /// <param name="content">The destroyed content window.</param>
    void ContentDestroyed(TkWindow content);

    /// <summary>
    /// Notifies this manager that a container whose content it manages has
    /// been destroyed, so all its content must be released.
    /// </summary>
    /// <param name="container">The destroyed container window.</param>
    void ContainerDestroyed(TkWindow container);
}

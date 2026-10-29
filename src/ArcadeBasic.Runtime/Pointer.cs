namespace ArcadeBasic.Runtime;

/// <summary>A button press or release on the drawing surface, where it happened (in the
/// device unit square, origin bottom-left — the space <see cref="IGraphicsDevice"/> draws in).</summary>
public readonly record struct PointerEvent(bool Pressed, GfxPoint At);

/// <summary>
/// Optional companion to <see cref="IGraphicsDevice"/> for a surface the user can point
/// at, like the browser's canvas. Graphic input (<c>LOCATE POINT</c>,
/// <c>GET POINT</c>) and <c>GET MOUSE</c> read it; a device without it has no pointer.
/// Implementations used from a program's background thread must be thread-safe.
/// </summary>
public interface IPointer
{
    /// <summary>Where the pointer is, in the device unit square; false when unknown
    /// (it hasn't been over the surface yet).</summary>
    bool TryGetPosition(out GfxPoint position);

    /// <summary>The oldest primary-button event not yet read; false when there is none.
    /// A device should report a release for every press — synthesizing one if, say,
    /// the window loses focus mid-drag.</summary>
    bool TryReadEvent(out PointerEvent e);
}

/// <summary>
/// One run's pointer as the program sees it, shared by both engines: <c>GET MOUSE</c>
/// samples it, <c>LOCATE POINT</c> waits on it. Button events are read one per sample,
/// so a click that starts and ends between two samples still reports "pressed", then
/// "released".
/// </summary>
public sealed class PointerState(IGraphicsDevice graphics)
{
    /// <summary>GET MOUSE states (the first is an Arcade BASIC addition; the rest follow True BASIC).</summary>
    public const int NoPointer = -1, Up = 0, Held = 1, Pressed = 2, Released = 3;

    private readonly IPointer? _pointer = graphics as IPointer;
    private GfxPoint _last;
    private bool _down;
    private bool _requesting;

    /// <summary>Is there a pointing device at all?</summary>
    public bool Present => _pointer is not null;

    /// <summary><c>GET MOUSE</c>: the button's state (<see cref="Up"/>, <see cref="Held"/>, or the
    /// next unread <see cref="Pressed"/>/<see cref="Released"/> edge) and where — the edge's
    /// position, else the pointer's. <see cref="NoPointer"/> without a pointing device.</summary>
    public int Sample(out GfxPoint at)
    {
        if (_pointer is null)
        {
            at = default;
            return NoPointer;
        }
        if (_pointer.TryReadEvent(out var e))
        {
            _down = e.Pressed;
            at = _last = e.At;
            return e.Pressed ? Pressed : Released;
        }
        if (_pointer.TryGetPosition(out var p)) _last = p;
        at = _last;
        return _down ? Held : Up;
    }

    /// <summary>
    /// <c>LOCATE POINT</c>: the first button press since the request began that
    /// <paramref name="accept"/> takes (the engines accept points inside the current
    /// window); false while there isn't one yet — call again to keep waiting. Clicks
    /// made before the request began don't answer it.
    /// </summary>
    public bool TryLocate(Func<GfxPoint, bool> accept, out GfxPoint at)
    {
        if (_pointer is null) throw new InvalidOperationException("no pointing device");
        if (!_requesting)
        {
            while (_pointer.TryReadEvent(out var stale)) _down = stale.Pressed;
            _requesting = true;
        }
        while (_pointer.TryReadEvent(out var e))
        {
            _down = e.Pressed;
            _last = e.At;
            if (e.Pressed && accept(e.At))
            {
                _requesting = false;
                at = e.At;
                return true;
            }
        }
        at = default;
        return false;
    }
}

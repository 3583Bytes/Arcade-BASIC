namespace ArcadeBasic.Runtime;

/// <summary>A button press or release on the drawing surface, where it happened (in the
/// device unit square, origin bottom-left — the space <see cref="IGraphicsDevice"/> draws in),
/// and which button: <see cref="PointerState.Primary"/> (the left), <see cref="PointerState.Secondary"/>
/// (the right, or a Mac's Ctrl+click) or <see cref="PointerState.Middle"/>.</summary>
public readonly record struct PointerEvent(bool Pressed, GfxPoint At, int Button = PointerState.Primary);

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

    /// <summary>The oldest button event not yet read; false when there is none.
    /// A device should report a release for every press — synthesizing one if, say,
    /// the window loses focus mid-drag.</summary>
    bool TryReadEvent(out PointerEvent e);
}

/// <summary>
/// Optional companion to <see cref="IPointer"/> for one that can't tell whether there's a
/// mouse until it reports — a terminal's, which only says so once the user moves or
/// clicks it (and an older terminal never will). Until then there's no pointer:
/// <c>GET MOUSE</c> answers -1 and <c>LOCATE POINT</c> asks for typed coordinates.
/// </summary>
public interface IPointerPresence
{
    /// <summary>Has the mouse reported yet? Asking also tells the device the program is
    /// looking for a pointer (a terminal starts reporting the mouse then).</summary>
    bool PointerSeen { get; }
}

/// <summary>
/// One run's pointer as the program sees it: <c>GET MOUSE</c>
/// samples it, <c>LOCATE POINT</c> waits on it. Button events are read one per sample,
/// so a click that starts and ends between two samples still reports "pressed", then
/// "released". Only <c>GET MOUSE x, y, s, b</c> sees the buttons besides the primary one.
/// </summary>
public sealed class PointerState(IGraphicsDevice graphics)
{
    /// <summary>GET MOUSE states (the first is an Arcade BASIC addition; the rest follow True BASIC).</summary>
    public const int NoPointer = -1, Up = 0, Held = 1, Pressed = 2, Released = 3;

    /// <summary>The buttons, as <c>GET MOUSE</c>'s fourth value numbers them (0: none).</summary>
    public const int Primary = 1, Secondary = 2, Middle = 3;

    private readonly IPointer? _pointer = graphics as IPointer;
    private GfxPoint _last;
    private int _held;           // a bit for each button held: 1 << (button - 1)
    private bool _requesting;

    /// <summary>Is there a pointing device at all? (Not yet, for one that hasn't reported
    /// — see <see cref="IPointerPresence"/>.)</summary>
    public bool Present => _pointer is not null && (_pointer is not IPointerPresence presence || presence.PointerSeen);

    /// <summary><c>GET MOUSE x, y, s</c>: the primary button's state (<see cref="Up"/>, <see cref="Held"/>,
    /// or the next unread <see cref="Pressed"/>/<see cref="Released"/> edge) and where — the edge's
    /// position, else the pointer's. <see cref="NoPointer"/> without a pointing device. The
    /// other buttons' edges pass unseen.</summary>
    public int Sample(out GfxPoint at) => Sample(out at, out _, everyButton: false);

    /// <summary><c>GET MOUSE x, y, s, b</c>: the same for every button, and which one it's
    /// about (<paramref name="button"/>: the edge's; else the one held — the primary if it's
    /// one of them — or 0, up).</summary>
    public int Sample(out GfxPoint at, out int button) => Sample(out at, out button, everyButton: true);

    private int Sample(out GfxPoint at, out int button, bool everyButton)
    {
        button = 0;
        if (_pointer is null || !Present)
        {
            at = default;
            return NoPointer;
        }
        while (_pointer.TryReadEvent(out var e))
        {
            Note(e);
            if (!everyButton && e.Button != Primary) continue;
            at = e.At;
            button = e.Button;
            return e.Pressed ? Pressed : Released;
        }
        if (_pointer.TryGetPosition(out var p)) _last = p;
        at = _last;
        var held = everyButton ? _held : _held & 1;
        if (held == 0) return Up;
        button = (held & 1) != 0 ? Primary : (held & 2) != 0 ? Secondary : Middle;
        return Held;
    }

    private void Note(PointerEvent e)
    {
        _last = e.At;
        var bit = 1 << (e.Button - 1);
        _held = e.Pressed ? _held | bit : _held & ~bit;
    }

    /// <summary>
    /// <c>LOCATE POINT</c>: the first primary-button press since the request began that
    /// <paramref name="accept"/> takes (the engines accept points inside the current
    /// window); false while there isn't one yet — call again to keep waiting. Clicks
    /// made before the request began don't answer it.
    /// </summary>
    public bool TryLocate(Func<GfxPoint, bool> accept, out GfxPoint at)
    {
        if (_pointer is null) throw new InvalidOperationException("no pointing device");
        if (!_requesting)
        {
            while (_pointer.TryReadEvent(out var stale)) Note(stale);
            _requesting = true;
        }
        while (_pointer.TryReadEvent(out var e))
        {
            Note(e);
            if (e.Pressed && e.Button == Primary && accept(e.At))
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

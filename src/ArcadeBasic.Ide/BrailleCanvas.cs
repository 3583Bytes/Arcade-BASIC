using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;
using Rune = System.Rune;

namespace ArcadeBasic.Ide;

/// <summary>
/// A Terminal.Gui view that renders a sub-cell bitmap using Unicode Braille
/// patterns: each character cell packs a 2×4 dot matrix (U+2800..U+28FF), so an
/// 80×24 region of cells addresses a 160×96 "pixel" grid. Graphic-output
/// statements plot into the dot grid (from the interpreter's background thread,
/// so access is locked); the main loop redraws. GRAPH TEXT labels are drawn
/// natively on top of the braille cells.
/// </summary>
internal sealed class BrailleCanvas : View
{
    public const int DotsW = 160;
    public const int DotsH = 96;
    private const int CellsW = DotsW / 2;   // 80
    private const int CellsH = DotsH / 4;   // 24

    private readonly bool[,] _dots = new bool[DotsW, DotsH];
    private readonly int[,] _cellColor = new int[CellsW, CellsH];
    private readonly List<(int Cx, int Cy, string Text, int Color)> _labels = new();
    private readonly object _lock = new();
    private bool _empty = true;

    public int PixelWidth => DotsW;
    public int PixelHeight => DotsH;
    public bool IsEmpty { get { lock (_lock) return _empty; } }

    public BrailleCanvas()
    {
        CanFocus = false;
        ClearBuffer();
    }

    public void ClearBuffer()
    {
        lock (_lock)
        {
            Array.Clear(_dots, 0, _dots.Length);
            for (var i = 0; i < CellsW; i++)
                for (var j = 0; j < CellsH; j++)
                    _cellColor[i, j] = 1;
            _labels.Clear();
            _empty = true;
        }
    }

    public void Plot(int x, int y, int colorIndex)
    {
        if (x < 0 || x >= DotsW || y < 0 || y >= DotsH) return;
        lock (_lock)
        {
            _dots[x, y] = true;
            _cellColor[x / 2, y / 4] = colorIndex;
            _empty = false;
        }
    }

    public void AddLabel(int dotX, int dotY, string text, int colorIndex)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_lock)
        {
            _labels.Add((dotX / 2, dotY / 4, text, colorIndex));
            _empty = false;
        }
    }

    /// <summary>Render the braille layer to a plain string (one line per cell
    /// row), no console driver required. For headless tests and debugging.</summary>
    internal string RenderToText()
    {
        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();
            for (var cy = 0; cy < CellsH; cy++)
            {
                for (var cx = 0; cx < CellsW; cx++)
                    sb.Append((char)(0x2800 + CellBits(cx, cy)));
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    public override void Redraw(Rect bounds)
    {
        var driver = Application.Driver;
        if (driver is null) return;

        lock (_lock)
        {
            var rows = Math.Min(CellsH, Bounds.Height);
            var cols = Math.Min(CellsW, Bounds.Width);
            for (var cy = 0; cy < rows; cy++)
            {
                for (var cx = 0; cx < cols; cx++)
                {
                    var bits = CellBits(cx, cy);
                    driver.SetAttribute(Attr(driver, bits == 0 ? 1 : _cellColor[cx, cy]));
                    AddRune(cx, cy, new Rune((char)(0x2800 + bits)));
                }
            }

            // Native text labels on top of the braille layer.
            foreach (var (lcx, lcy, text, color) in _labels)
            {
                if (lcy < 0 || lcy >= rows) continue;
                driver.SetAttribute(Attr(driver, color));
                for (var i = 0; i < text.Length; i++)
                {
                    var col = lcx + i;
                    if (col < 0 || col >= cols) continue;
                    AddRune(col, lcy, new Rune(text[i]));
                }
            }
        }
    }

    private int CellBits(int cx, int cy)
    {
        int bx = cx * 2, by = cy * 4;
        var bits = 0;
        if (_dots[bx, by]) bits |= 0x01;
        if (_dots[bx, by + 1]) bits |= 0x02;
        if (_dots[bx, by + 2]) bits |= 0x04;
        if (_dots[bx + 1, by]) bits |= 0x08;
        if (_dots[bx + 1, by + 1]) bits |= 0x10;
        if (_dots[bx + 1, by + 2]) bits |= 0x20;
        if (_dots[bx, by + 3]) bits |= 0x40;
        if (_dots[bx + 1, by + 3]) bits |= 0x80;
        return bits;
    }

    private static readonly Color[] Palette =
    [
        Color.DarkGray, Color.White, Color.BrightRed, Color.BrightGreen,
        Color.BrightBlue, Color.BrightMagenta, Color.BrightCyan, Color.BrightYellow,
        Color.Gray, Color.Red, Color.Green, Color.Blue,
        Color.Magenta, Color.Cyan, Color.Brown, Color.White,
    ];

    private static Attribute Attr(ConsoleDriver driver, int index) =>
        driver.MakeAttribute(Palette[index < 0 || index >= Palette.Length ? 1 : index], Color.Black);
}

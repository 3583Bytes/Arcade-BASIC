namespace ArcadeBasic.Runtime;

/// <summary>
/// The character grid that §13 drawings are composed onto in a terminal — shared by
/// the console backend (<see cref="AnsiGraphicsDevice"/>) and the IDE's canvas, so both
/// look the same. Each kind of drawing gets the characters that show it best:
/// <list type="bullet">
/// <item>horizontal and vertical lines → box-drawing characters (─ │ ┌ ┐ └ ┘ ├ ┤ ┬ ┴ ┼;
/// ┄ ┆ dashed, ┈ ┊ dotted), one cell's resolution: even weight both ways, no gaps;</item>
/// <item>filled areas → block elements, 2 × 2 per cell (▘ ▝ ▀ ▖ ▌ ▞ ▛ ▗ ▚ ▐ ▜ ▄ ▙ ▟ █);
/// cell arrays (MAT GRAPH CELLS) too, each quadrant the colour under its middle;</item>
/// <item>anything else (slanted lines, points) → Braille dots, 2 × 4 per cell;</item>
/// <item>text → its characters, in the cell that holds the middle of the letters (the
/// point is the text's baseline, as in ECMA-116's default LEFT, BASE alignment).</item>
/// </list>
/// A cell shows one of these at a time. Lines merge with lines (making corners and
/// junctions), blocks with blocks and dots with dots; anything else drawn later
/// replaces what was there — so a shape filled in colour 0 (black) erases, and a card
/// drawn over others hides them.
/// </summary>
public sealed class CellSurface
{
    public enum CellKind : byte { Empty, Dots, Line, Block, Text }

    /// <summary>One cell. <see cref="Bits"/> is the Braille dot mask (Dots), the
    /// connections left 1 / right 2 / up 4 / down 8 (Line), or the quadrants
    /// top-left 1 / top-right 2 / bottom-left 4 / bottom-right 8 (Block).</summary>
    public struct Cell
    {
        public CellKind Kind;
        public int Bits;
        public int Color;
        public int Style;   // Line: 1 solid, 2 dashed, 3 dotted
        public char Char;   // Text
    }

    private const int L = 1, R = 2, U = 4, D = 8;

    private Cell[] _cells = [];

    public CellSurface(int cols, int rows) => Resize(cols, rows);

    public int Cols { get; private set; }
    public int Rows { get; private set; }

    /// <summary>The Braille dot grid behind the cells (2 × 4 dots per cell), in which
    /// <see cref="IGraphicsDevice"/>'s unit square is laid out.</summary>
    public int DotsW => Cols * 2;
    public int DotsH => Rows * 4;

    /// <summary>Nothing has been drawn since the last <see cref="Clear"/>.</summary>
    public bool IsEmpty { get; private set; } = true;

    public Cell this[int col, int row] => _cells[row * Cols + col];

    /// <summary>Change the grid size, keeping the picture where it is (cropped, or with
    /// empty cells added, at the right and bottom) until the program redraws it.
    /// Scaling it instead would sample the characters and break the boxes apart.</summary>
    public void Resize(int cols, int rows)
    {
        cols = Math.Max(1, cols);
        rows = Math.Max(1, rows);
        if (cols == Cols && rows == Rows) return;
        var old = _cells;
        int oldCols = Cols, oldRows = Rows;
        _cells = new Cell[cols * rows];
        for (var r = 0; r < Math.Min(rows, oldRows); r++)
            Array.Copy(old, r * oldCols, _cells, r * cols, Math.Min(cols, oldCols));
        Cols = cols;
        Rows = rows;
    }

    public void Clear()
    {
        Array.Clear(_cells, 0, _cells.Length);
        IsEmpty = true;
    }

    /// <summary>Copy this surface onto <paramref name="other"/> (the IDE's front buffer).</summary>
    public void CopyTo(CellSurface other)
    {
        if (other._cells.Length != _cells.Length) other._cells = new Cell[_cells.Length];
        other.Cols = Cols;
        other.Rows = Rows;
        other.IsEmpty = IsEmpty;
        Array.Copy(_cells, other._cells, _cells.Length);
    }

    // -- Drawing, in the device unit square (origin bottom-left) -------------------

    public void Points(IReadOnlyList<GfxPoint> points, int color)
    {
        foreach (var p in points)
        {
            var (x, y) = ToDot(p);
            Dot(x, y, color);
        }
    }

    public void Lines(IReadOnlyList<GfxPoint> polyline, int color, int style)
    {
        if (polyline.Count == 1)
        {
            Points(polyline, color);
            return;
        }
        for (var i = 1; i < polyline.Count; i++)
        {
            var a = ToDot(polyline[i - 1]);
            var b = ToDot(polyline[i]);
            int ac = a.X / 2, ar = a.Y / 4, bc = b.X / 2, br = b.Y / 4;
            if (ar == br && (ac != bc || Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y)))
            {
                // horizontal within one cell row: ─ from cell to cell
                int c0 = Math.Min(ac, bc), c1 = Math.Max(ac, bc);
                for (var c = c0; c <= c1; c++)
                    Connect(c, ar, (c > c0 ? L : 0) | (c < c1 ? R : 0) | (c0 == c1 ? L | R : 0), color, style);
            }
            else if (ac == bc)
            {
                // vertical within one cell column: │ from cell to cell
                int r0 = Math.Min(ar, br), r1 = Math.Max(ar, br);
                for (var r = r0; r <= r1; r++)
                    Connect(ac, r, (r > r0 ? U : 0) | (r < r1 ? D : 0) | (r0 == r1 ? U | D : 0), color, style);
            }
            else
            {
                Rasterizer.Line(a.X, a.Y, b.X, b.Y, (x, y) => Dot(x, y, color));
            }
        }
    }

    public void Area(IReadOnlyList<GfxPoint> polygon, int color)
    {
        if (polygon.Count == 0) return;
        // Rasterize at dot resolution, then light each quadrant (1 x 2 dots) the shape
        // covers fully — so the gaps between neighbouring shapes (bricks) survive. A
        // shape too thin to cover any quadrant fully lights the ones it touches instead.
        var covered = new Dictionary<int, int>();   // cell * 4 + quadrant → dots covered
        var dots = new List<(int X, int Y)>(polygon.Count);
        foreach (var p in polygon) dots.Add(ToDot(p));
        var seen = new HashSet<(int, int)>();
        Rasterizer.FillPolygon(dots, (x, y) =>
        {
            if (x < 0 || x >= DotsW || y < 0 || y >= DotsH || !seen.Add((x, y))) return;
            var key = ((y / 4) * Cols + x / 2) * 4 + (x % 2) + (y % 4 < 2 ? 0 : 2);   // quadrant 0 TL, 1 TR, 2 BL, 3 BR
            covered[key] = (covered.TryGetValue(key, out var n) ? n : 0) + 1;
        });
        var full = covered.Any(c => c.Value == 2);
        var quadrants = new Dictionary<int, int>();
        foreach (var (key, n) in covered)
        {
            if (full && n < 2) continue;
            var cell = key / 4;
            quadrants[cell] = (quadrants.TryGetValue(cell, out var m) ? m : 0) | (1 << (key % 4));
        }
        foreach (var (index, mask) in quadrants)
        {
            ref var cell = ref _cells[index];
            if (color == 0)
            {
                cell = default;   // colour 0 is the background: filling with it erases
                continue;
            }
            cell = cell.Kind == CellKind.Block
                ? cell with { Bits = cell.Bits | mask, Color = color }
                : new Cell { Kind = CellKind.Block, Bits = mask, Color = color };
            IsEmpty = false;
        }
    }

    /// <summary>MAT GRAPH CELLS: a grid of coloured cells filling <paramref name="rect"/>
    /// (colours row-major from the top-left), drawn only inside <paramref name="visible"/>.
    /// It's an image, so it covers what was there: each block quadrant it reaches takes
    /// the cell under the quadrant's middle — colour 0, the background, leaves it dark —
    /// and as a character shows one colour, it's the one most of those quadrants have.</summary>
    public void Cells(GfxRect rect, int columns, int rows, int[] colors, GfxRect visible)
    {
        visible = Intersect(visible, rect);
        int x0 = Math.Max(0, (int)Math.Floor(visible.Left * DotsW)), x1 = Math.Min(DotsW, (int)Math.Ceiling(visible.Right * DotsW));
        int y0 = Math.Max(0, (int)Math.Floor((1 - visible.Top) * DotsH)), y1 = Math.Min(DotsH, (int)Math.Ceiling((1 - visible.Bottom) * DotsH));
        if (x0 >= x1 || y0 >= y1) return;
        double width = rect.Right - rect.Left, height = rect.Top - rect.Bottom;
        Span<int> quadrant = stackalloc int[4];
        for (var row = y0 / 4; row <= (y1 - 1) / 4; row++)
        {
            for (var col = x0 / 2; col <= (x1 - 1) / 2; col++)
            {
                var reached = false;
                for (var q = 0; q < 4; q++)   // 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right
                {
                    var x = (col * 2 + (q & 1) + 0.5) / DotsW;            // the quadrant's middle
                    var y = 1 - (row * 4 + (q >> 1) * 2 + 1.0) / DotsH;
                    if (x < visible.Left || x > visible.Right || y < visible.Bottom || y > visible.Top)
                    {
                        quadrant[q] = -1;
                        continue;
                    }
                    var cx = Math.Clamp((int)((x - rect.Left) / width * columns), 0, columns - 1);
                    var cy = Math.Clamp((int)((rect.Top - y) / height * rows), 0, rows - 1);
                    quadrant[q] = colors[cy * columns + cx];
                    reached = true;
                }
                if (!reached) continue;

                ref var cell = ref _cells[row * Cols + col];
                var bits = cell.Kind == CellKind.Block ? cell.Bits : 0;
                var color = cell.Kind == CellKind.Block ? cell.Color : 0;
                int best = -1, bestCount = 0;
                for (var q = 0; q < 4; q++)
                {
                    var c = quadrant[q];
                    if (c < 0) continue;
                    if (c == 0)
                    {
                        bits &= ~(1 << q);
                        continue;
                    }
                    bits |= 1 << q;
                    var count = 0;
                    for (var k = 0; k < 4; k++) if (quadrant[k] == c) count++;
                    if (count > bestCount) { best = c; bestCount = count; }
                }
                if (best > 0) color = best;
                cell = bits == 0 ? default : new Cell { Kind = CellKind.Block, Bits = bits, Color = color };
                if (bits != 0) IsEmpty = false;
            }
        }
    }

    public void Text(GfxPoint at, string text, int color)
    {
        if (string.IsNullOrEmpty(text)) return;
        var (x, y) = ToDot(at);
        // The point is the baseline; the letters sit above it, their middle about a
        // third of a row up (ECMA-116 LEFT, BASE). Put them in the cell that holds it.
        var row = (int)Math.Floor((y - 1.2) / 4);
        if (row < 0) row = 0;
        if (row >= Rows) return;
        var col = x / 2;
        for (var i = 0; i < text.Length; i++, col++)
        {
            if (col < 0) continue;
            if (col >= Cols) break;
            _cells[row * Cols + col] = new Cell { Kind = CellKind.Text, Char = text[i], Color = color };
            IsEmpty = false;
        }
    }

    // -- Composition -----------------------------------------------------------------

    /// <summary>The character a cell shows.</summary>
    public char Glyph(int col, int row)
    {
        var cell = _cells[row * Cols + col];
        return cell.Kind switch
        {
            CellKind.Dots => (char)(0x2800 + cell.Bits),
            CellKind.Line => LineGlyph(cell.Bits, cell.Style),
            CellKind.Block => BlockGlyphs[cell.Bits & 15],
            CellKind.Text => cell.Char,
            _ => ' ',
        };
    }

    /// <summary>The surface as text, one line per row (for tests and debugging).</summary>
    public string ToText()
    {
        var sb = new System.Text.StringBuilder(Rows * (Cols + 1));
        for (var r = 0; r < Rows; r++)
        {
            for (var c = 0; c < Cols; c++) sb.Append(Glyph(c, r));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static GfxRect Intersect(GfxRect a, GfxRect b) => new(
        Math.Max(a.Left, b.Left), Math.Min(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom), Math.Min(a.Top, b.Top));

    private static readonly char[] BlockGlyphs = [' ', '▘', '▝', '▀', '▖', '▌', '▞', '▛', '▗', '▚', '▐', '▜', '▄', '▙', '▟', '█'];

    private static char LineGlyph(int bits, int style)
    {
        // A line's end cells (one connection) draw as full ─ / │, so a line looks solid
        // to its last cell; ends that meet other lines become corners and junctions.
        if ((bits & (U | D)) == 0) return style switch { 2 => '┄', 3 => '┈', _ => '─' };
        if ((bits & (L | R)) == 0) return style switch { 2 => '┆', 3 => '┊', _ => '│' };
        return bits switch
        {
            R | D => '┌', L | D => '┐', R | U => '└', L | U => '┘',
            U | D | R => '├', U | D | L => '┤', L | R | D => '┬', L | R | U => '┴',
            _ => '┼',
        };
    }

    // -- Internals -------------------------------------------------------------------

    /// <summary>The dot a unit-square point falls in. The square spans the grid edge to
    /// edge, so a window as wide as the grid has columns (and rows) that land exactly on
    /// cells: x = k of an 80-unit window over 80 columns is column k.</summary>
    private (int X, int Y) ToDot(GfxPoint p) => (
        Math.Clamp((int)Math.Floor(p.X * DotsW), 0, DotsW - 1),
        Math.Clamp((int)Math.Floor((1.0 - p.Y) * DotsH), 0, DotsH - 1));   // flip Y: BASIC up → screen down

    private void Dot(int x, int y, int color)
    {
        if (x < 0 || x >= DotsW || y < 0 || y >= DotsH) return;
        ref var cell = ref _cells[(y / 4) * Cols + x / 2];
        var bit = BrailleBit(x % 2, y % 4);
        cell = cell.Kind == CellKind.Dots
            ? cell with { Bits = cell.Bits | bit, Color = color }
            : new Cell { Kind = CellKind.Dots, Bits = bit, Color = color };
        IsEmpty = false;
    }

    private void Connect(int col, int row, int bits, int color, int style)
    {
        if (col < 0 || col >= Cols || row < 0 || row >= Rows) return;
        ref var cell = ref _cells[row * Cols + col];
        cell = cell.Kind == CellKind.Line
            ? cell with { Bits = cell.Bits | bits, Color = color, Style = style }
            : new Cell { Kind = CellKind.Line, Bits = bits, Color = color, Style = style };
        IsEmpty = false;
    }

    /// <summary>Unicode Braille bit for dot (column 0–1, row 0–3) within a cell.</summary>
    private static int BrailleBit(int col, int row) => (col, row) switch
    {
        (0, 0) => 0x01, (0, 1) => 0x02, (0, 2) => 0x04, (0, 3) => 0x40,
        (1, 0) => 0x08, (1, 1) => 0x10, (1, 2) => 0x20, _ => 0x80,
    };
}

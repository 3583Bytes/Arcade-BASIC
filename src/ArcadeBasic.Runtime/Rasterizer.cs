namespace ArcadeBasic.Runtime;

/// <summary>
/// Integer-grid rasterization shared by the raster graphics backends (terminal
/// braille canvas, Unity texture). Backends map the core's normalized device
/// coordinates to their own pixel grid, then call these to turn vector
/// primitives into individual pixel plots via a callback. Pure int math, so it
/// is netstandard2.1- and IL2CPP-safe.
/// </summary>
public static class Rasterizer
{
    public delegate void Plot(int x, int y);

    /// <summary>Bresenham line from (x0,y0) to (x1,y1), endpoints included.</summary>
    public static void Line(int x0, int y0, int x1, int y1, Plot plot)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            plot(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>Connected line segments through the points.</summary>
    public static void Polyline(IReadOnlyList<(int X, int Y)> pts, Plot plot)
    {
        for (var i = 0; i + 1 < pts.Count; i++)
            Line(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, plot);
    }

    /// <summary>Even-odd scanline fill of a polygon, plus its outline so thin or
    /// degenerate shapes still show.</summary>
    public static void FillPolygon(IReadOnlyList<(int X, int Y)> pts, Plot plot)
    {
        if (pts.Count < 3)
        {
            Polyline(pts, plot);
            return;
        }

        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (var p in pts)
        {
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        var crossings = new List<int>();
        for (var y = minY; y <= maxY; y++)
        {
            crossings.Clear();
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                int yi = pts[i].Y, yj = pts[j].Y;
                if ((yi <= y && yj > y) || (yj <= y && yi > y))
                {
                    var x = pts[j].X + (y - yj) * (pts[i].X - pts[j].X) / (yi - yj);
                    crossings.Add(x);
                }
            }
            crossings.Sort();
            for (var k = 0; k + 1 < crossings.Count; k += 2)
            {
                for (var x = crossings[k]; x <= crossings[k + 1]; x++) plot(x, y);
            }
        }

        // Edges, so the boundary is crisp even when the fill rounds inward.
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            Line(pts[j].X, pts[j].Y, pts[i].X, pts[i].Y, plot);
    }
}

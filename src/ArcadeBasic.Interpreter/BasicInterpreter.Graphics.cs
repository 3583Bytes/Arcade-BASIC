using ArcadeBasic.Parser.Ast;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Interpreter;

/// <summary>ECMA-116 §13 graphics statement execution. All coordinate mapping
/// and clipping lives in <see cref="GraphicsState"/>, shared with the VM.</summary>
public sealed partial class BasicInterpreter
{
    private FlowControl ExecSetBounds(SetBoundsStmt sb, ActivationRecord frame)
    {
        var l = GraphicsState.ToCoord(EvalNumeric(sb.Left, frame));
        var r = GraphicsState.ToCoord(EvalNumeric(sb.Right, frame));
        var b = GraphicsState.ToCoord(EvalNumeric(sb.Bottom, frame));
        var t = GraphicsState.ToCoord(EvalNumeric(sb.Top, frame));
        switch (sb.Object)
        {
            case GfxRectKind.Window: _gfx.SetWindow(l, r, b, t); break;
            case GfxRectKind.Viewport: _gfx.SetViewport(l, r, b, t); break;
            case GfxRectKind.DeviceWindow: if (_gfx.SetDeviceWindow(l, r, b, t)) _graphics.Clear(); break;
            case GfxRectKind.DeviceViewport: if (_gfx.SetDeviceViewport(l, r, b, t)) _graphics.Clear(); break;
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecSetClip(SetClipStmt sc, ActivationRecord frame)
    {
        var v = EvalString(sc.OnOff, frame).Trim().ToUpperInvariant();
        if (v == "ON") _gfx.ClipEnabled = true;
        else if (v == "OFF") _gfx.ClipEnabled = false;
        // Any other value is the spec's nonfatal case: keep the current setting.
        return FlowControl.Continue;
    }

    private FlowControl ExecSetStyle(SetStyleStmt ss, ActivationRecord frame)
    {
        var n = GraphicsState.ToIndex(EvalNumeric(ss.Index, frame));
        if (ss.Prim == GfxStyleKind.Point) { _gfx.PointStyle = n; _graphics.SetPointStyle(n); }
        else { _gfx.LineStyle = n; _graphics.SetLineStyle(n); }
        return FlowControl.Continue;
    }

    private FlowControl ExecSetColor(SetColorStmt scl, ActivationRecord frame)
    {
        var n = GraphicsState.ToIndex(EvalNumeric(scl.Index, frame));
        switch (scl.Target)
        {
            case GfxColorKind.Point: _gfx.PointColor = n; _graphics.SetColor(GfxColorTarget.Point, n); break;
            case GfxColorKind.Line: _gfx.LineColor = n; _graphics.SetColor(GfxColorTarget.Line, n); break;
            case GfxColorKind.Text: _gfx.TextColor = n; _graphics.SetColor(GfxColorTarget.Text, n); break;
            case GfxColorKind.Area: _gfx.AreaColor = n; _graphics.SetColor(GfxColorTarget.Area, n); break;
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecGraph(GraphStmt g, ActivationRecord frame)
    {
        var pts = new List<GfxPoint>(g.Points.Count);
        foreach (var c in g.Points)
        {
            pts.Add(new GfxPoint(
                GraphicsState.ToCoord(EvalNumeric(c.X, frame)),
                GraphicsState.ToCoord(EvalNumeric(c.Y, frame))));
        }
        switch (g.Kind)
        {
            case GfxGeometry.Points: _gfx.EmitPoints(pts, _graphics); break;
            case GfxGeometry.Lines: _gfx.EmitLines(pts, _graphics); break;
            case GfxGeometry.Area: _gfx.EmitArea(pts, _graphics); break;
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecGraphText(GraphTextStmt gt, ActivationRecord frame)
    {
        var at = new GfxPoint(
            GraphicsState.ToCoord(EvalNumeric(gt.AtX, frame)),
            GraphicsState.ToCoord(EvalNumeric(gt.AtY, frame)));
        string text;
        if (gt.Image is null)
        {
            text = EvalString(gt.Items[0], frame);
        }
        else
        {
            var parts = PictureFormat.Parse(EvalString(gt.Image, frame));
            var vals = new List<Value>(gt.Items.Count);
            foreach (var it in gt.Items) vals.Add(EvalExpr(it, frame));
            text = PictureFormat.Apply(parts, vals);
        }
        _gfx.EmitText(at, text, _graphics);
        return FlowControl.Continue;
    }

    private FlowControl ExecAskGfx(AskGfxStmt ag, ActivationRecord frame)
    {
        // GfxAskObject and GfxQuery share ordinals (see their definitions). The
        // shared GraphicsState.Query computes the value so the VM matches us.
        var q = (GfxQuery)ag.Object;
        for (var i = 0; i < ag.Targets.Count; i++)
        {
            WriteAssignableTarget(ag.Targets[i], _gfx.Query(q, i, _graphics), frame);
        }
        // A status-clause always reports success (0) for the values we answer.
        if (ag.Status is not null)
            WriteAssignableTarget(ag.Status, new NumericValue(GraphicsState.FromCoord(0)), frame);
        return FlowControl.Continue;
    }
}

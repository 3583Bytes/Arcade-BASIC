using ArcadeBasic.Core;

namespace ArcadeBasic.Parser.Ast;

// ECMA-116 §13 graphics statements. Multi-word objects are modelled with enums,
// following the OpenStmt/OpenAccess pattern in FileStmt.cs.

/// <summary>SET WINDOW|VIEWPORT|DEVICE WINDOW|DEVICE VIEWPORT l, r, b, t.</summary>
public sealed record class SetBoundsStmt(
    SourceSpan Span, GfxRectKind Object, Expr Left, Expr Right, Expr Bottom, Expr Top) : Stmt(Span);

/// <summary>SET CLIP "ON"|"OFF".</summary>
public sealed record class SetClipStmt(SourceSpan Span, Expr OnOff) : Stmt(Span);

/// <summary>SET POINT|LINE STYLE n.</summary>
public sealed record class SetStyleStmt(SourceSpan Span, GfxStyleKind Prim, Expr Index) : Stmt(Span);

/// <summary>SET POINT|LINE|TEXT|AREA COLOR n.</summary>
public sealed record class SetColorStmt(SourceSpan Span, GfxColorKind Target, Expr Index) : Stmt(Span);

/// <summary>ASK &lt;object&gt; targets [STATUS var].</summary>
public sealed record class AskGfxStmt(
    SourceSpan Span, GfxAskObject Object, IReadOnlyList<Expr> Targets, Expr? Status) : Stmt(Span);

/// <summary>CLEAR — clear the graphic display.</summary>
public sealed record class ClearStmt(SourceSpan Span) : Stmt(Span);

/// <summary>GRAPH POINTS|LINES|AREA : x,y; x,y; …</summary>
public sealed record class GraphStmt(SourceSpan Span, GfxGeometry Kind, IReadOnlyList<GfxCoord> Points) : Stmt(Span);

/// <summary>GRAPH TEXT, AT x,y (: str$ | , USING image : items).</summary>
public sealed record class GraphTextStmt(
    SourceSpan Span, Expr AtX, Expr AtY, Expr? Image, IReadOnlyList<Expr> Items) : Stmt(Span);

/// <summary>A coordinate pair in a GRAPH point-list.</summary>
public sealed record class GfxCoord(Expr X, Expr Y);

public enum GfxRectKind { Window, Viewport, DeviceWindow, DeviceViewport }
public enum GfxStyleKind { Point, Line }
public enum GfxColorKind { Point, Line, Text, Area }
public enum GfxGeometry { Points, Lines, Area }

/// <summary>What an ASK statement queries. Ordinals must stay in lock-step with
/// <c>ArcadeBasic.Runtime.GfxQuery</c> — the interpreter and compiler cast
/// between the two by ordinal.</summary>
public enum GfxAskObject
{
    Window, Viewport, DeviceWindow, DeviceViewport, DeviceSize, Clip,
    PointStyle, LineStyle, MaxPointStyle, MaxLineStyle,
    PointColor, LineColor, TextColor, AreaColor, MaxColor,
}

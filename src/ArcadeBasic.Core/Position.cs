namespace ArcadeBasic.Core;

/// <summary>A single point in a source file: byte offset relative to file start.</summary>
public readonly record struct Position(SourceFile File, int Offset)
{
    public (int Line, int Column) LineCol => File.GetLineCol(Offset);

    public override string ToString()
    {
        var (line, col) = LineCol;
        return $"{File.Path}:{line}:{col}";
    }
}

/// <summary>A contiguous range of characters in a source file.</summary>
public readonly record struct SourceSpan(SourceFile File, int Start, int Length)
{
    public int End => Start + Length;

    public Position StartPosition => new(File, Start);

    public Position EndPosition => new(File, End);

    public ReadOnlySpan<char> Text => File.Slice(Start, Length);

    public override string ToString()
    {
        var (line, col) = StartPosition.LineCol;
        return $"{File.Path}:{line}:{col}";
    }
}

namespace FullBasic.Core;

/// <summary>
/// A source file: filename, text content, and a precomputed line-start table
/// for fast offset → (line, col) mapping.
/// </summary>
public sealed class SourceFile
{
    private readonly int[] _lineStarts;

    public SourceFile(string path, string text)
    {
        Path = path;
        Text = text;
        _lineStarts = ComputeLineStarts(text);
    }

    public string Path { get; }

    public string Text { get; }

    /// <summary>Number of lines (1-based count; an empty file has 1 line).</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>Maps a 0-based byte offset to a 1-based (line, column) pair.</summary>
    public (int Line, int Column) GetLineCol(int offset)
    {
        if (offset < 0 || offset > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var idx = Array.BinarySearch(_lineStarts, offset);
        if (idx < 0)
        {
            idx = ~idx - 1;
        }

        var line = idx + 1;
        var column = offset - _lineStarts[idx] + 1;
        return (line, column);
    }

    /// <summary>Returns the full text of the given 1-based line, without trailing newline.</summary>
    public ReadOnlySpan<char> GetLineText(int line)
    {
        if (line < 1 || line > _lineStarts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        var start = _lineStarts[line - 1];
        var end = line < _lineStarts.Length ? _lineStarts[line] : Text.Length;

        // Strip trailing CR / LF.
        while (end > start && (Text[end - 1] == '\n' || Text[end - 1] == '\r'))
        {
            end--;
        }

        return Text.AsSpan(start, end - start);
    }

    /// <summary>Returns text in [start, start+length).</summary>
    public ReadOnlySpan<char> Slice(int start, int length) =>
        Text.AsSpan(start, length);

    private static int[] ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                starts.Add(i + 1);
            }
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }
}

namespace ArcadeBasic.Runtime;

/// <summary>
/// Abstraction over a Arcade BASIC file channel. Phase-5 ships only the DISPLAY
/// mode (text I/O); INTERNAL and BYTE modes are deferred. Each file has a
/// configured access type (Input / Output / OutIn) which gates read/write
/// operations.
/// </summary>
public abstract class BasicFile : IDisposable
{
    public abstract bool CanRead { get; }
    public abstract bool CanWrite { get; }

    /// <summary>True if opened RECTYPE INTERNAL — exact-value records accessed
    /// via WRITE #/READ # rather than the text PRINT #/INPUT # statements.</summary>
    public bool IsInternal { get; set; }

    /// <summary>Read a complete logical record (a line in DISPLAY mode).</summary>
    public abstract string? ReadLine();

    /// <summary>Append text and a newline.</summary>
    public abstract void WriteLine(string text);

    /// <summary>Append text without a newline.</summary>
    public abstract void Write(string text);

    public virtual void Flush() { }

    public abstract void Dispose();
}

/// <summary>DISPLAY-mode file: text I/O via StreamReader/StreamWriter, UTF-8.</summary>
public sealed class DisplayFile : BasicFile
{
    private readonly FileStream _stream;
    private readonly StreamReader? _reader;
    private readonly StreamWriter? _writer;

    public DisplayFile(string path, FileMode mode, FileAccess access)
    {
        _stream = new FileStream(path, mode, access, FileShare.Read);
        if ((access & FileAccess.Read) != 0)
        {
            _reader = new StreamReader(_stream, System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        }
        if ((access & FileAccess.Write) != 0)
        {
            _writer = new StreamWriter(_stream, System.Text.Encoding.UTF8,
                bufferSize: 1024, leaveOpen: true);
        }
    }

    public override bool CanRead => _reader is not null;

    public override bool CanWrite => _writer is not null;

    public override string? ReadLine()
    {
        if (_reader is null) throw new BasicRuntimeException(7001, "channel is not open for reading");
        return _reader.ReadLine();
    }

    public override void WriteLine(string text)
    {
        if (_writer is null) throw new BasicRuntimeException(7002, "channel is not open for writing");
        _writer.WriteLine(text);
    }

    public override void Write(string text)
    {
        if (_writer is null) throw new BasicRuntimeException(7002, "channel is not open for writing");
        _writer.Write(text);
    }

    public override void Flush()
    {
        _writer?.Flush();
    }

    public override void Dispose()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _reader?.Dispose();
        _stream.Dispose();
    }
}

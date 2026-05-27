namespace ArcadeBasic.Runtime;

/// <summary>
/// Maps channel numbers (Arcade BASIC's #n) to open BasicFile instances. Channel
/// 0 is reserved for the program's stdin/stdout (handled by the interpreter,
/// not this table). Channels 1..N hold files opened by user code.
/// </summary>
public sealed class ChannelTable : IDisposable
{
    private readonly Dictionary<int, BasicFile> _channels = new();

    public bool IsOpen(int channel) => _channels.ContainsKey(channel);

    public void Open(int channel, BasicFile file)
    {
        if (channel <= 0)
            throw new BasicRuntimeException(7003,
                $"channel #{channel} is reserved (0 = stdin/stdout)");
        if (_channels.ContainsKey(channel))
            throw new BasicRuntimeException(7004, $"channel #{channel} is already open");
        _channels[channel] = file;
    }

    public BasicFile Get(int channel)
    {
        if (channel <= 0)
            throw new BasicRuntimeException(7005, $"channel #{channel} is not a user channel");
        if (!_channels.TryGetValue(channel, out var f))
            throw new BasicRuntimeException(7006, $"channel #{channel} is not open");
        return f;
    }

    public void Close(int channel)
    {
        if (_channels.Remove(channel, out var f))
        {
            f.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var f in _channels.Values) f.Dispose();
        _channels.Clear();
    }
}

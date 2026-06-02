using System.Runtime.InteropServices;
using System.Text;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Cli;

/// <summary>
/// Wires the <see cref="AnsiGraphicsDevice"/> to the real console: detects an
/// interactive terminal, enables ANSI/VT processing on Windows, and restores the
/// terminal on exit (including Ctrl-C). When the terminal isn't interactive
/// (piped/redirected) it returns <c>null</c> and graphics fall back to the
/// no-op device, exactly as before.
/// </summary>
internal static class ConsoleGraphics
{
    private static AnsiGraphicsDevice? _current;

    static ConsoleGraphics()
    {
        // If the user aborts a running graphics program, still restore the screen.
        Console.CancelKeyPress += (_, _) => _current?.EndSession();
    }

    private static bool IsInteractive() =>
        !Console.IsOutputRedirected && !Console.IsInputRedirected;

    /// <summary>Create a console graphics device if stdout/stdin are a real
    /// terminal; otherwise <c>null</c>.</summary>
    public static AnsiGraphicsDevice? TryCreate()
    {
        if (!IsInteractive()) return null;
        try
        {
            if (Console.WindowWidth <= 0 || Console.WindowHeight <= 1) return null;
        }
        catch
        {
            return null;   // no real console attached
        }

        // Braille glyphs (U+2800…) need a UTF-8 console; otherwise the default
        // Windows code page renders each one as "?". Set this before capturing
        // Console.Out so the device (and the program's text output) use UTF-8.
        try { Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false); }
        catch { /* best effort */ }

        EnableVirtualTerminal();
        var device = new AnsiGraphicsDevice(Console.Out, CurrentSize);
        _current = device;
        return device;
    }

    /// <summary>End-of-program handling: show the final frame, hold for a
    /// keypress if it was a static drawing (so the user can see it), then restore
    /// the terminal.</summary>
    public static void Finish(AnsiGraphicsDevice? device, TextReader input)
    {
        if (device is null) return;
        device.Present();
        if (device.Active && input is PresentingTextReader r && r.LineReadCount == 0)
        {
            try { Console.In.ReadLine(); } catch { /* no input available */ }
        }
        device.EndSession();
        _current = null;
    }

    private static (int Cols, int Rows) CurrentSize()
    {
        try { return (Console.WindowWidth, Console.WindowHeight); }
        catch { return (80, 24); }
    }

    private static void EnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = GetStdHandle(StdOutputHandle);
            if (GetConsoleMode(handle, out var mode))
                SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch { /* best effort — modern Windows Terminal already has VT on */ }
    }

    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}

using ArcadeBasic.Tui;

// CI smoke-tests this without a TTY, so --version must short-circuit
// before any Terminal.Gui initialization touches the console.
if (args.Length == 1 && args[0] is "--version" or "-v")
{
    Console.WriteLine($"arcade-basic-tui {TuiInfo.Version}");
    return 0;
}

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine($"""
        arcade-basic-tui {TuiInfo.Version}
        Full-screen editor + runner for Arcade BASIC programs.

        usage:
          arcade-basic-tui              Open with an empty buffer.
          arcade-basic-tui <file.bas>   Open the given file.
          arcade-basic-tui --version    Print version and exit.
          arcade-basic-tui --help       Print this message.

        keys (inside the TUI):
          F5 / Ctrl-R   Run program          Esc / Shift-F5  Stop running program
          Ctrl-O        Open file            Ctrl-S          Save file
          Ctrl-L        Clear output         Ctrl-Q          Quit
        """);
    return 0;
}

string? initialFile = null;
if (args.Length == 1 && !args[0].StartsWith('-'))
{
    initialFile = args[0];
}
else if (args.Length > 1)
{
    Console.Error.WriteLine("arcade-basic-tui: too many arguments. Try --help.");
    return 2;
}

return TuiShell.Run(initialFile);

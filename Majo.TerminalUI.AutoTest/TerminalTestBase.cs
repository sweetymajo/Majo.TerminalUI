using Majo.Logging;

namespace Majo.TerminalUI.AutoTest;

public abstract class TerminalTestBase : IDisposable
{
    private readonly TextWriter _originalOut;

    protected TerminalTestBase()
    {
        Terminal.Shutdown();
        Logger.Shutdown();

        _originalOut = Console.Out;
        Output = new StringWriter();
        Console.SetOut(Output);
    }

    protected StringWriter Output { get; }

    public void Dispose()
    {
        try
        {
            Terminal.Shutdown();
        }
        finally
        {
            Logger.Shutdown();
            Console.SetOut(_originalOut);
            Output.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    protected void InitializeTerminal(LogLevel terminalLogLevel = LogLevel.Information,
        TerminalLogColorMode colorMode = TerminalLogColorMode.None)
    {
        Terminal.InitializeCore(CreateConfig(terminalLogLevel, colorMode), interactive: false);
    }

    protected static TerminalConfig CreateConfig(LogLevel terminalLogLevel = LogLevel.Information,
        TerminalLogColorMode colorMode = TerminalLogColorMode.None)
    {
        return new TerminalConfig
        {
            Logging = CreateLogOption(),
            TerminalLogLevel = terminalLogLevel,
            TerminalLogColorMode = colorMode
        };
    }

    protected static LogOption CreateLogOption()
    {
        return new LogOption
        {
            WriteToFile = false
        };
    }
}

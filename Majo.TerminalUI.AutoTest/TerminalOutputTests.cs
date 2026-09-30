using Xunit;

namespace Majo.TerminalUI.AutoTest;

public class TerminalOutputTests : TerminalTestBase
{
    [Fact]
    public void WriteLineAppendsEnvironmentNewLine()
    {
        InitializeTerminal();

        Terminal.WriteLine("hello");

        Assert.Equal($"hello{Environment.NewLine}", Output.ToString());
    }

    [Fact]
    public void WriteLineDoesNotAppendAnotherNewLineWhenTextAlreadyEndsWithLf()
    {
        InitializeTerminal();

        Terminal.WriteLine("hello\n");

        Assert.Equal("hello\n", Output.ToString());
    }

    [Fact]
    public void WriteLineWithEmptyTextWritesOneEnvironmentNewLine()
    {
        InitializeTerminal();

        Terminal.WriteLine(string.Empty);

        Assert.Equal(Environment.NewLine, Output.ToString());
    }
}

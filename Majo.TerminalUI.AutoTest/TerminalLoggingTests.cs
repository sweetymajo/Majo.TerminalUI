using Majo.Logging;
using Xunit;

namespace Majo.TerminalUI.AutoTest;

public class TerminalLoggingTests : TerminalTestBase
{
    [Fact]
    public void LogCallbackWritesFormattedEntryToTerminalOutput()
    {
        InitializeTerminal();

        Logger.Information("hello", "AutoTest");

        string output = Output.ToString();

        Assert.Contains("[INFO] [AutoTest] hello", output);
        Assert.EndsWith(Environment.NewLine, output);
    }

    [Fact]
    public void TerminalLogLevelFiltersLowerLevelEntries()
    {
        InitializeTerminal(LogLevel.Warning);

        Logger.Information("hidden", "AutoTest");
        Logger.Warning("visible", "AutoTest");

        string output = Output.ToString();

        Assert.DoesNotContain("hidden", output);
        Assert.Contains("[WARN] [AutoTest] visible", output);
    }

    [Fact]
    public void RepeatedInitializeDoesNotSubscribeLogCallbackTwice()
    {
        TerminalConfig config = CreateConfig();

        Terminal.InitializeCore(config, interactive: false);
        Terminal.InitializeCore(config, interactive: false);

        Logger.Information("once", "AutoTest");

        string output = Output.ToString();
        int occurrences = output.Split("once").Length - 1;

        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void ShutdownUnsubscribesLogCallbackWithoutStoppingExternalLogger()
    {
        Assert.True(Logger.Initialize(CreateLogOption()));
        Terminal.InitializeCore(CreateConfig(), interactive: false);

        Logger.Information("before", "AutoTest");
        Assert.Contains("before", Output.ToString());

        Output.GetStringBuilder().Clear();

        Terminal.Shutdown();
        Logger.Information("after", "AutoTest");

        Assert.Equal(string.Empty, Output.ToString());
        Assert.False(Logger.Initialize(CreateLogOption()));
    }

    [Fact]
    public void FormatLogEntryWithoutColorHasExpectedShape()
    {
        DateTime timestamp = new(2026, 10, 1, 12, 34, 56);
        LogEntry entry = new(timestamp, LogLevel.Information, "AutoTest", "hello");

        string actual = Terminal.FormatLogEntry(
            entry,
            TerminalLogColorMode.None,
            supportsColor: false);

        Assert.Equal(
            $"[2026-10-01 12:34:56] [INFO] [AutoTest] hello{Environment.NewLine}",
            actual);
    }

    [Fact]
    public void FormatLogEntryIncludesException()
    {
        InvalidOperationException error = new("boom");
        LogEntry entry = new(
            new DateTime(2026, 10, 1, 12, 34, 56),
            LogLevel.Error,
            "AutoTest",
            "failed",
            error);

        string actual = Terminal.FormatLogEntry(
            entry,
            TerminalLogColorMode.None,
            supportsColor: false);

        Assert.Contains("[ERROR] [AutoTest] failed", actual);
        Assert.Contains(error.ToString(), actual);
    }

    [Fact]
    public void FormatLogEntryDoesNotUseColorWhenTerminalDoesNotSupportColor()
    {
        LogEntry entry = new(
            new DateTime(2026, 10, 1, 12, 34, 56),
            LogLevel.Information,
            "AutoTest",
            "hello");

        string actual = Terminal.FormatLogEntry(
            entry,
            TerminalLogColorMode.TrueColor,
            supportsColor: false);

        Assert.DoesNotContain("\e[", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatLogEntryDoesNotUseColorWhenColorModeIsNone()
    {
        LogEntry entry = new(
            new DateTime(2026, 10, 1, 12, 34, 56),
            LogLevel.Information,
            "AutoTest",
            "hello");

        string actual = Terminal.FormatLogEntry(
            entry,
            TerminalLogColorMode.None,
            supportsColor: true);

        Assert.DoesNotContain("\e[", actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LogLevel.Verbose, TerminalLogColorMode.Ansi16, "\e[90m")]
    [InlineData(LogLevel.Debug, TerminalLogColorMode.Ansi16, "\e[94m")]
    [InlineData(LogLevel.Information, TerminalLogColorMode.Ansi16, "\e[92m")]
    [InlineData(LogLevel.Warning, TerminalLogColorMode.Ansi16, "\e[93m")]
    [InlineData(LogLevel.Error, TerminalLogColorMode.Ansi16, "\e[91m")]
    [InlineData(LogLevel.Fatal, TerminalLogColorMode.Ansi16, "\e[31m")]
    [InlineData(LogLevel.Verbose, TerminalLogColorMode.TrueColor, "\e[38;2;118;118;118m")]
    [InlineData(LogLevel.Debug, TerminalLogColorMode.TrueColor, "\e[38;2;59;120;255m")]
    [InlineData(LogLevel.Information, TerminalLogColorMode.TrueColor, "\e[38;2;22;198;12m")]
    [InlineData(LogLevel.Warning, TerminalLogColorMode.TrueColor, "\e[38;2;249;241;165m")]
    [InlineData(LogLevel.Error, TerminalLogColorMode.TrueColor, "\e[38;2;231;72;86m")]
    [InlineData(LogLevel.Fatal, TerminalLogColorMode.TrueColor, "\e[38;2;197;15;31m")]
    public void GetLogLevelColorReturnsExpectedEscapeSequence(
        LogLevel level,
        TerminalLogColorMode mode,
        string expected)
    {
        Assert.Equal(expected, Terminal.GetLogLevelColor(level, mode));
    }

    [Fact]
    public void ColoredFormatResetsForegroundAfterLevelText()
    {
        LogEntry entry = new(
            new DateTime(2026, 10, 1, 12, 34, 56),
            LogLevel.Warning,
            "AutoTest",
            "hello");

        string actual = Terminal.FormatLogEntry(
            entry,
            TerminalLogColorMode.Ansi16,
            supportsColor: true);

        Assert.Contains($"[\e[93m{entry.LevelText}\e[39m]", actual);
    }
}

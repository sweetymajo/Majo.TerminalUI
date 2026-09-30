using Majo.Logging;
using Xunit;

namespace Majo.TerminalUI.AutoTest;

public class TerminalConfigTests
{
    [Fact]
    public void DefaultsAreExpected()
    {
        TerminalConfig config = new();

        Assert.Equal(64 * 1024, config.CommandBufferSize);
        Assert.Equal(100, config.HistoryCount);
        Assert.Equal(100, config.PollInterval);
        Assert.Equal("> ", config.Prompt);
        Assert.False(config.MultiLine);
        Assert.NotNull(config.Logging);
        Assert.Equal(LogLevel.Information, config.TerminalLogLevel);
        Assert.Equal(TerminalLogColorMode.TrueColor, config.TerminalLogColorMode);
    }
}

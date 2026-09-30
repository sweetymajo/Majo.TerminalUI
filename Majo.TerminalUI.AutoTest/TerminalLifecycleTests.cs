using Majo.Logging;
using Xunit;

namespace Majo.TerminalUI.AutoTest;

public class TerminalLifecycleTests : TerminalTestBase
{
    [Fact]
    public void InitializeIsIdempotent()
    {
        TerminalConfig config = CreateConfig();

        Terminal.InitializeCore(config, interactive: false);
        Terminal.InitializeCore(config, interactive: false);

        Terminal.WriteLine("alive");

        Assert.Equal($"alive{Environment.NewLine}", Output.ToString());
    }

    [Fact]
    public void ShutdownIsIdempotent()
    {
        InitializeTerminal();

        Terminal.Shutdown();
        Terminal.Shutdown();

        Assert.Throws<InvalidOperationException>(() => Terminal.WriteLine("test"));
    }

    [Fact]
    public async Task RunAsyncReturnsImmediatelyInNonInteractiveEnvironment()
    {
        InitializeTerminal();

        await Terminal.RunAsync(TestContext.Current.CancellationToken);
        await Terminal.RunAsync(TestContext.Current.CancellationToken);

        Terminal.WriteLine("still initialized");

        Assert.Equal($"still initialized{Environment.NewLine}", Output.ToString());
    }

    [Fact]
    public async Task RunModalAsyncRejectsNonInteractiveEnvironment()
    {
        InitializeTerminal();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Terminal.RunModalAsync(() => 1, TestContext.Current.CancellationToken));

        Assert.Contains("does not support interactive terminal operations", exception.Message);
    }

    [Fact]
    public void TerminalShutsDownLoggerWhenItOwnsLogger()
    {
        InitializeTerminal();

        Terminal.Shutdown();

        Assert.True(Logger.Initialize(CreateLogOption()));
    }

    [Fact]
    public void TerminalDoesNotShutdownExternallyOwnedLogger()
    {
        Assert.True(Logger.Initialize(CreateLogOption()));
        Terminal.InitializeCore(CreateConfig(), interactive: false);

        Terminal.Shutdown();

        Assert.False(Logger.Initialize(CreateLogOption()));
    }
}

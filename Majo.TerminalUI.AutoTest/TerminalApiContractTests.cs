using Xunit;

namespace Majo.TerminalUI.AutoTest;

public class TerminalApiContractTests : TerminalTestBase
{
    [Fact]
    public void WriteLineBeforeInitializeThrows()
    {
        Assert.Throws<InvalidOperationException>(() => Terminal.WriteLine("test"));
    }

    [Fact]
    public async Task RunAsyncBeforeInitializeThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            Terminal.RunAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunModalAsyncBeforeInitializeThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            Terminal.RunModalAsync(() => 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SelectAsyncBeforeInitializeThrows()
    {
        SelectionOption<int>[] options = [new(1, "One")];

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Terminal.SelectAsync("Select", options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InputAsyncBeforeInitializeThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            Terminal.InputAsync("Input", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void WriteLineRejectsNull()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => Terminal.WriteLine(null!));

        Assert.Equal("text", exception.ParamName);
    }

    [Fact]
    public async Task RunModalAsyncRejectsNullAction()
    {
        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => Terminal.RunModalAsync<int>(null!, TestContext.Current.CancellationToken));

        Assert.Equal("action", exception.ParamName);
    }

    [Fact]
    public async Task SelectAsyncRejectsNullTitle()
    {
        SelectionOption<int>[] options = [new(1, "One")];

        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => Terminal.SelectAsync(null!, options, TestContext.Current.CancellationToken));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public async Task SelectAsyncRejectsNullOptions()
    {
        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => Terminal.SelectAsync<int>("Select", null!, TestContext.Current.CancellationToken));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public async Task InputAsyncRejectsNullTitle()
    {
        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => Terminal.InputAsync(null!, ct: TestContext.Current.CancellationToken));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public async Task SelectAsyncHonorsPreCanceledTokenBeforeTerminalOwnership()
    {
        SelectionOption<int>[] options = [new(1, "One")];
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Terminal.SelectAsync("Select", options, cts.Token));
    }
}

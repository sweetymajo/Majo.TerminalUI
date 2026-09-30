using Xunit;

namespace Majo.TerminalUI.AutoTest;

public class SharpromptBuildContractTests
{
    // This private nested enum is intentional.
    // If Sharprompt.SourceGenerator leaks transitively into a normal
    // Majo.TerminalUI consumer again, this project should fail to compile.
    private enum PrivateConsumerEnum
    {
        Value
    }

    [Fact]
    public void PrivateNestedEnumCompilesInTerminalUiConsumer()
    {
        Assert.Equal(PrivateConsumerEnum.Value, PrivateConsumerEnum.Value);
    }
}

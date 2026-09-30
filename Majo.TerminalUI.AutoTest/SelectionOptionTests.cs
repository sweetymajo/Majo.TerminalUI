using Xunit;

namespace Majo.TerminalUI.AutoTest;

public class SelectionOptionTests
{
    [Fact]
    public void StoresValueAndDisplayText()
    {
        SelectionOption<int> option = new(42, "Answer");

        Assert.Equal(42, option.Value);
        Assert.Equal("Answer", option.Text);
    }
}

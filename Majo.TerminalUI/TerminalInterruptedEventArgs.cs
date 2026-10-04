namespace Majo.TerminalUI;

/// <summary>
/// Event arguments for the terminal interrupted event.
/// </summary>
public class TerminalInterruptedEventArgs
{
    /// <summary>
    /// Gets or sets a value indicating whether the terminal should continue running after the interruption.
    /// </summary>
    public bool Continue { get; set; }
}
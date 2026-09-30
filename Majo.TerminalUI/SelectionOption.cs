namespace Majo.TerminalUI;

/// <summary>
/// Represents an option in a terminal selection
/// </summary>
/// <param name="Value">The value represented by the option</param>
/// <param name="Text">The text displayed for the option</param>
/// <typeparam name="T">The option value type</typeparam>
public record SelectionOption<T>(T Value, string Text);
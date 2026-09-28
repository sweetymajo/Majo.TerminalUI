namespace Majo.Terminal;

/// <summary>
/// Represents the color mode for terminal log output
/// </summary>
public enum TerminalLogColorMode
{
    /// <summary>
    /// No color output
    /// </summary>
    None,
    
    /// <summary>
    /// ANSI 16 color output
    /// </summary>
    Ansi16,
    
    /// <summary>
    /// 24-bit RGB color output
    /// </summary>
    TrueColor
}
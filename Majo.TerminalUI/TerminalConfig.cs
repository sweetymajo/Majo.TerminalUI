using Majo.Logging;

namespace Majo.TerminalUI;

/// <summary>
/// Represents the configuration options for the terminal
/// </summary>
public class TerminalConfig
{
    /// <summary>
    /// Maximum size of the command buffer in UTF-8 bytes
    /// </summary>
    public int CommandBufferSize { get; set; } = 64 * 1024;

    /// <summary>
    /// Maximum number of commands to keep in history
    /// </summary>
    public int HistoryCount { get; set; } = 100;
    
    /// <summary>
    /// Interval in milliseconds to the poll for new input
    /// </summary>
    public int PollInterval { get; set; } = 100;
    
    /// <summary>
    /// Prompt string to display before each command
    /// </summary>
    public string Prompt { get; set; } = "> ";
    
    /// <summary>
    /// Whether to allow multi-line input in the terminal
    /// </summary>
    public bool MultiLine { get; set; } = false;
    
    /// <summary>
    /// Options for configuring logging behavior
    /// </summary>
    public LogOption Logging { get; set; } = new();
    
    /// <summary>
    /// The log level for terminal output
    /// </summary>
    public LogLevel TerminalLogLevel { get; set; } = LogLevel.Information;
    
    /// <summary>
    /// The color mode for terminal log output
    /// </summary>
    public TerminalLogColorMode TerminalLogColorMode { get; set; } = TerminalLogColorMode.TrueColor;
}
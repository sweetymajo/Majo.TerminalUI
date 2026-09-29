using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Majo.TerminalUI;

/// <summary>
/// Provides native methods for interacting with the Windows console
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Win32TerminalNative
{
    /// <summary>
    /// Gets the standard output handle for the console
    /// </summary>
    internal const int StdOutHandle = -11;
    
    /// <summary>
    /// Indicates an invalid handle value for console operations
    /// </summary>
    internal static readonly IntPtr InvalidHandleValue = new(-1);
    
    /// <summary>
    /// Enables processed output for the console, allowing special characters to be interpreted
    /// </summary>
    internal const uint EnableProcessedOutput = 0x0001;
    
    /// <summary>
    /// Enables virtual terminal processing for the console, allowing ANSI escape sequences to be interpreted
    /// </summary>
    internal const uint EnableVirtualTerminalProcessing = 0x0004;
    
    /// <summary>
    /// Gets the standard input handle for the console
    /// </summary>
    /// <param name="nStdHandle">The standard handle to retrieve (e.g., StdOutHandle)</param>
    /// <returns>The handle to the standard input/output/error stream</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int nStdHandle);
    
    /// <summary>
    /// Retrieves the current mode of the console input or output buffer
    /// </summary>
    /// <param name="hConsoleHandle">A handle to the console input or output buffer</param>
    /// <param name="lpMode">A pointer to a variable that receives the current mode</param>
    /// <returns>true if the function succeeds; otherwise, false</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
    
    /// <summary>
    /// Sets the mode of the console input or output buffer
    /// </summary>
    /// <param name="hConsoleHandle">A handle to the console input or output buffer</param>
    /// <param name="dwMode">The mode to set for the console input or output buffer</param>
    /// <returns>true if the function succeeds; otherwise, false</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
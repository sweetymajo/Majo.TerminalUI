using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Majo.LineEditor;
using Majo.Logging;
using Sharprompt;
using LineEditing = Majo.LineEditor.LineEditor;

#if NET9_0_OR_GREATER
using LockType = System.Threading.Lock;
#else
using LockType = System.Object;
#endif

namespace Majo.TerminalUI;


public static class Terminal
{
    /// <summary>
    /// Event triggered when the user enters a command
    /// </summary>
    public static event Action<string>? CommandEntered;
    
    /// <summary>
    /// Event triggered when the user interrupts the command loop
    /// </summary>
    public static event Action? Interrupted;
    
    /// <summary>
    /// Configuration for the terminal
    /// </summary>
    private static TerminalConfig? _config;
    
    /// <summary>
    /// Lock to synchronize access to the terminal
    /// </summary>
    private static readonly LockType TerminalLock = new();
    
    /// <summary>
    /// Queue to buffer output lines before they are written to the terminal
    /// </summary>
    private static readonly Queue<string> BufferedOutput = new();
    
    /// <summary>
    /// Semaphore to ensure that only one modal operation (like a prompt) is active at a time
    /// </summary>
    private static readonly SemaphoreSlim ModalSemaphore = new(1, 1);
    
    /// <summary>
    /// Indicates whether the current environment supports interactive terminal operations
    /// </summary>
    private static bool _interactive;
    
    /// <summary>
    /// Indicates whether the terminal has been initialized
    /// </summary>
    private static bool _initialized;

    /// <summary>
    /// The line editor instance for handling user input in interactive mode
    /// </summary>
    private static LineEditing? _lineEditor;
    
    /// <summary>
    /// Cancellation token source for the active read operation
    /// </summary>
    private static CancellationTokenSource? _activeReadCts;
    
    /// <summary>
    /// Task representing the active read operation
    /// </summary>
    private static Task<ReadResult>? _activeReadTask;
    
    /// <summary>
    /// Indicates whether the command loop is currently active
    /// </summary>
    private static bool _commandLoopActive;
    
    /// <summary>
    /// Indicates whether a modal operation currently owns the terminal
    /// </summary>
    private static bool _modalActive;
    
    /// <summary>
    /// Indicates whether the original Sharprompt.ThrowExceptionOnCancel value has been stored
    /// </summary>
    private static bool _originalThrowExceptionOnCancel;
    
    /// <summary>
    /// Indicates whether Sharprompt has been configured for the terminal
    /// </summary>
    private static bool _sharpromptConfigured;

    /// <summary>
    /// Indicates whether the terminal owns the logger instance
    /// </summary>
    private static bool _ownsLogger;
    
    /// <summary>
    /// The Windows console output handle, used for enabling virtual terminal processing
    /// </summary>
    private static IntPtr _windowsOutputHandle;
    
    /// <summary>
    /// Stores the original Windows console output mode before enabling virtual terminal processing
    /// </summary>
    private static uint _originalWindowsOutputMode;
    
    /// <summary>
    /// Indicates whether the Windows console output mode has been changed to enable virtual terminal processing
    /// </summary>
    private static bool _windowsOutputModeChanged;

    /// <summary>
    /// Indicates whether the terminal supports color output
    /// </summary>
    private static bool SupportsColor => !Console.IsOutputRedirected;
    
    /// <summary>
    /// Gets the terminal configuration, throwing an exception if the terminal has not been initialized
    /// </summary>
    private static TerminalConfig Config
    {
        get
        {
            ThrowIfNotInitialized();
            return _config!;
        }
    }

    /// <summary>
    /// Initializes the terminal with the specified configuration
    /// </summary>
    /// <param name="config">Optional configuration for the terminal</param>
    public static void  Initialize(TerminalConfig? config = null)
    {
        lock (TerminalLock)
        {
            if (_initialized)
            {
                return;
            }
            
            var actualConfig = config ?? new TerminalConfig();

            LineEditing? lineEditor = null;
            bool ownsLogger = false;
            bool logSubscribed = false;

            try
            {
                bool supportedSystem = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
                bool interactive = supportedSystem && !Console.IsInputRedirected && !Console.IsOutputRedirected;

                if (!supportedSystem)
                {
                    throw new PlatformNotSupportedException(
                        "The current operating system does not support interactive terminal operations.");
                }
                
                if (interactive)
                {
                    _originalThrowExceptionOnCancel = Prompt.ThrowExceptionOnCancel;
                    Prompt.ThrowExceptionOnCancel = true;
                    _sharpromptConfigured = true;
                    
                    if (OperatingSystem.IsWindows())
                    {
                        InitializeWindowsTerminal();
                    }
            
                    lineEditor = new LineEditing(new LineEditorOption
                    {
                        CommandBufferSize = actualConfig.CommandBufferSize,
                        HistoryCount = actualConfig.HistoryCount,
                        PollInterval = actualConfig.PollInterval,
                        Prompt = actualConfig.Prompt,
                        MultiLine = actualConfig.MultiLine
                    });
                }
                
                ownsLogger = Logger.Initialize(actualConfig.Logging);
                
                BufferedOutput.Clear();
                
                Logger.LogWritten += OnLogWritten;
                logSubscribed = true;
                
                _config = actualConfig;
                _lineEditor = lineEditor;
                _interactive = interactive;
                _ownsLogger = ownsLogger;
                
                _activeReadCts = null;
                _activeReadTask = null;
                _commandLoopActive = false;
                _modalActive = false;
                
                _initialized = true;
            }
            catch
            {
                if (logSubscribed)
                {
                    Logger.LogWritten -= OnLogWritten;
                }
                
                if (ownsLogger)
                {
                    Logger.Shutdown();
                }
                
                lineEditor?.Dispose();
                
                if (_sharpromptConfigured)
                {
                    Prompt.ThrowExceptionOnCancel = _originalThrowExceptionOnCancel;
                    _sharpromptConfigured = false;
                }
                
                _config = null;
                _lineEditor = null;
                _interactive = false;
                
                RestoreWindowsTerminal();
                
                _initialized = false;
                
                throw;
            }
        }
    }

    /// <summary>
    /// Runs the command loop asynchronously, reading commands from the user and invoking the CommandEntered event
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    public static async Task RunAsync(CancellationToken ct = default)
    {
        lock (TerminalLock)
        {
            ThrowIfNotInitialized();
            
            if (_commandLoopActive)
            {
                throw new InvalidOperationException("Command loop is already running.");
            }
            _commandLoopActive = true;
        }

        try
        {
            if (!_interactive)
            {
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                ReadResult result;

                try
                {
                    result = await ReadCommandAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    // This means the read was canceled by a modal operation, so we just continue
                    continue;
                }

                switch (result.Status)
                {
                    case ReadStatus.Accepted:
                        if (!string.IsNullOrWhiteSpace(result.Text))
                        {
                            CommandEntered?.Invoke(result.Text);
                        }

                        break;
                    case ReadStatus.Interrupted:
                        Interrupted?.Invoke();
                        return;
                    case ReadStatus.EndOfInput:
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected read status: {result.Status}");
                }
            }
        }
        finally
        {
            lock (TerminalLock)
            {
                _commandLoopActive = false;
            }
        }
    }

    /// <summary>
    /// Runs a modal operation that temporarily takes control of the terminal
    /// </summary>
    /// <param name="action">Operation</param>
    /// <param name="ct">Cancellation token</param>
    /// <typeparam name="T">Operation return type</typeparam>
    /// <returns>Operation result</returns>
    // ReSharper disable once MemberCanBePrivate.Global
    public static async Task<T> RunModalAsync<T>(Func<T> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (TerminalLock)
        {
            ThrowIfNotInitialized();

            if (!_interactive)
            {
                throw new InvalidOperationException(
                    "The current environment does not support interactive terminal operations.");
            }
        }
        
        await ModalSemaphore.WaitAsync(ct).ConfigureAwait(false);
        
        bool modalActive = false;
        bool alternateScreenEntered = false;

        try
        {
            CancellationTokenSource? activeReadCts;
            Task<ReadResult>? activeReadTask;

            lock (TerminalLock)
            {
                ThrowIfNotInitialized();

                if (_modalActive)
                {
                    throw new InvalidOperationException("A modal operation is already active.");
                }

                _modalActive = true;
                modalActive = true;

                activeReadCts = _activeReadCts;
                activeReadTask = _activeReadTask;
            }

            bool readCanceled = false;
            
            bool readWasActive = activeReadTask is { IsCompleted: false };

            if (readWasActive)
            {
                // ReSharper disable once MethodHasAsyncOverload
                activeReadCts?.Cancel();

                try
                {
                    await activeReadTask!.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (activeReadCts?.IsCancellationRequested == true)
                {
                    readCanceled = true;
                }

                if (readCanceled)
                {
                    ClearStoppedEditorLine();
                }
            }

            ct.ThrowIfCancellationRequested();

            EnterAlternateScreen();
            alternateScreenEntered = true;

            return action();
        }
        finally
        {
            try
            {
                if (alternateScreenEntered)
                {
                    ExitAlternateScreen();
                }
            }
            finally
            {
                if (modalActive)
                {
                    lock (TerminalLock)
                    {
                        try
                        {
                            FlushBufferedOutput();
                        }
                        finally
                        {
                            _modalActive = false;
                        }
                    }
                }
                
                ModalSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// Displays a selection prompt and returns the selected value
    /// </summary>
    /// <param name="title">Prompt title</param>
    /// <param name="options">Available options</param>
    /// <param name="ct">Cancellation token</param>
    /// <typeparam name="T">Option value type</typeparam>
    /// <returns>The selected value</returns>
    public static async Task<T> SelectAsync<T>(string title, IReadOnlyList<SelectionOption<T>> options, 
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            ct.ThrowIfCancellationRequested();

            SelectionOption<T> selected = await RunModalAsync(
                () => Prompt.Select(title, options, textSelector: option => option.Text), ct);

            return selected.Value;
        }
        catch (PromptCanceledException e)
        {
            throw new OperationCanceledException("The terminal selection was canceled.", e, ct);
        }
    }
    
    /// <summary>
    /// Displays a text-input prompt
    /// </summary>
    /// <param name="title">Prompt title</param>
    /// <param name="validator">Optional input validator that returns an error message for invalid input</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The entered text</returns>
    public static async Task<string> InputAsync(string title, Func<string, string?>? validator = null, 
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(title);

        try
        {
            return await RunModalAsync(() =>
            {
                while (true)
                {
                    string value = Prompt.Input<string?>(title) ?? string.Empty;
                    string? error = validator?.Invoke(value);

                    if (error is null)
                    {
                        return value;
                    }

                    Console.WriteLine(error);
                    Console.WriteLine();
                }
            }, ct).ConfigureAwait(false);
        }
        catch (PromptCanceledException e)
        {
            throw new OperationCanceledException("The terminal input was canceled.", e, ct);
        }
    }
    
    /// <summary>
    /// Writes text to the terminal
    /// </summary>
    /// <param name="text">Text</param>
    // ReSharper disable once MemberCanBePrivate.Global
    public static void WriteLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (TerminalLock)
        {
            ThrowIfNotInitialized();
            
            if (!text.EndsWith('\n'))
            {
                text += Environment.NewLine;
            }
            
            WriteCore(text);
        }
    }
    
    /// <summary>
    /// Disposes the terminal, cancelling any active read operations and releasing resources
    /// </summary>
    public static void Shutdown()
    {
        lock (TerminalLock)
        {
            if (!_initialized)
            {
                return;
            }
            
            if (_commandLoopActive)
            {
                throw new InvalidOperationException("Cannot shutdown while the command loop is active.");
            }
            
            if (_modalActive)
            {
                throw new InvalidOperationException("Cannot shutdown while a modal operation is active.");
            }
            
            Logger.LogWritten -= OnLogWritten;
            
            try
            {
                _lineEditor?.Dispose();
                

            }
            finally
            {
                try
                {
                    if (_ownsLogger)
                    {
                        Logger.Shutdown();
                    }
                }
                finally
                {
                    if (_sharpromptConfigured)
                    {
                        Prompt.ThrowExceptionOnCancel = _originalThrowExceptionOnCancel;
                        _sharpromptConfigured = false;
                    }

                    RestoreWindowsTerminal();
                
                    _ownsLogger = false;
                
                    _windowsOutputHandle = IntPtr.Zero;
                    _originalWindowsOutputMode = 0;
                
                    _activeReadCts = null;
                    _activeReadTask = null;
                    _lineEditor = null;
                    _config = null;
            
                    _interactive = false;
            
                    BufferedOutput.Clear();
                
                    CommandEntered = null;
                    Interrupted = null;
                
                    _initialized = false;
                }
            }
        }
    }
    
    /// <summary>
    /// Reads a command from the user asynchronously
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Read result</returns>
    private static async Task<ReadResult> ReadCommandAsync(CancellationToken ct = default)
    {
        using CancellationTokenSource readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        await ModalSemaphore.WaitAsync(ct).ConfigureAwait(false);
        
        Task<ReadResult> readTask;

        try
        {
            lock (TerminalLock)
            {
                ThrowIfNotInitialized();

                readTask = _lineEditor!.ReadLineAsync(readCts.Token).AsTask();

                _activeReadCts = readCts;
                _activeReadTask = readTask;
            }
        }
        finally
        {
            ModalSemaphore.Release();
        }

        try
        {
            return await readTask.ConfigureAwait(false);
        }
        finally
        {
            lock (TerminalLock)
            {
                if (ReferenceEquals(readTask, _activeReadTask))
                {
                    _activeReadCts = null;
                    _activeReadTask = null;
                }
            }
        }
    }

    /// <summary>
    /// Writes text to the terminal, buffering it if a modal operation is active
    /// </summary>
    /// <param name="text">Text</param>
    private static void WriteCore(string text)
    {
        if (_modalActive)
        {
            BufferedOutput.Enqueue(text);
            return;
        }
        
        WriteDirect(text);
    }
    
    /// <summary>
    /// Writes text directly to the console
    /// </summary>
    /// <param name="text">Text</param>
    private static void WriteDirect(string text)
    {
        if (_interactive && _lineEditor is not null && _activeReadTask is { IsCompleted: false })
        {
            _lineEditor.WriteAbove(text);
            return;
        }
        
        Console.Out.Write(text);
        Console.Out.Flush();
    }
    
    /// <summary>
    /// Flushes any buffered output to the console
    /// </summary>
    private static void FlushBufferedOutput()
    {
        while (0 < BufferedOutput.Count)
        {
            Console.Out.Write(BufferedOutput.Dequeue());
        }
        
        Console.Out.Flush();
    }

    /// <summary>
    /// Handles log entries written to the logger, formatting and writing them to the terminal
    /// </summary>
    /// <param name="entry">The log entry</param>
    private static void OnLogWritten(LogEntry entry)
    {
        lock(TerminalLock)
        {
            if (!_initialized)
            {
                return;
            }
            
            if ((int)entry.Level < (int)Config.TerminalLogLevel)
            {
                return;
            }
        
            WriteCore(FormatLogEntry(entry));
        }
    }

    /// <summary>
    /// Formats a log entry for terminal output
    /// </summary>
    /// <param name="entry">The log entry</param>
    /// <returns>The formatted log entry string</returns>
    private static string FormatLogEntry(LogEntry entry)
    {
        var builder = new StringBuilder();
        
        builder.Append('[').Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")).Append("] [");
        
        bool useColor = SupportsColor && Config.TerminalLogColorMode != TerminalLogColorMode.None;

        if (useColor)
        {
            builder.Append(GetLogLevelColor(entry.Level));
        }

        builder.Append(entry.LevelText);
        
        if (useColor)
        {
            builder.Append("\e[39m");
        }
        
        builder.Append("] [").Append(entry.Tag).Append("] ").Append(entry.Content).AppendLine();
        
        if (entry.Exception is not null)
        {
            builder.AppendLine(entry.Exception.ToString());
        }
        
        return builder.ToString();
    }
    
    /// <summary>
    /// Returns the ANSI escape code for the specified log level based on the configured color mode
    /// </summary>
    /// <param name="level">The log level</param>
    /// <returns>The ANSI escape code for the log level color</returns>
    private static string GetLogLevelColor(LogLevel level)
    {
        return Config.TerminalLogColorMode switch
        {
            TerminalLogColorMode.None => string.Empty,
            TerminalLogColorMode.Ansi16 => GetAnsi16LogLevelColor(level),
            TerminalLogColorMode.TrueColor => GetTrueColorLogLevelColor(level),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
    
    /// <summary>
    /// Returns the ANSI escape code for the specified log level in 16-color mode
    /// </summary>
    /// <param name="level">The log level</param>
    /// <returns>The ANSI escape code for the log level color</returns>
    private static string GetAnsi16LogLevelColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Verbose => "\e[90m",
            LogLevel.Debug => "\e[94m",
            LogLevel.Information => "\e[92m",
            LogLevel.Warning => "\e[93m",
            LogLevel.Error => "\e[91m",
            LogLevel.Fatal => "\e[31m",
            _ => "\e[39m"
        };
    }
    
    /// <summary>
    /// Returns the ANSI escape code for the specified log level in true color (24-bit RGB)
    /// </summary>
    /// <param name="level">The log level</param>
    /// <returns>The ANSI escape code for the log level color</returns>
    private static string GetTrueColorLogLevelColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Verbose => "\e[38;2;118;118;118m",
            LogLevel.Debug => "\e[38;2;59;120;255m",
            LogLevel.Information => "\e[38;2;22;198;12m",
            LogLevel.Warning => "\e[38;2;249;241;165m",
            LogLevel.Error => "\e[38;2;231;72;86m",
            LogLevel.Fatal => "\e[38;2;197;15;31m",
            _ => "\e[39m"
        };
    }
    
    /// <summary>
    /// Throws an exception if the terminal has not been initialized
    /// </summary>
    private static void ThrowIfNotInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Terminal has not been initialized. Call Initialize() first.");
        }
    }

    /// <summary>
    /// Enters the alternate screen buffer
    /// </summary>
    private static void EnterAlternateScreen()
    {
        // Enter the alternate screen buffer
        Console.Out.Write("\x1b[?1049h\x1b[H\x1b[2J");
        Console.Out.Flush();
    }
    
    /// <summary>
    /// Exits the alternate screen buffer
    /// </summary>
    private static void ExitAlternateScreen()
    {
        // Exit the alternate screen buffer
        Console.Out.Write("\x1b[?1049l");
        Console.Out.Flush();
    }
    
    /// <summary>
    /// Removes the prompt line left after cancelling an active line read
    /// </summary>
    private static void ClearStoppedEditorLine()
    {
        // Move cursor up one line and clear the line
        Console.Out.Write("\x1b[1A\r\x1b[2K"); 
        Console.Out.Flush();
    }

    /// <summary>
    /// Initializes the Windows terminal to enable virtual terminal processing for ANSI escape sequences
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void InitializeWindowsTerminal()
    {
        IntPtr handle = Win32TerminalNative.GetStdHandle(Win32TerminalNative.StdOutHandle);
        
        if (handle == IntPtr.Zero ||
            handle == Win32TerminalNative.InvalidHandleValue)
        {
            throw new IOException(
                $"Failed to get the Windows console output handle. Error code: {Marshal.GetLastPInvokeError()}");
        }

        if (!Win32TerminalNative.GetConsoleMode(handle, out uint originalMode))
        {
            throw new InvalidOperationException("Failed to get console mode.");
        }

        uint newMode = originalMode | Win32TerminalNative.EnableProcessedOutput | 
                       Win32TerminalNative.EnableVirtualTerminalProcessing;

        _windowsOutputHandle = handle;
        _originalWindowsOutputMode = originalMode;
        
        if (newMode == originalMode)
        {
            // Virtual terminal processing is already enabled
            return;
        }
        
        if (!Win32TerminalNative.SetConsoleMode(handle, newMode))
        {
            throw new InvalidOperationException("Failed to set console mode.");
        }

        _windowsOutputModeChanged = true;
    }
    
    /// <summary>
    /// Restores the original Windows console mode if it was changed to enable virtual terminal processing
    /// </summary>
    private static void RestoreWindowsTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_windowsOutputModeChanged)
        {
            _ = Win32TerminalNative.SetConsoleMode(_windowsOutputHandle, _originalWindowsOutputMode);
        }

        _windowsOutputModeChanged = false;
        _windowsOutputHandle = IntPtr.Zero;
        _originalWindowsOutputMode = 0;
    }
}
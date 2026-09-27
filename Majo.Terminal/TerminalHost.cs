using Majo.LineEditor;
using LineEditing = Majo.LineEditor.LineEditor;

#if NET9_0_OR_GREATER
using LockType = System.Threading.Lock;
#else
using LockType = System.Object;
#endif

namespace Majo.Terminal;

internal class TerminalHost : IDisposable
{
    /// <summary>
    /// Maximum size of the command buffer in UTF-8 bytes
    /// </summary>
    private const int CommandBufferSize = 64 * 1024;
    
    /// <summary>
    /// Maximum number of commands to keep in history
    /// </summary>
    private const int HistoryCount = 100;
    
    /// <summary>
    /// Interval in milliseconds to the poll for new input
    /// </summary>
    private const int PollInterval = 100;
    
    /// <summary>
    /// Prompt string to display before each command
    /// </summary>
    private const string Prompt = "> ";
    
    /// <summary>
    /// Lock to synchronize access to the terminal
    /// </summary>
    private readonly LockType _terminalLock = new();
    
    /// <summary>
    /// Queue to buffer output lines before they are written to the terminal
    /// </summary>
    private readonly Queue<string> _bufferedOutput = new();
    
    /// <summary>
    /// Semaphore to ensure that only one modal operation (like a prompt) is active at a time
    /// </summary>
    private readonly SemaphoreSlim _modalSemaphore = new(1, 1);
    
    /// <summary>
    /// Indicates whether the current environment supports interactive terminal operations
    /// </summary>
    private readonly bool _interactive;

    /// <summary>
    /// The line editor instance for handling user input in interactive mode
    /// </summary>
    private readonly LineEditing? _lineEditor;
    
    /// <summary>
    /// Cancellation token source for the active read operation
    /// </summary>
    private CancellationTokenSource? _activeReadCts;
    
    /// <summary>
    /// Task representing the active read operation
    /// </summary>
    private Task<ReadResult>? _activeReadTask;
    
    /// <summary>
    /// Indicates whether the command loop is currently active
    /// </summary>
    private bool _commandLoopActive;
    
    /// <summary>
    /// Indicates whether a modal operation currently owns the terminal
    /// </summary>
    private bool _modalActive;
    
    /// <summary>
    /// Indicates whether the object has been disposed
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Indicates whether the terminal supports color output
    /// </summary>
    internal bool SupportsColor => !Console.IsOutputRedirected;
    
    /// <summary>
    /// Event triggered when the user enters a command
    /// </summary>
    internal event Action<string>? CommandEntered;
    
    /// <summary>
    /// Event triggered when the user interrupts the command loop
    /// </summary>
    internal event Action? Interrupted;

    /// <summary>
    /// Construct
    /// </summary>
    internal TerminalHost()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        _interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
        
        if (_interactive)
        {
            _lineEditor = new LineEditing(new LineEditorOption
            {
                CommandBufferSize = CommandBufferSize,
                HistoryCount = HistoryCount,
                PollInterval = PollInterval,
                Prompt = Prompt
            });
        }
    }

    /// <summary>
    /// Runs the command loop asynchronously, reading commands from the user and invoking the CommandEntered event
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    internal async Task RunAsync(CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(Interrupted);
        
        lock (_terminalLock)
        {
            ThrowIfDisposed();
            
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
                            CommandEntered?.Invoke(result.Text.Trim());
                        }

                        break;
                    case ReadStatus.Interrupted:
                        Interrupted();
                        break;
                    case ReadStatus.EndOfInput:
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected read status: {result.Status}");
                }
            }
        }
        finally
        {
            lock (_terminalLock)
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
    internal async Task<T> RunModalAsync<T>(Func<T> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_interactive)
        {
            throw new InvalidOperationException(
                "The current environment does not support interactive terminal operations.");
        }
        
        await _modalSemaphore.WaitAsync(ct).ConfigureAwait(false);
        
        bool modalActive = false;
        bool alternateScreenEntered = false;

        try
        {
            CancellationTokenSource? activeReadCts;
            Task<ReadResult>? activeReadTask;

            lock (_terminalLock)
            {
                ThrowIfDisposed();

                if (_modalActive)
                {
                    throw new InvalidOperationException("A modal operation is already active.");
                }

                _modalActive = true;
                modalActive = true;

                activeReadCts = _activeReadCts;
                activeReadTask = _activeReadTask;
            }

            bool readWasActive = activeReadTask is { IsCompleted: false };

            if (readWasActive)
            {
                // ReSharper disable once MethodHasAsyncOverload
                activeReadCts?.Cancel();

                try
                {
                    await activeReadTask!.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected, ignore
                }

                ClearStoppedEditorLine();
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
                    lock (_terminalLock)
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
                
                _modalSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// Writes text to the terminal
    /// </summary>
    /// <param name="text">Text</param>
    internal void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_terminalLock)
        {
            ThrowIfDisposed();

            if (_modalActive)
            {
                _bufferedOutput.Enqueue(text);
                return;
            }
            
            WriteDirect(text);
        }
    }
    
    public void Dispose()
    {
        CancellationTokenSource? activeReadCts;
        
        lock (_terminalLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            activeReadCts = _activeReadCts;
        }
        
        activeReadCts?.Cancel();
    }
    
    /// <summary>
    /// Reads a command from the user asynchronously
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Read result</returns>
    private async Task<ReadResult> ReadCommandAsync(CancellationToken ct = default)
    {
        using CancellationTokenSource readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        await _modalSemaphore.WaitAsync(ct).ConfigureAwait(false);
        
        Task<ReadResult> readTask;

        try
        {
            lock (_terminalLock)
            {
                ThrowIfDisposed();

                readTask = _lineEditor!.ReadLineAsync(readCts.Token).AsTask();

                _activeReadCts = readCts;
                _activeReadTask = readTask;
            }
        }
        finally
        {
            _modalSemaphore.Release();
        }

        try
        {
            return await readTask.ConfigureAwait(false);
        }
        finally
        {
            lock (_terminalLock)
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
    /// Writes text directly to the console
    /// </summary>
    /// <param name="text">Text</param>
    private void WriteDirect(string text)
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
    private void FlushBufferedOutput()
    {
        while (0 < _bufferedOutput.Count)
        {
            Console.Out.Write(_bufferedOutput.Dequeue());
        }
        
        Console.Out.Flush();
    }
    
    /// <summary>
    /// Throws if this object has been disposed
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
}
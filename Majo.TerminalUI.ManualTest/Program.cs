using Majo.Logging;

namespace Majo.TerminalUI.ManualTest;

internal static class Program
{
    private enum InputMode
    {
        SingleLine,
        MultiLine
    }

    private static readonly Lock BackgroundTaskLock = new();
    private static readonly List<Task> BackgroundTasks = [];

    private static readonly SelectionOption<int>[] SelectionOptions =
    [
        new(1, "First option"),
        new(2, "Second option"),
        new(3, "Third option")
    ];

    private static CancellationTokenSource? _sessionCts;

    private static InputMode _mode = InputMode.SingleLine;
    private static InputMode _nextMode = InputMode.SingleLine;

    private static bool _exitRequested;
    private static bool _restartRequested;
    private static bool _interrupted;

    private static async Task<int> Main()
    {
        int exitCode = 0;

        try
        {
            while (!_exitRequested)
            {
                await RunSessionAsync();

                if (_exitRequested)
                {
                    break;
                }

                if (_interrupted)
                {
                    await Console.Out.WriteLineAsync();
                    await Console.Out.WriteLineAsync(
                        "Ctrl+C command-loop test completed.");
                    await Console.Out.WriteLineAsync(
                        "Terminal has been shut down. It will now be initialized again.");
                }
                else if (_restartRequested)
                {
                    await Console.Out.WriteLineAsync();
                    await Console.Out.WriteLineAsync(
                        $"Terminal lifecycle restart completed. Next mode: {GetModeText(_nextMode)}.");
                }

                _mode = _nextMode;
                _restartRequested = false;
                _interrupted = false;

                await Console.Out.WriteLineAsync();
            }
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync("Fatal manual-test failure:");
            await Console.Error.WriteLineAsync(e.ToString());

            exitCode = 1;
        }

        await Console.Out.WriteLineAsync();
        await Console.Out.WriteLineAsync(
            "Majo.TerminalUI manual test exited.");

        return exitCode;
    }

    private static async Task RunSessionAsync()
    {
        using CancellationTokenSource sessionCts = new();
        _sessionCts = sessionCts;

        lock (BackgroundTaskLock)
        {
            BackgroundTasks.Clear();
        }

        bool initialized = false;

        try
        {
            Terminal.Initialize(CreateConfig(_mode));
            initialized = true;

            Terminal.CommandEntered += OnCommandEntered;
            Terminal.Interrupted += OnInterrupted;

            PrintIntroduction();

            await Terminal.RunAsync(sessionCts.Token);
        }
        finally
        {
            await sessionCts.CancelAsync();
            await WaitForBackgroundTasksAsync();

            if (initialized)
            {
                Terminal.CommandEntered -= OnCommandEntered;
                Terminal.Interrupted -= OnInterrupted;

                Terminal.Shutdown();
            }

            _sessionCts = null;
        }
    }

    private static TerminalConfig CreateConfig(InputMode mode)
    {
        return new TerminalConfig
        {
            Prompt = "> ",
            MultiLine = mode == InputMode.MultiLine,

            Logging = new LogOption
            {
                WriteToFile = false
            },

            TerminalLogLevel = LogLevel.Verbose,
            TerminalLogColorMode = TerminalLogColorMode.TrueColor
        };
    }

    private static void OnCommandEntered(string command)
    {
        try
        {
            HandleCommandAsync(command).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
            when (_sessionCts is { IsCancellationRequested: false })
        {
            Terminal.WriteLine(
                "Modal operation canceled. Verify that the normal command prompt is usable again.");
        }
        catch (OperationCanceledException)
            when (_sessionCts?.IsCancellationRequested == true)
        {
            // Current terminal session is shutting down
        }
        catch (Exception e)
        {
            Terminal.WriteLine(
                $"Command failed: {e.GetType().Name}: {e.Message}");
        }
    }

    private static void OnInterrupted()
    {
        _interrupted = true;
        _restartRequested = true;
        _nextMode = _mode;
    }

    private static async Task HandleCommandAsync(string command)
    {
        PrintTestGuide(command);
        
        switch (command.Trim().ToLowerInvariant())
        {
            case "help":
                PrintTestPlan();
                break;

            case "log":
                RunLogTest();
                break;

            case "delayed-output":
                StartDelayedOutputTest();
                break;

            case "delayed-log":
                StartDelayedLogTest();
                break;

            case "select":
                await RunSelectTestAsync(GetSessionToken());
                break;

            case "input":
                await RunInputTestAsync(GetSessionToken());
                break;

            case "delayed-modal":
                StartDelayedModalTest();
                break;

            case "modal-output":
                await RunModalOutputTestAsync(GetSessionToken());
                break;

            case "modal-log":
                await RunModalLogTestAsync(GetSessionToken());
                break;

            case "restart":
                await RequestRestartAsync(_mode);
                break;

            case "single":
                await RequestModeAsync(InputMode.SingleLine);
                break;

            case "multi":
                await RequestModeAsync(InputMode.MultiLine);
                break;

            case "exit":
                _exitRequested = true;

                if (_sessionCts is not null)
                {
                    await _sessionCts.CancelAsync();
                }

                break;

            default:
                Terminal.WriteLine($"Unknown command: {command}");
                Terminal.WriteLine(
                    "Enter 'help' to show the available manual tests.");
                break;
        }
    }

    private static void PrintIntroduction()
    {
        Terminal.WriteLine("Majo.TerminalUI Manual Test");
        Terminal.WriteLine($"Current mode: {GetModeText(_mode)}");
        Terminal.WriteLine("");

        PrintTestPlan();
    }

    private static void RunLogTest()
    {
        Terminal.WriteLine(
            "The following entries are emitted through Majo.Logging.");
        Terminal.WriteLine(
            "Verify that they appear in the terminal with the expected level/tag structure and level colors.");

        Logger.Verbose(
            "Manual Verbose log.",
            "ManualTest");

        Logger.Debug(
            "Manual Debug log.",
            "ManualTest");

        Logger.Information(
            "Manual Information log.",
            "ManualTest");

        Logger.Warning(
            "Manual Warning log.",
            "ManualTest");

        Logger.Error(
            "Manual Error log.",
            "ManualTest");

        Logger.Fatal(
            "Manual Fatal log.",
            "ManualTest");
    }

    private static void StartDelayedOutputTest()
    {
        Terminal.WriteLine(
            "Terminal.WriteLine will run in 5 seconds.");
        Terminal.WriteLine(
            "Start typing immediately without pressing Enter.");
        Terminal.WriteLine(
            "Verify that the message appears above the active input and that the input remains intact.");

        StartBackgroundTask(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);

            Terminal.WriteLine(
                "[delayed-output] Terminal output while command input is active.");
        });
    }

    private static void StartDelayedLogTest()
    {
        Terminal.WriteLine(
            "Logger.Information will run in 5 seconds.");
        Terminal.WriteLine(
            "Start typing immediately without pressing Enter.");
        Terminal.WriteLine(
            "Verify that the log appears above the active input and that the input remains intact.");

        StartBackgroundTask(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);

            Logger.Information(
                "Logger callback while command input is active.",
                "DelayedLog");
        });
    }

    private static async Task RunSelectTestAsync(CancellationToken ct)
    {
        int selected = await Terminal.SelectAsync(
            "Choose an option",
            SelectionOptions,
            ct);

        Terminal.WriteLine(
            $"Selected value: {selected}");
    }

    private static async Task RunInputTestAsync(CancellationToken ct)
    {
        string value = await Terminal.InputAsync(
            "Enter at least three non-whitespace characters",
            input =>
            {
                if (string.IsNullOrWhiteSpace(input))
                {
                    return "Input cannot be empty.";
                }

                if (input.Trim().Length < 3)
                {
                    return "Input must contain at least three characters.";
                }

                return null;
            },
            ct);

        Terminal.WriteLine(
            $"Input value: {value}");
    }

    private static void StartDelayedModalTest()
    {
        Terminal.WriteLine(
            "A selection modal will open in 5 seconds.");

        if (_mode == InputMode.SingleLine)
        {
            Terminal.WriteLine(
                "Start typing a long line immediately without pressing Enter.");
            Terminal.WriteLine(
                "Verify that the active SingleLine input disappears cleanly when the modal opens.");
        }
        else
        {
            Terminal.WriteLine(
                "Start typing enough text to occupy several physical rows without pressing Enter.");
            Terminal.WriteLine(
                "Verify that the complete MultiLine input area disappears cleanly when the modal opens.");
        }

        Terminal.WriteLine(
            "After leaving the modal, verify that a clean new prompt is restored.");

        StartBackgroundTask(async ct =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);

                int selected = await Terminal.SelectAsync(
                    $"Delayed modal ({GetModeText(_mode)})",
                    SelectionOptions,
                    ct);

                Terminal.WriteLine(
                    $"Delayed modal completed. Selected value: {selected}");

                Terminal.WriteLine(
                    "Verify that the command prompt has been restored cleanly.");
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested)
            {
                Terminal.WriteLine(
                    "Delayed modal canceled with Ctrl+C.");

                Terminal.WriteLine(
                    "Verify that the command prompt has been restored cleanly.");
            }
        });
    }

    private static async Task RunModalOutputTestAsync(
        CancellationToken ct)
    {
        Terminal.WriteLine(
            "A modal will now open.");

        Terminal.WriteLine(
            "Three Terminal.WriteLine messages will be generated while it is active.");

        Terminal.WriteLine(
            "Wait at least 4 seconds before choosing an option.");

        Terminal.WriteLine(
            "Verify that the modal is not visually polluted and that all messages appear in order after it closes.");

        using CancellationTokenSource outputCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct);

        Task outputTask = ProduceModalOutputAsync(outputCts.Token);

        try
        {
            int selected = await Terminal.SelectAsync(
                "Wait at least 4 seconds, then choose an option",
                SelectionOptions,
                ct);

            Terminal.WriteLine(
                $"Modal completed. Selected value: {selected}");
        }
        finally
        {
            await outputCts.CancelAsync();

            try
            {
                await outputTask;
            }
            catch (OperationCanceledException)
                when (outputCts.IsCancellationRequested)
            {
                // Modal ended before all delayed messages were produced
            }
        }
    }

    private static async Task ProduceModalOutputAsync(
        CancellationToken ct)
    {
        for (int i = 1; i <= 3; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            Terminal.WriteLine(
                $"[modal-output] Buffered Terminal message {i}/3");
        }
    }

    private static async Task RunModalLogTestAsync(
        CancellationToken ct)
    {
        Terminal.WriteLine(
            "A modal will now open.");

        Terminal.WriteLine(
            "Three Logger.Information entries will be generated while it is active.");

        Terminal.WriteLine(
            "Wait at least 4 seconds before choosing an option.");

        Terminal.WriteLine(
            "Verify that no log appears inside the modal and that all logs appear in order after it closes.");

        using CancellationTokenSource outputCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct);

        Task outputTask = ProduceModalLogAsync(outputCts.Token);

        try
        {
            int selected = await Terminal.SelectAsync(
                "Wait at least 4 seconds, then choose an option",
                SelectionOptions,
                ct);

            Terminal.WriteLine(
                $"Modal completed. Selected value: {selected}");
        }
        finally
        {
            await outputCts.CancelAsync();

            try
            {
                await outputTask;
            }
            catch (OperationCanceledException)
                when (outputCts.IsCancellationRequested)
            {
                // Modal ended before all delayed log entries were produced
            }
        }
    }

    private static async Task ProduceModalLogAsync(
        CancellationToken ct)
    {
        for (int i = 1; i <= 3; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            Logger.Information(
                $"Buffered Logger message {i}/3.",
                "ModalLog");
        }
    }

    private static async Task RequestModeAsync(InputMode mode)
    {
        if (_mode == mode)
        {
            Terminal.WriteLine(
                $"Terminal is already running in {GetModeText(mode)} mode.");
            return;
        }

        Terminal.WriteLine(
            $"Terminal will shutdown and restart in {GetModeText(mode)} mode.");

        await RequestRestartAsync(mode);
    }

    private static async Task RequestRestartAsync(InputMode mode)
    {
        _nextMode = mode;
        _restartRequested = true;

        if (_sessionCts is not null)
        {
            await _sessionCts.CancelAsync();
        }
    }

    private static void StartBackgroundTask(
        Func<CancellationToken, Task> action)
    {
        CancellationToken ct = GetSessionToken();

        Task task = RunBackgroundTaskAsync(action, ct);

        lock (BackgroundTaskLock)
        {
            BackgroundTasks.Add(task);
        }
    }

    private static async Task RunBackgroundTaskAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct)
    {
        try
        {
            await action(ct);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            // Current terminal session is shutting down
        }
        catch (Exception e)
        {
            try
            {
                Terminal.WriteLine(
                    $"Background test failed: {e.GetType().Name}: {e.Message}");
            }
            catch
            {
                await Console.Error.WriteLineAsync(
                    $"Background test failed: {e}");
            }
        }
    }

    private static async Task WaitForBackgroundTasksAsync()
    {
        Task[] tasks;

        lock (BackgroundTaskLock)
        {
            tasks = BackgroundTasks.ToArray();
        }

        if (tasks.Length != 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    private static CancellationToken GetSessionToken()
    {
        return _sessionCts?.Token
            ?? throw new InvalidOperationException(
                "No terminal test session is active.");
    }

    private static string GetModeText(InputMode mode)
    {
        return mode switch
        {
            InputMode.SingleLine => "SingleLine",
            InputMode.MultiLine => "MultiLine",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
    
    private static void PrintTestGuide(string command)
    {
        var guide = command switch
        {
            "log" => """
Action:
  No additional input is required.

Verify:
  - Verbose, Debug, Information, Warning, Error and Fatal are displayed.
  - The output comes through the Logger.LogWritten integration path.
  - Log output does not corrupt the normal prompt.
""",

            "delayed-output" when _mode == InputMode.SingleLine => """
Action:
  After the normal prompt returns, immediately start typing an unfinished line.
  Do not press Enter until the delayed output appears.

Verify:
  - The delayed output appears above the active editor.
  - Your unfinished input remains visible and unchanged.
  - You can continue editing and submit it normally afterward.
""",

            "delayed-output" => """
Action:
  After the normal prompt returns, immediately type enough unfinished text to
  occupy two or three physical rows.
  Do not press Enter until the delayed output appears.

Verify:
  - The delayed output appears above the complete active input area.
  - The multi-line input remains intact.
  - Editing continues normally afterward.
""",

            "delayed-log" when _mode == InputMode.SingleLine => """
Action:
  After the normal prompt returns, immediately start typing an unfinished line.
  Do not press Enter until the delayed log appears.

Verify:
  - The log entry appears above the active editor.
  - The Logger.LogWritten -> TerminalUI path works while input is active.
  - Your unfinished input remains intact and usable.
""",

            "delayed-log" => """
Action:
  After the normal prompt returns, immediately type enough unfinished text to
  occupy two or three physical rows.
  Do not press Enter until the delayed log appears.

Verify:
  - The log entry appears above the complete active input area.
  - The Logger.LogWritten -> TerminalUI path works while MultiLine input is active.
  - The unfinished input remains intact and usable.
""",

            "select" => """
Action:
  First run:
    Select an option normally.

  Second run:
    Run 'select' again and press Ctrl+C while the modal is active.

Verify:
  - Normal selection returns the selected value correctly.
  - Ctrl+C closes the modal cleanly.
  - The normal terminal prompt works again afterward.
""",

            "input" => """
Action:
  First run:
    Enter a value normally.

  Second run:
    Run 'input' again and press Ctrl+C while the modal is active.

Verify:
  - Normal input returns the entered value correctly.
  - Ctrl+C closes the modal cleanly.
  - The normal terminal prompt works again afterward.
""",

            "delayed-modal" when _mode == InputMode.SingleLine => """
Action:
  After the normal prompt returns, immediately start typing a long unfinished line.
  Do not press Enter before the delayed modal opens.

Verify:
  - The active input disappears completely when the modal takes ownership.
  - No stale editor text remains on the modal screen.
  - After the modal closes, a clean new prompt appears.
  - The cancelled unfinished input does not return.
""",

            "delayed-modal" => """
Action:
  After the normal prompt returns, immediately type enough unfinished text to
  occupy two or three physical rows.
  Do not press Enter before the delayed modal opens.

Verify:
  - The entire multi-line input area disappears when the modal takes ownership.
  - No stale editor rows remain on the modal screen.
  - After the modal closes, a clean new prompt appears.
  - The cancelled unfinished input does not return.
""",

            "modal-output" => """
Action:
  Keep the modal open long enough for the delayed direct output to fire.
  Then complete the modal normally.

Verify:
  - The delayed output does not appear inside or corrupt the modal.
  - The buffered output appears after the modal closes.
  - The normal prompt is restored afterward.
""",

            "modal-log" => """
Action:
  Keep the modal open long enough for all delayed log entries to fire.
  Then complete the modal normally.

Verify:
  - Logger output does not appear inside or corrupt the modal.
  - All buffered log entries appear after the modal closes.
  - Their order is preserved.
  - The normal prompt is restored afterward.
""",

            "restart" => $"""
Action:
  Terminal will perform a complete Shutdown -> Initialize cycle.

Verify:
  - It returns in {GetModeText(_mode)} mode.
  - This complete acceptance plan is printed again.
  - The new prompt accepts input normally.
""",

            "single" => """
Action:
  Switch Terminal to SingleLine mode.
  A real mode change performs a complete lifecycle restart.

Verify:
  - Terminal reports SingleLine after restart.
  - The new prompt works normally.
""",

            "multi" => """
Action:
  Switch Terminal to MultiLine mode through a complete lifecycle restart.

Verify:
  - Terminal reports MultiLine after restart.
  - This complete acceptance plan is printed again.
  - Multi-line editing can then be used for the MultiLine regression tests.
""",

            "exit" => """
Action:
  The test application will shut Terminal down and return to the shell.

Verify after the process exits:
  - The cursor is visible normally.
  - Keyboard input and echo work normally.
  - The screen is not left in the alternate buffer.
  - The shell behaves exactly as it did before the test program started.
""",

            _ => null
        };

        if (guide is null)
        {
            return;
        }

        Terminal.WriteLine("");
        Terminal.WriteLine($"TEST: {command}");
        Terminal.WriteLine("------------------------------");
        Terminal.WriteLine(guide);

        if (command != "exit")
        {
            Terminal.WriteLine("Run 'help' at any normal prompt to return to the complete test menu.");
        }

        Terminal.WriteLine("");
    }
    
    private static void PrintTestPlan()
    {
        Terminal.WriteLine($"""
MANUAL ACCEPTANCE PLAN
======================

Current mode: {GetModeText(_mode)}

Run 'help' at any normal prompt to show this plan again.

Recommended test sequence
-------------------------

[SingleLine]

1. delayed-output
   Start typing before the delayed output arrives.
   Verify that the output is written above the active input and that the
   unfinished input remains intact and usable.

2. delayed-log
   Start typing before the delayed log arrives.
   Verify the Logger -> LogWritten -> TerminalUI output path while an input
   operation is active. The unfinished input must remain intact.

3. delayed-modal
   Start typing a long unfinished line immediately after the prompt returns.
   Do not press Enter.
   Verify that the complete active input disappears cleanly when the modal
   takes ownership of the terminal. After the modal closes, a clean new prompt
   must appear and the cancelled input must not return.

4. log
   Verify all configured log levels emitted through Majo.Logging appear in
   TerminalUI correctly and do not disturb the prompt.

5. select
   Run once and select an option normally.
   Run it again and press Ctrl+C inside the modal.
   Verify both normal completion and modal cancellation recovery.

6. input
   Run once and enter a value normally.
   Run it again and press Ctrl+C inside the modal.
   Verify both normal completion and modal cancellation recovery.

7. modal-output
   Keep the modal open until the delayed output has fired.
   Verify that normal terminal output does not pollute the modal and is flushed
   only after the modal closes.

8. modal-log
   Keep the modal open until all delayed log entries have fired.
   Verify that Logger output does not pollute the modal and is flushed only
   after the modal closes.

9. restart
   Verify a complete Shutdown -> Initialize cycle in the same mode.
   This acceptance plan should appear again and the new prompt must work.

10. Ctrl+C at the normal prompt
    Do not run a command. Press Ctrl+C while the normal editor is active.
    Verify that the current RunAsync session ends, Terminal performs a complete
    Shutdown, and the harness automatically initializes the same mode again.
    The terminal must remain fully usable afterward.

11. multi
    Switch to MultiLine through a complete lifecycle restart.

[MultiLine]

12. delayed-output
    Type enough unfinished text to occupy two or three physical rows.
    Verify that delayed output is written above the complete input area without
    corrupting the active multi-line input.

13. delayed-log
    Type enough unfinished text to occupy two or three physical rows.
    Verify the Logger -> TerminalUI path without corrupting the active
    multi-line input.

14. delayed-modal
    Type enough unfinished text to occupy two or three physical rows.
    Do not press Enter.
    Verify that the entire active input area disappears cleanly when the modal
    takes ownership. After the modal closes, a clean new prompt must appear.

[Final]

15. exit
    Exit the test program.
    After returning to the shell, verify that cursor visibility, input echo,
    keyboard input and the terminal screen state are all normal.

Available commands
------------------
help            Show this complete manual acceptance plan.
log             Test Logger -> TerminalUI output for all log levels.
delayed-output  Test output while an editor read is active.
delayed-log     Test Logger output while an editor read is active.
select          Test selection modal and modal cancellation.
input           Test input modal and modal cancellation.
delayed-modal   Test modal takeover while an editor read is active.
modal-output    Test buffering of direct output during a modal.
modal-log       Test buffering of Logger output during a modal.
restart         Shutdown and initialize Terminal again in the current mode.
single          Restart Terminal in SingleLine mode.
multi           Restart Terminal in MultiLine mode.
exit            Shutdown Terminal and return to the shell.
""");
    }
}
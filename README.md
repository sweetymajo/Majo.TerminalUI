<div align="center">

# Majo.TerminalUI

**A small terminal interaction layer for .NET console applications.**

Persistent command input, coordinated background output and logging, modal prompts, and terminal lifecycle management behind a compact static API.

[![NuGet](https://img.shields.io/nuget/v/Majo.TerminalUI?style=flat-square&logo=nuget&logoColor=white)](https://www.nuget.org/packages/Majo.TerminalUI)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
![Windows](https://img.shields.io/badge/Windows-supported-0078D4?style=flat-square&logo=windows&logoColor=white)
![Linux x64](https://img.shields.io/badge/Linux-x64-FCC624?style=flat-square&logo=linux&logoColor=black)

**English** · [简体中文](./README.zh-CN.md)

</div>

---

## Overview

`Majo.TerminalUI` provides a coordination layer for interactive .NET console applications.

It combines persistent command input, application logging, ordinary terminal output, and temporary modal prompts while keeping ownership of terminal state in one place.

The current implementation is built around:

- `Majo.LineEditing` for persistent editable command input;
- `Majo.Logging` for application logging and structured log notifications;
- `Sharprompt` for temporary selection and text-input prompts;
- TerminalUI-owned lifecycle, modal coordination, output buffering, and Windows virtual-terminal setup.

`Majo.TerminalUI` does not parse or dispatch business commands. Accepted command text is exposed through `Terminal.CommandEntered`, leaving command interpretation to the application.

## Highlights

- Process-wide static terminal API
- Persistent command loop with configurable prompt and history
- Single-line and wrapped multi-line command input
- Background output without destroying the active edit
- Automatic display of `Majo.Logging` entries in the terminal
- Configurable terminal log threshold and color mode
- Modal selection and text-input prompts
- Alternate-screen ownership for modal prompts
- Output and log buffering while a modal prompt is active
- `Ctrl+C` interruption handling
- Cancellation-aware command loop and modal acquisition
- Logger ownership tracking
- Automatic Windows virtual-terminal mode setup and restoration
- Sharprompt kept behind the TerminalUI public API

## Installation

`Majo.TerminalUI` is available on [NuGet.org](https://www.nuget.org/packages/Majo.TerminalUI):

```bash
dotnet add package Majo.TerminalUI
```

The package currently targets .NET 10.

Supported interactive runtime environments currently include:

- Windows
- Linux x64

## Quick Start

Initialize the terminal, subscribe to command events, and start the command loop:

```csharp
using Majo.TerminalUI;

Terminal.Initialize(new TerminalConfig
{
    Prompt = "> ",
    HistoryCount = 100,
    MultiLine = false
});

Terminal.CommandEntered += command =>
{
    Terminal.WriteLine($"Command: {command}");
};

Terminal.Interrupted += () =>
{
    Terminal.WriteLine("Interrupted.");
};

try
{
    await Terminal.RunAsync();
}
finally
{
    Terminal.Shutdown();
}
```

`Ctrl+C` interrupts the current command read, raises `Terminal.Interrupted`, and ends the current `RunAsync()` command loop.

## Command Loop

`Terminal.RunAsync(...)` owns the persistent command-input loop.

When the user submits a non-whitespace command, TerminalUI raises:

```csharp
Terminal.CommandEntered += command =>
{
    // Parse or dispatch the command in application code.
};
```

Accepted command text is passed through without trimming. Whitespace-only input is ignored.

Only one command loop may be active at a time.

A cancellation token can be used to stop the loop from application code:

```csharp
using CancellationTokenSource cts = new();

Task terminalTask = Terminal.RunAsync(cts.Token);

// Later:
await cts.CancelAsync();
await terminalTask;
```

## Output While the User Is Typing

Use `Terminal.WriteLine(...)` instead of writing directly to `Console.Out` when output belongs to the TerminalUI-managed session:

```csharp
Terminal.WriteLine("Peer connected.");
```

When command input is active, TerminalUI routes the output through `Majo.LineEditing` so the current edit is preserved.

When no command read is active, the text is written directly to standard output.

`WriteLine(...)` is line-oriented. If the supplied text does not already end with `\n`, an environment newline is appended automatically.

The current implementation intentionally keeps the terminal emulator's native scrollback model. Background output preserves the live edit, but the input line is not a separate floating pane while the user browses native scrollback history.

## Logging Integration

TerminalUI subscribes to `Majo.Logging.Logger.LogWritten` and displays matching log entries through the same output path used by `Terminal.WriteLine(...)`.

```csharp
using Majo.Logging;
using Majo.TerminalUI;

Terminal.Initialize(new TerminalConfig
{
    TerminalLogLevel = LogLevel.Information,
    TerminalLogColorMode = TerminalLogColorMode.TrueColor,
    Logging = new LogOption
    {
        WriteToFile = true,
        FileLogLevel = LogLevel.Debug
    }
});

Logger.Information("Server started.", "Host");
```

Terminal output uses the format:

```text
[yyyy-MM-dd HH:mm:ss] [LEVEL] [Tag] Message
Exception
```

`TerminalLogLevel` controls only what is displayed in the terminal. File-log filtering remains controlled by `Majo.Logging.LogOption.FileLogLevel`.

### Logger Ownership

During initialization, TerminalUI attempts to initialize `Majo.Logging` using `TerminalConfig.Logging`.

- If TerminalUI initializes the logger, it owns that logger and shuts it down from `Terminal.Shutdown()`.
- If the application already initialized `Majo.Logging`, TerminalUI uses the existing logger and does not shut it down.

This allows TerminalUI to work both as the owner of logging in a small application and as a consumer of an application-wide logger in a larger program.

## Modal Prompts

TerminalUI provides two high-level modal prompt helpers.

### Selection

```csharp
int value = await Terminal.SelectAsync(
    "Select a value",
    [
        new SelectionOption<int>(1, "One"),
        new SelectionOption<int>(2, "Two"),
        new SelectionOption<int>(3, "Three")
    ]);
```

`SelectionOption<T>` keeps Sharprompt-specific option types out of the TerminalUI public API.

### Text Input

```csharp
string name = await Terminal.InputAsync(
    "Name",
    value => string.IsNullOrWhiteSpace(value)
        ? "Name cannot be empty."
        : null);
```

The optional validator returns `null` when the value is valid, or an error message when the prompt should be shown again.

### Modal Behavior

A modal prompt temporarily takes ownership of the terminal:

```text
active command read
    ↓ cancel and wait for recovery
alternate screen
    ↓
modal prompt
    ↓
restore primary screen
    ↓
flush buffered output and logs
    ↓
command loop continues
```

While a modal prompt is active, `Terminal.WriteLine(...)` output and terminal log output are buffered and written after the modal finishes.

`Terminal.RunModalAsync<T>(...)` exposes the lower-level synchronous modal wrapper used by `SelectAsync(...)` and `InputAsync(...)`.

A cancellation token can cancel a modal while it is waiting to acquire terminal ownership or before the synchronous prompt starts. Once the Sharprompt prompt itself is active, `Ctrl+C` is the supported user cancellation path; TerminalUI converts Sharprompt cancellation to `OperationCanceledException`.

## Configuration

`TerminalConfig` controls the current terminal session:

| Option | Default | Description |
| --- | ---: | --- |
| `CommandBufferSize` | `64 KiB` | Maximum command size in UTF-8 bytes. |
| `HistoryCount` | `100` | Maximum number of commands retained by the line editor. |
| `PollInterval` | `100 ms` | Polling interval used by the line editor while waiting for input and terminal changes. |
| `Prompt` | `"> "` | Prompt displayed before command input. |
| `MultiLine` | `false` | Enables wrapped multi-line command editing when `true`. |
| `Logging` | `new LogOption()` | Configuration used when TerminalUI initializes `Majo.Logging`. |
| `TerminalLogLevel` | `Information` | Minimum log level displayed in the terminal. |
| `TerminalLogColorMode` | `TrueColor` | Color mode used for terminal log-level text. |

### Terminal Log Colors

`TerminalLogColorMode` supports:

| Mode | Behavior |
| --- | --- |
| `None` | No ANSI color is emitted for log levels. |
| `Ansi16` | Uses standard 16-color ANSI foreground sequences. |
| `TrueColor` | Uses 24-bit RGB ANSI foreground sequences. |

Color affects terminal display only. It does not change file-log output.

## Lifecycle and Ownership

`Majo.TerminalUI` represents one process-wide terminal session.

```csharp
Terminal.Initialize();
```

Calling `Initialize(...)` again while TerminalUI is already initialized has no effect.

`Terminal.Shutdown()` releases TerminalUI-owned resources and restores terminal state. Calling it while TerminalUI is not initialized has no effect.

Shutdown must happen after the active command loop and any active modal operation have completed. Calling `Shutdown()` while either is active throws `InvalidOperationException`.

The lifecycle includes:

- the persistent `Majo.LineEditing.LineEditor` instance;
- subscription to `Majo.Logging.Logger.LogWritten`;
- TerminalUI-owned logger shutdown when applicable;
- Sharprompt global cancellation configuration;
- Windows virtual-terminal output mode;
- buffered terminal output and TerminalUI event state.

## Interactive Console Requirement

The interactive features of `Majo.TerminalUI` are intended for a real terminal/console.

On Windows, TerminalUI enables the virtual-terminal output flags it needs and restores the original console mode during shutdown.

When standard input or output is redirected, initialization can still provide the ordinary output/logging path, but persistent command input and modal prompts are not available. In that environment `RunAsync()` returns without starting an interactive loop, while modal APIs reject the operation.

Operating systems other than Windows and Linux are not currently supported.

## Dependencies

The current implementation is built on:

- `Majo.LineEditing`
- `Majo.Logging`
- `Sharprompt`

`Majo.LineEditing` and Sharprompt are implementation details of TerminalUI's interaction layer. Typical callers do not need to use either directly.

`Majo.Logging` is intentionally part of the configuration surface through `LogOption` and `LogLevel`, allowing applications to share one logging pipeline with TerminalUI.

Sharprompt is kept out of the normal consumer API, including its option types and transitive source-generator behavior.

## Contributing

Contributions are welcome.

Development setup, automated and manual testing, local NuGet package validation, and dependency-isolation details are documented in [CONTRIBUTING.md](./CONTRIBUTING.md).

## Design Goals

`Majo.TerminalUI` deliberately focuses on coordination rather than becoming a full terminal framework:

- keep the public API small and application-oriented;
- keep business command parsing outside TerminalUI;
- let `Majo.LineEditing` own persistent editing behavior;
- let `Majo.Logging` own logging behavior;
- use modal prompts only when temporary terminal takeover is useful;
- preserve native terminal scrollback, selection, copy, search, and other emulator capabilities where practical;
- restore terminal and dependency state reliably across normal lifecycle transitions;
- add new terminal interaction strategies only when a concrete use case justifies them.

## License

See [LICENSE.txt](./LICENSE.txt).

---

<div align="center">

Built as a focused terminal interaction component for the Majo project family.

[简体中文](./README.zh-CN.md)

</div>

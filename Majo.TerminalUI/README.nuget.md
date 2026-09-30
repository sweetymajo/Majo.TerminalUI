# Majo.TerminalUI

A small terminal interaction layer for .NET console applications.

`Majo.TerminalUI` coordinates persistent command input, application logging, ordinary output, modal prompts, and terminal lifecycle behind a compact static API.

## Installation

```bash
dotnet add package Majo.TerminalUI
```

The package currently targets .NET 10.

Supported interactive runtime environments currently include:

- Windows
- Linux x64

## Features

- Persistent command loop with configurable prompt and history
- Single-line and wrapped multi-line command input
- Background output without destroying the active edit
- Automatic terminal display of `Majo.Logging` entries
- Configurable terminal log threshold and color mode
- Modal selection and text-input prompts
- Alternate-screen ownership for modal prompts
- Output and log buffering while a modal is active
- `Ctrl+C` interruption handling
- Cancellation-aware command loop and modal acquisition
- Logger ownership tracking
- Automatic Windows virtual-terminal mode setup and restoration

## Quick Start

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

try
{
    await Terminal.RunAsync();
}
finally
{
    Terminal.Shutdown();
}
```

`Ctrl+C` interrupts the current command read and ends the current `RunAsync()` loop after raising `Terminal.Interrupted`.

## Output and Logging

Use `Terminal.WriteLine(...)` for output that belongs to the TerminalUI-managed session:

```csharp
Terminal.WriteLine("Peer connected.");
```

When command input is active, TerminalUI preserves the current edit through `Majo.LineEditing`. During a modal prompt, ordinary output is buffered until the modal finishes.

TerminalUI also subscribes to `Majo.Logging.Logger.LogWritten`:

```csharp
using Majo.Logging;

Terminal.Initialize(new TerminalConfig
{
    TerminalLogLevel = LogLevel.Information,
    TerminalLogColorMode = TerminalLogColorMode.TrueColor
});

Logger.Information("Server started.", "Host");
```

Terminal log output uses:

```text
[yyyy-MM-dd HH:mm:ss] [LEVEL] [Tag] Message
Exception
```

`TerminalLogLevel` controls terminal display only. File-log filtering remains controlled by `Majo.Logging.LogOption`.

If TerminalUI initializes `Majo.Logging`, it owns and shuts down that logger. If the application already initialized the logger, TerminalUI reuses it without taking ownership.

## Modal Prompts

Selection:

```csharp
int value = await Terminal.SelectAsync(
    "Select a value",
    [
        new SelectionOption<int>(1, "One"),
        new SelectionOption<int>(2, "Two")
    ]);
```

Text input with validation:

```csharp
string name = await Terminal.InputAsync(
    "Name",
    value => string.IsNullOrWhiteSpace(value)
        ? "Name cannot be empty."
        : null);
```

Modal prompts temporarily use the alternate screen. Terminal output and logs produced during the modal are buffered and flushed after the primary screen is restored.

`Ctrl+C` is the supported cancellation path once the underlying synchronous prompt is active. Sharprompt cancellation is exposed as `OperationCanceledException`.

## Configuration

| Option | Default | Description |
| --- | ---: | --- |
| `CommandBufferSize` | `64 KiB` | Maximum command size in UTF-8 bytes. |
| `HistoryCount` | `100` | Maximum number of commands retained by the line editor. |
| `PollInterval` | `100 ms` | Polling interval used by the line editor. |
| `Prompt` | `"> "` | Prompt displayed before command input. |
| `MultiLine` | `false` | Enables wrapped multi-line command editing. |
| `Logging` | `new LogOption()` | Configuration used when TerminalUI initializes `Majo.Logging`. |
| `TerminalLogLevel` | `Information` | Minimum log level displayed in the terminal. |
| `TerminalLogColorMode` | `TrueColor` | Terminal log color mode. |

`TerminalLogColorMode` supports `None`, `Ansi16`, and `TrueColor`.

## Lifecycle

`Majo.TerminalUI` represents one process-wide terminal session.

Calling `Terminal.Initialize(...)` again while already initialized has no effect.

Call `Terminal.Shutdown()` after the command loop and any modal operation have finished. Shutdown restores TerminalUI-owned state, including Windows virtual-terminal mode and an owned `Majo.Logging` logger.

## Interactive Console Requirement

Interactive command input and modal prompts require a real console/TTY.

When standard input or output is redirected, initialization can still provide ordinary output/logging integration, but `RunAsync()` does not start an interactive loop and modal prompt APIs are unavailable.

The current implementation supports Windows and Linux. Linux runtime support currently targets x64 through `Majo.LineEditing`.

The implementation intentionally keeps the terminal emulator's native scrollback model. Background output preserves the active edit, but command input is not a separate floating pane while the user browses native scrollback history.

## Dependencies

`Majo.TerminalUI` is built on:

- `Majo.LineEditing`
- `Majo.Logging`
- `Sharprompt`

`Majo.LineEditing` and Sharprompt are kept behind TerminalUI's interaction API. `Majo.Logging` is intentionally visible through `TerminalConfig` so applications can share one logging pipeline with TerminalUI.

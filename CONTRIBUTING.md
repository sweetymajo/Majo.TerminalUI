# Contributing

Thank you for your interest in contributing to Majo.TerminalUI.

Majo.TerminalUI is a focused terminal interaction layer for .NET applications. It coordinates persistent command input, terminal output, logging, modal prompts, cancellation, and terminal lifecycle behavior across Windows and Linux.

Contributions should keep the project focused and prefer concrete fixes or features over unnecessary abstraction.

## Development Requirements

The main library targets:

- .NET 8
- .NET 10

The test projects target .NET 10.

This distinction is intentional. The library itself must remain compatible with every target framework it declares, while the test projects use the primary development framework and do not need to be mechanically restricted to .NET 8 language features.

The project currently supports interactive terminal use on:

- Windows
- Linux

A real interactive console/TTY is required for manual terminal testing.

## Build

Restore and build the solution with:

```bash
dotnet restore Majo.TerminalUI.slnx
dotnet build Majo.TerminalUI.slnx
```

For a release build:

```bash
dotnet build Majo.TerminalUI.slnx -c Release
```

Changes to the main `Majo.TerminalUI` project must compile for both target frameworks.

The automated and manual test projects intentionally remain on .NET 10 unless their own requirements change.

## Testing

Majo.TerminalUI uses two complementary test projects:

```text
Majo.TerminalUI.AutoTest
    → automated contract and coordination tests

Majo.TerminalUI.ManualTest
    → real-terminal acceptance testing
```

These projects have different responsibilities. Do not replace one with the other.

The general rule is:

> Automate behavior that can be tested deterministically without pretending to be a real terminal. Keep behavior that depends on an actual terminal, user interaction, rendering, or shell state in ManualTest.

## Automated Testing

`Majo.TerminalUI.AutoTest` uses xUnit and runs on .NET 10.

Run the automated tests with:

```bash
dotnet test Majo.TerminalUI.AutoTest/Majo.TerminalUI.AutoTest.csproj
```

The test suite runs without parallel test execution.

This is required because several parts of the tested integration are process-wide or static, including:

- `Terminal`;
- `Majo.Logging.Logger`;
- `Console.Out`;
- Sharprompt global state.

Tests must restore shared process state during cleanup and must not depend on execution order.

### What AutoTest should cover

AutoTest is intended for behavior owned by Majo.TerminalUI that can be verified deterministically, including:

- public API behavior before initialization;
- initialization and shutdown lifecycle;
- repeated initialization and shutdown;
- logger ownership;
- terminal log-level filtering;
- log formatting and color-selection logic;
- output line normalization;
- null-argument validation;
- `SelectionOption<T>` behavior;
- pre-canceled cancellation tokens;
- non-interactive behavior;
- coordination behavior that can be tested without a real TTY;
- consumer build contracts, including isolation of Sharprompt's transitive source generator.

When a TerminalUI behavior can be reduced to pure or deterministic logic, prefer an automated regression test.

### What AutoTest should not cover

Do not use fake terminals or large mocks merely to increase automated coverage.

AutoTest should not re-test behavior owned by dependencies, including:

- Majo.Logging file output, rolling, or Serilog behavior;
- Majo.LineEditing editing, history, Unicode width, wrapping, or cursor algorithms;
- Sharprompt rendering or interactive prompt behavior.

AutoTest should also not pretend to verify real-terminal behavior such as:

- cursor geometry;
- physical terminal scrolling or scrollback;
- soft wrapping;
- active editing while a human is typing;
- real modal rendering;
- alternate-screen visual behavior;
- shell restoration.

Those belong to ManualTest.

## Local NuGet Package Validation

The test projects support two dependency modes for `Majo.TerminalUI`.

By default:

```xml
<UsePackageReference>false</UsePackageReference>
```

they use a `ProjectReference` to the main project. This is the normal mode for day-to-day development because source changes are immediately visible to the test projects.

When `UsePackageReference` is `true`, the test projects instead consume the locally packed `Majo.TerminalUI` NuGet package.

```text
ProjectReference
    → normal development

PackageReference
    → local NuGet package validation
```

This dual-mode arrangement is part of the standard release workflow for published Majo libraries. It should be preserved even when the current development task does not require package-level testing.

The repository-level `NuGet.config` maps `Majo.TerminalUI` to the local package source under:

```text
artifacts/nuget
```

To validate the locally produced package, first pack the main project:

```bash
dotnet pack Majo.TerminalUI/Majo.TerminalUI.csproj -c Release
```

Then restore and build or test the consumer projects with package mode enabled:

```bash
dotnet restore Majo.TerminalUI.AutoTest/Majo.TerminalUI.AutoTest.csproj     -p:UsePackageReference=true     --force

dotnet test Majo.TerminalUI.AutoTest/Majo.TerminalUI.AutoTest.csproj     -c Release     --no-restore     -p:UsePackageReference=true
```

For real-terminal package validation, run ManualTest in package mode as well:

```bash
dotnet restore Majo.TerminalUI.ManualTest/Majo.TerminalUI.ManualTest.csproj     -p:UsePackageReference=true     --force

dotnet run --project Majo.TerminalUI.ManualTest/Majo.TerminalUI.ManualTest.csproj     -c Release     -p:UsePackageReference=true
```

Package mode is especially important when changing:

- NuGet package metadata;
- package assets;
- transitive dependencies;
- `build` or `buildTransitive` targets;
- Sharprompt source-generator isolation;
- consumer-facing MSBuild behavior.

If `UsePackageReference` is changed directly in a project file, restore that project before building or testing it. IDE builds may not automatically perform the required restore after the conditional dependency changes.

## Manual Testing

`Majo.TerminalUI.ManualTest` is a formal acceptance-test application, not a demo project.

Run it in a real interactive terminal with:

```bash
dotnet run --project Majo.TerminalUI.ManualTest/Majo.TerminalUI.ManualTest.csproj
```

The application prints its manual acceptance plan and provides commands that exercise the interactive behavior.

Current test commands include:

```text
help
log
delayed-output
delayed-log
select
input
delayed-modal
modal-output
modal-log
restart
single
multi
exit
```

The test application provides `Action` and `Verify` guidance for the individual scenarios. Use that guidance as the authoritative checklist while performing manual acceptance testing.

### What ManualTest should verify

ManualTest covers behavior that depends on a real terminal and on the interaction between TerminalUI and its dependencies, including:

- ordinary output while command input is active;
- logging while command input is active;
- modal takeover of an active read;
- output buffering while a modal is active;
- log buffering while a modal is active;
- `SelectAsync` and `InputAsync` interactive behavior;
- `Ctrl+C` behavior in prompts and normal command input;
- restart and repeated lifecycle behavior;
- SingleLine integration;
- MultiLine integration;
- terminal and shell restoration after exit.

MultiLine scenarios are not intended to duplicate Majo.LineEditing's own algorithm tests. They verify that TerminalUI's output, logging, cancellation, modal takeover, and recovery paths continue to work when the underlying editor is operating in MultiLine mode.

### Platform testing

Changes that affect terminal ownership, console modes, rendering coordination, modal transitions, cancellation recovery, or shell restoration should be manually tested on both Windows and Linux whenever practical.

A passing AutoTest suite does not replace this requirement for real-terminal changes.

Conversely, a change limited to deterministic non-terminal logic does not require repeating the complete manual acceptance plan unless it can affect the interactive path.

## Test Boundaries

Keep test ownership aligned with project ownership.

```text
Majo.LineEditing
    → line editing implementation

Majo.Logging
    → logging implementation

Sharprompt
    → modal prompt implementation

Majo.TerminalUI
    → coordination between terminal input, output, logging,
      modal ownership, cancellation, and lifecycle
```

TerminalUI tests may verify that these components are coordinated correctly, but they should not duplicate the dependencies' own test suites.

For example:

- verifying that `Logger.LogWritten` is displayed by TerminalUI is a TerminalUI integration test;
- verifying Majo.Logging's file rolling is not;
- verifying that modal takeover cancels and later restores TerminalUI command input is a TerminalUI test;
- verifying Sharprompt's own visual layout engine is not.

## Sharprompt Consumer Isolation

Sharprompt is an implementation detail of TerminalUI's modal APIs.

TerminalUI should not expose Sharprompt-specific option types as part of its public API.

The repository also contains build logic that prevents Sharprompt's transitive source generator from leaking into ordinary consumers of Majo.TerminalUI.

This behavior is important because the source generator can otherwise inspect consumer code and fail on constructs that are unrelated to TerminalUI.

When changing package references, MSBuild targets, package assets, or consumer-facing build behavior:

- preserve Sharprompt API isolation;
- preserve the transitive source-generator filtering behavior unless there is a verified replacement;
- run the relevant AutoTest consumer build-contract test;
- verify both normal ProjectReference development and packaged-consumer behavior when the change affects packaging.

Do not work around source-generator problems by imposing unrelated restrictions on consumer source code.

## Public API

Keep the public API small and focused on terminal coordination.

Majo.TerminalUI should provide application-level terminal behavior without unnecessarily exposing implementation details from Majo.LineEditing or Sharprompt.

Majo.Logging configuration types are intentionally part of TerminalUI configuration where logging integration requires them.

Prefer extending the public API only when there is a concrete use case.

When public API or observable behavior changes, update the user-facing README files and NuGet README as appropriate.

## Pull Requests

Please keep pull requests focused on one logical change.

Before submitting a pull request:

- make sure the full solution builds successfully;
- make sure the main library builds for both .NET 8 and .NET 10;
- run the automated test suite;
- run the relevant ManualTest scenarios when interactive terminal behavior is affected;
- test platform-specific behavior on the affected platform whenever practical;
- validate the local NuGet package in `PackageReference` mode when packaging, dependency, or consumer build behavior changes;
- validate consumer build behavior when package/MSBuild/Sharprompt isolation changes;
- update user-facing documentation when public API or observable behavior changes;
- explain the motivation and main changes in the pull request description.

Do not add tests only to increase test count or coverage. Tests should protect meaningful behavior or regressions.

## Code Style

Follow the existing C# style and project structure where practical.

Prefer small, focused changes over unrelated cleanup or broad refactoring in the same pull request.

Do not rewrite stable code only for stylistic consistency.

When framework compatibility requires a source change, make the compatibility change in the main library without mechanically rewriting .NET 10-only test code that does not need the same restriction.

<div align="center">

# Majo.TerminalUI

**一个面向 .NET 控制台程序的小型终端交互协调层。**

通过精简的静态 API 统一持久命令输入、后台输出与日志、临时 Modal Prompt，以及终端生命周期管理。

[![NuGet](https://img.shields.io/nuget/v/Majo.TerminalUI?style=flat-square&logo=nuget&logoColor=white)](https://www.nuget.org/packages/Majo.TerminalUI)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
![Windows](https://img.shields.io/badge/Windows-supported-0078D4?style=flat-square&logo=windows&logoColor=white)
![Linux x64](https://img.shields.io/badge/Linux-x64-FCC624?style=flat-square&logo=linux&logoColor=black)

[English](./README.md) · **简体中文**

</div>

---

## 项目简介

`Majo.TerminalUI` 为交互式 .NET 控制台程序提供一层统一的终端协调能力。

它把持久命令输入、应用日志、普通终端输出和临时 Modal Prompt 组合在一起，并把终端状态的所有权集中在一个位置管理。

当前实现主要建立在：

- `Majo.LineEditing`：负责持久可编辑的命令输入；
- `Majo.Logging`：负责应用日志与结构化日志通知；
- `Sharprompt`：负责临时选择和文本输入 Prompt；
- TerminalUI 自身：负责生命周期、Modal 协调、输出缓冲以及 Windows Virtual Terminal 设置。

`Majo.TerminalUI` 不负责解析或分发业务命令。用户提交的命令文本通过 `Terminal.CommandEntered` 交给上层，由应用自行决定如何处理。

## 主要特性

- 进程级静态 Terminal API
- 可配置 Prompt 与 History 的持久命令循环
- 单行与自动折行多行命令输入
- 后台输出时保护当前正在编辑的命令
- 自动把 `Majo.Logging` 日志显示到终端
- 可配置终端日志级别与颜色模式
- Modal 选择和文本输入 Prompt
- Modal 使用 Alternate Screen 获取临时终端所有权
- Modal 期间自动缓冲普通输出和日志
- `Ctrl+C` 中断处理
- 支持 CancellationToken 的命令循环和 Modal 获取过程
- Logger 所有权跟踪
- 自动启用并恢复 Windows Virtual Terminal 模式
- Sharprompt 保持在 TerminalUI 公共 API 之外

## 安装

`Majo.TerminalUI` 发布至 [NuGet.org](https://www.nuget.org/packages/Majo.TerminalUI)：

```bash
dotnet add package Majo.TerminalUI
```

当前包目标框架为 .NET 10。

当前支持的交互式运行环境包括：

- Windows
- Linux x64

## 快速开始

初始化 Terminal，订阅命令事件，然后启动命令循环：

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

按下 `Ctrl+C` 会中断当前命令读取、触发 `Terminal.Interrupted`，并结束本次 `RunAsync()` 命令循环。

## 命令循环

`Terminal.RunAsync(...)` 负责持有持续运行的命令输入循环。

用户提交非空白命令后，TerminalUI 会触发：

```csharp
Terminal.CommandEntered += command =>
{
    // 在应用层解析或分发命令。
};
```

已接受的命令文本会按 `Majo.LineEditing` 返回的原始内容传递，不会额外 `Trim()`。纯空白输入会被忽略。

同一时间只允许存在一个活动的命令循环。

应用也可以使用 CancellationToken 主动停止命令循环：

```csharp
using CancellationTokenSource cts = new();

Task terminalTask = Terminal.RunAsync(cts.Token);

// 稍后：
await cts.CancelAsync();
await terminalTask;
```

## 用户输入时的输出

属于 TerminalUI 会话的输出应优先使用 `Terminal.WriteLine(...)`，而不是直接写入 `Console.Out`：

```csharp
Terminal.WriteLine("Peer connected.");
```

当命令输入正在进行时，TerminalUI 会通过 `Majo.LineEditing` 输出内容，从而保留当前编辑状态。

没有活动命令读取时，文本会直接写入标准输出。

`WriteLine(...)` 以行为单位工作。如果传入文本本身没有以 `\n` 结尾，会自动补充当前环境的换行符。

当前实现有意继续使用 Terminal Emulator 自己的原生 Scrollback。后台输出会保护 Live Input，但当用户主动浏览原生历史记录时，输入行并不是独立悬浮在屏幕底部的固定 Pane。

## 日志集成

TerminalUI 会订阅 `Majo.Logging.Logger.LogWritten`，并让符合条件的日志通过与 `Terminal.WriteLine(...)` 相同的终端输出路径显示。

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

终端日志格式为：

```text
[yyyy-MM-dd HH:mm:ss] [LEVEL] [Tag] Message
Exception
```

`TerminalLogLevel` 只控制哪些日志显示在终端。文件日志的过滤仍由 `Majo.Logging.LogOption.FileLogLevel` 控制。

### Logger 所有权

初始化时，TerminalUI 会使用 `TerminalConfig.Logging` 尝试初始化 `Majo.Logging`。

- 如果 Logger 是由 TerminalUI 初始化的，则 TerminalUI 拥有该 Logger，并在 `Terminal.Shutdown()` 时负责关闭；
- 如果应用已经提前初始化 `Majo.Logging`，TerminalUI 会复用现有 Logger，并且不会在 Shutdown 时关闭它。

因此，小型程序可以让 TerminalUI 直接管理日志生命周期，而更大的应用也可以让 TerminalUI 接入已有的全局日志系统。

## Modal Prompt

TerminalUI 提供两种常用的高级 Modal Prompt。

### 选择

```csharp
int value = await Terminal.SelectAsync(
    "Select a value",
    [
        new SelectionOption<int>(1, "One"),
        new SelectionOption<int>(2, "Two"),
        new SelectionOption<int>(3, "Three")
    ]);
```

`SelectionOption<T>` 用于隔离 Sharprompt 自身的 option 类型，使 TerminalUI 的公共 API 不直接依赖 Sharprompt 类型。

### 文本输入

```csharp
string name = await Terminal.InputAsync(
    "Name",
    value => string.IsNullOrWhiteSpace(value)
        ? "Name cannot be empty."
        : null);
```

可选 validator 在输入有效时返回 `null`；如果需要重新输入，则返回要显示的错误信息。

### Modal 行为

Modal Prompt 会暂时取得终端所有权：

```text
活动命令读取
    ↓ 取消并等待输入状态恢复
Alternate Screen
    ↓
Modal Prompt
    ↓
恢复 Primary Screen
    ↓
刷新 Modal 期间缓冲的普通输出和日志
    ↓
命令循环继续
```

Modal 运行期间，`Terminal.WriteLine(...)` 和终端日志都会进入缓冲队列，并在 Modal 结束后统一输出。

`Terminal.RunModalAsync<T>(...)` 是 `SelectAsync(...)` 和 `InputAsync(...)` 使用的底层同步 Modal 包装入口。

CancellationToken 可以在等待 Modal 所有权时取消，也可以在同步 Prompt 真正开始之前取消。一旦 Sharprompt 的同步 Prompt 已经运行，实际的用户取消方式是 `Ctrl+C`；TerminalUI 会把 Sharprompt 的取消转换为 `OperationCanceledException`。

## 配置项

`TerminalConfig` 用于配置当前 Terminal 会话：

| 配置项 | 默认值 | 说明 |
| --- | ---: | --- |
| `CommandBufferSize` | `64 KiB` | 命令允许占用的最大 UTF-8 字节数。 |
| `HistoryCount` | `100` | LineEditor 最多保留的历史命令数量。 |
| `PollInterval` | `100 ms` | LineEditor 等待输入和终端状态变化时使用的轮询间隔。 |
| `Prompt` | `"> "` | 显示在命令输入前的 Prompt。 |
| `MultiLine` | `false` | 为 `true` 时启用自动折行的多行命令编辑。 |
| `Logging` | `new LogOption()` | TerminalUI 自己初始化 `Majo.Logging` 时使用的配置。 |
| `TerminalLogLevel` | `Information` | 显示到终端的最低日志级别。 |
| `TerminalLogColorMode` | `TrueColor` | 终端日志级别文本使用的颜色模式。 |

### 终端日志颜色

`TerminalLogColorMode` 支持：

| 模式 | 行为 |
| --- | --- |
| `None` | 日志级别不输出 ANSI 颜色。 |
| `Ansi16` | 使用标准 16 色 ANSI 前景色。 |
| `TrueColor` | 使用 24-bit RGB ANSI 前景色。 |

颜色只影响终端显示，不会改变文件日志内容。

## 生命周期与资源所有权

`Majo.TerminalUI` 表示一个进程级的 Terminal 会话。

```csharp
Terminal.Initialize();
```

TerminalUI 已经初始化后再次调用 `Initialize(...)` 不会重复初始化。

`Terminal.Shutdown()` 会释放 TerminalUI 自己拥有的资源并恢复终端状态。尚未初始化时调用不会执行任何操作。

Shutdown 必须发生在活动命令循环和 Modal 操作已经结束之后。如果这两者仍处于活动状态，调用 `Shutdown()` 会抛出 `InvalidOperationException`。

TerminalUI 生命周期管理的内容包括：

- 持久的 `Majo.LineEditing.LineEditor` 实例；
- 对 `Majo.Logging.Logger.LogWritten` 的订阅；
- 在 TerminalUI 拥有 Logger 时负责关闭它；
- Sharprompt 的全局取消配置；
- Windows Virtual Terminal 输出模式；
- 缓冲输出与 TerminalUI 自身的事件状态。

## 交互式终端要求

`Majo.TerminalUI` 的交互功能面向真实的 Terminal / Console。

Windows 下，TerminalUI 会自动启用需要的 Virtual Terminal Output Flag，并在 Shutdown 时恢复原始 Console Mode。

当标准输入或标准输出被重定向时，初始化仍可以保留普通输出与日志集成，但持久命令输入和 Modal Prompt 不可用。此时 `RunAsync()` 会直接返回而不启动交互循环，Modal API 则会拒绝执行。

当前不支持 Windows 与 Linux 之外的操作系统。

## 依赖

当前实现建立在：

- `Majo.LineEditing`
- `Majo.Logging`
- `Sharprompt`

`Majo.LineEditing` 与 Sharprompt 属于 TerminalUI 交互层的实现细节，普通调用方通常不需要直接使用它们。

`Majo.Logging` 则有意通过 `LogOption` 和 `LogLevel` 进入 TerminalUI 的配置 API，使应用可以让 TerminalUI 与自己的日志系统共享同一条 Logging Pipeline。

Sharprompt 不会作为普通调用方 API 的一部分暴露，其中也包括它的 option 类型以及传递式 Source Generator 行为。

## 参与开发

欢迎参与 `Majo.TerminalUI` 的开发。

开发环境、自动化/人工测试、本地 NuGet 包验证以及依赖隔离细节请参阅 [CONTRIBUTING.md](./CONTRIBUTING.md)。

`CONTRIBUTING.md` 使用英文编写。

## 设计原则

`Majo.TerminalUI` 有意专注于“协调”，而不是发展成一个完整的 Terminal Framework：

- 保持公共 API 精简并面向应用使用；
- 业务命令解析留在 TerminalUI 之外；
- 持久输入编辑交给 `Majo.LineEditing`；
- 日志行为交给 `Majo.Logging`；
- 只有需要临时接管终端时才使用 Modal Prompt；
- 在可行的情况下保留原生 Terminal 的 Scrollback、文本选择、复制、搜索等能力；
- 在正常生命周期切换中可靠恢复终端和依赖状态；
- 只有真实需求出现时才增加新的终端交互实现策略。

## 许可证

参阅 [LICENSE.txt](./LICENSE.txt)。

---

<div align="center">

作为 Majo 系列项目中一个专注的终端交互组件。

[English](./README.md)

</div>

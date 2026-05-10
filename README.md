#  Remote C++ Compiler

<div align="center">

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-WinForms-239120?style=for-the-badge&logo=csharp&logoColor=white)
![C++](https://img.shields.io/badge/C++-g++-00599C?style=for-the-badge&logo=cplusplus&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)

**A multithreaded TCP-based remote C++ compiler with a dark-themed Windows Forms UI.**  
Write C++ code on the client, compile and run it on the server — all in real time.

[Features](#-features) • [Architecture](#-architecture) • [Getting Started](#-getting-started) • [Usage](#-usage) • [Project Structure](#-project-structure) • [Troubleshooting](#-troubleshooting)

---

![App Screenshot Placeholder](https://placehold.co/900x480/18141C/6E57E0?text=Remote+C%2B%2B+Compiler+UI)

</div>

---

## ✨ Features

| Feature | Details |
|---|---|
| 🔀 **Multithreaded Server** | Each client gets its own `Task` — no blocking, no waiting |
| 🚦 **Concurrency Throttle** | `SemaphoreSlim` caps simultaneous `g++` processes (configurable) |
| ⏱️ **Dual Timeouts** | 30s session timeout + 10s execution limit stops infinite loops |
| 🗂️ **Collision-free Temp Files** | Every compile uses a unique GUID-based filename — no race conditions |
| 🎨 **Syntax Highlighting** | Keywords, preprocessor directives, strings, and comments highlighted live |
| 📟 **Live Session Monitor** | Server tab shows active client count in real time |
| ✅ **Async Client** | Full `async/await` — UI never freezes during compilation |
| ❌ **Cancel Support** | Cancel any in-flight compile request instantly |
| 📍 **Line / Col Indicator** | Editor shows current cursor position at all times |

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        MainForm (WinForms UI)                   │
│   ┌─────────────────────┐       ┌───────────────────────────┐   │
│   │     Server Tab      │       │        Client Tab         │   │
│   │  Start / Stop       │       │  IP · Port · Editor       │   │
│   │  Port · Max Conc.   │       │  Compile & Run · Output   │   │
│   │  Live Log           │       │  Syntax Highlight         │   │
│   └─────────────────────┘       └───────────────────────────┘   │
└───────────────┬─────────────────────────┬───────────────────────┘
                │                         │
                ▼                         ▼
  CompilerServerEngine            CompilerClient
  ┌──────────────────┐            ┌───────────────────┐
  │  TcpListener     │  TCP:8888  │  TcpClient        │
  │  AcceptLoopAsync │◄──────────►│  ConnectAsync     │
  │  SemaphoreSlim   │            │  WriteAsync (code)│
  │  Per-client Task │            │  ReadAsync (result│
  │  g++ compile     │            └───────────────────┘
  │  .exe execute    │
  │  GUID temp files │
  └──────────────────┘
```

### Request Lifecycle

```
Client                          Server
  │                               │
  │──── C++ source code ─────────►│
  │                               │ Write to /tmp/rc_<uid>.cpp
  │                               │ g++ compile (std=c++17)
  │                               │   ├─ FAIL → return compile errors
  │                               │   └─ OK   → execute with 10s limit
  │                               │             return stdout + exit code
  │◄─── result (text) ────────────│
  │                               │ Delete temp .cpp + .exe
```

---

## 🚀 Getting Started

### Prerequisites

> Make sure all three are installed before opening the project.

#### 1 · .NET 8 SDK

Download from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download).  
Verify in terminal:
```bash
dotnet --version
# Expected: 8.x.x
```

#### 2 · Visual Studio 2022

Download from [visualstudio.microsoft.com](https://visualstudio.microsoft.com/).  
During installation, select the workload:
```
✅ .NET desktop development
```

#### 3 · g++ (MinGW via MSYS2)

1. Download and install **MSYS2** from [msys2.org](https://www.msys2.org/)
2. Open the **MSYS2 UCRT64** terminal and run:
```bash
pacman -S mingw-w64-ucrt-x86_64-gcc
```
3. Add to Windows **System PATH**:
```
C:\msys64\ucrt64\bin
```
4. Verify:
```bash
g++ --version
# Expected: g++ (Rev...) 13.x.x
```

> [!IMPORTANT]
> You **must** restart Visual Studio after adding g++ to PATH, otherwise the server won't find it at runtime.

---

### Installation

#### Step 1 — Download & Extract

Download `RemoteCompiler.zip` and extract it. You will get:
```
RemoteCompiler/
├── RemoteCompiler.sln          ← open this
└── RemoteCompiler/
    ├── RemoteCompiler.csproj
    ├── Program.cs
    ├── CompilerServerEngine.cs
    ├── CompilerClient.cs
    └── MainForm.cs
```

#### Step 2 — Open in Visual Studio 2022

Double-click **`RemoteCompiler.sln`**  
→ Visual Studio opens with the full project loaded automatically.

#### Step 3 — Build

Press **`Ctrl + Shift + B`**

You should see at the bottom status bar:
```
Build succeeded    0 Error(s)    0 Warning(s)
```

#### Step 4 — Run

Press **`F5`**  
The app launches with the dark-themed UI.

---

## 🖱️ Usage

### Starting the Server

1. Click the **⚙ Server** tab
2. Set **Port** (default: `8888`) and **Max Concurrent** compilations (default: `4`)
3. Click **▶ Start**
4. The log will show:
```
[10:30:00]  Server online  |  port 8888  |  max concurrent: 4
```

### Compiling Code

1. Click the **Client** tab
2. Confirm **Server IP** is `127.0.0.1` and **Port** is `8888`
3. Write or paste your C++ code in the left editor pane
4. Click **▶ Compile & Run**
5. Output appears in the right pane

### Example — successful run

**Input (editor):**
```cpp
#include <iostream>
#include <vector>

int main() {
    std::vector<int> nums = {1, 2, 3, 4, 5};
    int sum = 0;
    for (int n : nums) sum += n;
    std::cout << "Sum = " << sum << std::endl;
    return 0;
}
```

**Output pane:**
```
[10:30:05]  Connecting to 127.0.0.1:8888 …
[10:30:05]  Connected. Sending code …
[10:30:05]  Sent 187 bytes. Waiting for result …
[10:30:06]  Result received (72 chars).

[EXECUTION OUTPUT]
Sum = 15

[Exit code: 0]
```

### Example — compile error

**Output pane:**
```
[COMPILATION FAILED]
temp.cpp:5:5: error: 'vectorr' was not declared in this scope
```

> [!TIP]
> Use the **✕ Cancel** button to abort a compile request at any time — useful if your code has an infinite loop that hasn't been caught by the 10-second timeout yet.

---

## 📁 Project Structure

```
RemoteCompiler/
│
├── 📄 RemoteCompiler.sln              Solution file (open this in VS2022)
│
└── 📂 RemoteCompiler/
    │
    ├── 📄 Program.cs                  Entry point — launches MainForm
    │
    ├── 📄 CompilerServerEngine.cs     TCP server with threading
    │   ├── CompilerServerEngine       Main engine class
    │   ├── ClientSession              Tracks one connected client
    │   ├── AcceptLoopAsync()          Accepts connections in background
    │   ├── HandleClientAsync()        Per-client Task (threaded)
    │   └── CompileAndRunAsync()       g++ compile + execute pipeline
    │
    ├── 📄 CompilerClient.cs           Async TCP client
    │   ├── CompilerClient             Client class
    │   └── CompileAsync()             Sends code, returns result
    │
    ├── 📄 MainForm.cs                 Windows Forms UI
    │   ├── BuildServerTab()           Server config + live log
    │   ├── BuildClientTab()           Editor + output split view
    │   ├── ApplyHighlight()           Syntax highlighting engine
    │   └── SendMessage() P/Invoke     Flicker-free editor updates
    │
    └── 📄 RemoteCompiler.csproj       .NET 8 WinForms project config
```

---

## ⚙️ Configuration

All settings are adjustable at runtime from the UI — no config files needed.

| Setting | Location | Default | Description |
|---|---|---|---|
| Server Port | Server tab | `8888` | TCP port the server listens on |
| Max Concurrent | Server tab | `4` | Max simultaneous `g++` processes |
| Server IP | Client tab | `127.0.0.1` | IP address to connect to |
| Client Port | Client tab | `8888` | Must match server port |

> [!NOTE]
> To run the server and client on **different machines**, change the Client IP from `127.0.0.1` to the server machine's local IP (e.g. `192.168.1.10`). Make sure port `8888` is open in Windows Firewall.

---

## 🛡️ Safety Limits

```
Session timeout  ──────  30 seconds   (drops unresponsive clients)
Execution limit  ──────  10 seconds   (kills infinite loops)
Buffer size      ──────  64 KB        (max code size per request)
Temp files       ──────  GUID-named   (no cross-client collisions)
Cleanup          ──────  Always       (deleted even on error/timeout)
```

---

## 🔧 Troubleshooting

<details>
<summary><b>❌ 'g++' is not recognized as a command</b></summary>

g++ is not on your system PATH.

1. Install MSYS2 from [msys2.org](https://www.msys2.org/)
2. Run in MSYS2 terminal: `pacman -S mingw-w64-ucrt-x86_64-gcc`
3. Add `C:\msys64\ucrt64\bin` to Windows System PATH
4. **Restart Visual Studio completely**
5. Test: open any terminal and run `g++ --version`

</details>

<details>
<summary><b>❌ Port 8888 already in use</b></summary>

Another process is using the port. Either:

- Change the port in the Server tab to any free port (e.g. `9000`)
- Or find and kill the process using it:
```bash
netstat -ano | findstr :8888
taskkill /PID <PID_NUMBER> /F
```

</details>

<details>
<summary><b>❌ Connection timed out on client</b></summary>

The server isn't running or isn't reachable.

- Make sure you clicked **▶ Start** in the Server tab
- Confirm the IP and port in the Client tab match the server
- If connecting across machines, check Windows Firewall allows port `8888`

</details>

<details>
<summary><b>❌ Build error: UseWindowsForms not found</b></summary>

The project is targeting the wrong framework.

Open `RemoteCompiler.csproj` and confirm:
```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWindowsForms>true</UseWindowsForms>
```
Not `net8.0` — it must be `net8.0-windows`.

</details>

<details>
<summary><b>❌ App only runs on Windows</b></summary>

This is by design. Windows Forms (`System.Windows.Forms`) is a Windows-only framework. To run on macOS or Linux, the UI layer would need to be replaced with a cross-platform framework such as Avalonia or MAUI.

</details>

---

## 🧰 Tech Stack

| Layer | Technology |
|---|---|
| Language | C# 12 |
| Framework | .NET 8 — Windows Forms |
| Networking | `System.Net.Sockets.TcpListener` / `TcpClient` |
| Threading | `Task.Run`, `async/await`, `SemaphoreSlim`, `CancellationToken` |
| Compiler | `g++` via `System.Diagnostics.Process` |
| C++ Standard | ISO C++17 (`-std=c++17`) |
| UI Rendering | `RichTextBox` + `SendMessage` P/Invoke (WM_SETREDRAW) |

---

## 📜 License

```
MIT License — free to use, modify, and distribute.
```

---

<div align="center">

Built with Bydii'S using C# · .NET 8 · WinForms

</div>

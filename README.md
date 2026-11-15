# NOVA — Universal Local-First Windows Desktop Companion

NOVA is a lightweight, local-first Windows desktop companion inspired by Dynamic Island interfaces. It provides a floating, interactive pill interface that responds to system events, media playback, notifications, and user actions.

## Core Philosophy

- **Local-First & Offline**: Zero mandatory cloud services, API keys, or remote accounts.
- **Hardware Efficient**: Native WPF / XAML rendering with event-driven updates. No heavy 3D game engines, WebGL, or constant polling.
- **Native Windows Integration**: Built directly on native Windows APIs and the modern .NET runtime.
- **Honest Integrations**: Never fakes functionality; accurately exposes capabilities supported by the Windows platform.

---

## Project Structure

```
Nova Island bar/
├── Nova.sln                     # Visual Studio / .NET Solution
├── README.md                    # Project documentation & build guide
├── DEVELOPMENT_STATUS.md        # Phase progress tracking & findings
├── src/
│   └── Nova/                    # Core WPF Application
│       ├── App.xaml             # Application markup
│       ├── App.xaml.cs          # Application entry, lifecycle & global unhandled exception handling
│       ├── MainWindow.xaml      # Foundation window (Phase 0)
│       ├── MainWindow.xaml.cs   # Foundation window code-behind
│       ├── Nova.csproj          # Project configuration (net8.0-windows, x64)
│       └── Core/
│           ├── AppInfo.cs       # Metadata & storage path resolution
│           └── AppLogger.cs     # Thread-safe, zero-dependency file logger
└── tests/
    └── Nova.Tests/              # Zero-dependency Unit Test Suite
        ├── Nova.Tests.csproj    # Test project configuration
        ├── TestHarness.cs       # Self-contained test runner & assertions
        ├── FoundationTests.cs   # Phase 0 environment & foundation tests
        └── Program.cs           # Test runner entry point
```

---

## Prerequisites

- **Operating System**: Windows 10 (version 1903+) or Windows 11 (x64)
- **Runtime / SDK**: .NET SDK 8.0.400+ installed (Host: .NET 10 runtime present)

---

## Build and Run Instructions

### 1. Build the Solution
```powershell
dotnet build Nova.sln -m:1
```
*(Note: `-m:1` is used to enforce single-node compilation when running inside sandboxed environments).*

### 2. Run the Test Suite
```powershell
dotnet run --project tests/Nova.Tests/Nova.Tests.csproj --no-build
```

### 3. Run the Application
```powershell
dotnet run --project src/Nova/Nova.csproj
```

To run a headless smoke test:
```powershell
dotnet run --project src/Nova/Nova.csproj -- --smoke-test
```

---

## YouTube Music Background Control & Shiny Dark UI

- **YouTube Music Integration**:
  - Connects to `https://music.youtube.com/` (or YouTube Music PWA / Edge / Chrome / Brave tabs) seamlessly via Windows Global System Media Transport Controls (`Windows.Media.Control`).
  - Detects active audio streams, displaying track title, artist name, and album artwork without requiring browser window focus.
  - Full background playback controls: Play/Pause (`⏯`), Next (`⏭`), and Previous (`⏮`) operate directly in the background.
  - Dedicated `🎵 YouTube Music` action button and tray/context menu item `🎵 Connect YouTube Music` for instant navigation.
  - Dynamic audio visualizer wave graphics (`ılıll`) with YouTube Music red glow.

- **Shiny Dark UI Aesthetic**:
  - Top specular glass rim reflection sheen on the floating island pill.
  - Obsidian glass gradient background (`#0D111A` to `#04060A`).
  - Cybernetic neon cyan rim border gradient (`#00F2FE` to `#0F172A`).
  - Upgraded companion mascot: metallic chrome shell with specular highlights, obsidian visor, and glowing cyan LED eyes with drop-shadow aura.
  - Frosted glass cards with dual-tone gradient borders and metallic transport buttons.

---

## Panoramic Top HUD Bar & Slide Workspaces

- **Panoramic Top HUD Mode**:
  - Reshaped top-anchored HUD bar (920px wide) featuring a 3-module dashboard:
    - **Media Player**: Album artwork, track title, artist, transport pills (`⏮ ▶ ⏭`), seek timeline (`00:41 ———— 03:04`), and YouTube Music integration.
    - **Date & Live Calendar**: Large month, 7-day live calendar strip with highlighted current day pill and red weekend indicators, and digital clock (`16:34`).
    - **System Monitor & Hardware HUD**: Real-time CPU, RAM, and DISK animated progress bars with live GB metrics, plus vertical volume capsule control.
- **Slide Modules**:
  - `📊 HUD`: Panoramic system and media overview.
  - `📝 Quick Notes`: Local-first scratchpad editor with auto-save, char/word counters, and one-click clipboard copy.
  - `✅ Tasks / To-Do List`: Checkable tasks with strikethrough, inline task creation, filters (`All`, `Active`, `Done`), and local persistence.
  - `⚡ Quick Tools`: Focus / Pomodoro timer (`25:00`) with presets + one-click desktop app launchers (Calculator, Notepad, Snipping Tool, Task Manager).
  - `🤖 Mascot Companion`: Interactive robotic companion with expressions (`Happy`, `Confused`, `Nod`) and bus telemetry.
- **Fluid Motions & Animations**:
  - Smooth morphing between compact pill (210x42) and panoramic HUD (920x118).
  - Smooth horizontal sliding card transitions (220ms slide-and-fade) between workspace views.
  - Fluid hardware stat bar glides with easing.

---

## Logs and Storage

Application logs are stored locally at:
- `%LOCALAPPDATA%\Nova\Logs\nova.log`
*(Can be overridden with the `NOVA_APPDATA_DIR` environment variable).*


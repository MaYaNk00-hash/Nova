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

## Logs and Storage

Application logs are stored locally at:
- `%LOCALAPPDATA%\Nova\Logs\nova.log`
*(Can be overridden with the `NOVA_APPDATA_DIR` environment variable).*

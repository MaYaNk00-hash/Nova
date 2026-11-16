# NOVA Desktop Companion — Installation & Deployment Guide

This guide explains how to install, configure, update, and uninstall **NOVA Desktop Companion** as a standalone Windows desktop application.

---

## 1. System Requirements

| Requirement | Specification |
|---|---|
| **Operating System** | Windows 10 (version 2004 / build 19041 or later) or Windows 11 |
| **Architecture** | 64-bit (x64 / AMD64) |
| **Runtime Requirements** | **None.** .NET 8 runtime is fully self-contained within the installation. |
| **Memory (RAM)** | 4 GB minimum (NOVA consumes ~50–90 MB at runtime) |
| **Storage** | ~220 MB free disk space for application binaries |
| **Privileges** | Standard user account (`asInvoker`). Administrative privileges are **not** required. |

---

## 2. Standard Installation (Interactive GUI)

1. **Obtain the Installer**:
   Download `NovaSetup-0.1.0-x64.exe` from your release distribution.

2. **Run Setup**:
   Double-click `NovaSetup-0.1.0-x64.exe`.

3. **Select Destination Folder**:
   - Default for current user: `%LOCALAPPDATA%\Programs\NOVA`
   - Default for system-wide (if elevated): `C:\Program Files\NOVA`

4. **Select Additional Tasks**:
   - [x] **Create a desktop shortcut** (optional)
   - [x] **Launch NOVA automatically when Windows starts** (optional, recommended for companion experience)

5. **Complete Installation**:
   Click **Install**. Once finished, check **Launch NOVA Desktop Companion** and click **Finish**.

---

## 3. Silent / Unattended Installation (IT / Automation)

For automated or enterprise deployments, run the installer with silent command-line switches:

```powershell
# Silent installation with default settings
.\NovaSetup-0.1.0-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

# Silent installation with custom destination directory
.\NovaSetup-0.1.0-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /DIR="C:\NovaApp"

# Silent installation enabling desktop shortcut and startup launch
.\NovaSetup-0.1.0-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /TASKS="desktopicon,startup"
```

---

## 4. Application Lifecycle & Daily Usage

### Launching NOVA
- **Start Menu**: Open the Windows Start Menu, search for **NOVA Desktop Companion**, and press `Enter`.
- **Desktop**: Double-click the **NOVA Desktop Companion** desktop shortcut (if created).
- **Run Dialog**: Press `Win + R`, type `Nova.exe`, and press `Enter` (if added to user PATH).

### Single-Instance Protection
NOVA enforces single-instance execution via a Windows named Mutex (`Local\NovaDesktopCompanion_SingleInstance_Mutex`). If you launch NOVA while it is already running, the new process signals the existing instance to restore/focus its window and exits immediately. Duplicate processes are never created.

### System Tray Management
- When minimized, NOVA docks into the Windows System Notification Area (System Tray).
- **Left-Click Tray Icon**: Restores the floating island to the top-center of the primary monitor.
- **Right-Click Tray Icon**: Opens the tray context menu:
  - **Show NOVA**: Restores the window.
  - **Launch with Windows**: Toggles automatic startup launch.
  - **Exit NOVA**: Completely closes all application processes and releases system hooks.

### Island Context Menu
Right-click anywhere on the floating island to access quick controls:
- **Toggle Mode**: Switch between Compact Notch, Expanded HUD, and Circle Bubble.
- **Eye Tracking**: Enable or disable robot cursor gaze tracking.
- **Launch with Windows**: Toggle automatic startup launch.
- **Minimize to Tray**: Send NOVA to the system notification area.
- **Exit NOVA**: Gracefully shut down.

---

## 5. User Data & Settings Persistence

NOVA follows Windows application data best practices:
- **Application Binaries**: Located in `{app}` (`%LOCALAPPDATA%\Programs\NOVA` or `C:\Program Files\NOVA`).
- **User Storage & Databases**: Stored in `%LOCALAPPDATA%\Nova\Storage\`:
  - `notes.json`: User quick notes.
  - `todos.json`: User task list items and completion states.
  - `preferences.json`: Window mode, eye-tracking toggle, and user settings.
- **Application Logs**: Stored in `%LOCALAPPDATA%\Nova\Logs\nova-runtime.log`.

> [!NOTE]
> During application upgrades or uninstallation, your personal data in `%LOCALAPPDATA%\Nova\Storage` is preserved by default so your notes and to-dos are never accidentally lost.

---

## 6. Upgrading NOVA

To upgrade to a newer version of NOVA:
1. Download the new installer (e.g. `NovaSetup-0.2.0-x64.exe`).
2. Run the installer. The setup wizard automatically detects the existing installation, cleanly stops the running `Nova.exe` process, updates all binaries, and preserves your user data and shortcuts.

---

## 7. Uninstallation

### Via Windows Settings
1. Open **Windows Settings** (`Win + I`).
2. Go to **Apps** > **Installed apps** (or **Apps & features**).
3. Search for **NOVA Desktop Companion**.
4. Click the three dots `...` and select **Uninstall**.

### Via Direct Uninstaller
Run the uninstaller executable in the application directory:
```powershell
& "$env:LOCALAPPDATA\Programs\NOVA\unins000.exe" /VERYSILENT
```

Uninstallation removes:
- All program files and libraries.
- Start Menu and Desktop shortcuts.
- Windows startup registry entry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NovaDesktopCompanion`).
- Add/Remove Programs registry keys.


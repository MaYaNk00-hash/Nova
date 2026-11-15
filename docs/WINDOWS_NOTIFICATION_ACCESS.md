# Windows Notification Access Architecture & Security Specification

## 1. Executive Summary

This specification documents the Windows operating system security boundaries, API requirements, and architectural trade-offs regarding notification interception for **NOVA** (Universal Local-First Windows Desktop Companion).

During Phase 5 development, rigorous empirical testing of the Windows WinRT notification subsystem was conducted. The findings confirm that:
1. Native Windows toast interception across third-party applications (e.g., Slack, Microsoft Edge, Outlook) is governed strictly by the **Windows Security & Privacy Model**.
2. The primary and only official API for third-party notification capture is `Windows.UI.Notifications.Management.UserNotificationListener`.
3. In a classic unpackaged Win32 desktop executable (`.exe`), `UserNotificationListener.Current.GetAccessStatus()` and `RequestAccessAsync()` return **`Denied`** unconditionally by Windows OS design.
4. Windows requires **Package Identity** and the declared restricted capability `<uap3:Capability Name="userNotificationListener" />` in an `AppxManifest.xml` to permit notification capture.
5. Sideloading packages with restricted capabilities requires administrative elevation to install signing certificates into the machine-wide `Cert:\LocalMachine\TrustedPeople` store.

Accordingly, NOVA enforces honest, explicit action semantics:
- Internal companion and application notifications operate via NOVA's priority queue and Dynamic Island banner.
- Clearing a notification from NOVA removes it from the Island queue and does **not** falsely claim to dismiss it from the native Windows Action Center when running unpackaged.

---

## 2. Windows Notification Security Model

### 2.1 The `UserNotificationListener` API
Introduced in Windows 10 Build 14393 (Anniversary Update), `Windows.UI.Notifications.Management.UserNotificationListener` allows an application to monitor, read, and manage notifications in the Windows Action Center across all desktop applications.

Because notifications frequently contain sensitive personal data (e.g., private messages, two-factor authentication codes, calendar events, emails), Microsoft categorizes `userNotificationListener` as a **Restricted Capability**:

```xml
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap3 rescap">

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
    <uap3:Capability Name="userNotificationListener" />
  </Capabilities>
</Package>
```

### 2.2 Why Classic Unpackaged Win32 Receives `Denied`
Classic Win32 executables launched directly from a folder have no **Package Identity** (`PackageFamilyName` or `PackageIdentity`).
When an unpackaged process calls:
```csharp
var status = UserNotificationListener.Current.GetAccessStatus();
```
The Windows kernel inspects the calling token. Finding no package manifest declaring `<uap3:Capability Name="userNotificationListener" />`, Windows immediately returns `UserNotificationListenerAccessStatus.Denied`.

When calling:
```csharp
var status = await UserNotificationListener.Current.RequestAccessAsync();
```
Windows does **not** display a user consent prompt. The prompt is only wired to applications with package identity where user consent can be stored in the Windows Privacy registry (`HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\userNotificationListener`).

---

## 3. Empirical Findings on Windows 10/11

The following automated and manual tests were executed on the target environment:

| Test Case | Invocation | Return Value | Rationale |
| :--- | :--- | :--- | :--- |
| **Direct Status Query** | `UserNotificationListener.Current.GetAccessStatus()` | `Denied` | Unpackaged Win32 token lacks package identity. |
| **Request Access Async** | `UserNotificationListener.Current.RequestAccessAsync()` | `Denied` | Consent prompt suppressed for unpackaged processes. |
| **Sparse Package Registration** | `PackageManager.RegisterPackageByUriAsync` | `0x80070005 (Access Denied)` | Installing restricted capability packages requires admin certificate trust in `LocalMachine\TrustedPeople`. |
| **Self-Signed Cert Install** | `Move-Item Cert:\CurrentUser\My -> LocalMachine\TrustedPeople` | `0x80070005 (Access Denied)` | Standard non-elevated user token cannot write to machine root cert stores. |

---

## 4. Packaging Options & Distribution Trade-Offs

Before altering NOVA's distribution model, the engineering trade-offs between packaging strategies must be evaluated:

| Dimension | Option 1: Unpackaged Portable Win32 *(Current)* | Option 2: Packaged MSIX (Store / Sideload) | Option 3: Sparse Package (External Location) |
| :--- | :--- | :--- | :--- |
| **Packaging Format** | Standalone `.exe` / Inno Setup | `.msix` or `.msixbundle` | Manifest referencing existing folder |
| **Installation Friction** | **Zero**. Run anywhere, portable. | Requires Store install or sideload certificate trust. | Requires registration script or installer. |
| **Administrator Privilege**| **Not Required**. Runs standard user. | Required once if installing self-signed sideload cert. | Required to register restricted capabilities. |
| **External Toast Interception**| **Unavailable** (`Denied` by OS). | **Available** (after user grants consent in Settings).| **Available** (if registration succeeds). |
| **Local Companion & App Notifs**| **100% Functional**. | **100% Functional**. | **100% Functional**. |
| **Media Controls (GSMTC)**| **100% Functional** (no package needed).| **100% Functional**. | **100% Functional**. |
| **File System / Registry**| Full native desktop access. | Virtualized / AppData container. | Native desktop access. |
| **Auto-Update Complexity**| Simple GitHub release check. | Windows Store or AppInstaller file. | Custom manifest updater. |

### Architectural Recommendation
NOVA should maintain its **universal, lightweight, portable architecture** as the default distribution. External notification capture should be exposed honestly as requiring a packaged installation or operating system consent. When running unpackaged, NOVA does not pretend to intercept Action Center toasts, and clearly presents local application and companion notifications.

---

## 5. Notification Action Semantics

In accordance with strict engineering discipline, NOVA explicitly differentiates between local queue management and native OS actions:

1. **Clear from NOVA Island (`CanDismissFromIsland`)**:
   - Always supported for any notification displayed in the Dynamic Island.
   - Clears the notification from NOVA's memory and hides the compact/expanded banner.
   - Button label: `✕ Clear` (when unpackaged).
   - Tooltip: `Clear from NOVA Island (Native Action Center dismissal requires packaged MSIX with notificationListener capability)`.

2. **Dismiss from Windows Action Center (`CanDismissFromActionCenter`)**:
   - Supported **only** when `WindowsAccessStatus == NotificationAccessStatus.Allowed` and `NativeId.HasValue`.
   - Invokes `_listener.RemoveNotification(nativeId)`.
   - Button label: `✕ Dismiss`.
   - Tooltip: `Dismiss from NOVA Island and native Windows Action Center`.

3. **Application Activation (`CanOpen`)**:
   - Guaranteed false unless a registered protocol or executable activation handler is explicitly available.
   - Avoids misleading "Open" affordances that cannot reliably activate the target process.

---

## 6. Implementation Summary

- **Service Interface**: `INotificationService` provides:
  - `WindowsAccessStatus`: Real enum reported by WinRT (`Allowed`, `Denied`, `NotSupported`, `Unspecified`).
  - `IsExternalCaptureAvailable`: Boolean (`WindowsAccessStatus == Allowed`).
  - `CanDismissNativeNotifications`: Boolean (`WindowsAccessStatus == Allowed`).
  - `AccessStatusExplanation`: Human-readable diagnostic explaining the exact OS security state.
- **Event Engine Integration**:
  - `notification.arrived`: Published on arrival with category `EventCategory.Notification`.
  - `notification.dismissed`: Published on dismissal, differentiating `"Cleared from NOVA Island queue"` from `"Action Center Notification Dismissed"`.
  - `notification.cleared`: Published when queue is cleared.
- **Automated Test Suite**:
  - 51 passing tests covering all states, priority ordering, expiry pruning, deduplication, real WinRT security boundaries, and local schema processing.


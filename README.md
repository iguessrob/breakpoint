# Breakpoint

<img src="docs/assets/breakpoint-icon.png" width="64" height="64" alt="Breakpoint icon">

Breakpoint is a Windows 11 app for setting internet hours and shutting down the PC at curfew. A small island at the top of the screen shows the time left. An owner PIN controls schedule changes, overrides, and closing the interface.

## How the schedule works

Choose Start, when internet becomes available, and End, when it turns off. Set the times with AM/PM wheels and choose which days to repeat. A new schedule first takes effect at its upcoming End.

At End, Breakpoint blocks internet and LAN access and starts a one-minute shutdown warning. The owner can enter their PIN to skip the restriction. The next Start restores networking, but it does not power on the PC.

| Island state | What it means |
| --- | --- |
| Green countdown | Internet is available. The timer counts down to End. |
| Orange countdown before End | End is five minutes away or less. |
| Orange shutdown countdown | Internet is off. The PC has one minute before a graceful shutdown request. |
| Logo only | No countdown is available. Hover to check status or open settings. |

<img src="docs/assets/online-countdown-dark.png" alt="Green island with two hours of internet remaining"> <img src="docs/assets/online-warning-dark.png" alt="Orange island with five minutes before internet turns off">

Hover over the pill for details. Click it to open Curfew, Appearance, or Owner access inside the island. Notifications appear in the same window. The app has a tray icon and stays out of the taskbar and Alt+Tab.

## Install

[Download the Windows 11 x64 installer](https://github.com/iguessrob/breakpoint/releases/download/v0.1.7/Breakpoint-Setup-0.1.7-win-x64.exe).

Run the installer and approve the Windows administrator prompt. Open Breakpoint from Start, set an owner PIN, and save the recovery code somewhere private. Choose your Start and End times, turn Curfew on, and save with your PIN.

The installer bundles .NET. You do not need VS Code or a separate runtime. Version 0.1.5 is an unsigned development build, so Windows may show a publisher or reputation warning. Installation starts with curfew disabled.

## Restore internet or stop curfew

Choose Skip this curfew and enter the owner PIN to restore internet for the current restriction. Future repeats stay enabled. To stop recurring curfew, turn it off in Curfew and save with your PIN.

These controls remain available after a restart or a delayed shutdown. An earlier shutdown request may still complete after internet is restored. Breakpoint reports restoration errors and allows another attempt by saving Curfew off.

Use the private recovery code in Owner access to reset a forgotten PIN. Uninstall through Windows Settings, Apps, Installed apps. Uninstall removes the service and Breakpoint's firewall rules but retains saved owner policy.

## Build and preview

Development requires Windows 11 and the .NET 10 SDK. Clone this repository and add the licensed [Areal desktop fonts](docs/typography.md). Then open PowerShell in its folder and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/preview.ps1
```

Preview uses a private worker that simulates enforcement. It does not block networking or shut down Windows. The build script runs the core, service, and UI checks before publishing the development executables.

To create a standalone installer, install the Inno Setup 7 compiler and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build-release.ps1 -Compiler "C:\Path\To\Inno Setup 7\ISCC.exe"
```

The script builds the installer and a portable preview ZIP in `outputs/release`. Generated packages, local policy files, and test data stay out of Git.

## Service and security

A Windows service owns the schedule, firewall rules, and shutdown requests. Closing the interface does not stop that service. The desktop talks to it over a local named pipe and verifies the registered service identity.

The service stores PIN and recovery-code hashes in a protected ProgramData folder. Authentication delays increase after repeated wrong attempts. A Windows administrator can still stop or uninstall the service.

The 0.1.7 build passed 204 automated checks. Shutdown and reboot recovery checks use fake enforcement and simulated restarts. Coverage of actual power cycles, VPNs, WSL, Hyper-V, and managed firewall policies still needs testing on disposable Windows machines.

## Source layout

- `src/Breakpoint.Desktop` contains the WPF island and tray interface.
- `src/Breakpoint.Core` contains schedules, authentication, and the message protocol.
- `src/Breakpoint.Service` contains the Windows service and enforcement adapters.
- `tests` contains the automated checks and installer fixtures.
- `packaging` contains the Inno Setup installer definition.
- `scripts` contains build, preview, and installation commands.

Read [usage and recovery instructions](docs/getting-started.md) or the [architecture notes](docs/ARCHITECTURE.md) for more detail.

## Privacy and terms

Please review the Privacy Policy and Terms and Conditions before installing. The installer displays both documents and places offline copies into the installation folder.

If you have questions or encounter issues, reach out via GitHub Issues.

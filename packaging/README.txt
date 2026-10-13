Breakpoint 0.1.8 — Windows 11 x64

INSTALL
Run Breakpoint-Setup-0.1.8-win-x64.exe. Windows administrator approval is required to install the background service.
Choose the Windows account that uses this PC. Open Breakpoint from the Start menu, set the owner PIN, save the recovery code privately, then enable Curfew.
Start means internet on. End means internet off, followed by a one-minute shutdown warning. An owner PIN can skip the current restriction. The PC must be powered on manually.
Fresh installation is disarmed; reinstall retains owner credentials and times but disarms enforcement until enabled again.

PORTABLE PREVIEW
Extract the ZIP and open Breakpoint.exe inside the extracted folder. Keep Breakpoint.Service.exe beside it.
The portable-preview.flag makes this package a safe preview: networking and shutdown are simulated.
Do not remove the marker to enable real enforcement. Use the installer for enforcement.
The runtime is included; no VS Code, terminal, or separate .NET installation is required.

BACKGROUND
Breakpoint has a tray icon and an island, with no taskbar or Alt+Tab entry. Quit interface requires the owner PIN. The installed service continues independently of the interface.

UNINSTALL
Use Windows Settings > Apps > Installed apps > Breakpoint > Uninstall.
The uninstaller stops/removes the service and removes only Breakpoint-owned firewall rules before deleting installed files. Saved owner hashes and policy in ProgramData are retained.
If cleanup fails, uninstall stops and retains the files needed to retry cleanup.

LIMITS
This is an unsigned development build. Windows may display a publisher/reputation warning.
Real installation, firewall cleanup, and shutdown must be qualified in a disposable Windows VM before relying on enforcement. Automated tests use fake or no-op enforcement.
An accepted Windows shutdown can be too late to cancel. Shared administrator access can stop or uninstall the service.

THIRD-PARTY SOFTWARE
Includes the Microsoft .NET runtime under the MIT license and its third-party notices. Installer built using Inno Setup.
https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT
https://github.com/dotnet/runtime/blob/v10.0.0/THIRD-PARTY-NOTICES.TXT

INSTALL FAILURE (0.1.8)
Read-only preflight stops early failures before copying files. Later service setup failure reports its actual stage/error, does not launch the app, exits with code 41, and retains app files for repair or uninstall. Setup logs are enabled in the Windows temporary directory. Missing owned firewall rules are normal on first install and no longer abort setup.

SCHEDULE AND RECOVERY (0.1.8)
A newly saved schedule first enforces at its upcoming End time, not a restriction window from yesterday. At End, internet turns off and the one-minute shutdown warning begins. A valid owner PIN can skip the current restriction or save Curfew off, even after a delayed shutdown or restart. Networking restoration is independent of cancelling an already requested Windows shutdown; the app warns if Windows might still shut down. Failed restoration is reported and can be retried by saving Curfew off.

PRIVACY AND TERMS
Read PrivacyPolicy.txt and TermsAndConditions.txt before installing. Offline copies and Start menu shortcuts are included. Publisher: iguessrob. Contact: https://github.com/iguessrob/breakpoint/issues

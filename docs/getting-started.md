# Breakpoint 0.1.5

Download the installer EXE from [the 0.1.5 release](https://github.com/iguessrob/breakpoint/releases/tag/v0.1.5). It bundles the .NET runtime and installs the background service. The portable preview ZIP simulates internet restrictions and shutdown.

Open **Breakpoint** from Start, configure your private owner PIN in **Owner access**, and save the recovery code privately. Open **Curfew**, choose **Start · internet on** and **End · internet off** using AM/PM time wheels, select repeat days, enable Curfew, and save with your PIN.

A newly saved schedule first enforces at the next End time. For example, saving at 8:37 PM with Start 8:38 PM and End 8:39 PM leaves internet available until 8:39 PM. At 8:39 PM, internet turns off and the one-minute shutdown countdown begins. Saving a schedule does not activate yesterday's restriction. Later Start restores internet automatically; the PC must be switched on manually.

## Recover internet without uninstalling

Open island settings and choose **Skip this curfew · PIN** to restore internet for the current restriction while keeping future repeats. To stop recurring restrictions, open **Curfew**, turn **Curfew enabled** off, and save with your owner PIN. Both work after a delayed shutdown or reboot. Windows might still complete a shutdown that was already requested; the app reports that separately from internet restoration.

If restoration fails, the app reports the error. Try saving Curfew off again to retry cleanup. Use **Owner access** and your private recovery code to reset a forgotten PIN. Keep that code private.

## Uninstall

Use Windows Settings → Apps → Installed apps → Breakpoint → Uninstall. The cleanup removes only Breakpoint-owned service/firewall rules. Owner policy is retained in ProgramData. Installation is initially disarmed, including after reinstall.

## Development and checks

Open a PowerShell terminal in the cloned repository. Use `scripts/build.ps1` for builds and fake/no-op enforcement checks, `scripts/preview.ps1` for preview, and `scripts/build-release.ps1` for standalone packages. Installer failure tests are in `scripts/check-installer.ps1`. This development build is unsigned. Reboot recovery is covered by simulated restart tests; the checks do not shut down your PC.

## Pill countdown

While internet is available and an End is scheduled, the logo and countdown are green. At five minutes remaining they turn orange. At End the internet-off state takes over and the one-minute shutdown countdown begins. Hovering identifies whether the timer means internet off or shutdown. No schedule or a networking error keeps the idle logo-only view.

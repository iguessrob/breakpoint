# Breakpoint terms and conditions

Effective date: October 13, 2026
Publisher: iguessrob
Contact: https://github.com/iguessrob/breakpoint/issues

## Using Breakpoint

These terms apply to the Breakpoint Windows desktop app and service distributed by iguessrob. Review them before installing or using the app. The installer requires acceptance before installation. If you do not agree, do not install it, or uninstall an existing copy.

You may download, install, and use the distributed app on a PC you own or are authorized to administer, subject to these terms and applicable third-party licenses. These terms do not supply an open-source license for the source repository. Any separate source-code license must be reviewed on its own terms.

## Authority and other users

Only configure restrictions on a PC you are authorized to manage. Inform other affected users about the schedule, internet restrictions, shutdown warning, and how to contact the owner. Obtain any consent or permission required by applicable law. Do not use Breakpoint to harass someone, secretly interfere with a device you do not control, or obstruct emergency access.

If you use Breakpoint for a child or another person in your care, you remain responsible for having the necessary authority and for an appropriate recovery plan. The app does not independently verify that relationship or obtain consent from other users.

## How restrictions work

Start means internet becomes available; End means internet becomes restricted. Repeat days and the time zone affect the schedule. A newly enabled schedule first takes effect at its upcoming End. Once a restriction begins, it can persist after reboot until its release time or an authorized override.

At End, the installed service attempts to block networking across the PC, including LAN access, and begins a one-minute warning before requesting a graceful Windows shutdown. The owner PIN can skip the current restriction or save Curfew off. Once Windows has accepted a shutdown request, an override may restore networking without stopping that shutdown. The app cannot guarantee that Windows will finish shutting down, or that every network configuration can be restricted or restored successfully.

The next Start restores networking; it does not turn on a powered-off PC. Safe preview simulates networking and shutdown and does not provide real enforcement.

## Background operation and security limits

The live service runs with Windows system privileges. The interface can run in the background without a taskbar or Alt+Tab entry. Closing or hiding the interface does not disable the service or an enabled curfew.

Keep your PIN and recovery code private. The PIN protects supported owner actions but does not stop a Windows administrator from changing or removing the service. Shared Windows accounts do not provide separate identity boundaries. Breakpoint is not a substitute for Windows account security or a guaranteed tamper-proof control.

## Your responsibilities and risks

Save your work before curfew. Shutdown and loss of networking can interrupt downloads, calls, games, remote sessions, and applications, and may cause loss of unsaved work. Keep backups and a way to regain administrator access. Test your intended schedule and recovery controls before relying on them.

Do not rely on Breakpoint as the sole control for emergency communications, medical equipment, critical infrastructure, or any situation where losing networking or power could cause injury or serious harm. Firewall policies, VPNs, virtual machines, system clock changes, sleep, restarts, or Windows updates can affect behavior.

Use supported uninstall procedures. Do not delete program or policy files while the service is running. Uninstall attempts to remove the service and its owned restrictions and retains owner policy. Resolve a failed removal before assuming networking has been restored.

## Privacy and third-party components

The accompanying privacy policy describes local storage, logs, recovery codes, and optional GitHub support submissions. It is a notice of data handling, not permission for unrelated collection.

The app includes Microsoft .NET components and ABC Areal fonts with their supplied licenses. Those licenses govern those components. Permission to use Breakpoint does not grant permission to extract, distribute, convert, or modify Areal fonts beyond the publisher's license. License notices accompany the installed app.

## Updates and support

Breakpoint is currently a development prerelease. Releases can change behavior or contain defects. Updates are downloaded and installed manually; there is no built-in automatic updater. Support through GitHub Issues is provided as available, with no promised response time, uptime, maintenance period, or recovery outcome.

## Warranty and liability

To the extent applicable law permits, the app is provided as is and as available, without warranties of uninterrupted operation, fitness for a particular purpose, or error-free enforcement. The publisher does not guarantee prevention of access, shutdown completion, or recovery of interrupted work.

To the extent applicable law permits, the publisher is not liable for loss of data, loss of access, lost profits, or indirect or consequential damage arising from use of the app. These terms do not exclude liability that cannot lawfully be excluded, including non-waivable consumer rights or liability for fraud or other conduct that applicable law protects against. No fixed monetary liability cap is imposed by these terms.

## Ending use and changes

You can stop using the app by disabling curfew and uninstalling it. The third-party license requirements and any provisions needed to address prior use continue to apply where legally enforceable.

Updated terms will include a new effective date and accompany the relevant release. Changes do not retroactively alter rights that applicable law protects. Disputes and mandatory rights are governed by applicable law; these terms do not impose a particular court, arbitration clause, or waiver of statutory rights.

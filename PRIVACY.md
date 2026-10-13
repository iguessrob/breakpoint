# Breakpoint privacy policy

Effective date: October 13, 2026
Publisher: iguessrob
Contact: https://github.com/iguessrob/breakpoint/issues

## Scope

This policy describes the Breakpoint Windows desktop app and its background service. Downloading the app or contacting the publisher through GitHub also involves GitHub, which has its own privacy statement at https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement.

## What stays on your PC

Breakpoint uses local data to enforce the schedule you choose:

- Schedule Start and End times, repeat days, time zone, one-time dates, enabled state, and schedule revision and activation time.
- Salted owner PIN and recovery-code hashes, failed authentication counts, and lockout deadlines. The saved policy does not contain the plaintext PIN or recovery code.
- Current and skipped curfew identifiers, shutdown deadlines and outcomes, and legacy timer state used for compatibility and recovery after restart.
- Appearance, warning sound, and reduced motion preferences.
- Local service event logs with timestamps, curfew identifiers, command names, authentication failures, shutdown outcomes, and error types. Service logging does not record PINs or recovery codes.

Setup uses the selected Windows account to identify its security identifier, or SID. The registered service command contains that SID so the service can authorize local communication. Setup and uninstall logs can contain account names, installation paths, and diagnostic messages.

The app checks Windows service identity, firewall state, system time, and display information to operate. It does not record browsing history, website contents, network traffic contents, screenshots, or general keyboard activity. Text entered into a PIN field is handled for authentication.

## Storage and retention

The installed service stores its policy and event logs in %ProgramData%\Breakpoint. Windows access permissions restrict this directory to administrators and LocalSystem. This is access protection, not encryption of the complete policy file.

Desktop preferences are stored under %LocalAppData%\Breakpoint. Safe preview stores its separate simulated policy and logs under %LocalAppData%\Breakpoint\Preview, in a folder identified by the preview installation.

Schedule, authentication, and recovery state are kept until replaced or removed. The service retains the most recent 100 skipped curfew identifiers. On a log write, it attempts to remove event log files whose modification time is more than seven days old. Old logs can remain when the service is inactive or cleanup fails. Windows manages setup, uninstall, clipboard history, and operating-system logs separately.

Uninstall removes the service and its owned firewall rules, but deliberately retains saved policy. Local preferences and preview data can also remain. Backups you create have their own retention.

## Transmission and sharing

The shipped app has no built-in analytics, advertising, cloud account, remote administration, automatic update check, or automatic crash-report upload. It does not send its local policy, PIN hashes, or service logs to iguessrob. The desktop and service exchange commands and status through a local Windows named pipe.

If you download a release, open a GitHub page, or submit an issue, GitHub processes that interaction under its own terms. Information you voluntarily submit is available to the publisher and people who can access that repository. An issue may be public if the repository is public. Do not post PINs, recovery codes, complete state files, personal account names, or unredacted logs. The repository does not currently provide a dedicated private contact channel for sensitive privacy reports.

## Recovery codes and shared PCs

A recovery code is shown after owner setup or rotation. Clicking Copy recovery code places it in the Windows clipboard. Clipboard history, synchronization, or other software with clipboard access may retain it. Breakpoint does not automatically clear the clipboard. Store the code privately and clear clipboard history as appropriate.

People sharing one Windows account share its preferences and access to information displayed by the app. A PIN protects supported owner actions; it does not create a separate Windows identity. Administrators and software running with sufficient privileges can access or change local files and service configuration.

## Your choices

Use Curfew and your owner PIN to change the schedule or turn it off. Use Owner access and your private recovery code to reset a forgotten PIN. You can uninstall through Windows Settings.

To remove retained app data, first successfully uninstall and confirm that Breakpoint's service and restrictions have been removed. A Windows administrator can then delete the retained %ProgramData%\Breakpoint directory, and each Windows user can delete their %LocalAppData%\Breakpoint directory. Do not delete live policy as a way to recover internet. Breakpoint cannot remove copies from your backups, GitHub submissions, or Windows-managed history.

For information you submitted to the publisher, use the contact above to ask about access, correction, or deletion. Share only a non-sensitive description there. GitHub account data and platform copies must also be addressed through GitHub. Rights and obligations depend on applicable law; this policy does not limit those rights.

## Changes

Material changes to app data handling will be described in an updated policy and release notes, with a new effective date. The policy included with a release describes that release. Check the repository policy when downloading a newer version.

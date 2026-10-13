# Breakpoint architecture decision — accepted implementation

Date: 2026-10-13. Windows 11, C#/.NET 10, WPF. The user approved the design and directed implementation in this workspace. This supersedes the original spec's framework-undecided and self-managed defaults.

## Authority and schedule

The user and brother share one Windows account. First setup requires administrator approval. Thereafter the owner PIN authorizes schedule edits, enabling/disabling, starting/cancelling timers, overrides, PIN changes and interface exit. The island can be hidden and settings closed without authentication. Windows identity scopes the pipe to the installed account; the PIN identifies the owner within that account. Administrator bypass remains possible.

Schedule labels are **Start – End**. Start blocks non-loopback networking and begins a 60-second service-owned save-work countdown. End removes owned rules. An End earlier than Start is the next day. Weekdays refer to Start. Manual timers coexist with the recurring schedule; manual restrictions last eight hours after timer Start unless skipped. This duration is an explicit initial default, exposed in the timer confirmation.

Skip is available before the service requests shutdown, including during the final minute. It skips all currently overlapping occurrences, removes owned rules, and leaves future schedules enabled. There is no 10-minute extension quota in this implementation. An already accepted immediate shutdown cannot be promised cancellable.

## Process boundaries

- `Breakpoint.Core`: typed protocol, calendar/DST resolution, PBKDF2 owner secrets.
- `Breakpoint.Service`: native Windows Service dispatcher, durable state, serial scheduler/commands, local pipe, OS adapters.
- `Breakpoint.Desktop`: WPF settings and island; Windows tray icon; no taskbar button. The UI cannot grant authorization locally.
- `Breakpoint.Checks`: dependency-free automated checks with injected clocks/fake effects and a real no-op pipe worker.

Native Windows service hosting and Windows Firewall COM management avoid third-party runtime packages. Framework-dependent publication requires the .NET 10 Desktop Runtime. Installation copies both executables to Program Files, protects that directory, registers an automatic LocalSystem service, and configures UI startup. LocalSystem is used for machine-wide firewall/shutdown access; the surface is limited to typed commands. A lower-privilege service account is a future hardening task requiring Windows VM validation.

## IPC and state

Length-prefixed JSON is limited to 16 KiB. The pipe uses a DACL limited to SYSTEM, administrators and the selected account; native `PIPE_REJECT_REMOTE_CLIENTS` rejects network callers. Caller impersonation occurs after the request is read and before its command executes. A single first-instance pipe prevents a second server while the real service owns the name. The client matches the pipe server PID to the running service and checks its executable path and LocalSystem account in protected registration. Preview uses process-image verification. Installed Program Files ACLs protect the executable against standard-user replacement. This does not protect against a Windows administrator.

Policy, PIN/recovery hashes and operation intent are written atomically with disk flushes to a SYSTEM/administrators-only ProgramData directory. PIN hashing is PBKDF2-SHA256, 600,000 iterations, random 16-byte salts, fixed-time comparison. Recovery is a random 160-bit code returned once. Global increasing login backoff persists across restarts. No credentials or packet data are logged. Logs expire after seven days.

## Windows effects

Windows Firewall COM creates two owned block rules, inbound and outbound, protocol Any, all profiles and interfaces, excluding IPv4 loopback and IPv6 `::1`. Only exact owned rule names/group are removed. Disabled profiles or local policy conflicts prevent arming. Readback verifies configured rules; it does not prove full network coverage. The UI therefore reports **Firewall rules active**, not a verified guarantee of all traffic blocked.

Shutdown uses local `InitiateSystemShutdownExW`, zero OS timeout after the internal one-minute countdown, planned reason, no reboot, and no forced application closure. The journal marks intent before calling Windows. Crash uncertainty is reported as Unknown and is not blindly retried. Accepted is distinct from complete and becomes Blocked or delayed if still running after 30 seconds. The UI never claims observed power-off.

Monotonic elapsed time prevents clock rollback buying time within a running service. Calendar windows use the configured zone. A scheduling gap over ten seconds is conservatively treated as resume and gives a new save-work minute while restriction is active. Restart inside curfew also gives a fresh minute unless shutdown acceptance is already recorded. Persistent firewall rules outlive a service crash; service recovery and offline cleanup are supplied.

## Validation and release boundary

The build and fake-effect checks are safe for the developer PC. Real adapters are implemented but are **not release-qualified** until tested in a disposable Windows VM. Required remaining evidence: existing connections, IPv4/IPv6 and QUIC, VPN/WSL/Hyper-V, group policy conflicts, standard-user versus SYSTEM IPC, service restart/reboot, actual graceful shutdown, installer/uninstall, Narrator/high-contrast/DPI and monitor switching.

Notifications currently use tray balloon notifications, which Windows can suppress. Persistent island/main status provide the equivalent warning. Warning deduplication is per UI session, not yet a durable delivery ledger. Island placement clamps to the primary monitor and is not yet persisted. No kernel driver, telemetry, remote management, auto-update, forced shutdown or wake timer is included.

Platform references: [Windows Firewall rule properties](https://learn.microsoft.com/en-us/windows/win32/api/netfw/nn-netfw-inetfwrule), [remote address syntax](https://learn.microsoft.com/en-us/windows/win32/api/netfw/nf-netfw-inetfwrule-put_remoteaddresses), [Windows service entry point](https://learn.microsoft.com/en-us/windows/win32/services/writing-a-service-program-s-main-function), [shutdown behavior](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-initiatesystemshutdownexw).

## Island interaction update — 2026-10-13

Apple's Live Activities guidance describes minimal, compact and expanded presentations; touch-and-hold reveals expansion on iPhone. Breakpoint adapts expansion to desktop hover rather than claiming Apple uses hover. Its default is a 190 × 58 logical-pixel pill containing the brand and current status/time. Hover dwell is 120 ms; pointer-exit grace is 260 ms. Click pins/unpins details, keyboard focus can open details and Escape collapses. An active shutdown countdown remains legible in the minimal pill without forcing expansion; PIN-protected actions remain available in expanded/main/tray views.

Motion uses a stationary transparent host and centered surface rather than repeatedly resizing the native window. Width/height morph over 320 ms with cubic ease-out; detail opacity/translation are staggered. Transitions start from current values and replace interrupted animations. Windows ClientAreaAnimation disables motion when requested. These timings are Breakpoint product choices, not Apple-prescribed values. The implementation has no perpetual bounce or per-second layout animation.

Primary button labels inherit their control foreground instead of the global TextBlock foreground. Contrast is verified on rendered controls in dark and light themes (minimum 4.5:1). Offscreen routed-event tests verify minimal startup, hover expansion, exit collapse, click pinning, and no forced full-panel expansion at shutdown start.

References: https://developer.apple.com/design/human-interface-guidelines/live-activities ; https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/easing-functions ; https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemparameters.clientareaanimation
## Approved integrated island — 2026-10-13

The reviewed browser preview is now implemented as the application surface. App starts `DesktopController` and a single `IslandWindow`; the old separate settings/dialog views are excluded from compilation. Default placement is top center. The visible idle surface is 100×28 logical pixels, hover peek 240 pixels wide, settings 360 pixels wide with content-driven height capped at 420. The transparent native host stays stationary; the visible surface morphs centrally. Its palette, Segoe UI typography, spacing, rounded inputs, menu and inline flows follow the approved preview.

Schedule, Timer, Appearance, owner setup/change/recovery, PIN confirmation, dirty-edit confirmation and one-time recovery-code display all stay inside the island. Commands use the existing typed pipe service; UI never grants owner permission locally. Editors keep their draft revision. Wrong PINs stay on the auth page, credentials clear after use, and polling never rebuilds active forms. First live setup still uses Windows UAC and a separate elevated setup instance because Windows approval cannot be embedded in an app-controlled surface.

Island notifications are primary: 300×54 notification presentation for six monotonic seconds, paused while hovered. In settings a fixed banner remains visible above scrollable content and preserves input. Expiry shrinks the pill or removes only the banner. Hidden-island warnings fall back to the tray notification. Active shutdown retains a countdown in the tiny pill. Accepted/delayed shutdown has distinct text and is never represented as completed power-off.

Build validation includes 46 core/service checks and integrated offscreen UI checks for both themes: idle/peek/menu geometry, contrast, vertical/horizontal clipping, typed PIN-confirmed save, failed-PIN behavior, preserved drafts, dismissal, notification pause/return and maximum height. Generated images are under `outputs/ui-checks`. Real enforcement and installer still require the previously documented disposable-Windows-VM checks.
### Animated transition regression fixes
The animated hover/page harness reproduces bugs that the static render checks missed: the collapse opacity clock held the hover body at zero, and measuring the ContentControl before its deferred content update reused the previous page height. Opening now clears old opacity/translation clocks and measures the incoming page directly. The cap uses a fixed target layout with single-line brand text and device-pixel rounding so width morphs cannot wrap the name. Navigation resets page scrolling; display reposition refreshes available height. Tests exercise actual WPF animation frames, repeat interrupted collapse/hover, compare returning page height with a fresh menu, and verify constrained-height navigation. Host dimensions are compared after Windows DPI rounding.

### Logo-only idle and reliable hover exit
Idle is now a centered 40 × 28 logo-only pill. Mouse-leave starts a 260ms exit delay; background status updates refresh the peek text in place and cannot cancel that delay or restart its fade. A 100ms pointer-position reconciliation checks the visible rounded shell, covering missed mouse-leave events without dismissing explicit settings panels. Hover waits 160ms, then uses critically damped size easing (420ms opening/340ms closing), a staggered content fade, and a small translated reveal. Reduce motion remains immediate. Animated regression checks reproduce exit-time polling, missing leave events, and polling during the content fade.

### Alt+Tab exclusion and first-frame text flash
SourceInitialized sets WS_EX_TOOLWINDOW and clears WS_EX_APPWINDOW on the native island handle. ShowInTaskbar=false alone did not exclude the borderless island from Alt+Tab. No WS_EX_NOACTIVATE is added: PIN fields retain interactive keyboard access. Native-style checks cover both startup and interactive settings.
The delayed content fade previously had base opacity 1, exposing incoming text before the delayed animation changed it to zero. Incoming content and an opening header now start at opacity 0 before delayed reveals. Fractional text translation is removed; shell dimensions continue their damped morph while text fades at stable local positions. Animation-frame regressions verify hidden initial frames and monotonic opacity.
Windows style reference: https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles

### Compact active curfew
The resting pill remains 40 × 28 with only the logo while inactive. During an active curfew it widens to 100 × 28 and shows the logo plus the service's shutdown countdown in MM:SS. Countdown values derive from ShutdownAt minus service Now; polling updates the label without restarting the morph. Non-countdown shutdown outcomes retain truthful status labels. Hover and owner controls remain available. Notification dismissal returns to the active pill, and skip/release restores the logo-only pill.

### Tray GDI+ regression
Repeated opening of the light-theme tray menu reproduced ExternalException in Bitmap.SetPixel during header rendering. The renderer was recoloring an icon-derived bitmap clone in place. It now keeps the source immutable and applies an ImageAttributes color matrix only when drawing. Regression checks open/close the menu eight times in each theme with Windows Forms exceptions propagated to the test process, then capture the rendered result.


### Unified Curfew with opening/closing times
The desktop removes the separate Timer navigation and exposes one Curfew editor. Start means internet on, End means internet off. Internally HH:mm remains the protocol format; the UI uses h:mm AM/PM and validates 12 AM/PM conversions. Schedule.WindowKind preserves missing/legacy Blocked semantics and new saves opt into Allowed. AllowedWindows resolves the complement of selected internet-on windows, including overnight and weekend gaps. OneTimeDate identifies the next local opening day; its restriction ends at the following opening and disarms. Persisted schema 2 prevents older binaries from silently interpreting new policies backward; the new service reads versions 1 and 2. Saving an Allowed policy clears legacy manual timers. No firewall/shutdown effects were executed outside fake/no-op adapters.

## Standalone packaging

The release script publishes separate self-contained win-x64 single-file desktop/service executables with native dependencies bundled. The desktop apphost is named Breakpoint.exe. A portable-preview.flag opts the ZIP into no-op enforcement; the installer explicitly includes only the two executables, README, and runtime license notices. Preview state moved to LocalAppData, preserving old per-folder preview state when available, so it works from Program Files and read-only distribution folders.

The Inno installer requires elevation, validates the selected Windows account, installs only to Program Files/Breakpoint, registers the service, recovery actions, login startup, and shortcuts, and integrates Windows Apps uninstall. The helper disarms policy on installation while retaining owner credentials, and removes only owned firewall rules during uninstall. Cleanup failures abort uninstall before deleting the binaries. A cancelled installation attempts cleanup from the extracted bundled helper. Existing services require uninstall first; automatic in-place upgrades are not yet qualified.

Standalone smoke checks run the desktop resource check and owner translation with DOTNET_ROOT set to a nonexistent directory, and exercise real local IPC against the packaged no-op worker. These are not substitutes for live install/uninstall/firewall qualification in a Windows VM. No live service was installed on the development PC. Windows file-lock persistence regression was reproduced with a destination lease; atomic rename now retries bounded lock/access errors and still propagates persistent failures.

### Installer failure paths (0.1.2)

The isolated installer fixture reproduced the file-callback Abort error and an incorrect success exit code. Service setup now runs a read-only preflight before file installation and captures helper output, including the failing setup stage. Later service setup failure retains app files for repair/uninstall, suppresses launch, shows a failure finish page, and exits 41. It does not promise automatic file rollback. Native registration failure cleans up the newly created service and preserves both primary and cleanup errors. `scripts/check-installer.ps1` exercises the production Pascal code using a fake helper without SCM/firewall/shutdown calls; `-PreflightFailure` also checks failure before app files are copied. Live service installation failure remains under diagnosis pending elevated execution authorization.

Root cause reproduced without mutation: `--diagnose-install --owner-account ...` failed when reading absent owned firewall rules with HRESULT 0x80070002. .NET mapped that COM HRESULT to an exception outside the old COMException-only catch. Shared rule lookup now returns null only for ERROR_FILE_NOT_FOUND, and propagates other failures. Apply, cleanup, and removal verification use that shared lookup. The read-only diagnostic is the regression loop against the actual Windows Firewall COM API.

### Repairing uninstall from an older installed build
Version 0.1.1 remains installed on the reported PC, so its uninstaller still calls the defective missing-rule cleanup implementation. The reviewed scripts/repair-uninstall.ps1 verifies the 0.1.2 helper hash, backs up the installed helper, removes only the owned service/rules using the fixed helper, replaces the old helper, and invokes the existing Inno uninstaller. It refuses unexpected versions, missing uninstall files, or an already-open uninstall window. Owner policy is retained. Execution awaits explicit authorization; no live cleanup has been performed.

### Unelevated installed-client verification (0.1.3)

The installed service answered a raw read-only status request, but the real desktop client failed opening the SYSTEM process with Win32 access denied. Live verification now queries only SERVICE_QUERY_STATUS in SCM, matches the named-pipe server PID to the running own-process service, and checks protected SCM registry executable and LocalSystem account. Preview verification retains same-user process-image checking. The CLI regression `dotnet run --project tests/Breakpoint.DesktopChecks -c Release -- --installed-service-check` reproduced failure and now succeeds without elevation or schedule changes. Negative identity snapshots reject a different PID, stopped state, shared host, wrong executable/account, or ambiguous path suffix.

### First End and owner recovery (0.1.4)

Saving an Allowed schedule records a service-owned ScheduleArmedAt timestamp; candidate restrictions whose End-derived onset is earlier than that timestamp are excluded. This prevents an 8:37 PM save with Start 8:38 PM and End 8:39 PM from enforcing yesterday’s gap. Arming survives restart; subsequent End/Start windows retain their prior behavior. Legacy policies without the field retain their interpretation until saved.

Accepted/unknown/delayed shutdown journals no longer prohibit authenticated Skip or schedule changes. Those commands restore networking and persist the skip or disabled schedule, while warning that Windows might still complete a previously requested shutdown. They do not claim native shutdown cancellation. Failed rule removal reports failure, and an authenticated Save of Curfew off resets restoration retries. Tests cover the exact reported times, restart journal states, wrong PIN, disable/skip, persisted override, and a failed removal retry. All enforcement tests use fakes/no-op workers. Delayed island views show Internet is off rather than Shutdown in 00:00.

### Internet-on countdown (0.1.5)

The compact island shows a green logo and countdown to the service-provided next restriction onset while internet is available. It uses HH:MM:SS for durations of at least one hour and MM:SS below an hour. Both change to orange at five minutes remaining. At End, the existing orange shutdown countdown takes over. Failed restoration, unknown networking, unavailable service, or no next restriction retain the logo-only state. Pill width follows timer length and polling never rebuilds editors.

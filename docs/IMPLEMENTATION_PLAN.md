# Desktop monitor implementation plan

## Provider expansion checkpoint (2026-08-25)

- [x] Define isolated per-provider policy and persistence domains.
- [x] Define multi-window quota aggregation and fail-closed semantics.
- [x] Add allowlisted Codex and Claude Code discovery/capability reporting.
- [x] Add independent provider policy/settings/window contracts and Claude's
  documented local 5-hour/weekly status-line bridge.
- [x] Render one settings/status tab per configured provider.
- [x] Add preserved one-click Codex and Claude Code configuration.
- [x] Add optional fixed Launch Together shortcuts without a process watcher.
- [x] Add a shareable, user-scoped package and configurable install root.
- [x] After user approval, install the verified build under a user-selected
  non-system directory and prepare the feature branch for its approved
  merge to `main`.
- [x] Complete pre-install provider, package, UI, security and live-read evidence.
- [x] Complete installed popup, tray lifecycle, second-display, independent
  provider-control, restart, and resource evidence. Startup add/remove remains
  covered by the user-scoped registry integration test and is off on this PC.

Status: final verification refreshed on 2026-08-28. The global `AGENTS.md`
normal-enforcement agreement remains authoritative. The machine-local
unrestricted-development override remains enabled only because the user
explicitly required it throughout this completion Goal; this project never
consumes a usage-reset credit.

## Definition of done

- A user-scoped Windows popup and notification-area monitor work while Codex Settings is closed.
- One bounded official App Server read supplies each observation; polling creates no Codex task or model turn.
- The helper stores only validated settings, a sanitized current observation, transition metadata, and a reset-keyed SafeWrap latch.
- The user can configure thresholds, polling, notifications, tray behavior, startup, and a deliberately enabled persistent unrestricted-development override.
- Unknown and provenance mismatch fail closed when normal enforcement is enabled. Override state never changes because of time, usage, reset, or restart.
- Startup is opt-in, HKCU-only, non-admin, visible, and reversible.
- Release build, tests, capability scans, installation hashes, UI inspection, live observation, process cleanup, and resource measurements are recorded truthfully.
- Repository and installed artifacts are coherent, rollback is bounded, Git is clean, and push occurs only when a legitimate remote exists.

## Execution phases

| Phase | Contract | Status |
|---|---|---|
| 0 | Preserve state, read instructions/code/evidence, create one backup branch, record CLI provenance | Complete |
| 1 | Specify state machine, storage schemas, ownership, shutdown, notifications, startup, and rollback | Complete |
| 2 | Refactor the bounded App Server reader and configured decision engine | Complete |
| 3 | Implement validated settings and atomic sanitized persistence | Complete |
| 4 | Implement single-instance cancellable monitor with backoff and no overlap | Complete |
| 5 | Implement accessible WinForms popup and notification-area behavior | Complete; actual-form renderer inspected, live capture bridge limitation recorded |
| 6 | Implement opt-in user-scoped startup | Complete |
| 7 | Align the repository skill and installed integration with normal configured enforcement | Complete; WinExe stream boundary repaired |
| 8 | Run unit, integration, security, UI, lifecycle, and performance verification | Complete: 172 tests, dual PowerShell parsing, focused security review, installed lifecycle/UI checks, and performance evidence are recorded |
| 9 | Package, install for the current user, hash-check, and prove rollback boundaries | Complete: final installed v.0.003 executable and package hashes match; immutable public Release `v0.003` contains the installer, checksum, and ZIP |
| 10 | Launch the popup for supervised acceptance and record live versus synthetic evidence separately | Complete for v.0.003: exact-window UI captures, genuine credential-free updater evidence, final installed popup, and synthetic/live distinctions are recorded |

## State contract

The UI-independent runtime states are:

- `Normal`: every required fresh, high-confidence live quota window is above its warning threshold.
- `Warning`: the strictest required live quota window is at or below its Warning threshold and above SafeWrap.
- `SafeWrap`: any required live quota window is at or below its SafeWrap threshold, or its genuine reset-keyed SafeWrap latch remains active.
- `Unknown`: missing, stale, malformed, low-confidence, inaccessible, authentication-unavailable, or ambiguous input, or invalid/corrupt/future configuration. Normal enforcement permits no new phase.
- `OverrideActive`: an explicit persistent user choice supersedes usage gating. The underlying observed state remains visible. Only a deliberate user action can disable the override.
- `ResetDetected`: a fresh live observation proves a different reset key for a required quota window. It clears that window's old latch only when the new window is above SafeWrap.
- `ProvenanceMismatch`: the approved CLI path, version, or SHA-256 differs. This is an `Unknown` subtype with recovery guidance and no fallback to an unapproved binary.

Allowed transitions are driven only by validated settings changes or a new sanitized observation. Local clock movement alone never clears a latch. Unknown never overwrites a genuine latch. Override activation/deactivation is independent of observation and latch transitions.

## Reset-aware continuation and isolated QA checkpoint

- Preserve exact provider-reported UTC reset timestamps plus safe local display
  values for both required Codex windows.
- Recommend resume only for currently constraining fresh live windows, using
  the latest exact reset plus a documented two-minute margin. Never infer.
- Expose a machine-local opt-in; skill coordination may maintain one
  deduplicated same-thread wake-up only after checkpoint completion and idle.
- Validate functional, install/rollback, and visual behavior in a default-deny
  Windows Sandbox. Bind and move only the exact newly owned Sandbox client to
  the stable approved non-primary display.
- Keep physical tray, DWM, multi-monitor, latency, GPU/driver, and updater
  acceptance as controlled real-host evidence.

## Persistence schemas

All files live under `%LOCALAPPDATA%\OpenAI\CodexUsageGuard` and are replaced atomically on the same volume.

- `settings.json`: schema version, thresholds, polling interval, notification switches, tray preference, startup preference, and explicit override state.
- `state.json`: schema version, current sanitized observation/decision, reset key, optional genuine SafeWrap latch, last successful check, transition-notification metadata, and consecutive failure count.
- `claude-state.json`: exactly the current sanitized five-hour/weekly pair or an unavailable reason. Concurrent CLI callbacks use one bounded writer lease; a retained lower same-window value keeps its genuine timestamp and becomes stale normally.

No raw JSON, stderr, credentials, tokens, cookies, chats, screenshots, account identifiers, or percentage history are written. Missing files produce validated defaults; corrupt, inaccessible, partial, and future-version files produce an explicit fail-closed load result. A fixed temporary sibling is cleaned after successful replacement or bounded failure.

## Process and lifecycle contract

- One per-user named mutex owns monitoring. A per-user named event asks an existing instance to show its popup; it carries no account data.
- One monitor owns at most one App Server child. Every read has startup/read/shutdown deadlines and linked cancellation. Timeout or cancellation terminates only the child started by that read, then waits boundedly for cleanup.
- The loop has no overlapping observations. `Check now` coalesces with an active check. Repeated failures back off up to the configured safe ceiling; a successful check restores the normal interval.
- Closing hides to the notification area only when configured and monitoring is active; explicit Exit cancels the loop, removes the icon, waits for the owned child, and releases instance objects.
- Crash recovery loads the last sanitized state as historical display only. It cannot be treated as a fresh observation. A genuine latch remains effective by reset key until a fresh live observation proves rearming.

## Notification contract

Notifications are eligible only for configured transitions into Warning, SafeWrap, Unknown, recovery, or a proven reset. Identical state/reset-key pairs are de-duplicated. A bounded cooldown prevents repeated Unknown notifications while preserving the visible state. OverrideActive is persistently visible but does not generate repeated balloons.

## Startup and rollback contract

Startup uses one helper-owned value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, quotes the verified installed executable, and passes only `--background`. It is off by default and is changed only by an explicit settings action. Removal targets only that exact value.

Rollback stops the helper, removes its exact HKCU startup value, and removes
only the selected installed tool, optional skill, shortcuts, and sanitized local
state. It does not edit or restore global `AGENTS.md`; repository history and
unrelated user files are untouched.

## Verification ledger

Commands, totals, hashes, live sanitized observations, UI states, process counts, CPU/working-set samples, request cadence, installation paths, and limitations will be captured in `docs/FINAL_EVIDENCE.md`. Synthetic tests are never described as live behavior.

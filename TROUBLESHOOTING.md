# Contributor troubleshooting knowledge

This file records verified engineering routes for recurring Usage Guard
problems. Investigate independently first, then consult the relevant entry.
Update an existing entry when a safer or better verified method is found.
Never record credentials, raw authentication data, account identifiers, raw
provider payloads, chat content, or unnecessary personal information here.
End-user recovery guidance belongs in `docs/TROUBLESHOOTING.md`.

## Approved secondary display is unavailable on a single-monitor host

- **Symptom/scope:** Host UI or performance QA stops before launch because the
  configured display is primary and no non-primary monitor is connected.
- **Confirmed cause:** The safety harness intentionally rejects primary-display
  input unless the user has specifically authorized the only connected screen.
- **Avoid:** Changing Windows display settings, guessing display numbers, or
  bypassing exact PID/HWND/DWM checks.
- **Safest verified method:** After confirming `Screen.AllScreens` contains
  exactly one primary display and obtaining explicit user authorization, pass
  `-AllowSinglePrimaryDisplay`. The production-inert app mode independently
  checks the same one-screen condition.
- **Evidence:** On 2026-08-28 the harness first failed closed with one primary
  `\\.\DISPLAY1`, then completed exact-window Codex/Claude capture and the
  holistic performance pass only after the user authorized that display.
- **Limits:** With two or more connected displays the switch cannot authorize a
  primary-screen run; use the approved non-primary display instead.

## Immediate shutdown can miss a just-starting background process

- **Symptom/scope:** A bounded `--shutdown` requester exits successfully but a
  helper launched at nearly the same moment remains running.
- **Confirmed cause:** Before the background process acquires the named mutex,
  the requester can temporarily become the mutex owner and conclude there is no
  primary instance. A second race formerly dropped requests received before the
  WinForms object attached.
- **Avoid:** Treating requester exit alone as proof, force-killing by process
  name, or launching an installer replacement before the exact old PID exits.
- **Safest verified method:** Queue pre-form Show/Shutdown requests, and when a
  shutdown requester owns the mutex while another exact same-path helper process
  exists, retry for up to five seconds before failing closed. Always wait for
  the exact owned PID afterward.
- **Evidence:** On 2026-08-28 the original single request left PID 13628 until a
  second request. After the fix, three immediate background-plus-shutdown races
  each returned exit 0 and left the corresponding exact PID exited.
- **Limits:** The retry considers only an exact executable-path match and does
  not inspect, stop, or signal unrelated processes.

## Computer Use cannot attach to Windows Sandbox

- **Symptom/scope:** Computer Use capture of the Sandbox launch or client
  window fails with `SetIsBorderRequired` and `0x80004002`.
- **Confirmed cause:** On Windows 10 Pro 19045, the available capture stack does
  not expose the required interface for `WindowsSandboxClient.exe`.
- **Avoid:** Repeated broad Computer Use attempts, host-wide input, Alt+Tab, or
  guessing a different window.
- **Verified method:** Run QA inside the guest and export sanitized evidence.
  For host-rendered evidence, revalidate the exact Microsoft-signed client
  path, newly owned PID, HWND, approved display, and DWM frame before one
  bounded `PrintWindow` capture.
- **Evidence/limits:** Reproduced on 2026-08-26. This does not prove behavior on
  another Windows build; minimized host-window capture remains untrusted.

## Exact-window interactive QA aborts

- **Symptom/scope:** A dedicated QA key or capture is refused because focus,
  ownership, or containment changed.
- **Confirmed cause:** Shared-host state can change between discovery and
  input. PID alone does not prove executable, HWND, foreground, or display
  ownership. A minimized Win32 window may also remain logically visible while
  DWM reports its reserved off-screen rectangle; visibility alone is not proof
  that a reopen completed.
- **Avoid:** Mouse input, Alt+Tab, blind/global keystrokes, mutable display
  numbers, primary-display movement, or cached HWNDs.
- **Verified method:** Immediately before every allowed key, validate exact
  executable path, owned PID and QA command line, HWND, foreground ownership,
  stable monitor hardware identity, and full DWM-frame containment on the
  approved working area. Reopen waits must require that same containment before
  accepting the window as restored. Abort on any change. Keep the host harness
  compatible with built-in Windows PowerShell 5.1: use block-bodied embedded C#
  members, PowerShell's `[uint16]` type name, and `File.WriteAllText` with an
  explicit no-BOM UTF-8 encoding instead of PowerShell 7-only
  `-Encoding utf8NoBOM`.
- **Evidence/limits:** On 2026-08-30 the repository inspection harness first
  failed on each of those three PowerShell 7 assumptions, cleaned up its exact
  owned process after every failure, and then completed both installed-provider
  captures plus sanitized JSON under Windows PowerShell 5.1. The harness uses
  this ownership protocol.
  It intentionally cannot guarantee that a Windows startup splash never
  appears briefly on the primary display.

## Live usage differs from the popup

- **Symptom/scope:** A fresh command result and an already-open UI display show
  different percentages or reset times.
- **Confirmed cause:** UI state can predate the latest provider observation;
  reset timestamps may also have small provider jitter.
- **Avoid:** Treating screenshots, OCR, cached sidebar text, or a historical
  percentage as authoritative.
- **Verified method:** Trigger one bounded supported provider observation,
  confirm high confidence and observed-now freshness, then wait for the popup's
  sanitized state update before comparing only normalized fields.
- **Evidence/limits:** Observation freshness proves receipt time, not when the
  provider generated its underlying account data.

## App Server quota windows are misclassified

- **Symptom/scope:** Codex shows only one quota, swaps 5-hour/weekly values, or
  becomes Unknown after a protocol change.
- **Confirmed cause:** Bucket order and compatibility aliases are not stable
  quota identities.
- **Avoid:** Selecting primary/secondary position, percentage proximity, or
  inferring reset time from duration.
- **Verified method:** From `rateLimitsByLimitId` (or the compatibility view
  only when required), select exactly one `windowDurationMins=300` and one
  `windowDurationMins=10080`. Require valid `usedPercent` and genuine future
  `resetsAt` for each; duplicates, omissions, conflicts, or malformed values
  are Unknown.
- **Evidence/limits:** Verified against Codex CLI/App Server 0.149.1 on
  2026-08-26. Protocol drift requires a new official review.

## Reset notifications repeat by one second

- **Symptom/scope:** The same weekly window repeatedly announces a reset after
  restart or polling.
- **Confirmed cause:** Provider `resetsAt` varied by about one second and was
  incorrectly used as an exact identity.
- **Avoid:** Exact second-level timestamps as stable window identity or clearing
  a latch from the local clock.
- **Verified method:** Anchor each trusted reset and treat values within the
  documented two-minute tolerance as the same identity. Persist a bounded
  notification key and derive any resume time from the latest exact live
  timestamp plus the separate jitter margin.
- **Evidence/limits:** Jitter outside tolerance fails closed or requires a
  genuine new-window proof; it is never silently merged.

## Claude remains Unknown after Configure

- **Symptom/scope:** Claude is detected, Configure reports success, a genuine
  Claude Code response completes, and the 5-hour and weekly windows still stay
  Unknown. Claude Code 2.1.247 on Windows.
- **Confirmed cause (2026-08-29):** the status-line bridge allowed the helper
  2500 ms. The helper is a self-contained single-file build, so its first run
  after an install has to extract and JIT; the measured cold start here was
  2409 ms. The bridge therefore killed it part-way through writing its state
  file, which left a `claude-state.json.new` behind. `ClaudeUsageStorage.Save`
  treated any leftover temporary as "a previous write is incomplete" and refused
  to write ever again, so one unlucky cold start pinned Claude usage at Unknown
  permanently, with nothing in the interface explaining it. Both halves are
  fixed: an abandoned temporary is now removed and the write retried, and the
  bridge allows the cold start while staying bounded.
- **Two setup gates that must be satisfied first:** the status line is a
  terminal feature. The Claude Desktop **Code tab** was observed never to
  execute a configured status-line command, so use a Claude Code CLI or IDE
  terminal session. Anthropic also documents that a status-line command runs
  under the same workspace-trust rule as hooks, so an untrusted folder skips it
  silently; accepting the folder, or a parent whose trust extends to it, is
  enough.
- **Avoid:** editing trust configuration to fake acceptance, automating a trust
  prompt or login, reading private Claude session data, launching Desktop's
  private bundled executable, or inferring a percentage from plan, elapsed
  time, or reset cadence.
- **Verified method:** run Configure, then complete one ordinary response in a
  trusted CLI/IDE terminal session. The bridge prints its own sanitized label
  into Claude Code's status-line row, which is the quickest confirmation that
  the callback is executing at all. If usage stays Unknown, check for a
  leftover `claude-state.json.new` under `%LOCALAPPDATA%\OpenAI\CodexUsageGuard`;
  current builds clear it themselves.
- **Evidence/limits (2026-08-29, Claude Code 2.1.247, Windows PowerShell
  5.1.19041.7663):** cold start measured at 2409 ms against the old 2500 ms
  budget; an orphaned helper-owned temporary was present after the interrupted
  write. After both fixes a genuine observation
  arrived with both windows populated and high confidence. `rate_limits` is
  documented only for Claude.ai Pro/Max (or a Claude apps gateway with spend
  limits) and only after the first API response, each window may be
  independently absent, and Claude Code drops a window once its `resets_at`
  passes. Free Chat-only use stays Unknown. The tested Desktop Code tab did not
  invoke status lines by itself; future releases may differ.
- **Freshness limit:** Anthropic documents `refreshInterval` as re-running the
  status-line command, not as a fresh account query. Usage Guard therefore does
  not configure that timer. A conservative value retained from another session
  keeps its original timestamp and becomes stale normally.

## Claude Configure must not merge the user settings file

- **Symptom/scope:** a Configure implementation appears convenient because it
  can add `statusLine` directly to `~/.claude/settings.json`, but doing so reads
  and copies the entire file during parsing, backup, and rewrite.
- **Confirmed cause (2026-08-30):** Anthropic user settings may contain `env`
  values and credential-helper configuration. Treating the whole document as a
  merge boundary violates Usage Guard's credential-free privacy boundary even
  when the merge preserves unknown keys.
- **Avoid:** parsing, backing up, copying, logging, or rewriting the user
  settings file; deleting an existing status line; or using Claude Desktop's
  private bundled executable to bypass setup.
- **Verified method:** write a minimal helper-owned
  `~/.claude/usage-guard/claude-session-settings.json` containing only the
  bridge command, and tell the user to start the official CLI with
  `claude --settings <that exact file>`. Keep the existing user status line
  untouched. Tests hold the user settings file with an exclusive lock while
  Configure succeeds and verify its hash is unchanged with no backup created.
- **Evidence/limits (2026-08-30):** Release build and the synthetic suite pass
  with the settings file unreadable. This is intentionally a two-step setup;
  Usage Guard cannot safely make every ordinary Claude CLI launch inherit the
  bridge without crossing the secret-adjacent settings boundary.

## Windows PowerShell 5.1 cannot use Process.Kill(entireProcessTree)

- **Symptom/scope:** a bounded WinExe or status-line wrapper times out, but its
  helper or descendant remains alive under Windows PowerShell 5.1.
- **Confirmed cause (2026-08-30):** `.Kill($true)` binds to the .NET overload
  available in newer PowerShell runtimes, but not the .NET Framework process
  type used by built-in Windows PowerShell 5.1.
- **Avoid:** assuming a PowerShell 7 process API works under `powershell.exe`,
  inheriting child streams, or killing by image name.
- **Verified method:** retain the exact started PID, invoke the system
  `taskkill.exe /PID <pid> /T /F` without shell interpolation, wait boundedly,
  and fall back to parameterless `.Kill()` only for that exact owned process.
  Capture stdout/stderr separately and validate one sanitized stdout object.
- **Evidence/limits (2026-08-30):** the focused Windows PowerShell 5.1
  regression starts a helper with a child, forces timeout, and proves the child
  exits. This is Windows-specific and does not authorize killing any process not
  created by the wrapper.

## Claude skill upgrade leaves a nested backup

- **Symptom/scope:** Reconfiguration creates a backup inside the installed
  skill, so the installed tree is not byte-identical to the source.
- **Confirmed cause:** The old upgrade path backed up a file within the
  directory being replaced.
- **Avoid:** In-place recursive overwrite or deletion of materially different
  unknown content.
- **Verified method:** Stage the complete verified skill beside the target,
  move the old directory to a uniquely named sibling backup, then atomically
  move the staged directory into place. Restore on failure.
- **Evidence/limits:** Only recognized Usage Guard-owned content may be upgraded
  automatically; unknown differences require reporting.

## WinExe guard wrapper returns no output or leaks warnings

- **Symptom/scope:** Direct PowerShell invocation returns before output and
  leaves `$LASTEXITCODE` undefined, or inherited stderr exposes unrelated CLI
  warnings before the decision JSON.
- **Confirmed cause:** A Windows-subsystem executable does not satisfy the
  shell pipeline contract used by a console executable.
- **Avoid:** `& executable; exit $LASTEXITCODE`, inherited child streams, or
  accepting the first JSON-looking line.
- **Verified method:** Launch the exact verified executable with
  `Start-Process`, redirect stdout/stderr separately to unique temporary files,
  wait with a hard timeout, preserve the child exit code, suppress stderr, and
  emit exactly one schema-validated sanitized object. Missing, multiple, or
  malformed output fails closed.
- **Evidence/limits:** Same-user process/file tampering is outside the helper's
  boundary; locator and executable hashes are still validated.

## Installed helper or CLI provenance cannot be resolved

- **Symptom/scope:** The wrapper reports unavailable/provenance mismatch even
  though a similarly named executable exists.
- **Confirmed cause:** Package aliases, inaccessible WindowsApps binaries,
  stale locators, or a changed version/hash are not the reviewed deployment.
- **Avoid:** Copying packaged Desktop executables, bypassing ACLs, changing
  permissions, attaching to private processes, or searching credentials.
- **Verified method:** Resolve the helper only through its validated
  user-local `installation.json`. Launch Codex only from the pinned official
  user-scoped CLI path/version/SHA-256. Drift is Unknown with recovery guidance.
- **Evidence/limits:** Official updates require separate verification; the
  helper never auto-trusts a changed binary.

## Tray icons appear duplicated after restart

- **Symptom/scope:** Explorer displays several Usage Guard icons while process
  inventory shows one helper.
- **Confirmed cause:** Repeated abnormal exits can leave stale shell icons;
  process count alone does not prove concurrent NotifyIcon ownership.
- **Avoid:** Manipulating unrelated tray icons or starting a replacement before
  the exact old PID exits.
- **Verified method:** Enforce a per-user single-instance mutex, signal the
  primary instance, dispose every owned NotifyIcon on all exits, and make
  installer/rollback wait for the exact PID. Validate one live process before
  attributing extra icons to concurrency.
- **Evidence/limits:** Explorer may retain a stale icon until its normal refresh
  after a hard crash; the helper cannot safely remove unrelated shell state.

## External settings changes are not reflected

- **Symptom/scope:** The popup or background monitor reports an old override or
  monitoring state after settings were changed by another validated helper
  path.
- **Confirmed cause:** A long-running process cached settings for the session.
- **Avoid:** Restart-only correctness or overwriting the user's current
  thresholds during reinstall.
- **Verified method:** Reload and validate settings before each observation and
  merge only helper-owned sanitized state under the storage mutex. Corrupt,
  inaccessible, or future-version files fail closed.
- **Evidence/limits:** A UI repaint follows the next serialized check rather
  than filesystem events.

## Hidden tray process ignores reopen or shutdown signals

- **Symptom/scope:** Closing a monitoring popup correctly leaves one tray
  process, but a second ordinary launch does not reopen it and `--shutdown`
  times out.
- **Confirmed cause:** `ShowInTaskbar = false` can recreate/destroy the form's
  native handle, and registered thread-pool waits did not reliably marshal
  named-event work after the popup was hidden on Windows 10. The monitor also
  captured the WinForms synchronization context, which could delay cleanup
  after the message loop stopped.
- **Avoid:** Treating event delivery alone as UI success, relying on
  `Form.InvokeRequired` when its handle may not exist, or starting another tray
  shell as a fallback.
- **Verified method:** Keep the named AutoReset events and mutex, but consume
  pending show/shutdown signals from a 100 ms WinForms timer on the owning UI
  thread. Keep monitor continuations UI-independent with
  `ConfigureAwait(false)`. Verify with a real
  production hide, ordinary second launch, reopen, hidden shutdown, and exact
  one-process inventory. For automation, use `WM_SYSCOMMAND/SC_CLOSE` or
  `SC_MINIMIZE`; raw `WM_CLOSE` injection is not equivalent to the shell close
  path's `CloseReason` and can invalidate tray conclusions.
- **Evidence/limits:** The corrected source passed minimize/reopen/shutdown and
  system-close/hide/shutdown regressions on Windows 10 on 2026-08-28. The final
  installed package must retain the same real pass rather than relying only on
  mutex unit tests.

## Windows Sandbox setup, placement, or capture fails

- **Symptom/scope:** Sandbox is unavailable, launches on the wrong display, or
  evidence cannot be trusted.
- **Confirmed cause:** Firmware virtualization alone does not install the
  Windows optional feature; `.wsb` cannot choose a monitor or window position;
  display numbering is mutable; minimized captures can be stale.
- **Avoid:** Enabling optional features or rebooting without coordination,
  guessing DISPLAY numbers, moving any non-Sandbox window, mapping a user
  profile, or enabling network/clipboard/devices for convenience.
- **Verified method:** Verify the feature/hypervisor first. Use the locked-down
  template, one exact read-only staged input and a newly empty evidence folder.
  Copy the hash-verified input into ephemeral guest-local storage and reverify
  before execution; mapped-folder execution is not assumed.
  Select the approved non-primary display by stable hardware ID and exact
  working bounds, bind exactly one newly owned Microsoft-signed client PID/HWND,
  then place only that window. Guest QA runs while the client is backgrounded;
  restore without activation for final exact-window capture.
- **Evidence/limits:** Windows may show a brief splash before the final HWND.
  Sandbox cannot prove physical GPU, driver, tray-shell, multi-monitor latency,
  or real-network behavior.

## Safe cleanup after a failed QA/install pass

- **Symptom/scope:** A bounded test fails with staged files, a guest, or a
  helper process still present.
- **Confirmed cause:** Cleanup was keyed to a broad name/path rather than exact
  ownership, or an early failure bypassed the normal handoff.
- **Avoid:** Wildcard process kills, broad recursive deletion, or resetting the
  dirty repository.
- **Verified method:** Record baseline PIDs and validated absolute roots. Stop
  only the exact owned PID/path, wait for graceful exit first, delete only the
  unique staged directory after prefix validation, and preserve sanitized
  evidence plus coherent source edits.
- **Evidence/limits:** A guest hard-stop discards guest state by design; host
  evidence is retained only in the dedicated output folder.

## Installer fails only under built-in Windows PowerShell

- **Symptom/scope:** The repository install succeeds under PowerShell 7 but the
  Windows 10 Sandbox or recipient bootstrapper exits before writing its locator.
- **Confirmed cause:** Windows PowerShell 5.1 does not accept PowerShell 7's
  `Set-Content -Encoding utf8NoBOM` value.
- **Avoid:** Requiring a separately installed PowerShell runtime or changing
  the locator encoding silently.
- **Verified method:** Serialize the bounded locator and write it with
  `System.IO.File.WriteAllText` plus `UTF8Encoding(false)`, which is available
  in the built-in framework and emits the same no-BOM contract.
- **Evidence/limits:** Verified through the locked-down Windows 10 Sandbox on
  2026-08-27; future installer syntax must continue to be parsed/tested under
  Windows PowerShell 5.1.

## Repeated QA artifacts exhaust the system drive

- **Symptom/scope:** C: reached under 0.5 GB free while the ignored project
  `artifacts` directory contained repeated publish, installer, Sandbox staging,
  UI, and performance evidence.
- **Confirmed cause:** Multiple auditable release candidates were preserved in
  the OneDrive-backed C: checkout; the directory reached 5.73 GB. Windows
  Sandbox's separate system image was not the dominant project-owned data.
- **Avoid:** Deleting evidence broadly, moving Windows container system files,
  or repeatedly retaining full self-contained publish copies on a small system
  volume.
- **Verified method:** Move the exact helper-owned `artifacts` directory to a
  spacious development volume and leave a directory junction at the repository
  path so existing scripts remain compatible. Validate source/target and the
  junction before continuing. Archive or prune superseded full publishes at
  coherent release boundaries.
- **Evidence/limits:** On 2026-08-28, 6,155,484,169 bytes moved to an
  external test-artifact directory; C: free space rose from 0.40 GB to
  about 5.8 GB. This machine-local junction is not a repository requirement.
  On 2026-08-29, the canonical source clone moved to the exFAT F: volume;
  exFAT cannot host the repository-side NTFS junction, so that checkout uses a
  normal ignored `artifacts` directory on F: instead.

## Source-structure test fails only after an F-drive clone

- **Symptom/scope:** The full suite reports only `App Server diagnostics are
  drained and suppressed` as failed after cloning the unchanged commit from an
  LF working tree to a normal Windows CRLF working tree.
- **Confirmed cause:** The test searched source text for a literal LF plus
  indentation sequence. Git's Windows checkout converted the file to CRLF, so
  the structural assertion became line-ending-dependent.
- **Avoid:** Disabling `core.autocrlf`, rewriting the whole repository's line
  endings, or treating the production stderr suppression as broken without
  inspecting the failing assertion.
- **Verified method:** Normalize CRLF to LF in the test's in-memory source text
  before checking the required bounded stderr-drain call. Production code and
  file bytes remain otherwise unchanged.
- **Evidence/limits:** Reproduced at commit `3fe20c5` on the exFAT F: clone on
  2026-08-29. The complete suite must pass from that clone before it becomes
  the canonical development checkout.

## Usage reaches zero after an earlier Normal or Warning result

- **Symptom/scope:** An agent consulted the guard, then a long Sandbox, build,
  release, research, or QA phase exhausted usage before the next check.
- **Confirmed cause:** A result is a point-in-time phase-admission decision, not
  continuous enforcement or reserved capacity. One open-ended phase can consume
  all headroom above SafeWrap.
- **Avoid:** Treating Normal as permission for an unbounded phase, relying on
  Critical SafeWrap to interrupt a running phase, or adding frequent polling.
- **Verified method:** Use conservative thresholds, split high or uncertain
  usage work into short recoverable checkpoints, and invoke the installed skill
  at every material checkpoint. Warning permits only a short bounded checkpoint;
  SafeWrap or Unknown starts no new phase. Keep the genuine provider-reset
  one-shot behavior separate from checkpoint sizing.
- **Evidence/limits:** On 2026-09-03 a NetSwitch task checked at 31% and 24%,
  then began a long Sandbox pass and exhausted usage before another boundary.
  The thresholds were restored to 30/25/20 for both windows on 2026-09-04.
  Thresholds reduce risk but cannot predict phase cost, so fresh checkpoints
  remain necessary.

## Apply saves thresholds but SafeWrap still shows an older latch

- **Symptom/scope:** The settings file contains the user's new threshold, but
  the status remains SafeWrap with `genuine_latch_active` for the same window.
- **Confirmed cause:** Before the user-authority fix, all settings updates were
  deliberately evaluated against the durable old latch, so lowering SafeWrap
  looked like Apply had failed even though the file was saved.
- **Avoid:** Replacing the user's settings with agent-selected defaults,
  deleting state blindly, or treating an external file edit as user consent to
  clear enforcement.
- **Verified method:** Only the explicit in-app Apply/Restore Defaults path may
  reconsider a latch. It clears a latch only when a fresh high-confidence live
  observation identifies the same quota window and its remaining percentage is
  above the applied SafeWrap threshold, then immediately re-evaluates. External
  settings reloads and stale/Unknown observations preserve the latch.
- **Limits:** Apply cannot safely clear a latch without fresh same-window
  evidence. Agents must obey user-applied settings and may not alter settings,
  override, or latch state without an explicit request for that exact change.

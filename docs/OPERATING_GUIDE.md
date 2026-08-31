# Operating guide

## Provider tabs

Use **Detect installed AIs** after installing or removing Codex or Claude
Desktop/Code. Each provider has its own tab and machine-local settings. Configure
every quota window separately; both Codex and Claude have distinct 5-hour and
weekly thresholds. The strictest required trustworthy window controls only
that provider.

Each tab contains its own **Check now**, **Start/Stop Monitoring**, and
**Configure AI** controls. Stopping Claude does not stop Codex, and vice versa.
Settings, Check for updates, Minimize to tray, and Exit are application-wide and
remain outside the tabs.

`Unknown` is provider-local and never borrows another tab's percentage. Press
**Configure AI** to install the preserved/idempotent Codex integration or the
Usage Guard-owned Claude Code assets. Claude Configure does not inspect or edit
Claude's user settings; start the official CLI with the isolated `--settings`
command shown by the app. Claude becomes live only after that CLI
completes a genuine response carrying both documented status-line windows. The
tested Desktop Code tab does not invoke that bridge on its own; the CLI may be
run in Desktop's integrated terminal after a separate official installation. Free
Chat-only usage remains Unknown. Ordinary Claude Chat is manual/advisory and no
account-wide profile instruction is changed.

All settings and AI working agreements apply only to this Windows user and
computer. A shared AI account may expose the same allowance to both people, but
Usage Guard does not sync thresholds or rules to another computer.

## Updates

Usage Guard checks the public repository's latest approved GitHub Release at
startup and every six hours, then notifies once per newer version. **Check for
updates** downloads only the version-matched setup executable and `.sha256`,
verifies the hash, requires the validly GitHub-signed official GitHub CLI to
prove the file belongs to the exact repository's immutable release, and asks
before launching the user-scoped installer for the current folder. Missing CLI,
mutable release, missing, duplicate, redirected-to-foreign, oversized, malformed,
or mismatched assets fail closed. Nothing installs silently, and a `main` branch
push alone is not an update; the release must publish both assets.

## Start and stop

Open the installed `CodexUsageGuard.exe` (for example,
`D:\Apps\Usage Guard\CodexUsageGuard.exe`). A second launch signals
the existing per-user instance to show its popup rather than starting another
monitor.

Each provider's monitoring button uses the same normal border as nearby buttons
while stopped. Pressing it enables only that provider; it turns red and reads
**Stop Monitoring** while active. Codex starts one immediate bounded check and
then waits its interval. `Check now` coalesces with an active Codex read. `Exit`
stops all helper-owned monitoring, disposes the icon, and releases ownership.

Closing the popup hides it to the notification area when that option and monitoring are both on. Otherwise it exits. The notification-area menu provides Open status, Check now, the same Start Monitoring/Stop Monitoring toggle, and Exit.

## States

- **Normal:** fresh trusted remaining usage is above Warning.
- **Warning:** fresh remaining usage is at or below Warning and above SafeWrap.
- **SafeWrap:** a fresh live threshold event or genuine same-window latch requires finishing the current coherent checkpoint and starting no new material phase. It does not kill a task.
- **Critical SafeWrap:** the more urgent lower range inside SafeWrap. The task still finishes its current coherent checkpoint safely; it is never instantly stopped, cancelled, or killed.
- **Unknown:** the observation is unavailable, stale, malformed, ambiguous, low-confidence, or untrusted. Normal enforcement starts no new phase, but Unknown is not called a genuine threshold event.
- **Provenance mismatch:** the pinned official CLI path or SHA-256 changed. This is a specific Unknown with update-verification guidance.
- **Reset detected:** a fresh trusted live observation proved a different quota-window reset key. An old latch clears only when that specific new window is above SafeWrap.
- **Override active:** usage-based gating is explicitly disabled. The underlying live/latch state remains visible and intact. Only a deliberate user action can disable the override.

## Settings

- **Warning threshold:** default 30% remaining.
- **SafeWrap threshold:** default 25% remaining.
- **Critical SafeWrap threshold:** default 20% remaining. At or below it, the UI marks SafeWrap as urgent, but enforcement remains checkpoint-safe rather than an instant stop.
- **Polling interval:** default 60 seconds; allowed range 30–300.
- **Notifications:** separate controls for Warning, SafeWrap, Unknown/provenance mismatch, recovery, and reset. Identical transition/reset pairs are de-duplicated for 30 minutes.
- **Minimize to notification area:** hides the popup while monitoring continues.
- **Start at sign-in:** off by default; creates one exact HKCU Run value after explicit selection.
- **Launch Together shortcuts:** off by default; creates helper-owned Start-menu shortcuts for fixed Codex/Claude URIs. Original AI shortcuts remain unchanged.
- **Unrestricted development override:** persistent and confirmation-gated. Restore defaults deliberately preserves it.

Thresholds must satisfy `0 <= critical <= SafeWrap <= Warning <= 100`. Invalid values are rejected. Corrupt, inaccessible, partial, or future-version settings/state load fail closed.

**Allow one-shot reset wake-up** permits an agent using the installed Codex
skill to create or update one deduplicated same-task wake-up at the sanitized
recommended time only after it completes its current checkpoint and becomes
idle. It is off by default, never polls, and never schedules from Unknown,
stale, inferred, or latch-only reset data.

For a genuine SafeWrap, machine-readable output includes both provider reset
timestamps in exact UTC and safe local display form. If only one quota is
currently constraining, its reset controls. If both constrain, the later reset
controls. A documented two-minute provider-jitter margin is then added. The
recommendation is guidance only; the task must recheck live usage when the
wake-up fires.

## Stored data

`%LOCALAPPDATA%\OpenAI\CodexUsageGuard\settings.json` contains validated Codex/general settings. `providers.json` contains machine-local provider settings. `state.json`, `claude-state.json`, and provider notification state contain only sanitized current decisions/windows, reset keys/latches, timestamps, de-duplication metadata, and failure count. Each file replaces a same-directory temporary sibling atomically. No history, raw JSONL/status-line input, stderr, credential, token, cookie, chat, account ID, or screenshot is retained.

The monitor reloads validated settings before every check. A manual settings
change therefore takes effect on the next bounded observation; corrupt or
inaccessible settings become Unknown. Reset notifications are emitted once for
an anchored quota-window pair, with up to two minutes of provider reset-time
jitter treated as the same window.

## Resource behavior

Idle monitoring uses one timer wait in one desktop process. Each interval launches one short-lived approved CLI App Server child, sends one rate-limit request, and closes it. Repeated failures back off exponentially up to 300 seconds. Polling never sends model/thread/turn methods and therefore does not create a Codex model task.

# Architecture

## Provider and quota-window contract

The desktop helper is provider-neutral at its outer boundary. Each configured
AI provider owns an isolated policy domain: provider identity, discovery
result, usage-source capability, settings, current sanitized observations,
reset/latch state, notification ledger, and monitor schedule. No percentage,
reset, latch, or threshold may be reused across provider IDs.

A provider may expose one or more named quota windows. A window carries only a
stable window kind, normalized remaining percentage, reset time, local
observation time, confidence, freshness, and sanitized availability/error.
Providers declare which windows are required. Their configured decision is the
most restrictive trustworthy required window. A missing, duplicated,
ambiguous, stale, low-confidence, or conflicting required window makes that
provider Unknown; it never makes another provider Unknown and never borrows a
different provider's result.

Codex supports required 300-minute and 10,080-minute windows through the
official App Server. The official Claude Code CLI supports two required windows through Anthropic's documented
status-line input: `rate_limits.five_hour` and `rate_limits.seven_day`, each with
`used_percentage` and `resets_at`. A local bridge receives at most 65,536 bytes
after a genuine Code response. Before PowerShell deserialization it performs a
bounded, depth-limited structural pass that rejects duplicate JSON properties.
It then selects exactly one required five-hour and weekly object by name,
discards all other session fields and uniquely named provider windows, and
passes only the required percentage/reset pair across the helper process
boundary. The managed parser independently validates types, ranges, future
resets, and uniqueness. A bounded cross-process writer lease then reconciles
concurrent CLI sessions and atomically stores the sanitized current pair.
Within one reset identity, a conservative lower reading retains its original
observation time so another idle session cannot make it appear fresh.
The bridge never starts a model response. Missing, stale, ambiguous, invalid,
or free Chat-only input stays Unknown. If the callback runs without a valid
required pair, it sends only an empty sanitized sentinel through the same
bounded helper process so current state can distinguish that case from a
callback that never ran. No raw or unrelated input crosses or persists.

Claude's provider allowance is shared across Chat and Code surfaces, but the
policy integration is machine-local. Ordinary Chat has no documented local
phase-boundary hook, so Usage Guard never writes account-wide Claude profile
instructions. The official Claude Code CLI is the enforceable local task path;
the tested Desktop Code tab did not invoke status-line commands by itself. The
CLI may run in Desktop's integrated terminal, but Usage Guard does not launch a
private bundled executable. Configure writes only an owned, minimal
`~/.claude/usage-guard/claude-session-settings.json`; the user starts the CLI
with `--settings` so Claude applies that command-line settings source to the
session. Usage Guard never reads, copies, or edits the potentially secret-
bearing user `settings.json`, and it deliberately omits `refreshInterval`
because a timer can re-render cached session fields without proving a provider
refresh.

The popup creates one tab per configured provider. Each tab owns its Check now,
monitoring toggle, Configure action, thresholds, polling and notification
settings, and quota-window display. Application-wide controls remain outside
the tabs. Multiple enabled providers may monitor concurrently, with a
per-provider no-overlap lock and independent backoff. Provider settings,
sanitized state, skills, and instruction additions are current-user files on one
machine and are never account-synced.

`LaunchTogetherRegistration` creates only exact current-user Start-menu
shortcuts that invoke the helper with a fixed provider identifier before opening
the registered `codex:` or `claude:` URI. It never accepts arbitrary commands,
does not replace provider shortcuts, and avoids WMI/audit watchers, scheduled
tasks, services, and process hooks.

Update checks are a separate boundary: once at startup, every six hours, or on
explicit request. The fixed GitHub API parser accepts only a non-draft,
non-prerelease numeric tag, exact repository page, and exactly one version-
matched setup asset plus checksum asset. Automatic checks only show a
de-duplicated notification. A user-confirmed install downloads both with hard
bounds and an approved GitHub asset redirect, verifies SHA-256 in constant time,
then invokes only a validly GitHub-signed official GitHub CLI from one of two
fixed install paths to verify that exact file as an asset of an immutable
`BionicVisionary/Usage-Guard-Main` release. Missing CLI, invalid publisher signature,
mutable release, or failed asset proof fails closed. The helper then launches
the user-scoped installer for the current folder and exits. Provider
monitoring never invokes this path.

The self-contained build permits eight seconds for App Server process launch
and eight seconds for initialization, followed by the existing eight-second read
and three-second shutdown bounds. The task wrapper remains a 40-second outer
limit and terminates only the owned helper/process tree on expiry.

## Production desktop target

The production target is one user-scoped .NET 8 Windows process with a UI-independent core and a built-in WinForms shell. The process owns the popup, notification-area icon, validated settings/state stores, periodic monitor, optional HKCU startup entry, and one bounded App Server child at a time. It is not a service, web server, plugin, browser extension, task coordinator, or desktop automation agent.

The core boundaries are:

1. `ApprovedCodexCli` verifies the exact official user-local executable path, version provenance, and SHA-256 before launch.
2. `AppServerUsageClient` sends only `initialize`, `initialized`, and one `account/rateLimits/read`, then closes its owned process within hard deadlines.
3. `GuardPolicy` accepts exactly one fresh, high-confidence 300-minute window and one 10,080-minute window, evaluates their independent settings, and combines each with its genuine reset-keyed latch. The stricter result controls.
4. `GuardSettingsStore` and `GuardStateStore` atomically persist only validated settings and sanitized current state.
5. `UsageMonitor` serializes immediate and periodic observations, applies bounded failure backoff, and raises sanitized state transitions.
6. `MainForm`, `NotifyIcon`, `StartupRegistration`, and `SingleInstanceCoordinator` remain adapters around the core contracts.

The persistent unrestricted-development override is an explicit user setting. When active, the externally exposed configured decision is `OverrideActive`; the underlying observation and any genuine latch remain intact and visible. The override is never inferred from usage, reset, time, or restart and can be removed only by deliberate user action.

Before each observation, `UsageMonitor` reloads and validates `settings.json` so
the background process and one-shot configured-decision command cannot continue
using different override or threshold settings after a validated external
change. Corrupt or inaccessible settings become Unknown without launching a
new observation.

Detailed state, storage, ownership, notification, startup, and rollback contracts are tracked in `IMPLEMENTATION_PLAN.md`.

## Components

```text
visible Codex weekly usage row
        |
        v
WindowsCodexAccessibilityProbe  -- read-only UI Automation
        |
        | transient target-scope accessible names
        v
UsageObservationService         -- fail-closed policy and timing
        |
        v
RemainingUsageParser            -- one explicit remaining/left percentage
        |
        v
UsageObservation                -- sanitized JSON only
```

The Settings-closed path is separate:

```text
CodexUsageGuard --guard-check
        |
        v
ApprovedCodexCli              -- exact user path + pinned SHA-256/version
        |
        v
ProcessAppServerTransportFactory -- `codex app-server --listen stdio://`
        |
        v
AppServerUsageClient             -- initialize, initialized, one read, EOF
        |
        v
AppServerRateLimitParser         -- exactly one valid 300- and 10,080-minute window
        |
        v
AppServerUsageObservation        -- sanitized JSON only
        |
        v
GuardPolicyEvaluator             -- pure offline classification, no actions
        |
        v
LiveGuardCheckResult             -- sanitized live decision + provenance
```

Startup/handshake, rate-limit read, and shutdown have separate timeouts. The
JSONL reader rejects a line over 1 MiB and the protocol reader accepts at most 32
messages while waiting for each response. On shutdown timeout, the helper
terminates only the App Server child it created so that the one-shot command
cannot leave a background process.

`WindowsCodexAccessibilityProbe` is retained as historical feasibility/test
code only and has no production executable entrypoint. It
uses exact Windows package family identity, process IDs, and top-level window
ownership to avoid querying accessibility properties from unrelated
applications. It uses an exact-name condition for `Weekly usage limit`, requires
that label to be on-screen, selects the nearest structurally isolated container,
and bounds target-scope tree traversal to 512 control-view elements. Equivalent
captures are de-duplicated only by UI Automation runtime identity; separate
label structures remain ambiguous.

`RemainingUsageParser` and `UsageObservationService` do not depend on Windows.
The test executable supplies fake accessibility results containing synthetic UI
names and validates policy, parsing, timing, ambiguity, and output redaction
without opening or controlling a real application.

## Accessibility result contract

The command writes exactly one compact JSON object to standard output.

| Field | Meaning |
| --- | --- |
| `status` | `available`, `unavailable`, or `error` |
| `remainingPercent` | Decimal from 0 through 100, otherwise `null` |
| `observedAtUtc` | UTC time when the passive probe completed |
| `confidence` | `medium` only for an unambiguous result; otherwise `none` |
| `freshness` | `observed_now` for a scan completed within five seconds; otherwise `unknown` |
| `error` | Stable enumerated reason, or `null` |

Confidence is capped at `medium` because package identity narrows the local app
source but its accessible text is not authenticated server-side quota evidence.
Freshness describes observation time, not the age of account data displayed by
Codex.

The App Server result adds a sanitized pair of quota windows and their
`resetsAtUtc` values. It reports `confidence=high` and
`freshness=observed_now` only after one documented response contains exactly one
valid 300-minute window and one valid 10,080-minute window, both with future
resets. All other states have null values, `confidence=none`, and
`freshness=unknown`.

## Offline policy contract

The evaluator accepts one sanitized observation, an evaluation timestamp, and
validated configuration. It returns only a classification, stable reason,
whether a new phase is allowed, whether only the current checkpoint should be
finished, and whether the policy configuration was valid.

Default inclusive thresholds are `Warning <= 30`, `SafeWrap <= 25`, and
`Critical SafeWrap <= 20`. Both SafeWrap ranges finish only the current coherent
checkpoint and start no new phase; Critical SafeWrap adds urgency and never
cancels or kills the active task. Thresholds must satisfy
`0 <= critical <= safe-wrap <= warning <= 100`. The default maximum observation
age is two minutes. Stale, expired, low-confidence, malformed, unavailable, or
invalid observations become `Unknown`. `Unknown` fails closed with no new phase
allowed and is handled like `SafeWrap` by the task workflow, without claiming a
real threshold event.

Reset timestamps are provider data, not stable identifiers. The core anchors
the first trusted reset time for each quota window and treats later values
within two minutes as the same window. A later timestamp outside that tolerance
is a new window; a backward timestamp outside tolerance fails closed. This
absorbs observed second-level jitter without merging genuinely distinct quota
windows. Reset notification keys use both anchored values.

This component has no clock, filesystem, network, process, timer, notification,
or task-control dependency. It is not wired to an automatic action.

## Reset-aware resume contract

The configured decision retains the exact UTC `resetsAt` supplied by the live
App Server for both the duration-classified 300- and 10,080-minute windows and
adds a culture-invariant local display value. It never derives reset time from
percentage, duration, the local clock, screenshot text, OCR, or cached UI.

For a fresh, high-confidence `live_app_server` SafeWrap decision, the pure
`GuardResumePlanner` identifies only windows currently at or below their
configured SafeWrap threshold. The recommendation is the latest exact reset
among those constraints plus a separately exposed 120-second provider-jitter
margin. A stable reset identity is derived from anchored window identities for
deduplication, while the recommendation uses the latest exact live timestamp.
Missing, expired, duplicated, malformed, stale, conflicting, or
provenance-invalid data produces `Unavailable` and no schedule. A durable latch
without a fresh live observation also cannot schedule.

The helper emits metadata only. When the machine-local opt-in is enabled, the
installed skill may create or update one same-thread one-shot wake-up after the
active agent finishes its checkpoint, restores state, and is idle. It dedupes
by reset identity, deletes the wake-up when it fires, and rechecks the live
guard before starting work. No task listing, polling, steering, interruption,
or cancellation capability exists in the application.

## Windows Sandbox QA boundary

The checked-in `.wsb` template explicitly disables network, clipboard, vGPU,
audio/video input, and printer redirection and enables Protected Client. The
host stages only self-contained Release inputs into one read-only mapping and
uses a distinct newly empty writable evidence mapping. The guest runs tests,
renders/captures its own windows, verifies user-scoped install/rollback, and
exports sanitized results without credentials or provider access.

The host launcher records baseline client PIDs, requires exactly one newly
owned Microsoft-signed `WindowsSandboxClient.exe` descendant and visible HWND,
and selects the approved non-primary display by stable hardware ID plus exact
working bounds. It never guesses from display numbering or moves another
window. Guest QA can run while the host client is minimized; minimized capture
is not evidence. The client is restored without activation on the approved
display for a bounded exact-HWND capture. A brief Windows startup splash before
the final HWND cannot be prevented. Physical GPU, driver, tray-shell,
multi-monitor latency, and real-network claims remain host-only acceptance.

## Failure policy

- No Codex process/window or no visible Usage marker: `unavailable`.
- No explicit remaining percentage, multiple percentages, multiple views, an
  over-large scope, or a scan over five seconds: `unavailable`.
- UI Automation/provider failure or unexpected exception: `error`.
- No error path includes exception messages or accessible names.

Exit code is 0 for `available`, 2 for `unavailable`, and 1 for `error`.

## Deliberate omissions

The production desktop helper contains no credential reader, raw protocol log,
percentage history, HTTP client, service, scheduled task, administrator process,
auto-updater, input simulation, OCR path, desktop navigation, or Codex task
control plane. Its only persistence is validated settings and minimum sanitized
current state. Its only optional startup mechanism is one explicit user-scoped
HKCU Run value. Its only recurring work is a cancellable local timer that owns at
most one bounded official App Server child.

## Installed configured-decision integration

The framework-dependent Release publish is user-scoped at
`%USERPROFILE%\.codex\tools\codex-usage-guard`. The repo-owned skill source at
`.agents/skills/codex-usage-guard` has an exact installed copy under the user's
Codex skills directory. Its zero-argument script invokes only `--guard-check`.
The WinExe wrapper uses a direct non-shell process launch, captures stdout and
stderr separately, waits under a 30-second hard deadline, and terminates only
the owned helper process tree on timeout. It suppresses child diagnostics and
emits stdout only after exactly one configured-decision object passes a strict
property, provenance, consistency, and exit-code contract.
The command returns the explicit configured override without launching App
Server; otherwise it performs one fresh read or enforces a genuine reset-keyed
latch. Thresholds live in validated helper settings rather than the skill or
global agreement.

Notification de-duplication is persisted as one bounded sanitized key/timestamp
per notification kind. Reset notifications are once per anchored window pair;
other identical transitions observe the cooldown across restarts. Cross-process
state writes merge that bounded ledger under a named per-user storage mutex.

The desktop mutex also owns separate show and graceful-shutdown events.
Ordinary secondary launch signals the existing popup; background secondary
launch exits silently; the installer/rollback signal graceful shutdown and wait
for every exact installed PID to exit before replacing files. Every constructed
`NotifyIcon` is hidden and disposed on form close, constructor failure, and
normal shutdown. A process crash cannot execute cleanup, so Windows may retain
a stale shell icon until the notification area naturally refreshes; this is not
evidence of another live process.

Global `AGENTS.md` contains one delimited working agreement directing future
substantive tasks to the skill at material phase and delegation boundaries.
The agreement and skill provide workflow guidance only: neither can message,
interrupt, cancel, enumerate, or otherwise control a Codex task.

All synthetic boundary and phase-suppression checks live only in the test
assembly. Production code has no simulated-observation entry point or CLI flag.

## App Server primary architecture milestone

Accessibility is not the permanent source. Official OpenAI documentation exposes
`account/rateLimits/read` over the Codex App Server's supported local stdio JSONL
transport. The bounded adapter now implements that exact exchange and discards
the raw response after normalization.

The adapter requires exactly one quota window whose `windowDurationMins` is 300
and one whose `windowDurationMins` is 10,080, validates each `usedPercent` from
0 through 100, and returns `100 - usedPercent` for each. It records
response-receipt time, retains both documented `resetsAt` timestamps, uses a
hard child timeout, and fails closed on missing, duplicate, invalid,
unauthenticated, conflicting, or stale results. The helper must
never read Codex credential files; the official App Server remains the credential
boundary.

The deployment blocker was resolved with OpenAI's official user-scoped Windows
installer. The verified CLI path is outside WindowsApps and the Settings-closed
one-shot read succeeds. The guard no longer trusts PATH: it constructs the
approved user-scoped path and verifies the pinned executable SHA-256 before
every launch. An official CLI upgrade therefore fails closed until reviewed and
repinned.

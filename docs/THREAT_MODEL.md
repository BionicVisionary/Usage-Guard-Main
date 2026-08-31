# Threat model

## Multi-provider isolation

- Executable discovery is allowlisted per provider and reads no credential,
  cookie, chat, browser, or broad application-data location.
- Merely finding a process is not proof of a supported usage source. A provider
  is usage-capable only when an official, non-interactive, machine-readable
  local interface can return sanitized quota windows without starting a model
  task.
- Provider and quota-window IDs namespace settings, latches, reset identities,
  failure counters, and notification keys. Cross-provider fallback is
  prohibited.
- Multi-window providers fail closed when any required window is missing,
  ambiguous, stale, low confidence, or invalid. The strictest valid required
  window controls that provider's decision.
- Provider discovery never invokes interactive commands such as Claude
  `/status`, never sends prompts, and never reads provider authentication or
  session state.
- Claude's local status-line bridge accepts at most 65,536 bytes after a genuine
  official CLI response, limits JSON nesting, rejects duplicate properties, and extracts
  exactly one required five-hour and weekly window. Unique additional provider
  windows are ignored at the boundary rather than copied to the helper. Raw
  input is never persisted, and the bridge never starts a response. An invoked
  callback with invalid or missing required data forwards only an empty
  sanitized sentinel so the current unavailable reason can be recorded.
- Concurrent CLI callbacks are serialized by a bounded same-user writer lease.
  A lower same-window observation keeps its genuine older timestamp rather than
  being re-stamped by an idle session. Backwards reset identity fails closed.
  No timer is configured to re-label cached session fields as a fresh read.
- Claude Desktop detection is not presented as proof of live status-line
  capability. The tested Code tab did not invoke the bridge by itself, and the
  helper neither locates nor launches Desktop's private bundled executable.
- Claude user settings may contain environment values or credential-helper
  commands. Configure never reads, copies, backs up, parses, logs, or edits that
  file. It writes only a minimal helper-owned session-settings file and requires
  the user to pass it explicitly to the official CLI with `--settings`.
- Claude's sanitized state loader rejects reparse points and enforces a 32 KiB
  bound before JSON deserialization. PowerShell wrappers stop only the exact
  child PID they started, including its child tree, under a hard timeout.
- Free Chat-only Claude usage or missing status-line fields remain Unknown.
  Ordinary Chat is not claimed as controllable and account-wide Claude profile
  instructions are never modified.
- Settings, state, skills, instruction additions, and shortcuts are current-user
  and machine-local. Shared provider accounts may share an allowance, but Usage
  Guard does not sync another computer's thresholds or rules.

## Update-channel boundary

- The only helper HTTP client is the update checker/installer. It uses a fixed
  `api.github.com/repos/BionicVisionary/Usage-Guard-Main/releases/latest` endpoint,
  no authentication/cookies, a 10-second timeout, a 64 KiB response limit, and
  no API redirects. A confirmed asset download permits only the exact GitHub
  repository URL followed by `release-assets.githubusercontent.com`.
- A response is accepted only for a non-draft/non-prerelease numeric tag and an
  HTTPS release page under the exact GitHub repository. Foreign/malformed data
  fails closed.
- Automatic checks may notify but never install. Installation requires a
  deliberate in-window confirmation, one exact version-matched setup asset and
  checksum asset, bounded downloads, an approved GitHub release-asset redirect,
  a constant-time SHA-256 match, and successful official GitHub CLI proof that
  the file is an asset of an immutable release in the exact repository. The CLI
  is accepted only from fixed machine/user install paths with a valid GitHub,
  Inc. Authenticode publisher signature. Missing, duplicate, foreign, oversized,
  mismatched, mutable, or unverifiable assets are deleted/fail closed. The
  installer is still unsigned; GitHub repository control and the signed CLI are
  in the trusted computing base.
- Update checks are independent of provider monitoring and cannot affect usage
  decisions. A main-branch push is never treated as an installable release.

## Desktop monitor additions

The desktop monitor adds four local assets: validated settings, sanitized current state/latch, one HKCU startup value, and a notification-area UI. They remain outside the Codex authentication boundary.

New threats and controls:

- **Executable substitution:** a changed path/version/hash yields `ProvenanceMismatch`/Unknown. The helper never searches PATH for a replacement and never auto-trusts an update.
- **Protocol overreach or drift:** production emits only the three approved protocol messages and parses only quota window fields. Authentication refresh requests, unexpected structure, ambiguity, or stale data fail closed; raw responses and stderr are never logged or persisted.
- **Child leakage:** every child is created by the helper, linked to cancellation, deadline-bounded, and the only process eligible for termination. No existing Codex Desktop, CLI, model task, or unrelated process is touched.
- **Corrupt or adversarial local state:** schema/version/range validation and atomic same-volume replacement prevent partial data from becoming a safe decision. Unsupported, corrupt, or inaccessible state is Unknown. Historical display data is never promoted to fresh.
- **Clock manipulation:** observation age can become Unknown, but the local clock alone cannot clear a reset-keyed SafeWrap latch or disable the unrestricted override.
- **Accidental override:** enabling unrestricted development requires an explicit UI confirmation and remains conspicuously visible. Restore defaults, reset detection, restart, and usage changes do not disable it.
- **Notification disclosure:** balloons and the popup show only normalized percentage, state, local reset/observation times, freshness, monitoring, and provenance health. No account identity, raw protocol, or history is shown.
- **Startup persistence abuse:** startup is opt-in, HKCU-only, exact-path, non-admin, visible in settings, and removable with one bounded action. The helper creates no service, scheduled task, hook, or hidden updater.
- **AI-launch watcher overreach:** the optional Launch Together feature creates
  only fixed helper-owned Start-menu shortcuts for registered provider URIs. It
  installs no process watcher, WMI subscription, audit policy, scheduled task,
  shell hook, or arbitrary launch target, and leaves original shortcuts intact.
- **Single-instance spoofing:** mutex/event names carry no data and duplicate instances exit without starting a second monitor. A spoof can deny availability but cannot obtain credentials or weaken a decision.
- **Override/state tampering:** files are stored in the current user's local profile and inherit user ACLs. Local code running as that same user remains in the trusted-computing base; the helper cannot defend against a fully compromised user session.

The global normal-enforcement agreement is a separate, explicit user instruction. The helper does not rewrite that file or enable/disable enforcement outside the user's validated setting.

## Scope and safety objective

This milestone verifies that a short-lived helper can read 5-hour and weekly quotas
while Codex Settings is closed through an ordinarily launchable official Codex
CLI and the documented App Server stdio protocol. It also adds a pure offline
policy evaluator, a live-only phase-boundary command, and advisory task-start
instructions. The prior visible-view accessibility reader remains a fallback
feasibility path.

The safety objective is to fail closed. Missing, stale, ambiguous, spoofed, or
unreadable data must never be interpreted as capacity being available.

## Protected assets

- Codex authentication files, session tokens, browser cookies, and account data
  beyond the two normalized quota percentages and reset times.
- Raw accessible names and other UI content in Codex or any unrelated app.
- User input, foreground focus, window placement, running tasks, and operating
  system configuration.
- The observation result, which must not overstate source authenticity or data
  freshness.
- The global Codex instructions and installed skill/tool, which must preserve
  existing content and must not create task-control authority.

## Allowed data flow

The primary path is limited to:

1. Construct the approved user-scoped CLI path, verify its pinned SHA-256, and
   launch only `codex app-server --listen stdio://` from that path.
2. Send `initialize`, then `initialized`.
3. Send exactly one `account/rateLimits/read` request.
4. Prefer `rateLimitsByLimitId`; use `rateLimits` only when the multi-bucket
   property is absent or null.
5. Require exactly one 300-minute and one 10,080-minute window; retain only
   their normalized remaining percentages, reset times, and local observation
   time.
6. Close stdin, wait briefly for exit, and terminate only the owned child if it
   fails to shut down within the hard timeout.
7. Pass the sanitized observation to a pure offline evaluator and emit only the
   decision, normalized quota/timestamps, confidence/freshness, and pinned
   `live_app_server` provenance.

The raw JSONL line is transient process memory only. It is not logged,
serialized, copied, or persisted. Server error messages and unrelated response
fields never enter the output contract. A server request to refresh externally
managed ChatGPT tokens is refused without a response.

The fallback accessibility path is limited to:

1. Enumerate process identifiers named `Codex` or `ChatGPT`, then retain only
   processes whose Windows package family is exactly
   `OpenAI.Codex_2p2nqsd0c76g0`.
2. Enumerate visible top-level window handles and keep only handles owned by
   those identifiers.
3. Ask Windows UI Automation for an exact visible `Weekly usage limit` label.
4. Walk outward structurally to the nearest accessibility container that has no
   differently labelled usage-limit row and contains a percentage explicitly
   paired with `remaining` or `left`.
5. Emit only the normalized result contract.

There are no writes to the Codex process or UI Automation patterns that can
invoke, select, focus, expand, scroll, or set a value.

## Explicitly excluded behavior

- Reading auth/configuration files, browser profiles, cookies, tokens, process
  memory, network traffic, or private/undocumented service endpoints. The
  official App Server child may use its managed ChatGPT authentication for the
  one documented rate-limit read; the helper never receives that credential.
- Login, logout, token-refresh, reset-credit, account-profile, usage-history,
  thread, chat, or turn requests.
- Mouse or keyboard input, Alt+Tab, UI navigation, screenshots,
  OCR, clipboard access, window manipulation, or admin elevation.
- Raw UI/protocol logging, telemetry, crash dumps created by this helper, or
  persistence of percentage history.
- Background services, scheduled tasks, machine-wide startup, automatic
  updates, task control/cancellation, or unrelated project changes. The only
  optional persistence is the explicit helper-owned HKCU Run value.

Explicitly authorized user-scoped integration changes are limited to the
published desktop tool, exact installed skill copy, validated local settings
and sanitized state, optional helper-owned HKCU startup value, one delimited
global `AGENTS.md` section, and dated rollback copies. They do not authorize a
service, plugin, task message, interruption, or cancellation.

One installation exception was explicitly authorized for this milestone:
OpenAI's official Windows installer installed CLI `0.149.1` under the user's
profile and updated only the user PATH. No administrator rights or UAC were
used. No future installation or update is implied by that exception.

One test-only exception was explicitly authorized for a single supervised pass:
the helper could set foreground between the recorded foreground window and the
validated Codex HWND, minimize/restore that Codex HWND, and terminate only its
own timed-out child reader. This mode is isolated from normal execution, uses no
input, and restores the recorded state in `finally`.

## Threats and controls

| Threat | Control in this milestone | Residual risk |
| --- | --- | --- |
| A different process uses an expected executable name | Require the installed Codex package family, visible top-level window, exact view marker, and explicit remaining-language percentage | UI text can still be misleading and package identity does not prove server-side quota |
| Another percentage is mistaken for weekly remaining usage | Require the exact weekly label, nearest isolated structural container, explicit `remaining`/`left` wording, and one distinct value | UI wording or hierarchy may change; multiple legitimate values remain ambiguous |
| Hidden or unrelated UI is inspected | UI Automation starts only on visible Codex-owned top-level windows; the value scan is bounded around a visible exact marker | Windows enumeration sees handles/PIDs for other top-level windows, but no accessibility properties are requested from them |
| UI text leaks through output or errors | Do not log; discard strings after parsing; emit enumerated error codes and no exception messages | A debugger or hostile process with memory access is out of scope |
| A slow/hung provider yields old evidence | Reject scans longer than five seconds and record the completion timestamp | A provider call can still block the foreground CLI because it is not forcibly cancelled |
| Displayed account data is stale | `freshness=observed_now` means only that the UI was read now | The helper cannot prove when Codex last refreshed from its service |
| Accessibility tree is unexpectedly large or malicious | Stop target-scope traversal after 512 elements and fail closed | Exact-marker discovery is performed by the UI Automation provider |
| Multiple windows/views disagree | Require exactly one matching visible Usage view | Duplicate accessibility markers may cause conservative unavailability |
| App Server returns missing or multiple required windows | Require exactly one 300-minute and one 10,080-minute window; equivalent duplicates, conflicts, and omissions all fail closed | Future protocol changes may require a reviewed selector update |
| Claude adds a uniquely named quota window | Structurally reject duplicate JSON properties, select the required `five_hour` and `seven_day` objects by exact name, and discard all other fields/windows before the helper boundary | A missing, duplicate, malformed, stale, or expired required window still returns Unknown; synthetic compatibility does not prove a live provider payload |
| App Server returns expired or invalid data | Require `usedPercent` in range, a valid future `resetsAt`, and record local receipt time | Receipt time proves observation time, not server generation time |
| Raw response leaks account details | Keep one bounded JSONL line in memory, inspect only quota fields, emit only the normalized contract, and discard all exception/server messages | Process memory inspection by a hostile peer is out of scope |
| Child process hangs | Separate startup/read/shutdown timeouts; terminate only the helper-owned child after shutdown failure | OS process creation itself is synchronous |
| An official executable is inaccessible | Return `executable_inaccessible`; never copy, re-permission, attach, or use a private runner | The Store path remains inaccessible; the verified user-scoped path works |
| A different executable shadows the official CLI | Construct the exact official user-scoped path and verify the pinned binary SHA-256 before launch | An official CLI upgrade fails closed until reviewed and repinned |
| Policy thresholds are inverted or out of range | Validate `0 <= finish <= safe-wrap <= warning <= 100` before evaluation | Invalid configuration returns `Unknown` and allows no new phase |
| An observation is old, expired, or unavailable | Apply a maximum age and future-reset check; classify it as `Unknown`, which permits no new phase | Receipt time does not prove when the service generated its data |
| Synthetic evidence is mistaken for a real threshold event | Keep fake observations inside the test assembly; expose no production simulation flag; require genuine live provenance or a genuine reset-keyed latch | A historical 24% event was observed separately; current normal enforcement still depends only on genuine live provenance |
| Reset timestamp jitter creates repeated reset events | Anchor each trusted quota-window reset and treat only values within a documented two-minute tolerance as the same window; backward/out-of-tolerance movement fails closed | A provider change larger than tolerance becomes Unknown/new-window review rather than being silently merged |
| WinExe wrapper leaks unrelated child diagnostics | Capture stdout/stderr separately, suppress stderr, require exactly one strict sanitized decision object, and preserve the validated helper exit code | Same-user process tampering remains outside the boundary |
| Restarts leave duplicate tray icons or notifications | One mutex owner, graceful shutdown event, exact-PID waits, explicit NotifyIcon disposal, and persisted per-kind notification ledger | A hard crash cannot notify Explorer; a stale shell icon may remain until natural shell refresh |
| A policy result is mistaken for task authority | Evaluator returns data only; skill/AGENTS guidance finishes a checkpoint without task APIs | Human/agent adherence is advisory, not a hard stop |
| A reset time is guessed or stale | Preserve exact live `resetsAt` for both required duration-classified windows; schedule only from a fresh high-confidence SafeWrap and expose the separate jitter margin | Provider generation time is not independently attested |
| A wake-up duplicates or resumes too early | Machine-local opt-in, stable reset identity, one same-thread occurrence, checkpoint/cleanup/idle prerequisite, self-deletion, and a new live guard check when fired | Codex automation availability and delivery are product dependencies; the helper cannot hard-resume a task |
| Sandbox QA crosses into the host | Disable network/clipboard/devices/vGPU, map exact read-only input plus empty evidence only, bind one signed newly owned client PID/HWND, and never use host-wide input | A startup splash can precede the final HWND; physical hardware claims need controlled host tests |
| Existing global instructions are damaged | Preserve every line, create a dated sibling backup, and use one delimited idempotent section | Codex reads global instructions only at the start of a future task |

## Trust boundaries

The Windows kernel/process table, package-identity API, User32 window ownership,
the UI Automation framework/provider, and Codex Desktop are outside this
helper's trust boundary. The parser and sanitized JSON serializer are inside it.
This milestone verifies package family identity but not executable integrity or
server-side quota.

## Security decision

The bounded source, configured evaluator, reset-keyed latch, desktop monitor,
installed skill, and global working agreement preserve the credential boundary
by design. Runtime/UI/performance/installation evidence is recorded separately
and must not be inferred from code or synthetic tests. The integration remains
advisory only and has no authority to stop, message, interrupt, or schedule
Codex tasks. Accessibility remains fallback feasibility evidence only.

# Usage Guard

Usage Guard v.0.003 is a user-scoped Windows status popup and notification-area monitor. Its verified Codex adapter reads sanitized 5-hour and weekly usage windows while Codex Settings is closed by launching the pinned official Codex CLI's documented App Server stdio mode for one bounded `account/rateLimits/read` observation per check.

The helper does not read credentials, authentication files, cookies, browser state, chats, account identifiers, screenshots, or unrelated UI. Provider monitoring has no direct HTTP client or private endpoint; the only HTTP capability is the separate bounded public GitHub release checker and user-confirmed installer download. It has no task/thread control, reset-credit action, service, administrator component, or production simulation switch. App Server responses exist only in bounded process memory until exactly one 300-minute and one 10,080-minute quota window are normalized.

## What the desktop helper provides

- Compact status/settings popup with native WinForms scrolling and a notification-area icon. Each provider tab owns its own **Check now**, **Start/Stop Monitoring**, and **Configure AI** actions; application-wide settings, updates, tray, and Exit stay outside the tabs.
- Genuine 5-hour and weekly remaining percentages, both reset times in local time, the controlling window, last successful check, freshness, monitoring state, and pinned-CLI provenance health.
- `Normal`, `Warning`, `SafeWrap`, `Unknown`, provenance mismatch, reset detection, and a visibly persistent `Override active` state.
- Configurable Warning/SafeWrap/critical thresholds, 30–300 second polling, transition notifications, tray behavior, monitoring on/off, opt-in start-at-sign-in, and Restore defaults.
- Atomic, schema-validated settings and minimum sanitized current state under `%LOCALAPPDATA%\OpenAI\CodexUsageGuard`; no percentage history or raw protocol data.
- Independent genuine reset-keyed SafeWrap latches that only a fresh, high-confidence live observation can create or rearm.
- Optional reset-aware handoff metadata derived only from the exact live
  provider `resetsAt` values for currently constraining windows. It recommends
  the later required reset plus a two-minute jitter margin; it never infers a
  cadence or schedules from stale or latch-only data.
- Change-only status rendering, no unnecessary shortcut/startup rewrites on Apply, and native child-control painting to prevent stale duplicate frames while scrolling.

SafeWrap is advisory and checkpoint-based: finish the current coherent checkpoint and start no new material phase. The lower Critical SafeWrap threshold marks greater urgency inside that same behavior; it never instantly stops or cancels a task. Neither state kills, interrupts, enumerates, messages, or controls Codex tasks. Unknown fails closed under normal enforcement but is never described as a genuine threshold event.

When the local one-shot reset wake-up preference is enabled, the installed
Codex skill may create or update one deduplicated same-task wake-up only after
the agent has finished its coherent checkpoint, completed cleanup, and become
idle. The wake-up time comes from the sanitized live recommendation, rechecks
the guard when it fires, and deletes itself. Usage Guard does not poll tasks,
start model work, or assume that it can resume a task by itself.

## Isolated QA

The repository includes a locked-down Windows Sandbox harness for disposable
functional, install/rollback, and visual QA. Networking, clipboard, vGPU,
microphone, camera, and printer redirection are disabled. Only a generated
read-only input folder and a newly empty evidence folder are mapped. The host
launcher binds one newly owned Microsoft-signed Sandbox client and places only
that exact window on an explicitly approved non-primary display identified by
stable hardware ID and exact working bounds. See `scripts/sandbox` and the
repository-root contributor `TROUBLESHOOTING.md`.

The host UI/performance harnesses also accept an explicit
`-AllowSinglePrimaryDisplay` test-only switch when exactly one monitor is
connected and the user has authorized that screen. They remain fail-closed for
multiple-display primary-screen testing and never change display settings.

## Multiple AI providers

The popup uses one tab per configured provider. Each provider owns
independent thresholds, polling, notification preferences, quota windows,
reset/latch state, and policy decisions. A result from one provider is never
substituted for another. These settings and the installed AI instructions are
stored only for the current Windows user on this computer. They are not synced
through an AI account and do not alter another person's Usage Guard rules on a
different computer, even if both people share the same AI account.

- **Codex:** live 5-hour and weekly remaining usage is supported through the
  pinned official Codex App Server interface. The documented window durations
  identify each quota, both are required, and the stricter configured decision
  controls.
- **Claude:** Configure Claude installs a local status-line bridge,
  phase-boundary skill, and isolated session-settings file for the official
  Claude Code CLI. It does not read, copy, back up, or edit Claude's user
  settings. Anthropic's documented `rate_limits.five_hour` and
  `rate_limits.seven_day` status-line fields supply the two independent windows
  after a genuine Claude Code response; the stricter configured result controls.
  Claude Chat, Desktop, web, mobile, and Code consume the same account allowance,
  so their consumption is reflected at the next trusted Code observation. The
  helper never starts a model response merely to refresh it. In the tested
  Claude Desktop release, the Code tab did not invoke status-line commands by
  itself; users need the separately installed official CLI and must start it
  with the exact `--settings` command shown by Usage Guard. It can run in
  Desktop's integrated terminal. A repeating timer is deliberately not used
  because re-rendering cached session fields is not proof of a new account read.

The Claude tab reports whether no status-line callback has arrived or a callback
arrived without both required windows. Both cases remain Unknown and expose no
raw status-line/session data; the distinction exists only to give safe setup
guidance.

Free Chat-only Claude accounts do not expose those documented machine-readable
percentage fields. Usage Guard therefore shows Unknown rather than estimating.
Ordinary Claude Chat also has no documented machine-local phase-boundary hook,
so its work is advisory/manual; Usage Guard deliberately does not modify
account-wide Claude profile instructions. Claude Code is the supported path for
automatic checkpoint-safe behavior.

When another provider gains an official safe local quota interface, its adapter
can supply its declared windows to the same fail-closed multi-window policy.

## Shareable Windows package

`scripts/New-Package.ps1` creates both a self-contained `win-x64` ZIP and a
console-free Windows `UsageGuard-Setup-0.003.exe` bootstrapper plus its matching
`.sha256` file. Recipients can
run `Install.cmd` for the standalone popup/monitor or deliberately choose
`Install-With-Codex-Integration.cmd` to add the optional Codex phase-boundary
skill. Neither path requests administrator rights. The install directory is
configurable; for example, `D:\Apps\Usage Guard`. The 0.003
installer is unsigned, so Windows may show
an unknown-publisher warning; recipients should compare its published SHA-256.

The optional **Launch Together** setting creates helper-owned Start-menu
shortcuts for Usage Guard + Codex and Usage Guard + Claude. It does not replace
the providers' original shortcuts or run a process watcher. Starting an AI from
its original icon intentionally bypasses this convenience feature.

The 0.003 UI checks the fixed public GitHub Releases channel at startup and every
six hours, independently of provider polling, and de-duplicates notifications by
version. **Check for updates** lets the user install inside Usage Guard: it
downloads exactly one version-matched setup executable and `.sha256` asset,
follows only an approved GitHub release-asset redirect, enforces time/size bounds,
requires GitHub's immutable-release flag and SHA-256 digest for each exact asset,
verifies the checksum file against its GitHub digest, and verifies the installer
against both its published checksum and GitHub's separate installer digest.
Only then does it ask for confirmation, launch the user-scoped installer for the
current installation folder, and exit cleanly. Recipients need no GitHub account,
GitHub CLI, or credentials. It never sends credentials, silently installs, or
treats a push to `main` as an installable release.

## Development and release workflow

New features are developed on a `codex/` feature branch. A completed branch must
include tests, security evidence, a version/change description, and a package
hash. It is merged to `main` and published only after explicit user approval.
Users should never be told an update exists before that approved package and its
release notes are published through the configured channel.

Normal enforcement is the clean-install default. The user may deliberately enable **Unrestricted development override** to disable usage-based gating; it remains visibly active until manually disabled. Usage, reset, local time, Restore defaults, and application restart cannot change that choice. The current global agreement requires future substantive tasks to consult the installed skill at material phase boundaries.

## Security and provenance

The supported source is documented in OpenAI's [Codex App Server documentation](https://learn.chatgpt.com/docs/app-server). Production sends only `initialize`, `initialized`, and one `account/rateLimits/read`.

The helper launches only `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe app-server --listen stdio://`. It pins official CLI `0.149.1` and SHA-256 `a395030b56b126f608f2403036dddb654a9c063213e9c2b5f85d954cf490ebe6`. Path or digest drift becomes `Provenance mismatch`/Unknown; the helper does not search PATH, auto-update, or auto-trust a changed binary.

The earlier accessibility reader remains feasibility source/test evidence only.
The production executable exposes no accessibility or window-state command.

## Build and test

The solution targets the installed .NET 8 Windows Desktop runtime and uses only built-in framework libraries.

```powershell
dotnet build .\CodexUsageGuard.sln --configuration Release
& .\tests\CodexUsageGuard.Tests\bin\Release\net8.0-windows\CodexUsageGuard.Tests.exe
dotnet format .\CodexUsageGuard.sln --verify-no-changes --no-restore
```

Launch the verified build:

```powershell
& .\src\CodexUsageGuard\bin\Release\net8.0-windows\CodexUsageGuard.exe
```

The installed future-task wrapper accepts no arguments and invokes only the configured decision command. Because the desktop binary is a WinExe, the wrapper waits for it with a hard timeout, captures stdout/stderr separately, suppresses unrelated child diagnostics, validates exactly one sanitized decision object, and preserves the helper exit code:

```powershell
& "$env:USERPROFILE\.codex\skills\codex-usage-guard\scripts\check_usage.ps1"
```

There is no `--test-mode`, `--simulate-remaining`, or equivalent production entrypoint. Threshold/parser fixtures remain inside the test assembly.

## Documentation

- [Implementation and state-machine plan](docs/IMPLEMENTATION_PLAN.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Threat model](docs/THREAT_MODEL.md)
- [Operating guide](docs/OPERATING_GUIDE.md)
- [Installation guide](docs/INSTALLATION.md)
- [Troubleshooting guide](docs/TROUBLESHOOTING.md)
- [AI integration guide](docs/INTEGRATION_GUIDE.md)
- [Rollback guide](docs/ROLLBACK.md)
- [Feasibility history](docs/FEASIBILITY_ASSESSMENT.md)
- [Final verification evidence](docs/FINAL_EVIDENCE.md)

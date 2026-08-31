# Feasibility assessment — 2026-08-25

> Production follow-on: the verified App Server source is now integrated into a
> user-scoped desktop monitor and settings popup. See `FINAL_EVIDENCE.md` for
> runtime verification. The historical results below remain the provenance for
> selecting App Server over accessibility; they are not current monitor proof.

## Decision

The bounded **Codex App Server usage source works with Settings closed** through
OpenAI's official user-scoped Windows CLI. A live-only phase-boundary command,
repo-owned/installed skill, and global working agreement are now implemented.

The current live reading remains above 25%, so the genuine safe-wrap behavioral
event is armed but **not observed**. Do not present unit fixtures as that event.
Do not enable continuous polling, startup, notifications, task messaging, or
task control yet. The Microsoft Store executable remains untouched; UI
Automation, a second Settings window, OCR, local app databases, and credential
files remain rejected as permanent sources.

## Official CLI installation provenance

The current [official Codex CLI documentation](https://learn.chatgpt.com/docs/codex/cli)
documents a Windows standalone installer at
`https://chatgpt.com/codex/install.ps1`. On 2026-08-25 that redirected to
`https://releases.openai.com/codex/install.ps1` and had SHA-256
`391f247de2c70c7e99041979ec02dae7e76be27ac9cfc1dfe7c1eb21d48d8b97`.

Verified installation:

| Field | Evidence |
| --- | --- |
| Scope | Current user; no administrator rights or UAC |
| Version | `codex-cli 0.149.1` |
| Resolved path | `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe` |
| Installer-managed target | `%USERPROFILE%\.codex\packages\standalone\current\bin` |
| Executable SHA-256 | `a395030b56b126f608f2403036dddb654a9c063213e9c2b5f85d954cf490ebe6` |
| App Server check | `codex app-server --help` exited successfully without a model task |

The resolved executable is outside WindowsApps and first on the installer-added
user PATH. The installed CLI's help currently labels App Server experimental;
that remains a maturity risk even though the documented stdio exchange works.

## Settings-closed App Server milestone

Official documentation specifies default stdio JSONL, the required
`initialize`/`initialized` handshake, and `account/rateLimits/read`. See the
[official Codex App Server documentation](https://learn.chatgpt.com/docs/app-server).

Implemented one-shot exchange:

1. Launch `codex app-server --listen stdio://` through normal process creation.
2. Send only `initialize`, `initialized`, and
   `account/rateLimits/read`.
3. Prefer `rateLimitsByLimitId`; use `rateLimits` only if the multi-bucket view
   is absent or null.
4. Require exactly one 10,080-minute window, `usedPercent` from 0 through 100,
   and a valid future `resetsAt`.
5. Calculate remaining as `100 - usedPercent` and expose only reset time, local
   observation time, confidence/freshness, and a stable status/error.
6. Close the short-lived child under separate startup/read/shutdown timeouts.

Synthetic result: **48 tests passed**, including multi-bucket priority, legacy
fallback, duplicate/conflicting windows, missing/invalid/stale data, rejection
redaction, refused token-refresh requests, timeout/owned-child cleanup paths,
and all offline policy boundaries/fail-closed states.

Live result with Settings closed:

| Status | Remaining | Reset | Confidence | Freshness | Error |
| --- | ---: | --- | --- | --- | --- |
| available | 26% | 2026-08-31 07:04:35 UTC | high | observed_now | none |

Final local observation time was 2026-08-24 15:22:50.8516039 UTC. Earlier
integration passes returned higher values with the same reset. The earlier 65%
accessibility observation was older, so the lower results are plausible as a
sanity check; no equality or account-data reconciliation was assumed. The final
decision in that pass was `Warning`, not `SafeWrap`. A later prior-milestone
genuine 24% event was observed and a separate NetSwitch Failover task
independently entered SafeWrap; that historical event is distinct from the
desktop monitor acceptance performed under the current unrestricted override.

Only the normalized fields above were emitted. No login, refresh, logout,
reset-credit, thread, turn, model, usage-history, or account-modifying request
was sent.

## Offline guard-policy milestone

Default policy:

- `>30%`: `Normal`;
- `<=30%`: `Warning`;
- `<=25%`: `SafeWrap`, start no new phase;
- `<=20%`: finish only the current coherent checkpoint and start no new phase;
- unavailable, invalid, expired, low-confidence, or older than two minutes:
  `Unknown`, treated as safe-wrap by the task workflow; and
- invalid threshold ordering: `Unknown`, with no new phase allowed.

The evaluator is pure and offline. It has no polling, persistence, startup,
messaging, notification, process, skill/plugin, or task-control capability. The
final live 26% observation falls in `Warning` while it is fresh, but no automatic
action was taken.

## Supervised safe-wrap integration milestone

Production command:

`%USERPROFILE%\.codex\tools\codex-usage-guard\CodexUsageGuard.exe --guard-check`

Installed skill:

`%USERPROFILE%\.codex\skills\codex-usage-guard`

Repo-owned source:

`.agents/skills/codex-usage-guard`

The command constructs only the official user-scoped CLI path and verifies the
pinned `0.149.1` executable SHA-256 before launch. It emits only decision,
remaining/reset/observation times, confidence/freshness,
`source=live_app_server`, and approved source provenance. It exposes no test or
simulation flag.

The repo and installed skill both passed the bundled official
`quick_validate.py`; the two installed files byte-match the repo source. The
host runtimes lacked PyYAML and blocked temporary installation, so the
unmodified validator was executed with Python's built-in JSON parser for the
skill's strict JSON frontmatter, which is also valid YAML. No validation package
was installed.

The [official skills documentation](https://learn.chatgpt.com/docs/build-skills)
documents repo/user skill discovery and `SKILL.md` activation. The
[official AGENTS.md documentation](https://learn.chatgpt.com/docs/agent-configuration/agents-md)
documents that global instructions are read once at task/session start and that
`AGENTS.override.md` would take precedence. No override exists. The previously
empty global file was backed up as
`%USERPROFILE%\.codex\AGENTS.md.backup-2026-08-25-before-usage-guard`; one
delimited section was added, and the idempotence check found exactly one begin
and end marker with unchanged content on a repeat candidate.

Internal unit fixtures cover 31, 30, 26, 25, 20, and Unknown. A test-assembly-
only phase harness creates and cleans a dedicated temporary checkpoint, verifies
the 25% policy decision, and proves phase two is not started. It cannot reach
the production wrapper or a Codex task coordinator and is not the real-world
threshold trial.

No Codex task was created, messaged, inspected, interrupted, or cancelled. No
recurring automation, heartbeat, service, startup registration, or plugin was
created.

## Accessibility fallback result

The reader now anchors to the exact `Weekly usage limit` accessibility label and
uses the nearest isolated structural container. It does not choose by screen
coordinates, page proximity, or an unqualified percent sign.

Verified supervised observations:

| State | Status | Remaining | Confidence | Freshness | Error |
| --- | --- | ---: | --- | --- | --- |
| Foreground validation | available | 68% | medium | observed_now | none |
| Focused one-shot state test | available | 65% | medium | observed_now | none |
| Open but separately unfocused | unavailable | — | none | unknown | no_distinct_foreground_window |
| Minimized | available | 65% | medium | observed_now | none |

The one-shot harness reported `restoration=restored`. The unfocused state was not
measured because the validated Codex window was already the recorded foreground
window and the safety contract prohibited selecting an unrelated alternate
window. Minimized availability establishes that this UI Automation provider can
remain readable without focus in that state, but only while the Usage view
remains selected in the accessibility tree.

This does not satisfy the permanent requirement because Settings must be able to
close.

## Non-visual sources, in priority order

### 1. Supported official command/protocol

The ordinary CLI reference documents `/usage`, `/usage weekly`, `/status`, and a
rate-limit status-line field only inside the interactive TUI. It does not
document a standalone `codex usage --json` command. See the
[official developer command reference](https://developers.openai.com/codex/cli/reference).

The supported local machine-readable source is Codex App Server over stdio JSONL.
Official documentation defines `account/rateLimits/read` and returns
`usedPercent`, `windowDurationMins`, and `resetsAt`, including multi-bucket
responses. See the
[official Codex App Server protocol](https://developers.openai.com/codex/app-server).

Implemented normalization:

1. Launch only an officially installed, accessible `codex app-server` child with
   the default stdio transport.
2. Send `initialize`, `initialized`, then only `account/rateLimits/read`.
3. Require exactly one 10,080-minute quota window.
4. Validate `usedPercent` is between 0 and 100; normalize remaining as
   `100 - usedPercent`.
5. Expose receipt timestamp, documented `resetsAt`, confidence/freshness, and a
   stable fail-closed error. Never log the raw response.
6. Apply a hard timeout and terminate only the helper-owned App Server child.

The official process owns authentication. The helper does not open `auth.json`,
receive tokens, or call an undocumented endpoint. In the verified live attempt,
the short-lived official child started normally and returned the one documented
rate-limit response before the helper shut it down.

Historical blocker: the bundled executable in the installed
`OpenAI.Codex_2p2nqsd0c76g0` Windows package is present, but Windows package ACLs
returned access denied when this helper attempted the documented App Server
mode. It was not copied, re-permissioned, or used. The official standalone
installer resolved the blocker through a separate supported user path.

### 2. Narrow non-secret local cache

A shallow filename-only review of `.codex/cache` found app/plugin catalogs and
computer-use cache folders, not a rate-limit record. A key-name-only audit of the
non-secret model catalog found model context-window keys but no account rate-limit
or reset fields.

Broad state, log, session, chat, and SQLite files were not opened. Therefore no
safe, narrowly scoped, source-timestamped local rate-limit cache is established.

### 3. Personal Analytics / Profile

Official Settings documentation describes Profile activity insights such as
lifetime tokens, peak tokens, streaks, longest task, and token activity. The App
Server similarly documents `account/usage/read` for historical activity. Neither
is documented as the source of the current weekly remaining quota.

Personal Analytics is therefore not a guard input.

## Dedicated second-window fallback

Official app and command documentation does not establish multiple independent
top-level desktop windows or a Usage view that remains independently attached to
one window. The current read-only evidence validates one bound Usage HWND only.
Multiple package processes are not evidence of multiple windows.

No second window was launched, duplicated, navigated, or manipulated. This
architecture remains hypothetical and is not stable enough for the guard. Even
if later demonstrated, it would still violate the permanent requirement that
Settings may be closed.

## Remaining risks and gates

- The installed CLI labels App Server experimental, so compatibility across CLI
  upgrades must be revalidated.
- The guard pins the approved official path/version/digest. An official CLI
  upgrade intentionally becomes `Unknown` until the new binary is reviewed and
  repinned.
- A weekly bucket must be selected by documented duration and fail closed if
  missing or duplicated; bucket IDs/names must not be guessed.
- Response receipt time establishes observation freshness, not an undocumented
  server-generation timestamp. `resetsAt` must be retained separately.
- Any prototype must ignore unrelated App Server notifications and never expose
  account identity, plan details, credits, raw JSON, or errors containing raw
  server content.
- One rate-limit response exists transiently in process memory; a hostile peer
  with memory access remains out of scope.
- UI Automation remains a supervised diagnostic fallback only. OCR is not
  justified while a supported structured protocol exists.

## Rollback

1. Restore the dated zero-byte sibling backup over
   `%USERPROFILE%\.codex\AGENTS.md`.
2. Remove only `%USERPROFILE%\.codex\skills\codex-usage-guard`.
3. Remove only `%USERPROFILE%\.codex\tools\codex-usage-guard`.

Those are the only installed surfaces from this milestone. Repository rollback
is available from branch
`codex/backup-2026-08-25-before-safe-wrap-trial`.

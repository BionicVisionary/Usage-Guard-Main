# Usage Guard release evidence

## v.0.003 public updater acceptance — 2026-08-31

The public repository is
`https://github.com/BionicVisionary/Usage-Guard-Main`. Remote `main` contains
only the sanitized replacement history, including the provenance-correct
v.0.003 release commit and its final evidence record. Old releases, diagnostic
branches, and feature branches were removed before visibility changed; no
open-source licence was added.

Immutable GitHub Release `v0.003` targets exact commit
`7d80553097a0bcfe4b93616a97f0107a8d4fc709`:

- `UsageGuard-Setup-0.003.exe`: 67,959,808 bytes, SHA-256
  `28279F81741C3D0EB3845E59B593ECF1A9722424C2BD57CF7E1F3C144109830D`;
- `UsageGuard-Setup-0.003.exe.sha256`: 94 bytes, SHA-256
  `588A8F72A115D66B5C1A44D92678AC9DE7172A4FD6DA4CAF75F33DC290562937`;
- `UsageGuard-0.003-win-x64.zip`: 67,945,132 bytes, SHA-256
  `6C1997D9BFCB50AE44EC3BFFD23D009FCE5069A0645668805166418F767A326A`;
- packaged executable: SHA-256
  `17D21152DA370640AE8DA63524AB9B61CED41E03A84B3287D6A663129C938C50`,
  product version
  `0.003+7d80553097a0bcfe4b93616a97f0107a8d4fc709`.

Credential-free updater evidence:

- the public latest-release endpoint returned HTTP 200 without an
  `Authorization` header;
- a fresh HTTP client with default credentials disabled downloaded the exact
  setup and checksum assets and matched both GitHub SHA-256 digests plus the
  checksum line;
- the installed v.0.002 application recorded exactly `Update:0.003` after a
  normal restart, proving its real automatic in-app checker observed the new
  public release;
- the production `GitHubReleaseUpdateService` reported v.0.003 `UpToDate`, and
  the production `GitHubReleaseUpdateInstaller` prepared the genuine v.0.003
  installer as `Ready` with the expected hash and no authorization value;
- that owned temporary download was deleted after verification. No GitHub CLI,
  GitHub account, or GitHub token is required by recipients.

Final installed evidence:

- installed path: `<user-selected-install-directory>\CodexUsageGuard.exe`;
- installed SHA-256 matches the packaged executable exactly;
- settings and provider-catalog hashes remained unchanged across both upgrades;
- the current-user Codex skill wrapper still returns one sanitized decision;
- the explicit unrestricted-development override remained enabled as required
  by this Goal.

Release build completed with zero warnings/errors, `dotnet format` reported no
changes, and **172 synthetic tests passed**. The dependency inventory contains
zero third-party packages. Current-tree privacy scans found no workstation
username, private absolute development path, high-confidence API token, or
private-key marker.

The exact installed v.0.003 QA process produced `artifacts/ui-evidence-v0.003`
using only its opt-in single-display mode and dedicated keyboard chords. Both
Codex and Claude captures were visually inspected: version text, tab-specific
controls, independent monitoring state, scrolling, borders, and override state
were readable without clipping, duplicate frames, or horizontal scrolling.

Final installed performance on the authorized single 1920x1080 display:

- cold visible/responsive: 1,753.2 ms;
- minimized reopen visible/responsive: 793.8 ms;
- full restart visible/responsive: 1,767.4 ms;
- ordinary second-instance signal: 102.0 ms with one primary process;
- active, idle, and restart samples: 0% CPU; GPU average/peak 0%; working set
  63.14–63.22 MiB; private memory 18.52–18.67 MiB;
- every sample was responsive and graceful shutdown left zero processes.

Residual limits: the installer is not Authenticode-signed, so Windows can show
an unknown-publisher warning. Immutable GitHub metadata and three-way hash
verification protect downloads from accidental corruption or post-publication
asset replacement, but cannot protect against compromise of the GitHub
publisher account before a malicious release is published. SafeWrap remains
advisory and phase-boundary based. Claude live values remain response-driven
and unavailable when the documented status-line source has not produced both
required windows.

## Historical v.0.001 evidence — 2026-08-31

## Candidate and release state

Machine-specific evidence paths are represented with descriptive placeholders
or environment variables in this publication copy. Hashes, timestamps, test
results, and security conclusions are unchanged.

The reviewed clean-history candidate was merged to `main` at
`50792f4be9e59ad9ccd452f61af1eb0e84a2b7cd` after the user approved release.
The dated pre-release backup branch is
`codex/backup-2026-08-31-before-usage-guard-0.001-release`.

GitHub Release `v0.001` is published from that exact commit at
`https://github.com/BionicVisionary/Usage-Guard-Main/releases/tag/v0.001`.
GitHub reports the release as immutable, non-draft, and non-prerelease. Its
three assets were verified with GitHub's signed release attestations:

- `UsageGuard-Setup-0.001.exe`: 67,959,808 bytes, SHA-256
  `E55BEDDEBC36ADC682AD0075C427297B0DCC251B97B510C3223B73A4989684FA`;
- `UsageGuard-Setup-0.001.exe.sha256`: 94 bytes, SHA-256
  `9C1F02B9085763A4D89793B42F1B7DCC93EDD6102638AE1321B98B3D2415D399`;
- `UsageGuard-0.001-win-x64.zip`: 67,944,887 bytes, SHA-256
  `20A349AD4AF51BA449B54FC7BA703BDE3DE13037E606811C1641FACE6CD8082E`.

The configured repository remains private. The authenticated release is real,
but recipients without repository access and the unauthenticated in-app update
client cannot retrieve it until the user separately approves changing GitHub
visibility. This evidence therefore does not claim public availability.

The publication-sanitization branch replaces current-tree workstation paths
with public-safe placeholders and removes the remote diagnostic transcript
branch. Earlier merged commits and the immutable release tag retain historical
documentation paths; `docs/PUBLICATION_READINESS.md` records the explicit
owner decision required before public visibility.

The user's machine-local unrestricted-development override remains enabled.
The one-shot reset-wake-up preference remains enabled. Usage, reset, restart,
or Restore defaults cannot silently turn the override off.

## Claude branch review

The external handoff and full session transcript were read as context and the
entire `claude-work` tree/history was compared with the pre-Claude base. The
review retained these changes:

- a bounded 10-second status-line helper cold-start allowance;
- recovery from an interrupted helper-owned `.new` state write;
- cross-process serialization of Claude state writers;
- conservative reconciliation when idle sessions repeat higher percentages;
- truthful UI/setup wording that the tested Desktop Code tab did not invoke the
  status-line bridge by itself.

The review removed or replaced these unsafe or unsupported changes:

- textual markers no longer prove that an installed script is Usage Guard's;
  only exact known SHA-256 values can authorize an automatic upgrade;
- the configured status line no longer uses `refreshInterval`, because rerunning
  a renderer does not prove that Claude produced a new provider observation;
- setup no longer locates or launches a private wildcard-matched Desktop-bundled
  executable; it points to Anthropic's official CLI installation and `claude`;
- Configure no longer reads, copies, backs up, or edits Claude's potentially
  secret-bearing user settings. It writes an isolated owned session-settings
  file and requires an explicit official CLI `--settings` launch;
- Windows PowerShell 5.1 timeout cleanup now terminates only the exact owned PID
  tree, and the Claude state loader rejects reparse points and oversized files;
- the large diagnostic transcript and handoff are excluded from the candidate
  tree and installer.

The user later authorized deletion of the remote `claude-work` diagnostic
branch. The remote ref was removed after its exact commit was verified against
two local recovery references. The diagnostic documents are absent from the
current publication tree and every remaining remote branch.

## Build, tests, skills, and dependency evidence

- `dotnet format CodexUsageGuard.sln --verify-no-changes --no-restore`: passed.
- `dotnet build CodexUsageGuard.sln -c Release --no-restore`: passed with zero
  warnings and zero errors.
- `dotnet run --project tests\CodexUsageGuard.Tests\CodexUsageGuard.Tests.csproj
  -c Release --no-build`: `PASS 171 synthetic tests`.
- `git diff --check`: passed; Git reported only expected working-tree line-ending
  conversion notices.
- All 17 PowerShell entrypoints parse under PowerShell 7 and built-in Windows
  PowerShell 5.1.
- The official bundled `quick_validate.py` reported both repository skills
  valid. Its PyYAML dependency is isolated under a user-selected tooling
  directory; it is not on
  PATH and is not a production dependency.
- Repository and installed Codex skill trees are byte-identical. Repository and
  installed Claude skill trees are byte-identical.
- The test project reports no packages. The runtime reports only the .NET SDK's
  auto-referenced `Microsoft.NET.ILLink.Tasks` 8.0.26 used for single-file
  publishing; there are no third-party application/runtime dependencies.

New or focused coverage includes simultaneous Claude writers, abandoned-state
recovery, live-writer timeout, idle-session conservatism, reset jitter and
regression, known-hash-only upgrades, removal of the misleading status-line
timer, PowerShell 5.1 QA and owned-child cleanup compatibility, an unreadable
Claude user-settings boundary, bounded/reparse-safe Claude state loading,
strict two-window parsing, stale and missing data, provider-isolated monitoring,
reset-derived resume metadata, single-instance/restart behavior, notification
deduplication, and update integrity checks.

## Package and installed provenance

- ZIP: `artifacts\UsageGuard-0.001-win-x64.zip`, 67,944,887 bytes, SHA-256
  `20A349AD4AF51BA449B54FC7BA703BDE3DE13037E606811C1641FACE6CD8082E`.
- Setup: `artifacts\UsageGuard-Setup-0.001.exe`, 67,959,808 bytes, SHA-256
  `E55BEDDEBC36ADC682AD0075C427297B0DCC251B97B510C3223B73A4989684FA`.
- The adjacent setup checksum file matches that installer.
- Installed application:
  `<user-selected-install-root>\Usage Guard\CodexUsageGuard.exe`, SHA-256
  `B2C2E52F03894DAD9EDBE21A67044964D5803EEBE81D6ED9502C30B174AE4FE3`.
- The current-user installation locator resolves that exact path and hash.
- The final ZIP has 20 entries and contains neither Claude diagnostic document
  nor a credential-named entry.
- Claude bridge repository/installed SHA-256:
  `485FFB265308A5DF164AAC4FB6CEAD35F9E5663038FDCF2268DDF5F6117D0E55`.
- Claude process wrapper repository/installed SHA-256:
  `22B91F2E758ACC00B99D4F76DB2E2E8F962C08F8BFD67D7A6B7133733AE64DE1`.
- The 0.001 installer is unsigned. Recipients must expect an unknown-publisher
  warning and verify its supplied checksum.

The normal user-scoped installer preserved settings, provider configuration,
and global AGENTS.md byte-for-byte. Both providers are enabled and monitoring
is on; every configured 5-hour/weekly window is Warning 30%, SafeWrap 25%, and
Critical SafeWrap 20%. Polling is 60 seconds, startup is off, reset wake-up is
on, and the explicit unrestricted-development override is on. Claude's minimal
owned session-settings file has SHA-256
`9A0B211DAE4F62033A2F88385ED2F6B99768D60357FFAEAED303991B67EA5E07`;
the Claude user settings and CLAUDE.md hashes were unchanged during the final
asset upgrade.

The replaced application and Codex skill remain recoverable in dated sibling
backups under the user-selected install root and
`%USERPROFILE%\.codex\skills`, respectively. The allowlisted older Claude
bridge/wrapper copies are retained under a dated external evidence directory.

## Genuine provider evidence

The final installed production monitor received a genuine high-confidence,
`observed_now` Codex observation at
`2026-08-30T17:06:37.2673435Z` during the final ordinary popup handoff:

- 5-hour remaining 70%, exact reset `2026-08-30T21:47:39Z`;
- weekly remaining 64%, exact reset `2026-09-06T04:02:06Z`.

The persistent user override caused the configured decision to be
`override_active`, while preserving `normal` as the underlying genuine
decision. Both timestamps came from the duration-classified 300- and
10,080-minute App Server fields. No reset was inferred from percentage,
duration, screenshot, OCR, or local cadence.

Claude produced a genuine sanitized two-window observation during the reviewed
CLI session at `2026-08-29T18:22:11.8650039Z`:

- 5-hour remaining 80%, exact reset `2026-08-29T22:20:00Z`;
- weekly remaining 76%, exact reset `2026-09-02T10:00:00Z`.

That observation is now stale. The final installed Claude wrapper therefore
returns `unknown`, no windows, and exit 2 rather than reusing the old
percentages. A new genuine acceptance requires one ordinary response from the
official Claude Code CLI in a trusted workspace. The tested Claude Desktop Code
tab did not invoke this status-line bridge by itself; future Claude releases may
change that behavior.

## Installed UI and lifecycle evidence

`<external-evidence-root>\ui-evidence-2026-08-31-b2c2e52f-repeat` contains the
installed Codex and Claude captures plus sanitized ownership evidence. The
harness bound one exact installed PID/HWND, used production-inert QA mode,
required full DWM containment on the explicitly authorized only connected
display, used only dedicated provider-tab/shutdown chords, and recorded no
mouse or Alt+Tab use. The exact process count was zero afterward.

Both inspected tabs are readable and independently show Check now, Start
Monitoring, and Configure AI. Global Settings, Check for updates, Minimize to
tray, and Exit remain outside provider tabs. QA correctly reports monitoring as
off and does not display the misleading Stop Monitoring state. The Codex tab
shows the visible development override; the Claude tab truthfully describes
the official CLI requirement. No horizontal scrolling or clipped header text
was observed in the final normal/minimum-size renderer evidence.

The host inspection script initially exposed three Windows PowerShell 5.1
compatibility defects (embedded expression-bodied C#, the `[ushort]` alias, and
PowerShell 7-only `utf8NoBOM`). Each failed run cleaned up its exact process.
The corrected pass completed both captures and JSON evidence under Windows
PowerShell 5.1.

The first final Claude capture had a transient non-client title-bar artifact
despite the exact HWND reporting the correct title. It was not accepted as
evidence. A complete repeat under the same ownership/containment checks showed
the full title and clean frame; only the repeat is the final visual evidence.

The ordinary installed popup was then launched with no arguments and left open
for user review. Exact PID/path/hash/HWND/foreground/one-display containment
were revalidated without input. Its first immediate screen copy caught an
incomplete paint frame and was rejected; after a three-second settle, the same
owned window produced the clean capture at
`<external-evidence-root>\final-popup-settled-2026-08-31-b2c2e52f.png`.
The running title is `Usage Guard v.0.001`, it is responsive, and exactly one
helper process remains by design.

## Final performance evidence

The exact installed hash was measured with visible windows on the authorized
single display:

- cold visible/responsive: 2,048.2 ms;
- minimized reopen visible/responsive: 1,176.8 ms;
- full restart visible/responsive: 2,039.1 ms;
- ordinary second launch: 139.3 ms, with exactly one primary process;
- sampled active/idle/restart CPU 1.608–1.85% over short four-second windows;
- GPU average/peak 0%; working set 62.81–63.13 MiB and private memory
  18.14–18.61 MiB;
- responsive in every sample; graceful shutdown left zero processes.

The ordinary production monitor was measured for 70.1 seconds:

- startup visible/responsive: 2,128 ms;
- exactly two owned App Server children, equivalent to 1.711 reads/minute at
  the configured 60-second interval;
- CPU 1.626%, peak working set 78.86 MiB, peak private memory 23.59 MiB;
- responsive throughout;
- settings and provider hashes preserved;
- graceful shutdown left zero helper processes.

That short production pass did not directly measure network request rate, GPU,
or model-task/thread creation, so no claim is based on it for those fields.
Code/protocol review still confirms the monitor sends only the supported
rate-limit request and contains no task/thread/turn start or control method.

## Security and residual limits

- Codex production launches only the pinned official user-scoped CLI at
  `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe`, version 0.149.1,
  SHA-256
  `A395030B56B126F608F2403036DDDB654A9C063213E9C2B5F85D954CF490EBE6`.
  Path/version/hash drift is Unknown.
- Each observation sends only `initialize`, `initialized`, and one
  `account/rateLimits/read`; child processes are time-bounded and cleaned up.
- Production does not read auth files, tokens, cookies, browser data, chats,
  account identifiers, screenshots/OCR, desktop input, private endpoints,
  reset credits, or task/thread/turn control APIs. The bounded Codex process
  response is parsed in memory and never logged, printed, or persisted; the
  Claude bridge discards unrelated fields before invoking the helper.
- The historical accessibility feasibility source remains in Git but is
  explicitly excluded from the production project build.
- Settings and state are machine-local to this Windows user and retain only
  sanitized current state, not percentage history or raw responses.
- Enforcement is advisory at task phase boundaries. It does not interrupt a
  long in-flight phase or hard-stop another task.
- Claude's documented local source is response-driven. Ordinary Claude Chat
  can consume the shared plan, but cannot invoke the machine-local bridge.
- App Server/CLI/status-line schema drift, ambiguity, stale observations, or
  provenance mismatch fail closed as Unknown.
- A focused independent security review reported five findings. All were
  addressed before this package: Windows PowerShell 5.1 owned-child cleanup,
  Claude user-settings isolation, preservation of existing status-line options
  by non-editing, bounded/reparse-safe Claude state loading, and display/PID
  revalidation with exact-process-only QA cleanup. The 171-test suite and both
  PowerShell parsers cover the resulting invariants.
- GitHub CLI 2.93.0 is installed with a valid `GitHub, Inc.` Authenticode
  signature. It verified all three local files against signed attestations for
  immutable Release `v0.001`. The repository is still private, so the
  unauthenticated in-app updater remains intentionally unavailable to users
  who do not have repository access.

Eleven small pre-review `settings.json.backup-UsageGuard-*` files remain under
the user's Claude directory. They were created by old builds, not the final
design, and may duplicate secret-adjacent settings. A bounded removal attempt
was rejected by the host safety layer before execution; no file was changed and
no content was read. The user may delete those exact helper-named backups
manually after deciding they are no longer needed. The extracted package test
stage `<external-evidence-root>\install-stage-2026-08-31-b2c2e52f` also
remains because recursive cleanup was rejected; it contains only the verified
public package on F: and is not executed or installed state.

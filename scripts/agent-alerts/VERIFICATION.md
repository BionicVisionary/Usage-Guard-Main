# Local receiver verification — 2026-09-11

## Deferred user-reported UI defect — v0.005

After disabling the override, the user reported that clicking away from Usage
Guard and returning to its window jumps the Codex tab from the upper status area
to the bottom settings/Apply area, without scrolling. Two supplied screenshots
show the before/after positions. Cause and reproduction have not been verified;
focus restoration bringing a lower control into view is only a hypothesis.
Next repair acceptance: retain the user's scroll position across window
deactivation/reactivation, while preserving deliberate keyboard navigation and
scrolling. Reproduce through isolated QA; do not steal host focus or disrupt
typing. The user asked to fix this later, not to schedule an automatic task.
The screenshot shows configured gating active and Critical SafeWrap; no new
implementation or diagnostic phase was begun for this report.

## v0.005 repair and package checkpoint

- Root cause: the installed official Codex CLI updated from the reviewed
  0.149.1 to 0.154.0. Its SHA-256 is
  be96b992178b1e467c225800da0d65f2c86d5eba1ef0b14632f65db381cbdfde.
  The hash exactly matches the openai/codex rust-v0.154.0 Windows x64 executable
  release asset digest; Authenticode reported Valid, signer OpenAI OpCo, LLC.
  Updated only the reviewed version/hash, retaining fail-closed identity checks.
- Release build: zero warnings/errors. Fifteen Node receiver/setup tests passed;
  PowerShell parsing and git diff whitespace checks passed. Hook setup tests
  cover unrelated configuration preservation, idempotence and conflict refusal.
- Host full C# tests did NOT pass: nine storage tests reported unauthorized
  operations. No storage security checks were weakened to make them pass.
- Locked-down Sandbox run sandbox-evidence-20260911-044531-cd4be93f failed at
  synthetic_tests (reported name: suffix percentage is normalized). Guest
  installation, rollback and UI proof were NOT reached. The host launcher was
  configured for the validated secondary DEL4015 display, no input injection.
- Built artifacts/UsageGuard-Setup-0.005.exe and companion .sha256, unsigned.
  Installer SHA-256: 5ec5a93b5ca90955f7f596b606c3bf28abec1419fae5a0fa52f661509868d5d0.
  ZIP SHA-256: 6192d1b9ae933a6c9161a5b29f7ad135c9a4c53aa877ba15cc874caf562446b7.
  Includes self-contained app, receiver, setup script and instructions, no user
  credentials, quota state or hook trust records. Extracted payload app matched
  its manifest. The GUI bootstrapper itself was not interactively verified.
- Installed that exact package through its user installer into
  D:\Codex\Apps\Usage Guard; app SHA-256
  5419bd80dd0f199a817b3695d34c3be569ea3955634200226027a29dafe20d4c.
  Settings hash remained identical. The helper was restarted with --background.
  Retained prior app at D:\Codex\Apps\Usage Guard.backup-2026-09-11-v0.004-to-v0.005
  and the dated installed-skill backup. Global AGENTS.md and hook definitions
  were not overwritten. The new bundled skill follows alert-only operation.
- Installed --app-server-usage returned available/high/observed_now, error null,
  five_hour 46%, weekly 2%, at 2026-09-11T04:47:10Z. This is a genuine read,
  not a guarantee of current percentages or successful threshold wrapping.
- User explicitly authorized repair/package override and subsequently enabled
  it in the helper. Override/settings remain user-owned; no reset credit or
  wake-up was used. Leave live threshold/agent wrapping acceptance to the user.

Remaining verification: investigate the host storage-test permissions and the
isolated synthetic-test failure, then finish clean-machine GUI install, rollback
and rendered instruction checks. Existing installer is built and its install
script worked on this host, but it is not a fully QA-approved public release.
No GitHub release was published. On another PC use the reviewed CLI version,
install Node for alerts, run the installed Install-CodexAlerts.ps1 and review
only its two hooks inside the interactive Codex /hooks interface.

## Observed

- 14 synthetic Node tests passed, including actual cached-check subprocess JSON.
- Settings boundaries match the helper's inclusive comparisons, including
  equal thresholds. Both quota windows and critical latch escalation are covered.
- Invalid/expired/duplicate windows, contradictory decisions, corrupt receipts,
  changed settings, recovery and per-task/per-turn suppression are covered.
- No settings or state writes in the receiver; no child-process, network, AI,
  scheduling, cancellation, key input or window manipulation interfaces.
- Source and installed receiver copies were hash-matched after installation.
- Five cached-check process samples had a median elapsed time of about 128 ms
  on this machine. These local samples made no provider or AI requests. This
  is not an end-to-end measurement of Codex hook execution latency.
- User hooks JSON configured PreToolUse/PostToolUse with a three-second bound;
  JSON parsed, and the official local CLI reports 0.149.1 with hook-trust support.
- Global agreement changed only its Usage Guard section. It requires a cached
  checkpoint fallback until observed hook events establish task delivery, and
  preserves user-owned settings and the ban on unrequested reset wake-ups.

## Running installation correction

An old v0.003 executable was running from F despite the D v0.004 locator. Both
Usage Guard + Codex/Claude Start-menu shortcuts also targeted F. Their exact
targets now point to the existing hash-verified D v0.004 executable. The old
process was closed through supported shutdown, and the D copy was started in
background mode. Settings remained byte-identical across that operation.
The F executable was not modified or deleted. Start-at-sign-in preference was
not enabled. A newly installed app binary was not required for this receiver.

## Later setup checkpoint — 2026-09-11

The user reports trusting the hooks. This coordinator subsequently received a
model-visible automatic alert: monitoring unavailable (invalid quota windows).
This proves delivery of an unavailable-monitor alert in this task, not successful
threshold delivery or agent wrapping across tasks. The one requested diagnostic
returned safe_wrap / genuine_latch_active with executable_not_approved, empty
windows and no successful live observation. No provenance bypass, threshold
change or latch reset was performed. Further build/package work was deferred.

The user now requests alert-only agent operation and will verify behavior.
Global AGENTS.md removes routine live, cached and receipt checks; explicit user
diagnostics and higher-priority delivered instructions remain exceptions.
The setup README now distinguishes PowerShell's `codex` launch from `/hooks`
inside the interactive Codex interface. The installed app's embedded setup text
has not yet been rebuilt with these instructions.

Installer inspection found artifacts/UsageGuard-Setup-0.003.exe and older
installers, but no 0.004 setup executable in the repository artifacts or
D:\Codex\Artifacts\UsageGuard. scripts/New-Package.ps1 defaults to 0.004 and
already implements a self-contained Windows installer plus checksum. Next:
update embedded Codex instructions and package integration for the separate
receiver, build and test the current installer, verify fresh-machine install
without copying user state, then hand off the exact installer and checksum.
Do not present the old 0.003 installer as the current alert-enabled setup.

## Earlier unverified state (superseded only by evidence above)

Live trusted hook delivery and actual agent wrapping at a threshold have not
been observed. The installed `--check` reports
`unverified_use_checkpoint_fallback`; no synthetic event was written as a
receipt for the real task. Review the two hooks through Codex `/hooks` before
claiming automatic delivery. No hook trust bypass or trust-record edit was used.
If that review flow is absent in the installed desktop runtime, keep the cached
fallback and report the runtime limitation. A Windows tray alert is not proof.

After review, a normal tool operation must record an event for the real task.
Then verify a model-visible threshold alert through an isolated receiver test
or a naturally reached threshold. Do not change the user's thresholds or
fabricate a production reading for testing. Hook receipts alone establish
execution, not proof that the model performed the requested wrap.

No model-generated periodic monitoring task, reset wake-up or reset credit was
created/consumed. Existing idle tasks were not woken for test messages.
Backups of the global agreement and the two changed shortcuts are under
`D:\Codex\Backups\UsageGuard\2026-09-11-agent-alerts`.

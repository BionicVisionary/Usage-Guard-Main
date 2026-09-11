# Local receiver verification — 2026-09-11

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

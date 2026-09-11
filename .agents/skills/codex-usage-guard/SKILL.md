---
name: codex-usage-guard
description: Follow delivered Usage Guard alerts; diagnose only when explicitly requested or required by a higher-priority delivered instruction.
---

# Usage Guard: alert-driven Codex integration

Usage Guard owns monitoring and user-configured thresholds. Trusted local
PreToolUse/PostToolUse hooks deliver alerts without provider calls or AI turns.
Do not run this skill's scripts or cached/receipt checks routinely at startup,
resume, checkpoints or phase/delegation boundaries. Follow the user's global
AGENTS.md. Missing alerts do not prove healthy usage.

- Warning: short recoverable checkpoints; prepare the handoff early.
- SafeWrap: finish only the active coherent checkpoint, cleanup and handoff;
  start no new phase. Critical SafeWrap makes this urgent, not destructive.
- Unknown/unavailable: report the problem and finish a safe checkpoint.
- Override: follow the user's explicit scope; never change settings silently.

Do not interrupt commands, discard work, control unrelated tasks, or weaken
self-review. Alerts arrive at tool boundaries and cannot reserve quota.

## Explicit diagnostics only

When explicitly requested, run `scripts/check_usage.ps1` once. It invokes the
installed configured-decision command. A startup timeout permits one retry after
30 seconds, not repeated polling. Trust genuine structured decisions only;
do not infer thresholds, percentages or reset times. Report provenance errors
instead of approving an unknown executable or disabling identity checks.

Thresholds, monitoring preferences, override state and latches are user-owned.
Never edit them without the user's exact authorization. Do not create reset
wake-ups or scheduled continuations without an explicit scheduling request;
a saved opt-in is insufficient. Never consume reset credits automatically.

## Setup

Install the packaged agent-alerts folder and run Install-CodexAlerts.ps1 with
an available Node executable. Launch `codex` from PowerShell, then enter `/hooks`
inside Codex and review the two Usage Guard entries. Do not bypass trust or
trust unrelated hooks. The app setup guide contains the complete instructions.

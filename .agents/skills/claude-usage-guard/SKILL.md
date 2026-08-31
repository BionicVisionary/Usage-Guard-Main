---
name: claude-usage-guard
description: Consult Usage Guard's fresh Claude Code 5-hour and weekly decision before substantive multi-phase work or delegation.
---

# Claude Usage Guard

Before substantive multi-phase work, each new material phase, or additional
delegation, run `scripts/check_usage.ps1` from this skill directory.

- Normal or Warning allows the next phase.
- SafeWrap or Unknown allows only finishing the current coherent checkpoint,
  safe cleanup, and a truthful handoff; start no new phase.
- Critical SafeWrap remains checkpoint-safe and never interrupts or kills work.

The command trusts only the official Claude Code status-line rate-limit fields
captured after a real assistant response. Both the rolling 5-hour and weekly
windows are required, and the stricter configured decision controls. Missing,
stale, malformed, or conflicting data is Unknown. Never substitute Codex usage,
invent a percentage, start a Claude turn to refresh usage, or consume credits.

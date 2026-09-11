# Codex threshold alert receiver

This dependency-free Node receiver connects Usage Guard's existing monitored
decision to Codex's supported `PreToolUse` and `PostToolUse` hook context.
It is a separately installed integration: no app binary or verified bundled
skill needs replacing. Only same-user local settings and sanitized state are
read. It never contacts a provider, starts an AI turn, blocks a tool, interrupts
a command, starts an app, changes guard settings, or schedules a continuation.

Warning tells the task to use short recoverable checkpoints. SafeWrap tells it
to finish the current checkpoint, save its handoff and become idle. Critical
SafeWrap makes the same action urgent. Both configured windows are checked;
the helper's decision governs admission, and a critical threshold can escalate
an existing SafeWrap latch. All thresholds come from user settings.

Normal is silent. Alerts are deduplicated per session/turn/level/settings and
escalate at the next hook boundary. SafeWrap reminders are at most once a minute
per active turn. Small receipts under `D:\Codex\State\UsageGuard\agent-alerts`
contain no prompts or tool arguments. Inactive tasks are never woken.
Unknown/missing/malformed/stale observations produce a fallback instruction.
The helper's two-minute freshness ceiling is enforced from its timestamp.

## Install and trust

1. Copy `usage-guard-alert.mjs` to `D:\Codex\Apps\Usage Guard\agent-alerts`.
   Keep it separate from source branches so a checkout cannot change it.
2. Add command handlers for `PreToolUse` and `PostToolUse` in the user
   `~/.codex/hooks.json`, preserving unrelated hooks. Use an absolute available
   Node executable and quoted script path, no matcher, timeout 3 seconds and
   additionalContextLimit 500.
3. Open PowerShell or a terminal and enter `codex`. Wait for the interactive
   Codex prompt. Enter `/hooks` **inside Codex**, not at the PowerShell `PS ...>`
   prompt. PowerShell reports CommandNotFound if `/hooks` is entered there.
4. In Hooks, review the PreToolUse and PostToolUse entries individually. Confirm
   each invokes your installed Node executable and `usage-guard-alert.mjs` path,
   then trust those two entries. Do not use Trust all for unrelated hooks, mark
   them managed, bypass trust, or edit trust records. Changed definitions require
   another review. Exit the terminal Codex interface when finished.
5. Keep Usage Guard monitoring enabled. Use an ordinary task and observe its
   delivered alert at a naturally reached threshold. The user is handling this
   acceptance test. Installation or a receipt alone does not prove an agent
   actually wraps. Do not change thresholds or fabricate quota data for testing.

On another computer, install the helper and Node, then configure these absolute
paths for that computer. The existing app installer does not establish that this
separate receiver is installed or trusted. Never copy credentials, quota state,
latches, or hook trust records from the first computer.

Official contract: https://learn.chatgpt.com/docs/hooks
Use Codex `.codex`/PascalCase events, not Cursor `.cursor` examples. If the
runtime lacks hook review, keep the fallback and report that limitation.

## Optional diagnostic, not routine agent work

`node "D:\Codex\Apps\Usage Guard\agent-alerts\usage-guard-alert.mjs" --check`

This returns one short JSON decision from local data, with no live quota
request. Under the user's revised 2026-09-11 agreement, agents must not run it
at startup, resume or checkpoints, or repeatedly verify delivery. Rely on
delivered alerts; use diagnostics only on explicit user request or when required
by a higher-priority delivered instruction. Absence of alerts is not Normal.
The manual check does not create a hook receipt.

## Limits and rollback

Delivery happens at tool boundaries, subject to the user's monitoring interval
and runtime hook behavior. It cannot notify during tool-free reasoning, stop a
long command, or guarantee wrapping finishes before exhaustion. Keep work
recoverable; no percentage threshold reserves tokens.

To disable deliberately, remove only these handlers from hooks.json. This stops
automatic delivery; do not silently replace it with agent polling.
No project files, guard settings or other hooks need changing. Do not remove
receipts while hook invocations are active.

## Tests

Run `node --test tests/agent-alerts.test.mjs`. Synthetic fixtures on D cover both
windows, equal thresholds, latches, stale/invalid data, deduplication, separate
tasks/turns, recovery and settings/state preservation. Passing these tests
does not establish trusted live Codex delivery.

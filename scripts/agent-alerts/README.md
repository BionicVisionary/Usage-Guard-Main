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
3. Review and trust the two exact definitions through Codex's `/hooks` flow.
   Do not mark them managed, bypass trust or edit trust records.
4. Perform an ordinary tool operation in the actual task, then use `--check`.
   `hook_event_observed_recently` means an event for `CODEX_THREAD_ID` was
   recorded recently. Installation alone is not delivery proof.

Official contract: https://learn.chatgpt.com/docs/hooks
Use Codex `.codex`/PascalCase events, not Cursor `.cursor` examples. If the
runtime lacks hook review, keep the fallback and report that limitation.

## Cached fallback

`node "D:\Codex\Apps\Usage Guard\agent-alerts\usage-guard-alert.mjs" --check`

This returns one short JSON decision from local data, with no live quota
request. Until hooks are verified, combine it with the first necessary read
and safe checkpoints. If stale/unavailable, invoke the existing guard wrapper
once; retry after 30 seconds only for startup timeout. Absence of alerts is
not Normal. The manual check does not create a hook receipt.

## Limits and rollback

Delivery happens at tool boundaries, subject to the user's monitoring interval
and runtime hook behavior. It cannot notify during tool-free reasoning, stop a
long command, or guarantee wrapping finishes before exhaustion. Keep work
recoverable; no percentage threshold reserves tokens.

To disable, remove only these handlers from hooks.json and use the fallback.
No project files, guard settings or other hooks need changing. Do not remove
receipts while hook invocations are active.

## Tests

Run `node --test tests/agent-alerts.test.mjs`. Synthetic fixtures on D cover both
windows, equal thresholds, latches, stale/invalid data, deduplication, separate
tasks/turns, recovery and settings/state preservation. Passing these tests
does not establish trusted live Codex delivery.

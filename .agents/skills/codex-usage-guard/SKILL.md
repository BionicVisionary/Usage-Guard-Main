---
{"name":"codex-usage-guard","description":"Consult the installed Codex Usage Guard's configured decision before substantive multi-phase work or additional delegation, honoring explicit override, fresh live policy, genuine latch, and fail-closed states."}
---

# Codex Usage Guard

Use this skill for substantive or multi-phase Codex work. Run the guard before
starting a new material phase and before creating or delegating additional work.

## Check

Run `scripts/check_usage.ps1` from this skill directory. It accepts no arguments
and invokes the installed configured-decision command exactly once. The helper
may return an explicit persistent user override without launching App Server.
Otherwise it performs at most one supported live rate-limit observation or
consults a genuine reset-keyed SafeWrap latch.

The wrapper waits for the WinExe child under a hard timeout, captures its streams
separately, suppresses unrelated child diagnostics, and emits exactly one
schema-validated sanitized decision JSON object. Missing, malformed, multiple,
or contradictory objects and process failures are Unknown.

Trust the configured decision only when the JSON is well formed and one of these
strict cases applies:

- `override_active`: `source` is `user_override`,
  `startNewPhaseAllowed` is true, and `finishCurrentCheckpointOnly` is false;
- `normal` or `warning`: `source` is `live_app_server`, confidence is `high`,
  freshness is `observed_now`, and exactly one sanitized 5-hour window plus one
  sanitized weekly window are present with non-null percentages and reset
  times; the helper reports which stricter window controls;
- `safe_wrap` from a fresh threshold event: the same live requirements hold; or
- `safe_wrap` from a durable latch: `source` is `genuine_live_latch` and
  `startNewPhaseAllowed` is false.

Treat command failure, malformed or contradictory output, `unknown`, or
`provenance_mismatch` as Unknown. Never invent, simulate, substitute, or reuse
an older percentage. Never infer configured thresholds from this skill; the
installed helper owns and validates them.

Thresholds, monitoring preferences, override state, and latch controls are
user-owned. Read and obey the configured decision, but do not edit the helper's
settings/state files, restore defaults, change thresholds, toggle override, or
clear/rearm a latch unless the user explicitly requests that exact change. The
app's Apply action is authoritative; never substitute agent-preferred defaults.

## Optional reset wake-up

The helper may include `resumeRecommendation`. Trust a scheduled time only when
the current decision is genuine SafeWrap, the recommendation is `recommended`,
`oneShotWakeUpOptIn` is true, and its exact reset timestamps came from the same
fresh high-confidence live App Server observation. The time is the latest reset
among every currently constraining 5-hour or weekly window plus the helper's
documented provider-jitter margin. A latch-only, stale, missing, duplicate,
malformed, expired, or provenance-invalid result is not schedulable.

Only after the active checkpoint, cleanup, and file mutations are complete and
the task is idle, create or update one same-thread one-shot Codex heartbeat for
that `resetIdentity`. Inspect existing local automations first and update the
matching task/reset identity instead of duplicating it. Use a one-occurrence
schedule at `recommendedAtUtc`; never create short-interval polling. Include the
automation id in its own prompt so that, when it fires, it first deletes itself,
then runs this guard once. Work resumes only if that new decision allows a phase;
otherwise remain idle and, if a new trustworthy recommendation is available,
deduplicate again. The wake-up is a recheck, never permission to resume by time.

## Phase decision

- `override_active`: usage-based gating is explicitly disabled, so the new
  material phase may start. This state persists until the user manually turns
  it off in the helper.
- `normal`: a bounded new material phase may start.
- `warning`: only a short, recoverable checkpoint may start; consult again at
  its next material boundary.
- `safe_wrap` or Unknown: start no new build, research, release, or delegation
  phase. Finish only the already-active coherent checkpoint, perform necessary
  safe cleanup or state restoration, record a truthful handoff, and commit only
  when that commit is normally authorized and coherent.

Every result is a point-in-time phase-admission decision, not continuous
monitoring or proof that an open-ended phase fits before the next threshold.
Before Sandbox/VM work, deep QA, builds, releases, research, or another high or
uncertain usage phase, split work into short recoverable checkpoints and invoke
this skill at each checkpoint. Never begin a long or open-ended phase when
usage could cross SafeWrap before the next check. Threshold ownership remains
with the installed helper; do not infer percentages.

Do not interrupt in-flight commands, discard work, cancel or control tasks, send
instructions to other tasks, create recurring monitoring, or claim this is a
hard stop. The narrowly authorized same-thread one-shot above is the only
exception to the no-scheduling rule, and only when the local user opted in. This
skill does not expand authorization for commits, releases, or external changes.

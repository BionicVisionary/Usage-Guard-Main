# Troubleshooting

## Provider is detected but usage is Unknown

Detection and usage capability are deliberately separate. For Claude, press
**Configure AI > Claude > Configure Claude**, then complete one ordinary
response in a Claude Code session launched with the exact isolated `--settings`
command shown by Usage Guard. Configure never reads or changes Claude's user
settings. Usage remains Unknown until both documented
5-hour and weekly status-line windows arrive. Anthropic documents these fields
for Claude.ai Pro/Max; a free Chat-only account may stay Unknown. Do not work
around it with credential files, cookies, screen scraping, prompts, private
endpoints, or a Codex percentage.

### Claude usage needs a CLI or IDE terminal session

Anthropic's status line is the only documented source of the 5-hour and weekly
fields, and it is a terminal feature. On 2026-08-30, with Claude Code 2.1.247,
all three local channels were measured on Windows:

| Channel | Runs in the Desktop Code tab? | Carries rate limits? |
| --- | --- | --- |
| Status line | No | Yes, but never delivered there |
| Hooks | Yes | No |
| OpenTelemetry | Yes | No |

Every observation that arrived during testing coincided with a Claude Code CLI
session being alive. Fresh Desktop sessions in a trusted workspace produced no
status-line invocation at all. Hooks were
confirmed to fire in the Desktop app, but their payloads contain no rate-limit
fields, and Claude Code's telemetry exports tokens, cost and session counts
rather than quota windows.

So a Desktop-only workflow currently has no supported way to feed Claude usage
to Usage Guard, and the Claude tab says so rather than waiting forever. To get
live usage, complete one ordinary response in a Claude Code **CLI or IDE
terminal** session.

Also make sure the workspace is trusted. Anthropic documents that a status-line
command runs under the same workspace-trust rule as hooks, so an untrusted
folder skips it silently. Accepting the prompt for the folder, or for a parent
directory whose trust extends to it, is enough. Accept it inside Claude Code;
never hand-edit Claude's configuration to fake acceptance.

### Usage stuck on Unknown after it once worked

Older builds could strand Claude usage permanently. The status-line bridge gave
the helper only 2.5 seconds, but the helper's first run after an install has to
unpack itself and could take slightly longer, so it was killed part-way through
writing its state. The leftover temporary file then made every later write fail,
and usage stayed Unknown with no explanation.

Current builds clear an abandoned temporary and retry under a bounded
cross-process writer lease, and allow the first run to finish. If you are on an older build, close Usage Guard and delete
`claude-state.json.new` from `%LOCALAPPDATA%\OpenAI\CodexUsageGuard`, then
complete one ordinary Claude Code response.

### Other Claude cases

An existing user status line remains untouched. Usage Guard supplies its bridge
through a minimal owned session-settings file only for the CLI session started
with `--settings`. If Configure reports a conflict inside an owned Usage Guard
path, do not overwrite the unknown file; reinstall or review it separately.
After setup, complete one new Claude Code response so the status line can
publish a fresh pair. Ordinary Claude Chat cannot consult the local guard
automatically.

Usage Guard deliberately does not configure `refreshInterval`. Anthropic
documents that it re-runs the command, but does not document it as a fresh
account query. Re-rendered cached session fields are therefore not promoted to
a new provider observation; complete a real CLI response when the result is stale.

**Check now** distinguishes two safe failure classes. "Waiting for a Claude Code
terminal response" means the configured callback has not reached Usage Guard at
all: use a CLI/IDE terminal session and confirm workspace trust. "Callback
received; both quota windows unavailable" means the bridge did run, but Claude
Code did not provide both documented Pro/Max windows. That case remains Unknown;
do not infer usage or reset timing from the plan, elapsed time, transcripts,
Chat UI, or account cadence.

## Installation locator is invalid

The optional Codex skill refuses a missing, malformed, redirected, or hash-
mismatched `installation.json`. Re-run the verified installer; do not hand-edit
the locator or bypass the hash check.

## Update check is unavailable

Confirm ordinary HTTPS access to GitHub and that the repository has an approved
non-draft, non-prerelease release containing exactly one matching
`UsageGuard-Setup-<version>.exe` and `.sha256` asset. Usage Guard rejects foreign
release/API hosts, unexpected redirect destinations, oversized or malformed
responses, hash mismatches, and timeouts. Automatic checks only notify; a user
must explicitly confirm the bounded download and then press Install in the
user-scoped setup window.

## More than one notification-area icon appears

First verify the process count for the exact installed
`CodexUsageGuard.exe`. Windows can retain a stale icon after a prior forced
termination even when only one helper process exists; it normally disappears
when the notification area refreshes. Do not click or remove unrelated icons.
Current installs use graceful shutdown and explicit icon disposal to avoid
creating new stale icons.

## Reset notification repeats

Current builds anchor each quota-window reset identity and absorb up to two
minutes of provider timestamp jitter. The combined per-kind notification ledger
survives restart.
If repeats continue, stop monitoring and preserve only the sanitized
`lastSuccessfulWeeklyResetAtUtc`, `lastNotificationKey`, and timestamps for
diagnosis; do not capture raw App Server output.

## Provenance mismatch

The official CLI path or SHA-256 no longer matches the reviewed version. The helper returns Unknown and does not search PATH or trust the new binary. Verify the update through current official OpenAI documentation, review protocol compatibility, update the pinned version/hash in source, rerun the entire Release/security/live verification, then reinstall. Do not copy WindowsApps executables or bypass package ACLs.

## Unknown or no percentage

Unknown is expected when App Server is unavailable, authentication is unavailable, a timeout occurs, the response is malformed/ambiguous, either required 5-hour or weekly bucket is missing/duplicated, a reset is expired, local state is corrupt/future-version, or provenance changed. It fails closed under normal enforcement. Use Check now after the official CLI/account state recovers. Do not inspect auth files or substitute a UI percentage.

## Monitor does not start

Check that only one `CodexUsageGuard.exe` instance is running from the installed path. A second launch should signal the first instance to show. If the first instance is unresponsive, exit it from its own notification menu or Task Manager, then start the installed executable again. The helper never terminates another Codex process.

## Startup does not run

## No reset wake-up is recommended

This is expected unless the decision is a genuine fresh, high-confidence live
SafeWrap, every required reset field is valid, and **Allow one-shot reset
wake-up** is enabled. Unknown, stale data, a durable latch without a new live
observation, missing or duplicate windows, and expired or malformed reset data
never produce a schedule. Do not calculate a replacement time from percentage
or window duration. Run **Check now** after provider access recovers.

## Startup does not run

Toggle Start automatically at user sign-in off and on, then confirm the UI reflects the exact helper-owned HKCU value. The executable must remain at its installed path. The helper never requests UAC or creates a service/scheduled task.

## Settings or state is corrupt

The popup reports Unknown rather than trusting the file. Exit the helper, move only `%LOCALAPPDATA%\OpenAI\CodexUsageGuard` aside for inspection, restart to generate defaults, and manually confirm the unrestricted override state. Never copy raw Codex data into this directory.

## Popup layout

The minimum supported size is 560×620 logical pixels. The content scrolls vertically at smaller available heights and does not require horizontal scrolling. Windows high contrast and the current app light/dark preference are read at startup; restart the helper after changing theme.

## App Server drift

Protocol or schema drift fails Unknown. Production must remain limited to initialize/initialized plus one `account/rateLimits/read`. Do not add login/refresh, private endpoints, task/thread/turn methods, or raw-response logging as a workaround.

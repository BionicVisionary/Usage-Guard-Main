# AI integration guide

Usage Guard can display supported allowance data without changing an AI's
instructions. Configuration is needed only if coding tasks should consult the
local configured decision before starting a new material phase.

All settings, sanitized state, skills, shortcuts, and instruction additions are
local to the current Windows user on this computer. They are not stored in the
AI account. Two people sharing one Codex or Claude account may see the same
provider allowance, but each computer keeps its own thresholds and working
rules. Usage Guard never edits Claude's account-wide profile instructions.

## Codex

Open **Configure AI**, select **Codex**, and press **Configure Codex**. The
operation installs or verifies the embedded `codex-usage-guard` skill and
appends one delimited section to the user's `AGENTS.md`. Existing content is
preserved and a dated sibling backup is created before a change. Start a new
Codex task afterward because tasks load instructions at their start.

## Claude

Open **Configure AI**, select **Claude**, and press **Configure Claude**. The
operation prepares the official Claude Code CLI. It installs a local skill,
adds one delimited section to `CLAUDE.md` after a dated backup, installs a
status-line bridge, and creates an isolated Usage Guard session-settings file.
It does not read, copy, back up, or edit `~/.claude/settings.json`, which may
contain environment or credential-helper configuration. Any existing user
status line remains untouched.

If the CLI is not installed, use Anthropic's documented Windows package
(`winget install Anthropic.ClaudeCode`) and complete any sign-in yourself.
Start it with the exact command shown by the app:
`claude --settings "$env:USERPROFILE/.claude/usage-guard/claude-session-settings.json"`.
The CLI may run in a normal terminal or Claude Desktop's integrated terminal.
The tested Desktop Code tab did not invoke the status-line bridge on its own.
After configuration, start a CLI session and complete one ordinary
response. That response supplies Anthropic's documented `five_hour` and
`seven_day` rate-limit fields to the local bridge; the bridge itself does not
start or consume a model response. Both windows must be present, fresh, and
valid. The stricter configured decision controls.
Usage Guard does not add a periodic status-line timer: repeating a session's
cached fields is not treated as a fresh provider observation.

Claude Chat, Desktop, web, mobile, and Claude Code consume the same Claude plan
allowance. Chat activity therefore affects the next trusted Code observation.
However, ordinary Chat has no documented local phase-boundary hook, and free
Chat-only plans do not expose supported local percentages. Those cases remain
Unknown/manual rather than being estimated or controlled.

## Launch Together

Enabling **Create Launch Together shortcuts** creates current-user Start-menu
shortcuts named **Usage Guard + Codex** and **Usage Guard + Claude**. Each fixed
shortcut starts the guard and then the selected registered app URI. Provider
original shortcuts remain unchanged, so using an original icon bypasses this
convenience. Usage Guard does not install a WMI watcher, audit policy, scheduled
task, service, or process hook.

## Troubleshooting/manual fallback

If Configure reports a conflict, do not replace unknown instructions or an
unknown file in Usage Guard's owned paths. Review the dated CLAUDE.md backup and
the exact conflict shown. Configure never uses Claude's user settings as an
ownership or merge boundary.
Codex's delimited agreement belongs in the current user's `AGENTS.md`; Claude's
delimited agreement belongs in the current user's `.claude\CLAUDE.md`. The
embedded skill folders and scripts must remain byte-identical to the verified
release. Re-running Configure is idempotent when the existing content matches.

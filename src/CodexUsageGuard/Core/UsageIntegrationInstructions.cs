namespace CodexUsageGuard.Core;

public static class UsageIntegrationInstructions
{
    public const string Overview =
        "Usage Guard monitors and displays supported usage limits without any AI instruction file. " +
        "Provider instructions are needed only when you want an AI coding task to consult the configured decision at safe phase boundaries. " +
        "That behavior is advisory: it finishes the current coherent checkpoint and starts no new phase; it never instantly stops, interrupts, or kills a task. " +
        "All thresholds, state, skills, and instruction-file changes are local to this Windows user and machine; they do not change the AI account's rules on another computer. " +
        "Each AI needs its own supported instruction and usage-source integration. Claude plan usage is shared across Chat and Code, but only Claude Code has a supported local phase-boundary integration.";

    public const string CodexSetup =
        "Recommended: press Configure Codex. Usage Guard installs or verifies its embedded Codex skill and appends only its delimited agreement to your user-wide AGENTS.md, preserving all existing instructions and creating a dated backup. " +
        "Codex loads these instructions when a task starts, so open a new task afterward; existing tasks do not gain them retroactively. " +
        "If automatic setup is blocked, use the troubleshooting instructions below. The documented /init command can create a project AGENTS.md scaffold.";

    public const string CodexAgreement =
        "<!-- BEGIN CODEX USAGE GUARD WORKING AGREEMENT -->\r\n" +
        "## Usage Guard normal enforcement\r\n\r\n" +
        "- Before substantive multi-phase work, each new material phase, or additional delegation, consult the installed `codex-usage-guard` skill and obey its configured decision.\r\n" +
        "- Thresholds, monitoring preferences, override state, and latch controls are user-owned. Agents read and obey the configured decision but must not edit settings/state, restore defaults, change thresholds, toggle override, or clear/rearm a latch unless the user explicitly requests that exact change. Values saved through the app's Apply action are authoritative.\r\n" +
        "- Normal permits a bounded new phase. Warning permits only a short recoverable checkpoint followed by another check. SafeWrap or Unknown permits only finishing the already-active coherent checkpoint, safe cleanup, and a truthful handoff; start no new phase.\r\n" +
        "- Every result is a point-in-time phase-admission decision, not continuous monitoring or proof that an open-ended phase will fit. Split Sandbox/VM work, deep QA, builds, releases, research, and other high or uncertain usage work into short recoverable checkpoints and recheck at each checkpoint. Never begin a long or open-ended phase when usage could cross SafeWrap before the next check.\r\n" +
        "- Critical SafeWrap is urgent but remains checkpoint-safe. Never interrupt in-flight commands, discard coherent work, cancel tasks, or claim a hard global stop.\r\n" +
        "- If Usage Guard returns a fresh, genuine reset recommendation and the local one-shot wake-up setting is enabled, an agent may schedule or update one deduplicated same-task wake-up only after the checkpoint and cleanup are complete. It must delete itself when it fires and recheck Usage Guard before resuming; time alone never permits work and short-interval polling is forbidden.\r\n" +
        "- Never consume a usage-reset credit automatically.\r\n" +
        "<!-- END CODEX USAGE GUARD WORKING AGREEMENT -->";

    public const string ClaudeSetup =
        "To prepare the integration, press Configure Claude. Usage Guard installs its Claude-specific skill, official local status-line bridge, and an isolated session-settings file; it never reads, copies, or edits Claude's user settings. It preserves CLAUDE.md and creates a dated backup before appending its delimited agreement. Separate 5-hour and weekly thresholds apply. " +
        "Live usage arrives through Anthropic's status line, which runs only in a Claude Code CLI or IDE terminal session. The Claude Desktop Code tab was measured not to run status-line commands, so a Desktop-only setup shows Unknown rather than a stale or invented number. The terminal steps below set that up once. " +
        "The resulting plan allowance is shared with consumption from Claude Chat, Desktop, web, mobile, and Claude Code. Claude Code supplies the machine-readable fields after a real response without the status-line command consuming API tokens. Anthropic documents those fields only for Claude.ai Pro/Max subscribers. " +
        "Free Chat-only accounts can be detected but do not expose a supported local percentage, so they remain Unknown instead of being estimated. The owned session file supplies Usage Guard's status line only to the explicitly launched CLI session and leaves any existing user status line untouched; Codex usage is never substituted.";

    public const string ClaudeTerminalSetup =
        "Live usage comes from the official Claude Code CLI status line. The Desktop Code tab alone did not invoke status lines in the tested release.\r\n\r\n" +
        "Step 1. Press Configure AI. Usage Guard installs only its own skill, agreement, bridge, and isolated session-settings file. It does not read, copy, or edit your Claude user settings.\r\n\r\n" +
        "Step 2. If the official Claude Code CLI is not installed, use Anthropic's documented Windows install: winget install Anthropic.ClaudeCode\r\n\r\n" +
        "Step 3. Open a PowerShell terminal and run: claude --settings \"$env:USERPROFILE/.claude/usage-guard/claude-session-settings.json\"\r\n\r\n" +
        "Step 4. Sign in if the official CLI asks, then accept the workspace trust prompt for the folder. Usage Guard never handles that sign-in.\r\n\r\n" +
        "Step 5. Complete one ordinary assistant response. Usage Guard updates from the genuine rate-limit fields attached to that response.\r\n\r\n" +
        "Later status-line events can repeat the session's last data, so Usage Guard does not use a timer to pretend it is a new provider reading. Complete another real Claude Code response when the displayed observation becomes stale.";

    public const string ClaudeChatLimits =
        "Claude Chat uses the same plan pool, so Chat activity changes the percentage shown after the next trusted Claude Code observation. Ordinary Chat has account-wide profile instructions, but no documented local command or phase-boundary hook that can read Usage Guard automatically. Usage Guard therefore does not claim to control normal chats and never edits account-wide Chat profile instructions. This avoids changing behavior for someone sharing the account on another machine. For a long chat, consult the Claude tab manually and tell that chat to finish its current response when SafeWrap is shown.";

    public const string ClaudeAgreement =
        "<!-- BEGIN USAGE GUARD CLAUDE WORKING AGREEMENT -->\r\n" +
        "## Usage Guard for Claude Code\r\n\r\n" +
        "- Before substantive multi-phase work, each new material phase, or additional delegation, consult the installed `claude-usage-guard` skill and obey its configured decision.\r\n" +
        "- Treat the rolling 5-hour and weekly windows independently; the stricter configured decision controls. Never substitute Codex usage.\r\n" +
        "- On SafeWrap or Unknown, finish only the current coherent checkpoint, perform safe cleanup, record a truthful handoff, and start no new phase.\r\n" +
        "- Never interrupt or kill the active task, start a model turn merely to refresh usage, or consume credits automatically.\r\n" +
        "<!-- END USAGE GUARD CLAUDE WORKING AGREEMENT -->";
}

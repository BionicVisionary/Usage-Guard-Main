# Bounded rollback

The current portable/user-scoped rollback resolves the exact application path
from the validated helper-owned locator, stops only that executable, removes the
helper-owned startup value and app directory, and leaves sanitized settings by
default:

Run from this repository:

```powershell
.\scripts\Rollback-User.ps1
```

Add `-RemoveCodexIntegration` only if the optional skill should be removed, and
`-RemoveSanitizedState` only if current helper settings/state should also be
removed. The script never edits global `AGENTS.md`. Exact recorded upgrade
backup paths can be supplied for a bounded restore.

The script validates the locator schema/hash, requests graceful shutdown only
from its exact executable, waits for that PID, and refuses to continue if it
remains. It removes only the helper-owned HKCU startup value and selected app,
skill, or sanitized-state paths. It refuses broad/ambiguous targets and never
force-kills the helper. The installer's output records any exact app, legacy
tool, and skill backup paths that may later be passed back for restoration.

The rollback does not change the official Codex CLI, Codex Desktop, other projects, other skills, unrelated global instructions, browser data, credentials, tasks, services, or reset credits.

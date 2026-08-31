# User-scoped installation

## Shareable package

The Release package is a self-contained Windows x64 ZIP plus an unsigned,
console-free `UsageGuard-Setup-0.003.exe` bootstrapper compiled with the built-in
Windows .NET Framework toolchain. The recipient does not
need a separate .NET runtime or administrator rights. Run the setup executable,
or extract the ZIP and run `Install.cmd`. Use
`Install-With-Codex-Integration.cmd` only to add the optional Codex skill during
installation; Codex and Claude can also be configured later from the popup.
Because 0.003 is not code-signed, recipients should expect an unknown-publisher
warning and compare the SHA-256 published with the release.
Distribute `UsageGuard-Setup-0.003.exe` together with
`UsageGuard-Setup-0.003.exe.sha256`; future in-window updates require both exact
assets on a non-draft immutable GitHub Release. Usage Guard also requires the
public GitHub API's SHA-256 digest for both assets, verifies the checksum file
against its digest, and verifies the installer against both that checksum and
its independent release digest. Recipients need no GitHub account, GitHub CLI,
or credentials; if any immutable metadata or digest proof is unavailable, the
app does not launch the update.

The default destination is
`%LOCALAPPDATA%\Programs\Usage Guard`. A custom user-writable
destination is supported:

```powershell
.\Install-User.ps1 -SourceDirectory .\app `
  -InstallDirectory 'D:\Apps\Usage Guard' `
  -LaunchAfterInstall
```

When launched by Usage Guard for an update, the same installer preselects the
current installation folder through a fixed `--install-directory` argument and
still requires the user to press Install.

Add `-SkillSourceDirectory .\skill -InstallCodexIntegration` only when Codex
task integration is wanted. Installation never edits global `AGENTS.md`.

The installer stages and hashes the app, waits for the exact old PID to exit,
preserves sanitized settings, creates recoverable upgrade backups, and writes a
user-local `installation.json` locator. The Codex skill validates the locator
schema and executable SHA-256 before each launch.

Provider discovery is read-only and allowlisted. It never reads provider auth or
starts a model session. Detection does not imply live quota support.

Every setting, shortcut, sanitized state file, skill, and instruction addition
is current-user and machine-local. Installation does not write any AI account
profile, so another computer sharing the same AI account keeps its own rules.

## Requirements

- Windows 10/11 x64. The shareable package is self-contained; repository builds
  require the .NET 8 SDK/Desktop tooling.
- Official user-scoped Codex CLI `0.149.1` at `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe` with the pinned SHA-256 recorded in the README.
- No administrator rights or UAC.
- Claude live windows require the separately installed official Claude Code CLI
  started with Usage Guard's isolated `--settings` file, plus a real response
  that provides Anthropic's documented status-line fields. Configure never
  reads, copies, backs up, or edits Claude's user settings.
  The tested Desktop Code tab alone does not invoke the bridge. Free
  Chat-only accounts remain Unknown.

## Verified install workflow

Build and test first, then run the repository installer with explicit verified sources:

```powershell
dotnet build .\CodexUsageGuard.sln --configuration Release
& .\tests\CodexUsageGuard.Tests\bin\Release\net8.0-windows\CodexUsageGuard.Tests.exe
.\scripts\Install-User.ps1 `
  -SourceDirectory .\src\CodexUsageGuard\bin\Release\net8.0-windows `
  -InstallDirectory 'D:\Apps\Usage Guard' `
  -SkillSourceDirectory .\.agents\skills\codex-usage-guard `
  -InstallCodexIntegration
```

The installer stages and SHA-256 compares the allowlisted runtime files, asks an existing helper to shut down through its helper-owned event, waits for every exact installed PID to exit, backs up prior app/skill directories when present, then swaps the staged directories into place. A non-empty custom destination is replaceable only when its locator, executable hash, and allowlisted top-level files prove it is the prior Usage Guard installation; unrelated content causes refusal before any move. It refuses replacement if graceful shutdown or an existing rollback backup is unresolved. It never force-kills the helper, enables startup, or edits global instructions.

Installed files:

- chosen install directory, for example
  `D:\Apps\Usage Guard\CodexUsageGuard.exe` (the public package is
  a self-contained single executable);
- `%LOCALAPPDATA%\OpenAI\CodexUsageGuard\installation.json` containing only the
  installed executable path and SHA-256;
- `%USERPROFILE%\.codex\skills\codex-usage-guard\SKILL.md`
- `%USERPROFILE%\.codex\skills\codex-usage-guard\scripts\check_usage.ps1`
- `%USERPROFILE%\.codex\skills\codex-usage-guard\scripts\invoke_guard_process.ps1`

Upgrade backup paths use a unique timestamp suffix and are reported by the
installer. Global instructions are neither installed nor rolled back by this
package.

When enabled in the popup, Launch Together adds only exact current-user
shortcuts beneath `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Usage Guard`.
It does not modify provider shortcuts or install a watcher/service.

An installation made before graceful `--shutdown` support must first be exited
once through its own popup or notification-area Exit action. This one-time
legacy transition is deliberate: forcing the old process would prevent it from
disposing its tray icon.

## Optional startup

Startup remains off after installation. Selecting Start automatically at user sign-in creates only `Usage Guard` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, pointing to the quoted installed executable plus `--background`. Clearing the setting also removes the legacy helper value. No service, scheduled task, UAC, machine-wide registry entry, or hidden updater is used.

## Portable package

The self-contained Release executable, installer/rollback scripts, optional
Codex skill, manifest, and documentation are packaged as a ZIP under the ignored
`artifacts` directory. Its SHA-256 is recorded in `FINAL_EVIDENCE.md`. Update
notifications and user-confirmed in-window installation never auto-trust a
changed provider executable and never silently install.

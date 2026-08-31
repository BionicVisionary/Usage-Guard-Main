# Usage Guard changes

## 0.002 — public updater and publication safety

- Makes the fixed GitHub Releases updater usable without a GitHub account or
  credentials after the repository becomes public.
- Requires a non-draft, non-prerelease immutable release with exactly one
  version-matched installer and checksum asset.
- Verifies the checksum file against GitHub's release-asset SHA-256 digest,
  then verifies the installer against both that checksum and GitHub's separate
  installer digest before asking the user to install.
- Removes the GitHub CLI/login dependency from recipient update checks and
  downloads. Updates remain user-confirmed and never install silently.
- Publishes only the sanitized current source through a replacement root
  history; no open-source licence is added.

## 0.001 — initial approved release

- Clarified the lower threshold as **Critical SafeWrap**: it increases urgency
  inside SafeWrap but never instantly stops, cancels, or kills a task.
- Display the application title as `Usage Guard v.0.001`.
- Replaces separate enabled/disabled monitoring buttons with one readable white
  **Start Monitoring** / red **Stop Monitoring** toggle.
- Places Check now, Start/Stop Monitoring, and Configure AI inside every provider
  tab so Codex and Claude can be monitored independently; app-wide controls stay
  outside the tabs. The stopped toggle now uses the same border as nearby buttons.
- Adds optimized double-buffered scrolling/layout and avoids redundant status
  text updates to improve popup responsiveness.
- Renames the user-facing product to **Usage Guard**.
- Adds safe Codex and Claude Desktop/Code installation discovery while clearly
  distinguishing detection from live-quota capability.
- Adds isolated Codex and Claude tabs with machine-local thresholds, overrides,
  monitoring, notifications, and separate sanitized state.
- Adds privacy-bounded Claude Code preparation for the official CLI. Configure
  installs only Usage Guard-owned assets and an isolated session-settings file;
  it never reads, copies, backs up, or edits Claude's user settings. The user
  starts the official CLI with the displayed `--settings` command. It uses
  Anthropic's documented local status-line `five_hour` and `seven_day` fields,
  requires both windows, and lets the stricter decision control. The tested
  Desktop Code tab does not invoke the bridge by itself, so setup points users
  to Anthropic's official CLI rather than launching a private bundled binary.
- Documents the shared Claude plan pool while keeping ordinary Chat manual:
  free Chat-only accounts have no supported local percentage, and account-wide
  Claude profile instructions are never changed.
- Adds optional helper-owned **Launch Together** Start-menu shortcuts without a
  persistent process watcher or changes to the providers' original shortcuts.
- Adds a self-contained, user-scoped ZIP and unsigned console-free installer plus
  bounded rollback.
- Allows a custom install directory, such as `D:\Apps\Usage Guard`.
- Adds a hash-validated installation locator for the optional Codex skill.
- Allows up to eight seconds for each bounded App Server launch/initialize step
  so the self-contained executable remains reliable on a cold first run; the
  complete one-shot wrapper is still hard-limited to 40 seconds.
- Adds de-duplicated update notifications and an in-window installer against the
  fixed public GitHub Releases channel. It requires one exact setup asset and
  matching `.sha256`, downloads with bounds, verifies the hash, and requires
  official GitHub CLI proof that the file belongs to the exact repository's
  immutable release before asking the user; it never silently installs.
- Refuses to replace a non-empty custom installation folder unless its locator,
  executable hash, and allowlisted contents prove Usage Guard owns it.
- Suppresses App Server diagnostics and filters Claude status-line input down to
  the two quota windows before either boundary reaches helper processing.
- Accepts uniquely named additional Claude quota windows without copying them
  across the process boundary, while still rejecting duplicate, missing,
  malformed, expired, or ambiguous required 5-hour/weekly data.
- Distinguishes a Claude callback that arrived without both supported quota
  windows from a callback that never ran, persisting only a sanitized current
  availability reason. Configure upgrades its owned command in place so Claude
  Code reloads it without exposing status-line input or session data.
- Opens the status popup on a single left-click of the notification-area icon;
  right-click remains the context menu.
- Makes every configuration local to the current Windows user and machine, so
  sharing an AI account does not share another computer's thresholds or rules.
- Adds exact provider-reported 5-hour and weekly reset timestamps plus a
  fail-closed, two-minute-jitter resume recommendation for an optional
  deduplicated one-shot same-task wake-up.
- Adds a locked-down Windows Sandbox QA harness and an explicitly authorized
  single-monitor host QA mode; neither is available as a production simulation.
- Queues Show/Shutdown requests received during startup and makes an immediate
  shutdown requester retry only while another exact same-path helper process is
  still acquiring the single-instance mutex.

Development was verified on `codex/multi-provider-installer-update-ui` before
the user's explicit approval to merge and publish the source to `main`.

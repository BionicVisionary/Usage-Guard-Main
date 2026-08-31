# Feature and release workflow

1. Create a uniquely named `codex/` feature branch from current `master` before
   implementing a new feature.
2. Preserve unrelated work and make coherent, plain-language commits on that
   branch.
3. Update `CHANGELOG.md`, version metadata, tests, security evidence, package
   manifest, and rollback notes.
4. Build and inspect a self-contained candidate package. Record app/package
   SHA-256 and distinguish synthetic tests from live observations.
5. Present the candidate and change description to the user. Do not merge,
   publish, install as the current release, or announce it through the update
   channel until the user explicitly approves.
6. After approval, merge to `main`, rebuild from the approved commit, verify
   hashes/tests again, publish the package and release notes through the real
   configured channel, and only then make it discoverable by **Check for
   updates**.

No release workflow may invent a Git remote, update endpoint, signing identity,
or trust a changed provider executable automatically.

Investigate a new problem independently first. When a symptom recurs or a
route is blocked, consult the repository-root `TROUBLESHOOTING.md`. Improve the
existing entry when stronger verified evidence becomes available instead of
adding a duplicate. Keep shipped user guidance in `docs/TROUBLESHOOTING.md`.

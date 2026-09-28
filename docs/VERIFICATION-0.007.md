# v0.007 local candidate verification — 2026-09-28

This is a branch candidate, not a published update. The installed v0.006 app,
user settings, thresholds, hooks and override were not changed in this pass.

- Release solution build: succeeded with 0 warnings and 0 errors.
- The non-visible `--ui-viewport-test` passed locally without activating a
  host app window.
- Locked-down Windows Sandbox QA: passed. The guest completed 49 synthetic
  checks, eight rendered UI scenarios, a user-scoped test install and rollback,
  and produced 11 screenshots. The supported minimum window size had no
  horizontal scrollbar. The viewport regression invoked the form's
  deactivation/activation handlers, moved the settings scroll, and verified
  restoration. The six provider scroll/repaint exercises took 67–99 ms each
  for 120 positions in this guest run; this is not a host latency benchmark.
- The exact validated Sandbox client was placed within the approved
  non-primary working area (1920,61,1280,984). The host used no input.
  Guest networking, clipboard and vGPU were disabled. A brief initial Sandbox
  splash on the primary display cannot be ruled out.
- Source app SHA-256: `29f858e6fc77c2d2f971fbfd91067a37e620dbdd9089d5e9878764efd5f5e22d`.
  Local ZIP SHA-256: `cd2e83845ee66479571e5254820b3523677e6f07de0c4836b5f758a4e3aa6363`.
  Unsigned local installer SHA-256: `36840fa46f473e69c3ae8f369a3315c8831d9f3574b4ad57e557aa463084d47e`.
  The installer and checksum are under `artifacts/` on the D-drive checkout.

Evidence is in `artifacts/sandbox-evidence-20260928-062059-f9f06697/`.
The UI test does not establish a real human click-away-and-back result on the
installed host app. The full production C# runner was not repeated here; its
previous nine storage-environment failures remain a separate unresolved test
class. No fresh-machine installer test, public release, or installed v0.007
acceptance is claimed. Main and the GitHub release channel require explicit
user approval after branch review.

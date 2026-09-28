# v0.007 verification — 2026-09-28

The isolated build and QA evidence below was gathered before the later local
upgrade and public-release approval. It must not be read as a real host
click-away-and-back test.

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
  Final ZIP SHA-256: `501fdefa100b0b5d205e450520364d9405ca99007e30ee0bfec5a431f7816fa3`.
  Final unsigned installer SHA-256:
  `e5a602a4d554282f6e6605e67707e7214f77884539d071e6a280d02cbda17bd1`.
  The installer checksum matches, its embedded ZIP matches the final ZIP, and
  the archive manifest/app hash matches the installed app binary.

Evidence is in `artifacts/sandbox-evidence-20260928-062059-f9f06697/`.
The UI test does not establish a real human click-away-and-back result on the
installed host app. The full production C# runner was not repeated here; its
previous nine storage-environment failures remain a separate unresolved test
class. No fresh-machine GUI installer or real click-away-and-back acceptance is
claimed.

The approved local upgrade installed the same v0.007 app hash at
`D:\Codex\Apps\Usage Guard`; the ownership locator, one exact background
process, and the old v0.006 backup were verified. Settings and hook-definition
SHA-256 values were unchanged. A fresh Normal decision was subsequently
delivered by Usage Guard. This does not prove that an actual reactivation of
the installed UI keeps the user's scroll position.

# Publication readiness

This document separates the sanitized publication tree from Git history and
external GitHub state. It contains no credentials or raw account data.

## Completed on the publication-sanitization branch

- Replaced the current tree's workstation username and private absolute paths
  with environment variables, generic examples, or descriptive evidence
  placeholders.
- Preserved release hashes, timestamps, test results, and security conclusions.
- Scanned every remote branch and reachable commit for high-confidence GitHub,
  OpenAI, Anthropic, AWS, and private-key patterns without printing candidate
  values. No high-confidence match was found.
- Deleted the remote `claude-work` diagnostic branch after confirming its exact
  commit remains recoverable through local archival references.
- Kept the repository private throughout preparation. Visibility changes only
  after the replacement history, v.0.002 package, and unauthenticated update
  checks pass.

## Approved history replacement

Earlier commits merged into `main`, including the immutable `v0.001` release
tag, still contain workstation-specific absolute paths in documentation. They
do not contain a high-confidence credential match, but making this repository
public would make those historical paths visible through Git history.

The owner explicitly authorized replacing the reachable repository history,
removing the old immutable release, and publishing the sanitized v.0.002 tree
before switching visibility. The replacement root commit is intentionally the
only public history; local archival branches remain outside the remote.

The repository currently has no open-source license file. Public visibility
without a license permits viewing and downloading through GitHub but does not
grant general reuse rights. Adding an open-source license is a separate owner
decision and is not inferred by this sanitation work.

## Public update-channel acceptance — passed for v.0.003

The following were verified after the sanitized replacement history was made
public:

- an unauthenticated request to the fixed GitHub latest-release endpoint returned
  the expected immutable v.0.003 release rather than `404`;
- the exact installer and checksum assets download without credentials;
- GitHub reports the release immutable and exposes independent SHA-256 digests
  for both exact assets;
- the downloaded checksum matches its GitHub digest and the downloaded
  installer matches both the checksum and its separate GitHub digest;
- Usage Guard reports the current release as up to date without exposing raw
  response data;
- the repository remains free of diagnostic transcript branches and old remote
  feature branches.

All checks passed. Remote `main` contains only sanitized replacement history,
the repository is public, Release `v0.003` is immutable, and recipients need no
GitHub account or credentials. The repository intentionally still has no
licence file.

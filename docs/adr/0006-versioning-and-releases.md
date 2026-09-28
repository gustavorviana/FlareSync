# ADR 0006 — Versions come from Conventional Commits

## Status
Accepted

## Context
Releases need a version number and ready-to-run Linux and Windows binaries, without editing version numbers by hand.
Commit messages already follow Conventional Commits.

## Decision
- The GitHub Actions workflow `.github/workflows/ci.yml` runs the tests on Linux and Windows for every pull request and
  push.
- On a push to `main`, after the tests pass, `mathieudutour/github-tag-action` computes the next version from the
  commits since the last `v*` tag (dry run): breaking change (`type!:` or `BREAKING CHANGE:`) → major, `feat` → minor,
  `fix`/`perf` → patch. Other types (`docs`, `chore`, `test`, `refactor`...) do not produce a release.
- The version is passed to `dotnet publish -p:Version=...` for `linux-x64` (`.tar.gz`, with the systemd unit) and
  `win-x64` (`.zip`).
- Only when both builds succeed is the tag `vX.Y.Z` created, together with a GitHub release that carries the archives
  and the changelog.

## Consequences
- No version number is stored in the repository; `flaresync --version` and the DynDNS2 User-Agent show the released
  version. Local builds report `1.0.0`.
- A failed build never leaves a tag behind, so the next push computes the same version again.
- Commit messages decide the version: a wrong type means a wrong version.

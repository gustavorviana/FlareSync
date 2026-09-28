# ADR 0004 — Configuration beside the executable, one file per provider

## Status
Accepted

## Context
FlareSync must be easy to deploy and back up, and modules must own their configuration.

## Decision
```
config/                 (next to the executable; override with --config-dir or FLARESYNC_CONFIG_DIR)
  flaresync.json        global settings: interval, IP sources, custom DNS
  secret.key            master key (only when not supplied by systemd credential / env var)
  state.json            last applied address per record
  providers/
    <provider>.json     everything for that provider: one credential + its records
```
- One provider file holds exactly one credential and all records served by it.
- The files are managed through CLI commands. Every component re-reads the files when it needs them (no caching), so
  changes made by the CLI reach the running service on its next cycle without a restart or file watcher.

## Alternatives considered
- `%ProgramData%` / `/etc/flaresync`: more conventional, less portable.
- One file per credential or per record: more flexible, but multiple credentials per provider were not required.

## Consequences
- The executable's directory must be writable by the service user (or `--config-dir` must be used).

# FlareSync

Modular dynamic DNS client (.NET 10). Requirements live in `docs/prd/`, decisions in `docs/adr/` — read them before
changing behavior, and update them when requirements change.

## Layout
- `src/FlareSync.Core` — abstractions (`IProviderModule`, `IDnsProvider`, `IIpResolver`, `IHostResolver`), neutral
  command model + `CommandCatalog` (`Commands/`), built-in commands, config, secrets, IP detection, sync orchestration.
- `src/FlareSync.Providers.Cloudflare` — Cloudflare module.
- `src/FlareSync.Cli` — `flaresync` executable: System.CommandLine adapter, `run` (Generic Host + `SyncWorker`).
- `tests/FlareSync.Tests` — xUnit; tests carry `[Trait("Req", "FR-xxx")]` pointing at PRD requirements.

## Rules
- All user-visible text (help, prompts, errors, logs), code comments and docs are in **English**.
- Only `FlareSync.Cli` references `System.CommandLine`. Core and provider modules use the neutral command model.
- Provider modules reference only `FlareSync.Core`.
- All of a provider's configuration (one credential + its records) lives in `config/providers/<name>.json`;
  secrets are encrypted with `ISecretProtector`.
- Configuration is re-read on each use (no caching) so CLI changes reach the running service.
- Throw `FlareSyncException` for user-facing errors; the CLI prints its message without a stack trace.

## Commands
```sh
dotnet build
dotnet test
dotnet publish src/FlareSync.Cli -c Release -r linux-x64 -o out
```

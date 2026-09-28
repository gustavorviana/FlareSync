# PRD 0004 — Module system

## Problem
FlareSync must support new DNS providers in the future without changes to the core.

## Functional requirements
- **FR-300** A provider is a module implementing `IProviderModule` (in `FlareSync.Core`). A module:
  - has a unique lowercase `Name`, which is also its command group and the name of its configuration file
    (`config/providers/<name>.json`);
  - registers its services (`ConfigureServices`), including an `IDnsProvider` whose `Name` equals the module name;
  - registers its commands in the central `CommandCatalog` (`RegisterCommands`).
- **FR-305** An `IDnsProvider` returns the DNS targets it manages (`GetTargetsAsync`, read from its configuration file on
  every call) and upserts a record for one address family (`UpsertAsync`).
- **FR-301** Modules depend only on `FlareSync.Core`. They never reference `System.CommandLine` or the CLI project.
- **FR-302** Commands are described with the neutral model in `FlareSync.Core.Commands`
  (`CommandDefinition`, `ArgumentDefinition<T>`, `OptionDefinition<T>`, `CommandContext`, `ICommandConsole`).
- **FR-303** `CommandCatalog` rejects duplicate command names/aliases and invalid definitions at start-up.
- **FR-304** Enabling a module in the executable is a single line in `Program.cs`.

## Adding a provider (checklist)
1. Create `src/FlareSync.Providers.<Name>` referencing only `FlareSync.Core`.
2. Implement `IDnsProvider` and `IProviderModule`.
3. Store all provider configuration (credential + records) in `providers/<name>.json` through `JsonConfigStore`,
   encrypting secrets with `ISecretProtector`.
4. Describe the commands with `CommandDefinition`.
5. Add the module in `src/FlareSync.Cli/Program.cs`.

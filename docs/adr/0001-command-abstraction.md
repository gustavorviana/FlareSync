# ADR 0001 — Neutral command model, System.CommandLine only in the CLI

## Status
Accepted

## Context
FlareSync is operated from the command line and provider modules contribute their own commands
(e.g. `cloudflare login`). We want to use Microsoft's `System.CommandLine`, but we do not want provider modules
to be coupled to a specific command-line library.

## Decision
- `FlareSync.Core.Commands` defines a library-agnostic model: `CommandDefinition`, `ArgumentDefinition<T>`,
  `OptionDefinition<T>`, `CommandContext`, `IParsedValues`, `ICommandConsole`.
- `CommandCatalog` is the central registry that receives built-in commands and module commands and validates them.
- `FlareSync.Cli` is the only project referencing `System.CommandLine`. `SystemCommandLineAdapter` translates the
  catalog into `System.CommandLine` symbols. Generic symbols (`Option<T>`, `Argument<T>`) are created through a
  visitor (`ICommandDefinitionVisitor<TResult>`), avoiding reflection so trimming/single-file publish works.

## Alternatives considered
- Modules returning `System.CommandLine.Command` directly: simplest, but couples every module to the library.
- Reflection-based adapter: less code, but fragile under trimming.

## Consequences
- Command handlers are testable with fake `IParsedValues` / `ICommandConsole`.
- Replacing the command-line library only requires rewriting the adapter.
- Features of `System.CommandLine` not represented in the model are unavailable to modules until the model grows.

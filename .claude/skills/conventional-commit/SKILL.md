---
name: conventional-commit
description: Write a short Conventional Commits message for the current changes; commit only when explicitly asked. Use when the user asks for a commit message, a commit comment, or to commit.
---

# Conventional commit

Write a simple, objective commit message following [Conventional Commits](https://www.conventionalcommits.org/).

**Default: return only the message.** Do not run `git add`, `git commit` or any other command that changes the
repository unless the user explicitly asks to commit in the same request (e.g. "commit", "faça o commit"). Asking for
a message, a "commit comment" or invoking this skill without instructions means: reply with the message and stop.

## Steps

1. Look at what will be committed (read-only): `git status --short`, `git diff --staged` (and `git diff` for unstaged
   changes).
2. Write the message using the rules below.
3. Reply with the message in a code block, nothing else to do.
4. Only if the user explicitly asked to commit: run `git commit` with that message. Stage with `git add -A` only if
   they want every change included.

## Format

```
<type>[(scope)][!]: <summary>

[body: 1-3 short lines, only if the summary is not enough]
```

- **Types:** `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `build`, `ci`, `perf`, `style`.
- **Scope** (optional): the affected area, e.g. `cloudflare`, `dyndns2`, `core`, `cli`, `docs`.
- **Summary:** English, imperative mood ("add", not "added"), lower case, no trailing period, at most ~72 characters.
  Say *what* changes for the user, not how.
- **Body:** optional, short and objective. No lists of touched files, no test counts, no repetition of the summary.
- **Breaking changes:** mark with `!` after the type/scope **or** with a `BREAKING CHANGE:` footer, never both.
  Prefer `!` and mention what was removed or changed in the body.
- One commit = one purpose. If the changes mix unrelated purposes, suggest splitting them.

## Examples

```
feat!: add --family option to choose IPv4, IPv6 or both per host

Replaces --no-ipv4/--no-ipv6 on add and --ipv4/--ipv6 on set.
New hosts default to IPv4 only.
```

```
fix(dyndns2): keep host block after a failed login
```

```
docs: add Windows service installation steps
```

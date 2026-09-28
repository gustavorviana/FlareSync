# PRD 0002 — Cloudflare provider

## Problem
The first DNS provider FlareSync must support is Cloudflare. Users need to authorize FlareSync against their
Cloudflare account and choose which host names to keep updated, entirely from the command line.

## Goals
- Update `A` / `AAAA` records through the Cloudflare API v4.
- Make obtaining a correctly scoped API token as easy as clicking a link and pasting the token back.
- Keep everything related to Cloudflare in a single configuration file.

## Non-goals
- OAuth-based login (see ADR 0003).
- Multiple Cloudflare credentials at the same time (one provider file = one credential).
- Managing Cloudflare settings other than DNS records.

## Functional requirements

### Configuration
- **FR-100** All Cloudflare configuration lives in `config/providers/cloudflare.json`: one API token plus the list
  of records served by that token.
- **FR-101** The API token is stored encrypted (see ADR 0002).
- **FR-102** Each record stores: host name, zone id, IPv4 enabled, IPv6 enabled, proxied flag, TTL (1 = automatic).

### Login
- **FR-110** `cloudflare login` prints a Cloudflare dashboard link that opens the "create API token" form pre-filled
  with the name `FlareSync` and the permissions `Zone:Read` and `DNS:Edit` on all zones.
- **FR-111** When a graphical environment is available the link is opened in the default browser, unless
  `--no-browser` is given.
- **FR-112** The user pastes the token; input is not echoed.
- **FR-113** `--token <value>` skips the interactive flow (automation).
- **FR-114** The token is verified (`/user/tokens/verify`) and at least one zone must be visible before it is saved.
- **FR-115** `cloudflare logout` removes the token but keeps the records.

### Records
- **FR-120** `cloudflare zones` lists the zones visible to the token.
- **FR-121** `cloudflare add <hostname>` finds the zone as the longest zone name that is a suffix of the host name
  and stores its id. Options: `--family ipv4|ipv6|both` (default `ipv4`), `--proxied`, `--ttl`, `--create`.
- **FR-122** `--create` creates the DNS record immediately when it does not exist.
- **FR-123** `cloudflare list`, `cloudflare remove <hostname>`, `cloudflare set <hostname> [--family F] [--proxied B] [--ttl N]`
  manage records.

### Sync
- **FR-130** Upsert: look up the record by type and name; no-op when content matches, `PATCH` when it differs,
  `POST` when it does not exist.
- **FR-131** API errors reported in the Cloudflare response envelope are surfaced as a failed sync result.

## Acceptance criteria
- After `login` and `add home.example.com`, `sync` creates/updates the record visible in the dashboard.
- `cloudflare.json` never contains the token in clear text.

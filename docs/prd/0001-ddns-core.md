# PRD 0001 — DDNS Core

## Problem
The host running FlareSync has a public IP address that changes over time (residential ISP, VPN exit, etc.).
DNS records pointing to this host must follow the current address without depending on third-party DDNS
services such as No-IP.

## Goals
- Detect the current public IPv4 and IPv6 addresses of the host.
- Keep an arbitrary number of DNS records, possibly spread across different DNS providers, up to date.
- Run unattended as a long-lived service (primarily Linux/systemd; Windows supported).
- Be modular: new DNS providers can be added without touching the core.

## Non-goals
- Hosting a DNS server.
- Managing DNS record types other than `A` and `AAAA`.
- A graphical user interface.

## Functional requirements

### IP detection
- **FR-001** The public IP is obtained by issuing an HTTP GET to a configurable list of IP sources.
  The default IPv4 source is `https://ipv4.am.i.mullvad.net/ip`, the default IPv6 source is
  `https://ipv6.am.i.mullvad.net/ip`.
- **FR-002** IP sources are configured per address family (IPv4 / IPv6) as an ordered list. Sources are tried in
  order; the first valid answer wins (fallback).
- **FR-003** Requests for a family are forced over that family (IPv4 sources are contacted over IPv4 only,
  IPv6 sources over IPv6 only), so dual-stack source hosts return the address of the requested family.
- **FR-004** A source answer is accepted only if it parses as an IP address of the requested family and is a
  public address (not private, loopback, link-local, unique-local, or unspecified).
- **FR-005** A source may return plain text (default) or JSON; for JSON the field holding the address is configurable.
- **FR-006** An optional, global list of DNS servers can be configured to resolve the host names of IP sources.
  When empty, the operating system resolver is used. When set, IPv4 sources are resolved with `A` queries and IPv6
  sources with `AAAA` queries against the configured servers (which may themselves be reached over IPv4).
- **FR-007** The custom DNS servers apply only to IP sources, not to provider APIs.

### Synchronization
- **FR-010** A sync cycle resolves each address family at most once and only if at least one record needs it.
- **FR-011** For every record and every enabled family, the provider is asked to upsert the record only when the
  detected address differs from the last address successfully applied to that record (persisted state).
- **FR-012** A failure while updating one record does not prevent the other records from being updated.
- **FR-013** If an address family cannot be detected, records needing it are skipped with a warning.
- **FR-014** A forced sync ignores the persisted state and asks providers to upsert every record.
- **FR-015** The persisted state (`state.json`) stores, per provider/record/family, the last applied address and timestamp.

### Service mode
- **FR-020** In service mode a sync cycle runs at start-up and then at a configurable interval (default 5 minutes).
- **FR-021** Configuration changes made through the CLI while the service runs are picked up without restarting.
- **FR-022** Service mode integrates with systemd (notify/journal) and Windows Services.

## Non-functional requirements
- **NFR-001** .NET 10, self-contained single-file publish for `linux-x64` must work (no reflection-dependent
  code paths in the command layer).
- **NFR-002** All user-visible text, logs, and documentation are in English.
- **NFR-003** HTTP calls use timeouts and retries with back-off.
- **NFR-004** Secrets are never written to logs.

## Acceptance criteria
- With two IPv4 sources where the first fails, the second source's address is used.
- A record whose address did not change produces no provider API call on the next cycle.
- On a network without a working IPv6 DNS server, configuring `1.1.1.1` as custom DNS allows IPv6 detection.

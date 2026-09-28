# PRD 0005 — DynDNS2 provider (No-IP)

## Problem
Many dynamic DNS services, No-IP among them, expose the DynDNS2 update protocol instead of a record API. FlareSync
must support No-IP and, with the same code, any other DynDNS2-compatible service.

References: https://www.noip.com/integrate/request, https://www.noip.com/integrate/response

## Goals
- A generic DynDNS2 module with presets. Initial presets:
  - `noip` — `https://dynupdate.no-ip.com/nic/update`;
  - `dyndns2` — any compatible service, update URL given at login.
- Follow the protocol rules strictly, so the account is never flagged for abuse.

## Non-goals
- No-IP groups, the `offline` parameter, hostname confirmation of free No-IP accounts.
- Several credentials for the same preset (one provider file = one credential).

## Functional requirements

### Configuration
- **FR-400** Each preset stores everything in `config/providers/<preset>.json`: update URL (only when not the preset
  default), username, password (encrypted), User-Agent contact, optional User-Agent product, block state, retry time
  and the hosts (name, IPv4 enabled, IPv6 enabled, host block state).
- **FR-401** Adding a preset for another service is one `DynDns2Preset` plus one line in `Program.cs`.

### Update request
- **FR-410** One `GET {server}?hostname={host}&myip={ipv4},{ipv6}` per host, containing every enabled and detected
  address, IPv4 first, even if only one family changed (so the other one is never left undefined).
- **FR-411** Requests use Basic authentication and the User-Agent `<product> <contact>`; the product defaults to
  `FlareSync FlareSync/<os>-<version>` and the contact e-mail is mandatory.
- **FR-412** Updates are only sent when an address changed since the last successful update (core state), or when
  the user forces it. The HTTP client does not retry automatically.

### Responses
- **FR-420** The first line of the body is interpreted regardless of the HTTP status: `good` → updated, `nochg` →
  unchanged.
- **FR-421** `badauth`, `badagent`, `!donator`, `abuse` block every host of the preset. No request is sent until the
  user runs `login` or `unblock`.
- **FR-422** `nohost`, `notfqdn`, `numhost` block that host only, until `set`, `unblock <host>` (or removal).
- **FR-423** `911`, `dnserr`: no request for that preset during the next 30 minutes.
- **FR-424** While blocked or waiting, each sync reports the host as failed with the reason and the command that
  releases it.

### Commands (`flaresync <preset> ...`)
- **FR-430** `login [--username] [--password] [--contact] [--user-agent] [--server]` asks for missing values
  (password without echo), clears blocks and the retry time. `--user-agent default` restores the default product.
  Because DynDNS2 has no read-only call, credentials are validated on the first update.
- **FR-431** The generic preset requires `--server`.
- **FR-432** `logout`, `add <host> [--family ipv4|ipv6|both] [--create]` (default `ipv4`), `list`, `set <host> [--family F]`,
  `remove <host>`, `unblock [host]`.

## Acceptance criteria
- After `noip login` and `noip add home.ddns.net --create`, the No-IP host points to the detected address.
- A wrong password produces exactly one request; later syncs report the block without contacting No-IP.

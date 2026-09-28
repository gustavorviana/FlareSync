# ADR 0005 — Providers update one target at a time

## Status
Accepted (replaces the per-family `UpsertAsync` of the first version)

## Context
`IDnsProvider.UpsertAsync(target, family, address)` updated one address family per call. That fits Cloudflare, where
`A` and `AAAA` are independent records, but not DynDNS2 (No-IP): one request carries both addresses
(`myip=v4,v6`). Two requests per host double the traffic and risk `nochg` answers, which No-IP may treat as abuse,
and sending only one family leaves the behaviour of the other one undefined.

## Decision
```csharp
public sealed record DnsUpdate(
    DnsTarget Target,
    IReadOnlyDictionary<IpFamily, IPAddress> Addresses,  // every enabled and detected family
    IReadOnlySet<IpFamily> Changed);                     // families whose address differs from the state

Task<IReadOnlyDictionary<IpFamily, SyncResult>> UpdateAsync(DnsUpdate update, CancellationToken ct);
```
- The orchestrator calls the provider once per target, only when `Changed` is not empty.
- Results are per changed family; state and logs stay per family.
- Cloudflare only touches the families in `Changed`; DynDNS2 sends every address in `Addresses`.

## Consequences
- Providers decide how to batch families.
- An exception from `UpdateAsync` fails every changed family of that target.

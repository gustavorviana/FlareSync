# ADR 0003 — Token template link instead of OAuth login

## Status
Accepted

## Context
A login experience similar to `aws sso login` (click a link, authenticate, done) was desired.

## Decision
`cloudflare login` prints (and opens, when possible) a Cloudflare dashboard link that pre-fills the "create API token"
form with the name `FlareSync` and the permissions `Zone:Read` + `DNS:Edit`. The user creates the token and pastes it
into the terminal. The token is verified and stored encrypted.

## Alternatives considered
- **OAuth authorization code / device flow**: Cloudflare does not offer OAuth client registration for third-party
  applications. `wrangler login` works because Wrangler is Cloudflare's own client; reusing its client id would be
  impersonation and is not acceptable.
- OAuth tokens also expire and require refresh handling, which complicates an unattended daemon.

## Consequences
- One manual step (copy/paste) remains.
- The permission group keys used in the template link must match Cloudflare's current names; they are isolated in
  `CloudflareTokenTemplate`.

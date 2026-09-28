# ADR 0002 — Secret encryption

## Status
Accepted

## Context
Provider credentials (e.g. the Cloudflare API token) are stored in the provider configuration file. FlareSync runs
mainly on Linux, where Windows DPAPI is not available.

## Decision
- Secrets are encrypted with AES-256-GCM (`System.Security.Cryptography.AesGcm`).
  Format: `enc:v1:` + base64(nonce[12] | ciphertext | tag[16]).
- The 32-byte master key is loaded from the first available source:
  1. systemd credential `$CREDENTIALS_DIRECTORY/flaresync-key` (`LoadCredentialEncrypted=`);
  2. environment variable `FLARESYNC_MASTER_KEY` (base64);
  3. file `config/secret.key` (created by `init`, mode `600`).
- Configuration files holding secrets are written with mode `600` on Unix.

## Threat model (honest)
- With source 3 the key sits next to the encrypted data. This protects against leaking the provider file alone
  (backups, copies, accidental commits), not against someone who can read the whole configuration directory.
- With source 1 (`systemd-creds encrypt`, optionally bound to the TPM) the key is protected at rest; this is the
  recommended production setup and is documented in the README.

## Alternatives considered
- ASP.NET Core Data Protection: heavier dependency, same key-at-rest problem on Linux.
- Plain text: rejected by requirement.

## Consequences
- Losing the master key makes stored tokens unreadable; the user must run `login` again.

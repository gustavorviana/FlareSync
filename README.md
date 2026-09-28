# FlareSync

A modular dynamic DNS client. FlareSync detects the public IPv4/IPv6 address of the host and keeps DNS records
up to date at your DNS provider. The first supported provider is **Cloudflare**.

- Multiple host names, IPv4 (`A`) and IPv6 (`AAAA`).
- Configurable IP sources with fallback (default: `am.i.mullvad.net`).
- Optional custom DNS servers to resolve the IP sources (useful when the local network has no IPv6 DNS).
- Configured entirely from the command line; the service picks up changes without a restart.
- API tokens stored encrypted (AES-256-GCM).
- Runs as a systemd service (or Windows Service).

Requirements and design decisions are documented in [`docs/`](docs/).

## Build

```sh
dotnet build
dotnet test

# Self-contained single-file binary for Linux
dotnet publish src/FlareSync.Cli -c Release -r linux-x64 -o out
```

## Quick start

```sh
./flaresync init                        # creates ./config next to the executable
./flaresync ip                          # check the detected addresses
./flaresync cloudflare login            # prints a link that pre-fills the token form, then paste the token
./flaresync cloudflare add home.example.com
./flaresync sync                        # one cycle
./flaresync run                         # service mode
```

The configuration directory defaults to `config/` next to the executable. Override it with `--config-dir <dir>` or the
`FLARESYNC_CONFIG_DIR` environment variable.

```
config/
  flaresync.json          interval, IP sources, custom DNS servers
  secret.key              master key (unless supplied another way, see below)
  state.json              last address applied to each record
  providers/
    cloudflare.json       Cloudflare token (encrypted) + records
```

## Commands

| Command | Description |
|---|---|
| `init` | Create the configuration directory, default settings and master key |
| `run` | Service mode: sync at start-up and then every interval |
| `sync [--force]` | Run one sync cycle (`--force` updates records even if the address did not change) |
| `ip` | Show the detected public addresses |
| `status` | Show every record with the last applied address |
| `interval [value]` | Show or set the interval (`30s`, `5m`, `1h`, `00:05:00`) |
| `ip-source list` | List IP sources in fallback order |
| `ip-source add <url> --family ipv4\|ipv6 [--position N] [--json-field F]` | Add an IP source |
| `ip-source remove <url> [--family ...]` | Remove an IP source |
| `dns list \| add <server> \| remove <server> \| clear` | Custom DNS servers for IP sources (`1.1.1.1`, `[2606:4700:4700::1111]:53`) |
| `dns test [host]` | Resolve the IP source hosts with the configured servers |
| `cloudflare login [--token T] [--no-browser]` | Store a Cloudflare API token |
| `cloudflare logout` | Remove the token |
| `cloudflare zones` | List zones accessible with the token |
| `cloudflare add <hostname> [--no-ipv4] [--no-ipv6] [--proxied] [--ttl N] [--create]` | Manage a host name |
| `cloudflare list` | List managed host names |
| `cloudflare set <hostname> [--ipv4 B] [--ipv6 B] [--proxied B] [--ttl N]` | Change a host name |
| `cloudflare remove <hostname>` | Stop managing a host name (the DNS record is kept) |

Global options: `--config-dir <dir>`, `-v|--verbose`, `-h|--help`.

## Cloudflare login

`flaresync cloudflare login` prints a dashboard link that opens the "Create API token" form pre-filled with the name
`FlareSync` and the permissions **Zone → Zone → Read** and **Zone → DNS → Edit**. You may restrict the token to specific
zones before creating it. Paste the token into the terminal (input is hidden); FlareSync verifies it and stores it
encrypted in `providers/cloudflare.json`.

OAuth-style login is not possible: Cloudflare does not offer OAuth client registration for third-party applications
(see [ADR 0003](docs/adr/0003-no-oauth-login.md)).

## Install as a systemd service

```sh
sudo useradd --system --home /opt/flaresync --shell /usr/sbin/nologin flaresync
sudo mkdir -p /opt/flaresync && sudo cp out/flaresync /opt/flaresync/
sudo chown -R flaresync:flaresync /opt/flaresync

sudo -u flaresync /opt/flaresync/flaresync init
sudo -u flaresync /opt/flaresync/flaresync cloudflare login
sudo -u flaresync /opt/flaresync/flaresync cloudflare add home.example.com

sudo cp deploy/flaresync.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now flaresync
journalctl -u flaresync -f
```

Always run CLI commands as the service user so the files keep the right owner and permissions (`600`).

## Protecting the master key

By default the master key lives in `config/secret.key`. That protects the provider file on its own (backups, copies),
but not against someone who can read the whole configuration directory
(see [ADR 0002](docs/adr/0002-secret-encryption.md)). For protection at rest, move the key into a systemd encrypted
credential (optionally bound to the TPM):

```sh
sudo mkdir -p /etc/credstore.encrypted
sudo systemd-creds encrypt --name=flaresync-key /opt/flaresync/config/secret.key /etc/credstore.encrypted/flaresync-key
sudo shred -u /opt/flaresync/config/secret.key
```

Then uncomment `LoadCredentialEncrypted=` in the unit and restart the service. CLI commands that read or write the
token now need the key through the environment:

```sh
KEY="$(sudo systemd-creds decrypt /etc/credstore.encrypted/flaresync-key -)"
sudo -u flaresync FLARESYNC_MASTER_KEY="$KEY" /opt/flaresync/flaresync cloudflare login
```

Key sources, in order: systemd credential `flaresync-key`, `FLARESYNC_MASTER_KEY` (base64 of 32 bytes),
`config/secret.key`. If no source exists, commands that store a secret create `config/secret.key`.

## Adding a provider

See [PRD 0004](docs/prd/0004-module-system.md). In short: create `src/FlareSync.Providers.<Name>` referencing only
`FlareSync.Core`, implement `IDnsProvider` and `IProviderModule`, describe commands with the neutral command model and
add the module to `src/FlareSync.Cli/Program.cs`.

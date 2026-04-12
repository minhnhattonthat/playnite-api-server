# Playnite API Server

A [Playnite](https://playnite.link/) generic plugin that exposes your local game library as a bearer-authenticated HTTP REST API, with an interactive Swagger UI at `/docs`. Useful for building companion apps, custom launchers, home dashboards, or Shortcut-style automations that need to read or mutate your Playnite database from outside the Playnite process.

```
┌───────────────────────┐      HTTP       ┌──────────────────────────┐
│   Phone / laptop /    │ ◄─────────────► │   Playnite (desktop)     │
│   custom dashboard    │    port 8083    │   ┌──────────────────┐   │
│                       │                 │   │ PlayniteApiServer│   │
└───────────────────────┘                 │   └──────────────────┘   │
                                          │            │             │
                                          │            ▼             │
                                          │      Game library        │
                                          └──────────────────────────┘
```

## Features

- **REST API over HTTPListener**, runs inside the Playnite process — no external daemon, no NuGet/ASP.NET
- **Games CRUD** with paginated listing, substring search, JSON-merge PATCH, foreign-key validation
- **12 lookup collections** with full CRUD (platforms, genres, companies, features, categories, tags, series, age ratings, regions, sources, completion statuses, emulators)
- **Game media endpoint** that streams icon / cover / background image bytes
- **Health check** with plugin version and per-collection counts
- **Bearer token auth** with constant-time comparison
- **Write-gate toggle** — flip one setting to make the whole API read-only without restarting
- **Swagger UI at `/docs`** — browse every endpoint, Authorize once, Try-it-out against your real library
- **OpenAPI 3.0.3 document at `/openapi.json`** for client code generation or machine-readable docs
- **Loopback-only by default** — nothing is exposed to the network until you explicitly do it

## Installation

The project is .NET Framework 4.6.2 with a classic `.csproj`. No NuGet, no package restore.

**Prerequisites:**

- Windows with Playnite installed
- Visual Studio 2019 / 2022 (any edition) *or* just the [Build Tools](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022) with the ".NET Framework 4.6.2 targeting pack"
- PowerShell 5.1+ (ships with Windows)

**Build and deploy:**

1. Edit `build.ps1` — the `$DeployTarget` variable is hardcoded to `H:\Playnite\Extensions\PlayniteApiServer_<guid>\`. Change it to match your Playnite extensions directory (usually `%APPDATA%\Playnite\Extensions\` for normal installs, or `<Playnite>\Extensions\` for portable installs).
2. Edit the `<HintPath>` entries in `PlayniteApiServer.csproj` for `Playnite.SDK.dll` and `Newtonsoft.Json.dll` — point them at your Playnite install directory.
3. **Close Playnite** (the build script deploys into Playnite's Extensions folder and cannot overwrite the running plugin DLL).
4. Run:

   ```powershell
   ./build.ps1
   ```

5. Start Playnite. Confirm the plugin loads: **top-left menu → Add-ons → Generic → Playnite API Server**.

The plugin auto-generates a 32-character bearer token on first run.

## Configuration

Settings live in **Add-ons → Generic → Playnite API Server → Settings**:

| Setting | Default | Notes |
| --- | --- | --- |
| **Port** | `8083` | Any port 1024–65535. Changing this restarts the listener in place; no Playnite restart needed. |
| **Bearer Token** | auto-generated | Minimum 16 characters. The **Regenerate** button creates a fresh cryptographically random token. |
| **Enable write operations** | on | When off, all non-GET requests return `403 writes_disabled`. Changes apply live. |

**Bind address** is loopback (`127.0.0.1`) and is not in the settings UI. See [Remote access](#remote-access) below for how to expose the service to other devices.

## Usage

### Interactive docs

Open `http://127.0.0.1:8083/docs` in any browser. Click **Authorize**, paste your token, and use **Try it out** on any endpoint.

### curl

```bash
TOKEN='paste-your-token-here'
BASE='http://127.0.0.1:8083'

# Health check
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/health"

# List games (paginated)
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/games?offset=0&limit=20"

# Search by substring
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/games?q=portal"

# Get a game by id
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/games/<guid>"

# Mark a game as favorite
curl -s -X PATCH \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"favorite": true}' \
  "$BASE/games/<guid>"

# Download a game's cover image
curl -s -o cover.jpg -H "Authorization: Bearer $TOKEN" \
  "$BASE/games/<guid>/media/cover"

# List all platforms
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/platforms"
```

### API overview

| Group | Endpoints |
| --- | --- |
| **System** | `GET /health`, `GET /openapi.json`, `GET /docs` |
| **Games** | `GET/POST /games`, `GET/PATCH/DELETE /games/{id}`, `GET /games/{id}/media/{icon|cover|background}` |
| **Lookup collections (×12)** | `GET/POST /{collection}`, `GET/PATCH/DELETE /{collection}/{id}` — where `{collection}` is one of `platforms`, `genres`, `companies`, `features`, `categories`, `tags`, `series`, `ageratings`, `regions`, `sources`, `completionstatuses`, `emulators` |

PATCH on `/games/{id}` accepts a subset of Game fields. The full writable allow-list — and exact types for each — is documented in the Game schema visible at `/docs`. Nested observable collections (`gameActions`, `links`, `roms`) are read-only in v1.

## Security model

- **Loopback-only by default.** The listener binds `127.0.0.1:8083` until you change it.
- **Bearer token required on every endpoint except `/docs`, `/openapi.json`, and the Swagger UI asset files.** Missing or wrong token → `401`. The token is compared in constant time.
- **Write-gate.** `POST`, `PATCH`, and `DELETE` all check the `EnableWrites` setting independently of auth. Flip it off for read-only mode without regenerating the token.
- **Docs are anonymous on purpose.** The whole point of `/docs` is that a browser can load it without fussing over headers. It advertises the `bearerAuth` scheme so the Swagger UI Authorize button lights up. The docs don't leak any data — they describe the API surface, which is already visible to anyone who can reach the port.

## Remote access

The plugin was designed for local use, but you'll often want to reach it from your phone, a second PC, or a home dashboard on your TV. **Do not forward the port directly to the public internet.** The attack surface is small (one bearer token) but the blast radius is large (your entire game library can be mutated and deleted via the API).

The best approach is a private overlay network that authenticates devices independently and routes traffic over an encrypted tunnel. **[Tailscale](https://tailscale.com) is the simplest such option** — it's free for personal use, works on every major OS, and has a native way to expose a localhost service to your other devices without any port binding changes.

### Option A — Tailscale `tailscale serve` (recommended)

This is the cleanest path because it requires **zero plugin reconfiguration** — your Playnite API Server keeps binding to `127.0.0.1:8083` and Tailscale proxies your tailnet-only traffic to it.

1. Install Tailscale on the Playnite machine and sign in: <https://tailscale.com/download>
2. Install Tailscale on your phone / laptop / whatever client will talk to the API. Sign in with the same account.
3. On the Playnite machine, open a shell and run:

   ```powershell
   tailscale serve --bg http://localhost:8083
   ```

   This proxies inbound tailnet HTTPS traffic (on port 443 of the Playnite machine's tailnet name) to `localhost:8083`. Tailscale handles the TLS certificate automatically via Let's Encrypt + your tailnet name.
4. Find your tailnet name: `tailscale status` shows it, or look at the admin console. It'll be something like `playnite-pc.tail-scale.ts.net`.
5. From a client device on the same tailnet, hit: [https://playnite-pc.tail-scale.ts.net/docs](https://playnite-pc.tail-scale.ts.net/docs)

   Everything works exactly as it does on `localhost:8083` — bearer token, Try it out, everything.

**Important:** do NOT use `tailscale funnel`. Funnel publishes the service to the **public internet**, which defeats the point of the tailnet-only scoping and exposes your library to anyone who guesses the hostname. Use `tailscale serve` (tailnet-only), not `funnel` (public).

Tighten further with [Tailscale ACLs](https://tailscale.com/kb/1018/acls) — you can restrict which devices in your tailnet can reach the Playnite machine at all.

### Option B — Direct bind to a non-loopback address

If you want the listener itself to bind to your LAN IP, a Tailscale IP, or `0.0.0.0`, you have to change `BindAddress` manually. The settings UI does not expose it.

1. **Close Playnite.**
2. Find the plugin's saved settings file. Playnite stores plugin config under `%APPDATA%\Playnite\ExtensionsData\PlayniteApiServer_0a96c485-030a-4178-9c6c-6a9098fac2d5\config.json` for standard installs (or `<Playnite>\ExtensionsData\...` for portable installs).
3. Edit the JSON:

   ```json
   {
     "Port": 8083,
     "Token": "...",
     "EnableWrites": true,
     "BindAddress": "100.64.0.5"
   }
   ```

   Use a specific IP (Tailscale interface address, LAN IP, etc.) rather than `0.0.0.0` unless you understand the firewall implications.
4. **Windows URL ACL gotcha.** `HttpListener` on Windows refuses to bind to any address other than `127.0.0.1` unless it's been explicitly allowed. Either run Playnite as Administrator *or* register a URL ACL once from an elevated PowerShell:

   ```powershell
   netsh http add urlacl url=http://+:8083/ user=Everyone
   ```

   (Replace `+` with a specific address to be stricter.)
5. **Windows Firewall.** Add an inbound rule for the port if you want it reachable from the LAN.
6. Start Playnite. Check the plugin started successfully — failures show as a Playnite notification, and common causes are "port in use" and "URL ACL missing".

This is more fragile than Option A because you now have to think about URL ACLs, firewalls, and which interface you bound. **Prefer Option A** unless you have a specific reason not to.

### Option C — Other tunneling tools

- **[ZeroTier](https://www.zerotier.com/)** — similar mesh VPN to Tailscale. Same pattern: install on both sides, use the ZeroTier interface IP as the target, still have to deal with Windows URL ACLs if binding the listener directly (or point ZeroTier routes at localhost).
- **[Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)** — supports private (Zero Trust) and public exposure. Use a private tunnel gated by Cloudflare Access if you want something browser-first without installing a client on every device. Public tunnels have the same caveat as Funnel: **don't expose this API publicly**.
- **SSH port forward** — if you already have OpenSSH Server on the Playnite machine, `ssh -L 8083:127.0.0.1:8083 user@playnite-pc` from any client gives you a loopback forward with zero plugin reconfiguration. Fine for ad-hoc use.
- **[ngrok](https://ngrok.com/)** — publishes to the public internet by default. Same warning as Funnel.

### Safety checklist before exposing the API anywhere

- [ ] **Regenerate the bearer token** after setting up remote access. The generated default is already random, but if you ever pasted it into a screenshot or chat, rotate it.
- [ ] **Turn off "Enable write operations"** if the client devices don't need to modify the library. A stolen read-only token costs much less than a stolen read-write one.
- [ ] **Don't share the token over unencrypted channels.** Use a password manager, a Tailscale-internal note, or a secret-sharing tool.
- [ ] **Do not** put the listener directly on the public internet. Not on a port-forwarded router. Not on a public Cloudflare Tunnel hostname. Not via `tailscale funnel`.
- [ ] **Log the port.** `netstat -ano | findstr :8083` tells you what's actually bound.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| Plugin fails to start, Playnite shows "could not bind to port" | Port in use — change the port in plugin settings, or find the other process with `netstat -ano \| findstr :<port>`. |
| Plugin fails to start, "Access denied" on non-loopback bind | Windows URL ACL missing — run Playnite as admin, or register via `netsh http add urlacl` (see Option B above). |
| `401 unauthorized` on every request | Missing/wrong `Authorization: Bearer <token>` header. Regenerate the token in settings if you've lost it — old tokens are not recoverable. |
| `403 writes_disabled` on POST/PATCH/DELETE | Enable write operations in plugin settings. |
| `/docs` loads but asset files 404 | The plugin DLL didn't fully deploy. Rebuild with `./build.ps1` with Playnite closed. |
| Browser shows cert warning via `tailscale serve` | First-time cert issuance can take ~30 seconds. Refresh. |

## Development

See [`CLAUDE.md`](./CLAUDE.md) for the architectural overview and contribution notes, and [`docs/superpowers/specs/`](./docs/superpowers/specs/) for the design spec that drove the current implementation.

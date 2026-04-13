# Scoped Bearer Tokens — Design Spec

**Date:** 2026-04-13
**Status:** Approved; ready for implementation plan.

## 1. Motivation

Today the plugin authenticates clients with a single shared bearer token
(`PluginSettings.Token`) and gates all write methods behind a single global
flag (`PluginSettings.EnableWrites`). Every caller that holds the token has
the same capabilities: everything, or nothing.

Goals for this change:

- Allow multiple named tokens to coexist, so different clients (e.g. a
  scrobbler script, a read-only dashboard, the owner's personal tooling)
  can each hold their own credential.
- Attach **scopes** to each token so that read-only tokens cannot write.
- Keep the data model open to finer-grained scopes later
  (e.g. `games:playtime:write`) without a settings-schema migration.
- Preserve the existing loopback, bearer-header, live-settings-reload UX.

Explicit non-goals:

- No OAuth2 protocol machinery (`/authorize`, `/token`, redirects, refresh
  tokens, grant flows, client registration). The plugin is loopback-only
  with no third-party client ecosystem — the *scope* model from OAuth2 is
  useful, the *protocol* is not.
- No field-level read filtering (hiding `playtime` from some tokens). This
  is a real but expensive capability that will be revisited if and when a
  concrete privacy use case appears.
- No expirations, soft-disable flags, last-used tracking, or audit log.

## 2. Architecture at a glance

One new POCO (`ApiToken`), one list-shaped field on `PluginSettings`, a
rewritten auth block in `Router.Dispatch`, and a new DataGrid in the
settings view. No new projects, assemblies, or dependencies.

Tokens are plaintext at rest (same trust model as today — Playnite plugin
settings live in the user's AppData), displayed in the UI, and matched
with a constant-time comparison on every request.

Scopes are modeled as a `List<string>`, not a bool. Today the valid values
are `"read"` and `"write"`, with `"write"` implying `"read"`. Route-to-scope
mapping is derived from the HTTP method (`GET`/`HEAD` → `"read"`, every
other method → `"write"`), so no per-route declaration is needed at
registration. Finer scopes can be added later as additive string values.

## 3. Data model

### 3.1 New type — `Settings/ApiToken.cs`

```csharp
public sealed class ApiToken
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public List<string> Scopes { get; set; } = new List<string>();
}
```

- `Name` — optional, free-form label. Not required to be unique. Purely
  for the user's own bookkeeping in the settings UI and (optionally) for
  audit logging. Never sent over the wire.
- `Value` — the bearer string, base64url-encoded from 32 random bytes via
  `TokenGen.NewToken()`. Minimum length 16 enforced at save time.
- `Scopes` — non-empty list of scope strings. Valid values today:
  `"read"`, `"write"`. `"write"` implies `"read"`.

### 3.2 Mutated `PluginSettings`

```csharp
public sealed class PluginSettings
{
    public int Port { get; set; } = 8083;
    public string BindAddress { get; set; } = "127.0.0.1";
    public List<ApiToken> Tokens { get; set; } = new List<ApiToken>();

    public PluginSettings Clone()
    {
        return new PluginSettings
        {
            Port = Port,
            BindAddress = BindAddress,
            Tokens = Tokens.Select(t => new ApiToken
            {
                Name = t.Name,
                Value = t.Value,
                Scopes = new List<string>(t.Scopes),
            }).ToList(),
        };
    }
}
```

`Token` (string) and `EnableWrites` (bool) are **removed**. No migration
code retained — the plugin has a single user, who will re-mint tokens on
first run after upgrade (§5 first-run behavior).

## 4. Auth pipeline changes (`Server/Router.cs`)

The `!methodMatch.AllowAnonymous` branch in `Dispatch` is rewritten:

```csharp
if (!methodMatch.AllowAnonymous)
{
    var provided = ExtractBearerToken(req);
    var token = FindToken(settings.Tokens, provided);
    if (token == null)
    {
        resp.AddHeader("WWW-Authenticate", "Bearer");
        WriteError(resp, 401, "unauthorized", "Missing or invalid bearer token.");
        return;
    }

    var required = RequiredScope(methodMatch.Method);
    if (!Allows(token, required))
    {
        WriteError(resp, 403, "forbidden",
            "Token lacks required scope: " + required + ".");
        return;
    }
}
```

Helpers (new, private static in `Router`):

```csharp
// GET/HEAD → "read"; everything else → "write". Route-level overrides
// would plug in here; none defined today.
private static string RequiredScope(string method)
    => IsReadMethod(method) ? "read" : "write";

// "write" implies "read". For any other required scope, exact match.
private static bool Allows(ApiToken token, string required)
{
    if (required == "read")
        return token.Scopes.Contains("read") || token.Scopes.Contains("write");
    return token.Scopes.Contains(required);
}

// Walk the token list with constant-time value comparison. Returns
// matching ApiToken or null. Short-circuits on empty input to avoid
// matching a blank-valued entry against a missing header.
private static ApiToken FindToken(IReadOnlyList<ApiToken> tokens, string provided)
{
    if (string.IsNullOrEmpty(provided)) return null;
    ApiToken match = null;
    foreach (var t in tokens)
    {
        // No short-circuit on match — uniform timing across list position.
        if (TokenGen.ConstantTimeEquals(provided, t.Value ?? ""))
            match = match ?? t;
    }
    return match;
}
```

Notes:

- `FindToken` walks the whole list even after a match, to keep timing
  uniform. Token counts are single-digit, so the cost is negligible.
- `WWW-Authenticate: Bearer` is sent on 401s, not on 403s — 403 means "we
  know who you are, you just can't do this." Matches RFC 6750.
- Token `Value` is never logged. `Name` is safe to log (and may be used
  later for audit lines like "authenticated as token 'Scrobbler'"), but
  no logging is added in this change.

## 5. Settings UI

### 5.1 `Settings/PluginSettingsViewModel.cs`

- Remove `Token`, `EnableWrites`, `RegenerateTokenCommand`.
- Add `ObservableCollection<ApiTokenRow> Tokens`. `ApiTokenRow` is a new
  internal `INotifyPropertyChanged` wrapper with:
  - `Name` (string, editable)
  - `Value` (string, shown in a monospace selectable TextBox; not
    free-text editable — mutated only by `RegenerateCommand`)
  - `ScopeChoice` (string, `"read"` or `"read+write"`, projects onto an
    underlying `List<string> Scopes`)
  - `RegenerateCommand` — replaces `Value` with a fresh `TokenGen.NewToken()`
  - `DeleteCommand` — removes this row from the parent collection
- Add `AddTokenCommand` — appends a row with
  `Name = ""`, `Value = TokenGen.NewToken()`, `ScopeChoice = "read+write"`.
- `BeginEdit` / `CancelEdit` rebuild the observable collection from the
  `live` clone.
- `EndEdit` projects the collection back to `live.Tokens` and calls
  `SavePluginSettings`.
- `VerifySettings` adds:
  - Every token's `Value` non-empty and ≥16 characters (blocks save).
  - Every token's `Scopes` non-empty (blocks save).
  - Scope strings outside the known set `{"read", "write"}` are accepted
    silently — they are not added to the errors list. Rationale:
    forward-compat with a newer binary that may have written finer
    scopes; the UI only surfaces the two known values via the ComboBox,
    so a user cannot type a bad scope through the UI anyway.
- First-run behavior: ctor after `LoadPluginSettings<PluginSettings>()`
  returns, if `live == null || live.Tokens == null || live.Tokens.Count == 0`,
  mint one `ApiToken { Name = "Default", Value = TokenGen.NewToken(),
  Scopes = ["read", "write"] }` and `SavePluginSettings(live)`.

Port changes still trigger a listener restart in `EndEdit`. Token changes
do not — `Router.Dispatch` calls `settingsAccessor()` on every request.

### 5.2 `Settings/SettingsView.xaml`

Replace the `Bearer Token` TextBox block and the `EnableWrites` CheckBox
with a `DataGrid`:

| Column    | Binding          | Control                                  |
|-----------|------------------|------------------------------------------|
| Name      | `Name`           | editable TextBox                         |
| Scope     | `ScopeChoice`    | ComboBox: `read`, `read+write`           |
| Token     | `Value`          | monospace selectable TextBox (read-only) |
| Regen     | `RegenerateCommand` | Button                                |
| Delete    | `DeleteCommand`  | Button                                   |

Below the grid: an `Add Token` Button bound to `AddTokenCommand`.

The explanatory footer text is updated from the current single-token
phrasing to describe multi-token semantics (each token carries its own
scope; deleting a token revokes it immediately; the server must have at
least one token for any client to authenticate).

The `Port` block above the token grid stays unchanged.

## 6. OpenAPI / Swagger UI

### 6.1 Security scheme

Unchanged: a single `bearerAuth` scheme of type `http`, scheme `bearer`.
OpenAPI 3.0 only allows scope declarations on `oauth2` / `openIdConnect`
schemes, so we can't declare scopes at the scheme level for `http bearer`.
That's acceptable — we're using OAuth2-style scoping, not the OAuth2
protocol.

### 6.2 Per-operation documentation

`OpenApiBuilder` is modified so that for each non-anonymous route it
appends a line to the operation's `description`:

> *Requires `<scope>` scope.*

where `<scope>` is `Router.RequiredScope(method)` (exposed as `internal`
or reimplemented in the builder — implementation detail for the plan).
Today that yields `"read"` for GET/HEAD operations and `"write"` for
everything else. When finer scopes land later, this line updates
automatically.

### 6.3 Swagger UI behavior

Unchanged. The user pastes a token into the Authorize dialog; whether it
can exercise a given operation depends on the scopes of that entry in
settings. A write with a read-only token returns 403, which the UI shows
inline.

## 7. Error semantics

| Situation | Status | Code | Message |
|---|---|---|---|
| No `Authorization` header or wrong scheme | 401 | `unauthorized` | `Missing or invalid bearer token.` |
| Header present but no token matches any entry | 401 | `unauthorized` | `Missing or invalid bearer token.` (identical — no leak) |
| Token matched, scope insufficient | 403 | `forbidden` | `Token lacks required scope: <scope>.` |
| Anonymous route | — | — | skips both checks (docs endpoints only) |
| Unknown path | 404 | `not_found` | unchanged |
| Path matched, method mismatch | 405 | `method_not_allowed` | unchanged |

`WWW-Authenticate: Bearer` is sent on 401, not on 403.

## 8. Verification

No automated test project. After `./build.ps1` deploys and Playnite
launches:

1. **Default token mints on first run.** Open plugin settings, confirm
   one row named `Default`, scope `read+write`, non-empty token value.
2. **Add a second token.** Click `Add Token`, set Name `"Read-Only"`,
   scope `read`. OK the dialog.
3. **Read with read-only token.** `curl -H "Authorization: Bearer <ro>"
   http://127.0.0.1:8083/games` → 200 with paged game list.
4. **Write with read-only token.** `curl -X PATCH -H "Authorization:
   Bearer <ro>" -H "Content-Type: application/json" -d '{"name":"x"}'
   http://127.0.0.1:8083/games/<id>` → 403, body
   `{"error":"forbidden","message":"Token lacks required scope: write."}`.
5. **Write with read+write token.** Same PATCH with the default token →
   200.
6. **Invalid token.** `curl -H "Authorization: Bearer deadbeef"
   http://127.0.0.1:8083/games` → 401 with `WWW-Authenticate: Bearer`
   header and `unauthorized` error code.
7. **Missing header.** `curl http://127.0.0.1:8083/games` → 401 identical
   to (6).
8. **Revocation = delete.** Delete the read-only token from settings, OK
   the dialog, retry (3) → 401 (matches no entry).
9. **Zero tokens.** Delete every token from settings, OK the dialog. Any
   protected route returns 401. `/health`, `/docs`, `/openapi.json`
   still accessible.
10. **Swagger UI.** Open `/docs`, click Authorize, paste a token, Try It
    Out on `GET /games` (success) and on a PATCH (403 with read-only,
    200 with read+write).

## 9. Files touched

- `Settings/ApiToken.cs` — new
- `Settings/PluginSettings.cs` — `Tokens` added, `Token` + `EnableWrites` removed, `Clone` updated
- `Settings/PluginSettingsViewModel.cs` — rewritten token section, new `ApiTokenRow` inner type, new commands, `VerifySettings` updated, first-run mint
- `Settings/SettingsView.xaml` — TextBox/CheckBox block replaced by DataGrid + Add button, footer text updated
- `Server/Router.cs` — auth block rewritten, `RequiredScope` / `Allows` / `FindToken` helpers added
- `Server/OpenApi/OpenApiBuilder.cs` — per-operation `description` appends scope line
- `PlayniteApiServer.csproj` — add the new `ApiToken.cs` to the compile items

No changes to controllers, route registrations, `Route.cs`, or Swagger UI
assets.

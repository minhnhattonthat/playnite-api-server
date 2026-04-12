# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Playnite Generic plugin that runs an HTTP REST API server inside the Playnite process, exposing the user's game library (games + 12 lookup collections) for read/write. Playnite is a Windows desktop game library manager. The plugin loads when Playnite starts and tears down when it stops.

## Build & deploy

```powershell
./build.ps1                    # Release build + deploy to H:\Playnite\Extensions\PlayniteApiServer_<guid>\
./build.ps1 -SkipBuild         # Redeploy last build without rebuilding
./build.ps1 -Configuration Debug
```

From bash on Windows, invoke via `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1` — running `./build.ps1` directly in bash fails because bash tries to interpret the PowerShell script.

The deploy target path (`H:\Playnite\Extensions\...`) is hardcoded in `build.ps1`. The script copies only `PlayniteApiServer.dll`, its `.pdb`, `extension.yaml`, and `icon.png` — **never** `Playnite.SDK.dll` or `Newtonsoft.Json.dll`, which Playnite loads from its own install directory. Duplicating those causes type-identity conflicts at load time.

**Playnite must be closed** when running `build.ps1` — otherwise the deploy step fails with `Access to the path 'PlayniteApiServer.dll' is denied` because Playnite holds a file lock on the loaded plugin DLL. MSBuild itself runs successfully before the deploy step, so build-only verification works while Playnite is open: `MSBuild.exe PlayniteApiServer.csproj /p:Configuration=Release`.

## No tests

There is no automated test suite and no test project. Verification for changes that affect HTTP behavior is done manually via browser + curl against a running Playnite instance — see `docs/superpowers/specs/2026-04-11-swagger-ui-and-openapi-design.md` §8 for the canonical smoke test list. Don't propose adding a test project unless the user asks for it.

## Architecture

### Targeting constraints

- **.NET Framework 4.6.2**, C# 7.3, **classic .csproj** (ToolsVersion 15.0). Not SDK-style. **No NuGet, no `PackageReference`.** Dependencies are bare `<Reference Include="...">` with `HintPath` to DLLs in `H:\Playnite\`.
- This means ASP.NET Core, Swashbuckle, NSwag, and modern DI containers are not available. The HTTP server, routing, and OpenAPI generation are all hand-rolled in `Server/`.
- The plugin targets the current Playnite SDK's `GenericPlugin` base class. `PlayniteApiServerPlugin.OnApplicationStarted` starts the listener; `OnApplicationStopped` tears it down.

### HTTP layer (`Server/`)

Three files you need to understand before touching request handling:

- **`ApiServer.cs`** — owns an `HttpListener` lifecycle, a dedicated accept thread, and a `CountdownEvent` that bounds the shutdown wait to ~2s. Request dispatch is handed to the thread pool. The stop path is deliberate: seed the countdown, wait bounded, then close. Don't wait unbounded — Playnite's shutdown must not be blocked by in-flight requests.
- **`Router.cs`** — dispatch table + pipeline. The flow is `route match → auth → write-gate → handler → exception translation`, in that order. Routes flagged `AllowAnonymous` skip both auth and the write-gate. Settings are re-read on every request (`settingsAccessor()`) so token and `EnableWrites` changes take effect without a listener restart. Unauthenticated requests to unknown paths return 404, not 401 — this is intentional and documented in the design spec.
- **`Route.cs`** — routing data (method/segments/handler/pathTemplate) **and** optional OpenAPI metadata (summary/description/tags/parameters/requestBody/responses/AllowAnonymous). One class carries both because the OpenAPI builder walks the same list the router uses for dispatch; keeping them together avoids a parallel store. The mutability split is explicit: routing properties are `{ get; }`, metadata properties are `{ get; set; }`.

### Route registration and OpenAPI

`PlayniteApiServerPlugin.BuildRouter()` is the single place every route is declared. It uses a fluent metadata builder:

```csharp
router.Add("GET", "/games", games.List)
    .Summary("List games")
    .Tags("games")
    .QueryParam("offset", "integer", "Pagination offset (default 0)")
    .Response(200, "Paged list of games", OpenApiSchemas.Schemas.GamePage);
```

`Router.Add` returns a `RouteBuilder` (in `Server/OpenApi/`). Callers that don't need metadata can ignore the return value — it's legal C#.

After all data routes are registered, `BuildRouter` calls `OpenApiBuilder.Build(router.Routes, ...)` **once**, captures the serialized JSON string in a closure, and registers five anonymous documentation routes (`/openapi.json`, `/docs`, `/swagger-ui.css`, `/swagger-ui-bundle.js`, `/swagger-ui-standalone-preset.js`). The ordering matters: the OpenAPI spec must be built after the data routes (so it sees them) and before the doc routes (so they don't appear in the spec).

### Single source of truth: `GamesController.AllowedPatchFields`

This `Dictionary<string, FieldShape>` is consumed by **two** things:

1. The PATCH validator in `GamesController.Patch` calls `ContainsKey(prop.Name)`.
2. `OpenApiSchemas.Game()` iterates the dictionary to generate the schema's `properties`.

If you add a writable `Game` field, add it here once and both surfaces update. The OpenAPI Game schema has one explicit override: `releaseDate` — the SDK type is a struct, not a string. `OpenApiSchemas.ExplicitOverrides` is a skip-set that keeps the loop from emitting the placeholder; the override line that follows re-adds the real nested-object schema. If you add more struct-typed fields, they go in both places.

### Swagger UI assets

`assets/swagger-ui-dist/` holds vendored Swagger UI 5.32.2 files (`swagger-ui.css`, `swagger-ui-bundle.js`, `swagger-ui-standalone-preset.js`, `index.html`, `VERSION.txt`). The four non-metadata files are embedded in the plugin DLL via `<EmbeddedResource>` with explicit `<LogicalName>` overrides (bare filenames — short, predictable). `SwaggerUiHandler.Resources` constants must match the `<LogicalName>` values exactly; changing one without the other breaks the runtime lookup silently.

To upgrade Swagger UI: replace the four files with new versions from `unpkg.com/swagger-ui-dist@<version>/`, update `VERSION.txt`, and verify the CSS contains no non-`data:` `url()` references (`grep -oE 'url\([^)]+\)' assets/swagger-ui-dist/swagger-ui.css | grep -v 'data:'` should be empty).

### Newtonsoft.Json 10.0 quirks

Playnite bundles Newtonsoft.Json **10.0**. Several newer APIs don't work:

- `JObject.ContainsKey(...)` — use `jobj["key"] == null` to check for missing keys (the indexer returns null, not a `JToken` wrapping null). See `OpenApiBuilder.BuildResponses` for the documented pattern.
- The `[Serializable]` / `ISerializable` handling works the same as modern versions.

### Threading

Request handlers run on thread-pool threads. Playnite's database mutations must happen on the UI thread — every controller that writes (`GamesController`, `LookupController`) marshals through `Dispatcher.Invoke(...)`. If you add a new write path, use the existing `InvokeOnUi` helpers; don't call `db.Games.Add(...)` directly from a handler. UI dispatch can throw `TaskCanceledException` during Playnite shutdown; wrap and translate to `ApiException(503, "Playnite is shutting down.")`.

## Security posture

- **Loopback only** by default (`BindAddress = "127.0.0.1"`). The `PluginSettings` model exposes `BindAddress` but v1 keeps it loopback — if that ever changes, the 401→404/405 dispatch behavior and the docs-route auth carve-out need a security review first.
- **Bearer token** on every non-anonymous route, compared with `TokenGen.ConstantTimeEquals`. Tokens are plugin-settings scalars (`PluginSettings.Token`), not secrets in source. Never log the token.
- **Write-gate**: `PluginSettings.EnableWrites` gates all non-GET requests regardless of token validity. The bearer check runs first, then the write-gate.

## When adding a new route

1. Add the handler in an existing `Controllers/*.cs` (or a new one if you're adding a new resource).
2. Register it in `PlayniteApiServerPlugin.BuildRouter()` with full `.Describes(...)` metadata. This is not optional — the OpenAPI builder walks the route metadata and silently-missing metadata means silently-missing docs.
3. Use `OpenApiSchemas.Schemas.<Name>` constants for `$ref` strings, never raw `#/components/schemas/...` strings.
4. For `PATCH` endpoints, the field must be in the single source of truth (see above).
5. Build and smoke-test via `/docs` in a browser — the "Try it out" button catches most regressions in 30 seconds.

## Commit convention

Short, imperative, lowercase type prefix: `feat:`, `fix:`, `refactor:`, `docs:`, `build:`. One commit per logical unit. The existing commit history uses these consistently; match the style.

# Swagger UI and OpenAPI document for the Playnite API Server

**Status:** approved (design)
**Date:** 2026-04-11
**Author:** Nathan + Claude

## 1. Goal

Expose interactive API documentation for the Playnite API Server plugin so that
users can:

1. Discover every route and its parameters/responses without reading the C#
   source.
2. Try requests live against their running Playnite instance from a browser,
   including operations that require the bearer token.

The deliverable is a single OpenAPI 3.0.3 document at `GET /openapi.json` and a
working Swagger UI at `GET /docs`, both served by the existing
`HttpListener`-based server.

## 2. Constraints

These constraints come from the existing project and are non-negotiable for
this work.

- **.NET Framework 4.6.2, classic .csproj, no NuGet.** Swashbuckle / NSwag
  cannot be used — they require ASP.NET Core or NuGet, neither of which exist
  in this project.
- **Existing custom router.** `Server/Router.cs` does method+path matching
  with simple `{id}` placeholders. The OpenAPI spec must be built against this
  router, not against any new framework.
- **Bearer token required on every route today.** Adding documentation routes
  without a carve-out would make Swagger UI unreachable from a browser.
- **Plugin DLL is the only deployable artefact.** `build.ps1` only copies
  `PlayniteApiServer.dll`, `extension.yaml`, and `icon.png`. Anything Swagger
  UI needs at runtime must travel inside the DLL.
- **Loopback by default.** The `BindAddress` setting defaults to `127.0.0.1`,
  so docs and data both live on the local machine.

## 3. Decisions

The following decisions were settled during brainstorming and are now fixed
inputs to the implementation plan.

| Topic                          | Decision                                                                          |
|--------------------------------|-----------------------------------------------------------------------------------|
| OpenAPI authoring approach     | **Programmatic builder** — metadata declared at route registration time.         |
| Swagger UI delivery            | **Embedded resources** in the plugin DLL.                                        |
| Schema fidelity                | **Targeted** — hand-author six schemas; reuse `AllowedPatchFields` as source.    |
| Docs auth                      | **Anonymous** — `/docs`, the asset routes, and `/openapi.json` skip the bearer.  |
| Operation IDs                  | **Skip** for v1.                                                                 |
| Path parameters                | **Auto-infer** from `{id}` placeholders, with `string` defaults.                 |
| Enum schemas for SDK enums     | **Yes for small enums** (≤5 values); skip larger ones.                           |
| Tests                          | **None** — no test project today; manual smoke test via curl + browser.          |
| 401 → 404 for unknown paths    | **Acceptable** — OpenAPI spec already enumerates routes; bind is loopback.       |
| Swagger UI version             | **Pinned to `5.32.2`** via vendored files in `tools/swagger-ui-dist/`.           |
| Asset URL layout               | **Flat top-level routes** (`/swagger-ui.css`, etc.) — no router wildcard.        |

## 4. Architecture

### 4.1 New files

```
Server/OpenApi/
  OpenApiBuilder.cs        ← walks Router.routes, emits one JObject document
  OpenApiSchemas.cs        ← static factory for the six component schemas
  OpenApiTypes.cs          ← internal POCOs: OpenApiParameter, OpenApiRequestBody, OpenApiResponse
  OpenApiHandler.cs        ← serves the precomputed JSON string
  SwaggerUiHandler.cs      ← serves embedded swagger-ui-dist resources
  RouteBuilder.cs          ← fluent metadata builder returned by Router.Add(...)

assets/swagger-ui-dist/    ← vendored, committed
  swagger-ui.css
  swagger-ui-bundle.js
  swagger-ui-standalone-preset.js
  index.html               ← hand-written wrapper
  VERSION.txt              ← "5.32.2"
```

The vendoring lives at `assets/`, not `tools/`, because `.gitignore` already
contains `/tools` (used to exclude the icon-generation files from
`tools/make_icon.py`). A separate `assets/` directory keeps the swagger
vendoring tracked without rewriting the existing ignore rule.

### 4.2 Modified files

| File                                 | Change                                                                                               |
|--------------------------------------|------------------------------------------------------------------------------------------------------|
| `Server/Router.cs`                    | `Add()` returns a `RouteBuilder`. Dispatch reorders to match-then-auth. Anonymous routes skip auth + write-gate. |
| `Server/Route.cs`                     | Adds `AllowAnonymous`, `Summary`, `Description`, `Tags`, `Parameters`, `RequestBody`, `Responses`, `PathTemplate`. All optional. |
| `Controllers/GamesController.cs`      | `AllowedPatchFields` becomes `Dictionary<string, FieldShape>` keyed by name; values describe JSON shape for the OpenAPI builder. The patch-validator uses `ContainsKey`. |
| `PlayniteApiServerPlugin.cs`          | `BuildRouter()` adds `.Describes(...)`/`.AllowAnonymous()` calls per route. After registration, builds the OpenAPI document and registers five anonymous routes: `/docs`, `/swagger-ui.css`, `/swagger-ui-bundle.js`, `/swagger-ui-standalone-preset.js`, `/openapi.json`. |
| `PlayniteApiServer.csproj`            | Four new `<EmbeddedResource>` items (one per file in `assets/swagger-ui-dist/`) with `<LogicalName>` overrides. Six new `<Compile>` items for the new files. |

### 4.3 Data flow

```
Plugin start
    │
    ▼
BuildRouter()
    ├─ register all data routes with metadata via .Describes(...)
    ├─ build OpenAPI JObject from the route table
    ├─ serialize to a string (capture in closure)
    ├─ register GET /openapi.json                    .AllowAnonymous()
    ├─ register GET /docs                            .AllowAnonymous()
    ├─ register GET /swagger-ui.css                  .AllowAnonymous()
    ├─ register GET /swagger-ui-bundle.js            .AllowAnonymous()
    └─ register GET /swagger-ui-standalone-preset.js .AllowAnonymous()

Browser hits http://127.0.0.1:port/docs
    │
    ▼
Router.Dispatch
    ├─ match route → /docs handler
    ├─ AllowAnonymous = true → skip auth, skip write-gate
    └─ SwaggerUiHandler.Serve(r, "index.html", "text/html; charset=utf-8")
    │
    ▼
Browser parses HTML, requests:
    /swagger-ui.css                  → SwaggerUiHandler.Serve(r, "swagger-ui.css", ...)
    /swagger-ui-bundle.js            → SwaggerUiHandler.Serve(r, "swagger-ui-bundle.js", ...)
    /swagger-ui-standalone-preset.js → SwaggerUiHandler.Serve(r, "swagger-ui-standalone-preset.js", ...)
    /openapi.json                    → OpenApiHandler.Serve(r, capturedJson)
    │
    ▼
Swagger UI renders. User clicks Authorize, pastes token, "Try it out" calls now
include `Authorization: Bearer <token>` and hit the real (auth-required) routes.
```

## 5. Component design

### 5.1 `Route` and `RouteBuilder`

`Route.cs` becomes:

```csharp
internal sealed class Route
{
    public string Method { get; }
    public string[] Segments { get; }
    public Action<RequestContext> Handler { get; }
    public string PathTemplate { get; }      // "/games/{id}", as registered

    // Documentation metadata — populated via RouteBuilder, all optional.
    public bool AllowAnonymous { get; set; }
    public string Summary       { get; set; }
    public string Description   { get; set; }
    public string[] Tags        { get; set; }
    public List<OpenApiParameter> Parameters { get; set; }
    public OpenApiRequestBody RequestBody    { get; set; }
    public List<OpenApiResponse> Responses   { get; set; }

    public Route(string method, string[] segments, Action<RequestContext> handler, string pathTemplate);
}
```

`RouteBuilder` is fluent and returns `this` from every method:

```csharp
internal sealed class RouteBuilder
{
    private readonly Route route;
    public RouteBuilder(Route r) { route = r; }

    public RouteBuilder Summary(string s);
    public RouteBuilder Description(string d);
    public RouteBuilder Tags(params string[] t);
    public RouteBuilder QueryParam(string name, string type, string description, bool required = false);
    public RouteBuilder PathParam(string name, string type, string description); // override only
    public RouteBuilder Body(string schemaRef, string description = null);
    public RouteBuilder Response(int status, string description, string schemaRef = null);
    public RouteBuilder Response(int status, string description, string mediaType, string schemaRef);
    public RouteBuilder AllowAnonymous();
}
```

Path parameters are auto-inferred at OpenAPI build time by parsing
`PathTemplate` for `{name}` segments. Auto-inferred params have
`type: string`, `required: true`, `description: ""`. Calling `PathParam(...)`
overrides the auto-inferred entry by name.

### 5.2 `Router.Dispatch` reorder

The new dispatch flow:

```
1. Walk routes for the first matching path.
   ├─ none → 404
   └─ found
2. Find the route with matching method.
   ├─ none → 405 with Allow header
   └─ found
3. If !route.AllowAnonymous:
     a. Bearer-token check → 401 on miss
     b. Write-gate check (non-GET requires EnableWrites) → 403 on miss
4. Invoke handler.
```

The existing try/catch in `Dispatch` continues to wrap steps 1–4 so handler
exceptions still translate to JSON error responses.

**Behavior changes** (see section 4 of the brainstorming dialogue and the
Constraints section): unauthenticated requests to unknown paths return 404
instead of 401, and unauthenticated requests with a known path but the wrong
method return 405 instead of 401. These are intentional and accepted.

### 5.3 `OpenApiBuilder`

Single public method:

```csharp
internal static class OpenApiBuilder
{
    public static string Build(IReadOnlyList<Route> routes, string title, string version);
}
```

Algorithm:

1. Start with an `info` block, a `servers: [{ url: "/" }]` block, and a
   `components.securitySchemes.bearerAuth` definition (`type: http, scheme: bearer`).
2. Pull every component schema from `OpenApiSchemas` into `components.schemas`.
3. Group routes by `PathTemplate`. For each template, build a path-item object.
4. For each route in a path-item, build an operation object:
   - `tags`, `summary`, `description` from the route metadata
   - Auto-infer path params from `{name}` placeholders, then merge any
     explicit `PathParam` overrides
   - Append explicit `QueryParam` entries
   - Attach the request body if set
   - Attach all declared responses; ensure a `401` response (referencing
     `Error`) is present unless `AllowAnonymous` is true
   - Attach `security: [{ bearerAuth: [] }]` unless `AllowAnonymous` is true
5. Serialize the JObject with `JsonSettings.Default`.

The result is a valid OpenAPI 3.0.3 document. There is no validator in the
build pipeline; we verify by loading it into Swagger UI manually.

### 5.4 `OpenApiSchemas`

Six schemas, all returned as JObject fragments. They are kept in the order
they appear in the spec output (alphabetical):

1. **`Error`** — `{ error: string, message: string }`, both required.
2. **`Game`** — populated by iterating
   `GamesController.AllowedPatchFields` (now a Dictionary&lt;string,
   FieldShape&gt;) plus a fixed read-only header (`id`, `added`, `modified`,
   `lastActivity`, etc.). Relationship arrays use
   `array<string format=uuid>`. Boolean flags use `boolean`. Score fields use
   `integer` nullable.
3. **`GameCreate`** — `{ name: string }`, `name` required.
4. **`GamePage`** — `{ total: integer, offset: integer, limit: integer, items: Game[] }`.
5. **`NamedItem`** — `{ id: string format=uuid, name: string }`.
6. **`Platform`** — `NamedItem` plus `specificationId`, `icon`, `cover`,
   `background` (all `string`, optional).

The other 11 lookup collections reference `NamedItem`. The single richer
schema for `Platform` exists because `Platform` is the lookup type whose extra
fields are most likely to be used by API consumers.

**Enum handling.** `GamesController.AllowedPatchFields` declares JSON shapes
in a single dictionary. For SDK enums with ≤5 values (e.g.
`OverrideInstallState` with `Install/Uninstall/None` or similar), the field
shape includes the enum value list. Enums with more than 5 values are
serialized as plain strings without an `enum:` constraint.

### 5.5 `SwaggerUiHandler` and `OpenApiHandler`

```csharp
internal static class SwaggerUiHandler
{
    // Single source of truth for the embedded-resource names. The csproj
    // <LogicalName> entries and the route registrations both reference these
    // constants — no string duplication.
    public static class Resources
    {
        public const string IndexHtml             = "index.html";
        public const string Css                   = "swagger-ui.css";
        public const string BundleJs              = "swagger-ui-bundle.js";
        public const string StandalonePresetJs    = "swagger-ui-standalone-preset.js";
    }

    public static void Serve(RequestContext r, string resourceName, string contentType);
}

internal static class OpenApiHandler
{
    public static void Serve(RequestContext r, string json);
}
```

`SwaggerUiHandler.Serve` uses `Assembly.GetManifestResourceStream(resourceName)`
where `resourceName` is one of the constants in `SwaggerUiHandler.Resources`.
The .csproj `<LogicalName>` entries use the exact same string values, so the
resource name appears in only one place in source. Missing resource →
`ApiException(404)`.

`OpenApiHandler` writes the precomputed JSON string with
`Content-Type: application/json; charset=utf-8`.

Caching headers and ETags are out of scope. Responses are uncached.

### 5.6 `index.html` wrapper

About 30 lines, hand-written:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>Playnite API Server</title>
  <link rel="stylesheet" href="swagger-ui.css">
  <style>
    body { margin: 0; }
    .topbar { display: none; }   /* hide swagger's URL bar */
  </style>
</head>
<body>
  <div id="swagger-ui"></div>
  <script src="swagger-ui-bundle.js"></script>
  <script src="swagger-ui-standalone-preset.js"></script>
  <script>
    window.ui = SwaggerUIBundle({
      url: '/openapi.json',
      dom_id: '#swagger-ui',
      presets: [
        SwaggerUIBundle.presets.apis,
        SwaggerUIStandalonePreset
      ],
      layout: 'StandaloneLayout',
      persistAuthorization: true
    });
  </script>
</body>
</html>
```

`persistAuthorization: true` lets the user paste their token once per
browser session and have it remembered across page reloads — significantly
nicer UX than the default.

## 6. Out of scope

The following are deliberately NOT part of this work and will not be addressed:

- **Auto-generated client libraries.** Operation IDs are skipped specifically
  to keep that door closed for now.
- **Schema documentation for `GameAction`, `Link`, `GameRom`** and other
  nested observable collections on `Game`. These are read-only in v1 and the
  PATCH validator already excludes them.
- **OPTIONS / CORS.** Swagger UI runs same-origin against the loopback
  listener, so no preflight is needed.
- **ETags, gzip, conditional GETs** for the docs/asset routes.
- **A test project.** None exists; adding one is a separate decision.
- **Externally bundled fonts/images** that some Swagger UI distributions ship
  alongside the core JS/CSS. The vendored set is exactly the four files
  listed in §4.1; if Swagger UI 5.32.2 references additional assets that we
  haven't bundled, the implementation plan will need to include them.

## 7. Risks and open questions

**Risk: Swagger UI 5.32.2 may reference additional asset files.** Some
Swagger UI versions inline-load PNG icons or web fonts. If 5.32.2 does this,
the implementation plan must include those files in the vendored set or
strip the references from the wrapper. Verification step in the
implementation plan: open the served `/docs` page in a browser and check the
network panel for any 404s.

**Risk: `JObject` order is not stable.** Newtonsoft preserves insertion order
in `JObject`, so the spec is deterministic given a deterministic builder.
The implementation must not use any code path that re-sorts properties.

**Risk: enum value enumeration drift.** Hardcoding SDK enum values in
`OpenApiSchemas` couples the spec to a specific Playnite SDK release.
Mitigation: only enumerate enums explicitly chosen for ≤5 values, and document
the SDK version they were taken from in a comment next to the enum. If the
SDK changes the enum, the spec is wrong but the API still works — a
recoverable failure mode.

**Risk: assembly resource name mismatches.** `<LogicalName>` is a stable .NET
Framework feature, but the resource lookup is case-sensitive on some
platforms. Mitigation: both the `<LogicalName>` and the
`SwaggerUiHandler.Serve` callsite use the same string constants, defined in
one place (`SwaggerUiHandler.Resources` static class with
`public const string Css = "swagger-ui.css";` etc.).

## 8. Verification

Manual smoke test, run after `build.ps1` deploys the plugin and Playnite is
restarted:

1. With Playnite running, in a browser: `http://127.0.0.1:<port>/docs`.
   Expected: Swagger UI loads, no 404s in the network panel, all routes
   visible grouped by tag.
2. Click **Authorize**, paste the bearer token from plugin settings, click
   **Authorize** again, close the dialog.
3. Expand `GET /games`, click **Try it out**, click **Execute**. Expected:
   2xx response with the JSON page payload.
4. Expand `POST /games` (with `EnableWrites` ON), Try it out, send
   `{ "name": "Test" }`. Expected: 201 with the new game. Verify the game
   appears in Playnite's main library.
5. Expand `POST /games` (with `EnableWrites` OFF), Try it out. Expected: 403
   `writes_disabled`.
6. Hit `http://127.0.0.1:<port>/openapi.json` directly. Expected: valid JSON,
   no 401.
7. Hit `http://127.0.0.1:<port>/games` with no `Authorization` header.
   Expected: 401 (proves the auth carve-out is scoped to the docs routes).
8. Hit `http://127.0.0.1:<port>/totally-fake-path` with no header.
   Expected: 404 (proves the dispatch reorder).

If any step fails, the implementation is not done.

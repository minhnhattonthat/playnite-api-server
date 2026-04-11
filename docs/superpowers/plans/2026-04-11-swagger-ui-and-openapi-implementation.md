# Swagger UI and OpenAPI document — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an OpenAPI 3.0.3 document at `GET /openapi.json` and an interactive Swagger UI at `GET /docs` to the Playnite API Server plugin, with both routes anonymous so the docs work in a browser without manual auth.

**Architecture:** Programmatic OpenAPI builder driven by metadata declared at route registration time. Swagger UI 5.32.2 ships as four embedded resources inside `PlayniteApiServer.dll`. Routes flagged `AllowAnonymous` skip the bearer-token check and the write-gate. The full design is in `docs/superpowers/specs/2026-04-11-swagger-ui-and-openapi-design.md` — read it before starting.

**Tech Stack:** .NET Framework 4.6.2, C# 7.3, classic .csproj (no NuGet, no PackageReference), Newtonsoft.Json (already referenced), `System.Net.HttpListener`, MSBuild via `build.ps1`.

---

## Required reading before starting

This plan assumes the engineer has read the design spec at
`docs/superpowers/specs/2026-04-11-swagger-ui-and-openapi-design.md` —
particularly:

- §3 (the 11 fixed decisions)
- §5.1–5.6 (component design — Route, RouteBuilder, OpenApiBuilder, the schemas, the handlers, the index.html wrapper)
- §6 (out of scope)
- §7 (risks; risk #1 about extra Swagger UI assets has been **resolved** during plan-writing — the four-file vendoring is sufficient, all CSS image references are inlined as `data:` URIs)
- §8 (manual smoke test, executed in Task 13)

This plan is executable without re-deriving any of the design decisions —
all the surface-area shapes and string constants are baked into the task
steps. If something in this plan disagrees with the spec, the **spec wins**;
stop and escalate.

## File structure

### New files

```
assets/swagger-ui-dist/
  swagger-ui.css                       (vendored from unpkg)
  swagger-ui-bundle.js                 (vendored from unpkg)
  swagger-ui-standalone-preset.js      (vendored from unpkg)
  index.html                           (hand-written, ~30 lines)
  VERSION.txt                          ("5.32.2")

Server/OpenApi/
  OpenApiTypes.cs                      POCOs: OpenApiParameter, OpenApiRequestBody,
                                        OpenApiResponse, FieldShape
  RouteBuilder.cs                      Fluent metadata builder returned by Router.Add()
  OpenApiSchemas.cs                    Six component schemas + a Schemas constants class
  OpenApiBuilder.cs                    Walks Router.routes, emits the OpenAPI JObject
  OpenApiHandler.cs                    Serves the precomputed JSON string
  SwaggerUiHandler.cs                  Serves embedded swagger-ui-dist resources
```

### Modified files

```
Server/Route.cs                        +metadata fields, +PathTemplate
Server/Router.cs                       Add() returns RouteBuilder; Dispatch reordered;
                                        AllowAnonymous bypass; Routes accessor
Controllers/GamesController.cs         AllowedPatchFields → Dictionary<string, FieldShape>;
                                        patch validator uses ContainsKey
PlayniteApiServerPlugin.cs             BuildRouter() — adds .Describes() per route,
                                        builds OpenAPI doc, registers anonymous routes
PlayniteApiServer.csproj               +6 <Compile> items (one per new file in
                                        Server/OpenApi/), +4 <EmbeddedResource> items
                                        for the vendored Swagger UI files
```

## Notes that apply to every task

- **Build verification.** Each task ends with `./build.ps1` (Release config). The script also deploys to `H:\Playnite\Extensions\...`, which is harmless during development — Playnite need not be restarted between tasks. Only Task 13 requires a Playnite restart.
- **Commits.** One commit per task. Use the `git commit -m "..."` form shown in each task. The message convention follows the existing repo: short, imperative, lowercase first word.
- **No TDD.** The spec explicitly excludes a test project (§6 of the spec). Verification is "the build succeeds" for incremental tasks and the §8 manual smoke test for the final task.
- **No placeholders, no abbreviation.** Every code block is the literal C#/HTML/XML the engineer should produce. If a step looks tedious, that is by design — the alternative is drift.
- **Working directory** is `C:\Users\Nathan\Projects\PlayniteApiServer` for every command.

---

## Task 1: Vendor Swagger UI 5.32.2 assets

**Files:**
- Create: `assets/swagger-ui-dist/swagger-ui.css`
- Create: `assets/swagger-ui-dist/swagger-ui-bundle.js`
- Create: `assets/swagger-ui-dist/swagger-ui-standalone-preset.js`
- Create: `assets/swagger-ui-dist/index.html`
- Create: `assets/swagger-ui-dist/VERSION.txt`

The three JS/CSS files are downloaded verbatim from unpkg. The `index.html` and `VERSION.txt` are hand-written.

- [ ] **Step 1: Create the assets directory**

Run:
```bash
mkdir -p assets/swagger-ui-dist
```

- [ ] **Step 2: Download swagger-ui.css**

Run:
```bash
curl -sL -o assets/swagger-ui-dist/swagger-ui.css "https://unpkg.com/swagger-ui-dist@5.32.2/swagger-ui.css"
```

Verify size is roughly 178 KB:
```bash
ls -la assets/swagger-ui-dist/swagger-ui.css
```

- [ ] **Step 3: Download swagger-ui-bundle.js**

Run:
```bash
curl -sL -o assets/swagger-ui-dist/swagger-ui-bundle.js "https://unpkg.com/swagger-ui-dist@5.32.2/swagger-ui-bundle.js"
```

Verify size is roughly 1.5 MB:
```bash
ls -la assets/swagger-ui-dist/swagger-ui-bundle.js
```

- [ ] **Step 4: Download swagger-ui-standalone-preset.js**

Run:
```bash
curl -sL -o assets/swagger-ui-dist/swagger-ui-standalone-preset.js "https://unpkg.com/swagger-ui-dist@5.32.2/swagger-ui-standalone-preset.js"
```

Verify size is roughly 300 KB:
```bash
ls -la assets/swagger-ui-dist/swagger-ui-standalone-preset.js
```

- [ ] **Step 5: Create the index.html wrapper**

Write `assets/swagger-ui-dist/index.html` with **exactly** the following content:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>Playnite API Server</title>
  <link rel="stylesheet" href="swagger-ui.css">
  <style>
    body { margin: 0; }
    .topbar { display: none; }
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

The relative paths (`swagger-ui.css`, `swagger-ui-bundle.js`, `swagger-ui-standalone-preset.js`) resolve in the browser to top-level URLs (`/swagger-ui.css`, etc.) because `/docs` is served from the root. Do not change them — the route registrations in Task 12 depend on this.

- [ ] **Step 6: Create VERSION.txt**

Write `assets/swagger-ui-dist/VERSION.txt` with exactly:

```
5.32.2
```

(One line, trailing newline.)

- [ ] **Step 7: Confirm no external asset references**

Run:
```bash
grep -oE 'url\([^)]+\)' assets/swagger-ui-dist/swagger-ui.css | grep -v 'data:' | head
```

Expected output: empty (no lines printed).

This confirms every CSS image reference is an inlined `data:` URI. If anything else appears, stop and escalate — the four-file vendoring would be insufficient.

- [ ] **Step 8: Commit**

```bash
git add assets/swagger-ui-dist
git commit -m "$(cat <<'EOF'
docs: vendor Swagger UI 5.32.2 assets

Adds the four files served at /docs and the static URLs the index.html
wrapper references. All CSS image refs are inlined data: URIs, so no
additional asset bundling is needed.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Add embedded resource entries to the .csproj

**Files:**
- Modify: `PlayniteApiServer.csproj`

This task wires the four vendored files into the build as embedded resources, with explicit `<LogicalName>` overrides so the runtime resource lookup uses short, predictable names. The build will succeed even though no code consumes the resources yet.

- [ ] **Step 1: Add the EmbeddedResource ItemGroup**

Open `PlayniteApiServer.csproj`. Find the existing `<ItemGroup>` that contains `<None Include="extension.yaml">` near the bottom of the file (around line 85). Insert a **new** `<ItemGroup>` immediately above it (between the existing `<ItemGroup>` of `<Compile>` items and the existing `<ItemGroup>` of `<None>` items):

```xml
  <ItemGroup>
    <EmbeddedResource Include="assets\swagger-ui-dist\swagger-ui.css">
      <LogicalName>swagger-ui.css</LogicalName>
    </EmbeddedResource>
    <EmbeddedResource Include="assets\swagger-ui-dist\swagger-ui-bundle.js">
      <LogicalName>swagger-ui-bundle.js</LogicalName>
    </EmbeddedResource>
    <EmbeddedResource Include="assets\swagger-ui-dist\swagger-ui-standalone-preset.js">
      <LogicalName>swagger-ui-standalone-preset.js</LogicalName>
    </EmbeddedResource>
    <EmbeddedResource Include="assets\swagger-ui-dist\index.html">
      <LogicalName>index.html</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
```

`<LogicalName>` overrides MSBuild's default mangling (which would produce names like `PlayniteApiServer.assets.swagger_ui_dist.swagger_ui.css`). The runtime lookup in Task 11 uses the literal short names.

- [ ] **Step 2: Build**

Run:
```bash
./build.ps1
```

Expected: build succeeds. The resulting `PlayniteApiServer.dll` is now ~1.7 MB larger than before because it embeds the Swagger UI files.

- [ ] **Step 3: Verify the resources are actually embedded**

Run a quick reflection check via PowerShell:

```powershell
[System.Reflection.Assembly]::LoadFile((Resolve-Path .\bin\Release\PlayniteApiServer.dll)).GetManifestResourceNames() | Sort-Object
```

Expected output (order may differ slightly):
```
index.html
swagger-ui-bundle.js
swagger-ui-standalone-preset.js
swagger-ui.css
```

If the output contains long namespaced names instead, the `<LogicalName>` was not applied correctly — recheck Step 1.

- [ ] **Step 4: Commit**

```bash
git add PlayniteApiServer.csproj
git commit -m "$(cat <<'EOF'
build: embed Swagger UI assets in the plugin DLL

Adds four EmbeddedResource entries with LogicalName overrides so the
runtime resource lookup uses short names. The DLL grows ~1.7 MB.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Create OpenApiTypes.cs (POCOs and FieldShape)

**Files:**
- Create: `Server/OpenApi/OpenApiTypes.cs`
- Modify: `PlayniteApiServer.csproj`

Adds the data carriers used by `RouteBuilder`, `OpenApiBuilder`, and (for `FieldShape`) the `GamesController` patch validator. These are intentionally tiny — no logic, just fields.

- [ ] **Step 1: Create the file**

Write `Server/OpenApi/OpenApiTypes.cs` with the following content:

```csharp
using System.Collections.Generic;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Tiny data carriers consumed by the OpenAPI builder. They mirror the
    /// shapes of OpenAPI 3.0.3 objects but are *not* a complete OpenAPI model —
    /// only the parts the builder actually needs are represented here.
    /// </summary>
    internal sealed class OpenApiParameter
    {
        public string Name { get; set; }
        public string In { get; set; }            // "path" or "query"
        public string Description { get; set; }
        public bool Required { get; set; }
        public string Type { get; set; }          // "string", "integer", etc.
        public string Format { get; set; }        // optional, e.g. "uuid"
    }

    internal sealed class OpenApiRequestBody
    {
        public string Description { get; set; }
        public string SchemaRef { get; set; }     // "#/components/schemas/GameCreate"
        public bool Required { get; set; }
    }

    internal sealed class OpenApiResponse
    {
        public int Status { get; set; }
        public string Description { get; set; }
        public string MediaType { get; set; }     // defaults to "application/json"
        public string SchemaRef { get; set; }     // "#/components/schemas/Game" or null
        public bool IsArray { get; set; }         // true => schema is array<SchemaRef>
        public bool IsBinary { get; set; }        // true => string format=binary
    }

    /// <summary>
    /// JSON shape for a single field in a Game/Lookup schema. Consumed by both
    /// the OpenAPI builder (to render Game's properties) and the GamesController
    /// patch validator (to know which fields are writable). Single source of
    /// truth — adding a field here updates the docs and the validator together.
    /// </summary>
    internal sealed class FieldShape
    {
        public string Type { get; set; }          // "string", "integer", "boolean", "array"
        public string Format { get; set; }        // optional, e.g. "uuid", "date-time"
        public string ItemType { get; set; }      // for arrays: element type ("string")
        public string ItemFormat { get; set; }    // for arrays: element format ("uuid")
        public string Description { get; set; }
        public bool Nullable { get; set; }
        public List<string> EnumValues { get; set; }  // only set for ≤5-value enums; null otherwise

        public static FieldShape Str(string description = null) => new FieldShape { Type = "string", Description = description };
        public static FieldShape StrUuid(string description = null) => new FieldShape { Type = "string", Format = "uuid", Description = description };
        public static FieldShape Bool(string description = null) => new FieldShape { Type = "boolean", Description = description };
        public static FieldShape Int(string description = null) => new FieldShape { Type = "integer", Description = description };
        public static FieldShape IntNullable(string description = null) => new FieldShape { Type = "integer", Nullable = true, Description = description };
        public static FieldShape Long(string description = null) => new FieldShape { Type = "integer", Format = "int64", Description = description };
        public static FieldShape LongNullable(string description = null) => new FieldShape { Type = "integer", Format = "int64", Nullable = true, Description = description };
        public static FieldShape DateTimeNullable(string description = null) => new FieldShape { Type = "string", Format = "date-time", Nullable = true, Description = description };
        public static FieldShape UuidArray(string description = null) => new FieldShape { Type = "array", ItemType = "string", ItemFormat = "uuid", Description = description };
    }
}
```

**Why these factory methods?** They make the field map in `GamesController` a single readable column instead of repeated `new FieldShape { Type = ..., Format = ... }` boilerplate. Compare:
```csharp
{ "name",     FieldShape.Str("Display name") },
```
vs
```csharp
{ "name",     new FieldShape { Type = "string", Description = "Display name" } },
```

- [ ] **Step 2: Add the Compile entry to the .csproj**

Open `PlayniteApiServer.csproj`. In the existing `<ItemGroup>` containing all the `<Compile>` items, add this entry alongside the other `Server/...` lines (e.g. just after `<Compile Include="Server\TokenGen.cs" />`):

```xml
    <Compile Include="Server\OpenApi\OpenApiTypes.cs" />
```

- [ ] **Step 3: Build**

```bash
./build.ps1
```

Expected: build succeeds. No code references these types yet — they exist but are unused.

- [ ] **Step 4: Commit**

```bash
git add Server/OpenApi/OpenApiTypes.cs PlayniteApiServer.csproj
git commit -m "$(cat <<'EOF'
feat: add OpenAPI POCOs and FieldShape

Carriers for route metadata and JSON field shapes. Used in subsequent
tasks by RouteBuilder, OpenApiBuilder, and the GamesController patch
validator (single source of truth for writable fields).

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Extend Route.cs with metadata fields and PathTemplate

**Files:**
- Modify: `Server/Route.cs`
- Modify: `Server/Router.cs:32` (the `Add` method)

Adds the optional metadata properties to `Route`. The existing `Router.Add` callsites continue to work because every new field is nullable/default. `Add` is updated to capture the original path template string so the OpenAPI builder can use `/games/{id}` as a path key.

- [ ] **Step 1: Read the current Route.cs**

Run:
```bash
cat Server/Route.cs
```

Confirm it currently looks like a small record class with `Method`, `Segments`, and `Handler`. (You'll be replacing the whole file in the next step.)

- [ ] **Step 2: Replace Server/Route.cs with the extended version**

Write the entire file:

```csharp
using System;
using System.Collections.Generic;
using PlayniteApiServer.Server.OpenApi;

namespace PlayniteApiServer.Server
{
    /// <summary>
    /// One row in the routing table. Carries both routing data (method,
    /// segments, handler) and optional OpenAPI documentation metadata
    /// populated via <see cref="RouteBuilder"/>.
    /// </summary>
    internal sealed class Route
    {
        public string Method { get; }
        public string[] Segments { get; }
        public Action<RequestContext> Handler { get; }
        public string PathTemplate { get; }

        // Optional documentation metadata. All default to null/false so
        // existing routes registered without .Describes(...) still work.
        public bool AllowAnonymous { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string[] Tags { get; set; }
        public List<OpenApiParameter> Parameters { get; set; }
        public OpenApiRequestBody RequestBody { get; set; }
        public List<OpenApiResponse> Responses { get; set; }

        public Route(string method, string[] segments, Action<RequestContext> handler, string pathTemplate)
        {
            Method = method;
            Segments = segments;
            Handler = handler;
            PathTemplate = pathTemplate;
        }
    }
}
```

- [ ] **Step 3: Update Router.Add to pass the path template**

Open `Server/Router.cs`. Find the `Add` method (currently around line 30):

```csharp
public void Add(string method, string pathPattern, Action<RequestContext> handler)
{
    var segments = SplitPath(pathPattern);
    routes.Add(new Route(method, segments, handler));
}
```

Replace it with:

```csharp
public void Add(string method, string pathPattern, Action<RequestContext> handler)
{
    var segments = SplitPath(pathPattern);
    routes.Add(new Route(method, segments, handler, pathPattern));
}
```

(Just adding `pathPattern` as the fourth constructor argument. The return type stays `void` for now — Task 5 changes it to `RouteBuilder`.)

- [ ] **Step 4: Build**

```bash
./build.ps1
```

Expected: build succeeds. The new Route fields are unused but legal.

- [ ] **Step 5: Commit**

```bash
git add Server/Route.cs Server/Router.cs
git commit -m "$(cat <<'EOF'
feat: extend Route with OpenAPI metadata fields

Adds AllowAnonymous, Summary, Description, Tags, Parameters,
RequestBody, Responses, and PathTemplate. All optional — existing
routes are unchanged in behavior.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Create RouteBuilder and make Router.Add return it

**Files:**
- Create: `Server/OpenApi/RouteBuilder.cs`
- Modify: `Server/Router.cs:30-34` (the `Add` method)
- Modify: `PlayniteApiServer.csproj`

Adds the fluent metadata builder. Existing `BuildRouter()` callsites (which look like `router.Add("GET", "/games", games.List);`) continue to work — they'll now return a `RouteBuilder` whose value is silently discarded, which is legal C#.

- [ ] **Step 1: Create Server/OpenApi/RouteBuilder.cs**

Write the file:

```csharp
using System.Collections.Generic;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Fluent metadata builder returned by <see cref="Router.Add"/>. Each
    /// method mutates the underlying <see cref="Route"/> and returns
    /// <c>this</c> so calls can be chained.
    ///
    /// Path parameters are auto-inferred at OpenAPI build time from
    /// <c>{name}</c> placeholders in the route's <see cref="Route.PathTemplate"/>.
    /// Calling <see cref="PathParam"/> here only overrides the auto-inferred
    /// entry to set a non-string type or a description.
    /// </summary>
    internal sealed class RouteBuilder
    {
        private readonly Route route;

        public RouteBuilder(Route route)
        {
            this.route = route;
        }

        public RouteBuilder Summary(string summary)
        {
            route.Summary = summary;
            return this;
        }

        public RouteBuilder Description(string description)
        {
            route.Description = description;
            return this;
        }

        public RouteBuilder Tags(params string[] tags)
        {
            route.Tags = tags;
            return this;
        }

        public RouteBuilder QueryParam(string name, string type, string description, bool required = false)
        {
            EnsureParameters();
            route.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = "query",
                Type = type,
                Description = description,
                Required = required,
            });
            return this;
        }

        public RouteBuilder PathParam(string name, string type, string description)
        {
            EnsureParameters();
            route.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = "path",
                Type = type,
                Description = description,
                Required = true,
            });
            return this;
        }

        public RouteBuilder Body(string schemaRef, string description = null)
        {
            route.RequestBody = new OpenApiRequestBody
            {
                SchemaRef = schemaRef,
                Description = description,
                Required = true,
            };
            return this;
        }

        public RouteBuilder Response(int status, string description, string schemaRef = null)
        {
            EnsureResponses();
            route.Responses.Add(new OpenApiResponse
            {
                Status = status,
                Description = description,
                MediaType = "application/json",
                SchemaRef = schemaRef,
            });
            return this;
        }

        public RouteBuilder ArrayResponse(int status, string description, string itemSchemaRef)
        {
            EnsureResponses();
            route.Responses.Add(new OpenApiResponse
            {
                Status = status,
                Description = description,
                MediaType = "application/json",
                SchemaRef = itemSchemaRef,
                IsArray = true,
            });
            return this;
        }

        public RouteBuilder BinaryResponse(int status, string description, string mediaType)
        {
            EnsureResponses();
            route.Responses.Add(new OpenApiResponse
            {
                Status = status,
                Description = description,
                MediaType = mediaType,
                IsBinary = true,
            });
            return this;
        }

        public RouteBuilder AllowAnonymous()
        {
            route.AllowAnonymous = true;
            return this;
        }

        private void EnsureParameters()
        {
            if (route.Parameters == null)
            {
                route.Parameters = new List<OpenApiParameter>();
            }
        }

        private void EnsureResponses()
        {
            if (route.Responses == null)
            {
                route.Responses = new List<OpenApiResponse>();
            }
        }
    }
}
```

- [ ] **Step 2: Add Compile entry to .csproj**

In `PlayniteApiServer.csproj`, add (next to the other Server/... lines):

```xml
    <Compile Include="Server\OpenApi\RouteBuilder.cs" />
```

- [ ] **Step 3: Update Router.Add to return RouteBuilder**

In `Server/Router.cs`, modify the `Add` method to return `RouteBuilder`:

```csharp
public RouteBuilder Add(string method, string pathPattern, Action<RequestContext> handler)
{
    var segments = SplitPath(pathPattern);
    var route = new Route(method, segments, handler, pathPattern);
    routes.Add(route);
    return new RouteBuilder(route);
}
```

Add a `using PlayniteApiServer.Server.OpenApi;` to the top of `Server/Router.cs` if it isn't already there. (Existing usings are at the top of the file — slot it in alphabetical order.)

- [ ] **Step 4: Build**

```bash
./build.ps1
```

Expected: build succeeds. The existing `BuildRouter()` callsites in `PlayniteApiServerPlugin.cs` ignore the new return value, which is legal C#. No callers use `.Describes(...)` yet.

- [ ] **Step 5: Commit**

```bash
git add Server/OpenApi/RouteBuilder.cs Server/Router.cs PlayniteApiServer.csproj
git commit -m "$(cat <<'EOF'
feat: add RouteBuilder and return it from Router.Add

Existing call sites ignore the return value (legal C#); the metadata
builder will be wired into BuildRouter in a later task.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Reorder Router.Dispatch (match-then-auth + AllowAnonymous bypass)

**Files:**
- Modify: `Server/Router.cs:36-113` (the `Dispatch` method)

The new flow matches the route first, then checks auth + write-gate (skipping both for anonymous routes). See the design spec §5.2 for the rationale and the accepted behavior changes (401 → 404 on unknown paths for unauthenticated callers).

- [ ] **Step 1: Replace the Dispatch method**

In `Server/Router.cs`, find the existing `Dispatch` method (currently lines ~36–113). Replace its entire body with:

```csharp
public void Dispatch(HttpListenerContext http)
{
    var settings = settingsAccessor();
    var req = http.Request;
    var resp = http.Response;

    try
    {
        var pathSegments = SplitPath(req.Url.AbsolutePath);

        // 1. Walk the route table looking for a matching path. Track both
        //    the first method-match and any path-only match (for the 405).
        Route methodMatch = null;
        Dictionary<string, string> methodMatchVars = null;
        Route pathOnlyMatch = null;

        foreach (var route in routes)
        {
            if (!TryMatch(route.Segments, pathSegments, out var vars))
            {
                continue;
            }

            if (string.Equals(route.Method, req.HttpMethod, StringComparison.OrdinalIgnoreCase))
            {
                methodMatch = route;
                methodMatchVars = vars;
                break;
            }

            if (pathOnlyMatch == null)
            {
                pathOnlyMatch = route;
            }
        }

        // 2. No path match at all → 404 (no auth check; nothing to protect).
        if (methodMatch == null && pathOnlyMatch == null)
        {
            WriteError(resp, 404, "not_found", "No route matches " + req.HttpMethod + " " + req.Url.AbsolutePath + ".");
            return;
        }

        // 3. Path matched but method did not → 405 with Allow header.
        if (methodMatch == null)
        {
            var allowed = routes
                .Where(r => SegmentsEqual(r.Segments, pathOnlyMatch.Segments))
                .Select(r => r.Method.ToUpperInvariant())
                .Distinct()
                .ToArray();
            resp.AddHeader("Allow", string.Join(", ", allowed));
            WriteError(resp, 405, "method_not_allowed", "Method " + req.HttpMethod + " is not allowed on this resource.");
            return;
        }

        // 4. Auth + write-gate, skipped for anonymous routes.
        if (!methodMatch.AllowAnonymous)
        {
            var expected = settings.Token ?? "";
            var provided = ExtractBearerToken(req);
            if (string.IsNullOrEmpty(expected) || !TokenGen.ConstantTimeEquals(provided, expected))
            {
                resp.AddHeader("WWW-Authenticate", "Bearer");
                WriteError(resp, 401, "unauthorized", "Missing or invalid bearer token.");
                return;
            }

            if (!settings.EnableWrites && !IsReadMethod(methodMatch.Method))
            {
                WriteError(resp, 403, "writes_disabled", "Write operations are disabled in plugin settings.");
                return;
            }
        }

        // 5. Invoke handler.
        var query = ParseQueryString(req.Url.Query);
        var ctx = new RequestContext(http, methodMatchVars, query);
        methodMatch.Handler(ctx);
    }
    catch (ApiException apiEx)
    {
        WriteError(resp, apiEx.StatusCode, ClassifyCode(apiEx.StatusCode), apiEx.Message);
    }
    catch (Exception ex)
    {
        var errorId = Guid.NewGuid().ToString();
        logger.Error(ex, "Unhandled exception in request " + req.HttpMethod + " " + req.Url.AbsolutePath + " (errorId=" + errorId + ")");
        WriteError(resp, 500, "internal", "Internal server error (id=" + errorId + ").");
    }
}
```

The changes vs the original:
1. Path/method matching happens first (no auth dependency).
2. The `404` path no longer requires auth — unauthenticated unknown paths return 404.
3. The `405` path no longer requires auth.
4. The bearer + write-gate checks now wrap around the handler invocation only, and are skipped when `methodMatch.AllowAnonymous` is true.

The existing `try/catch` continues to wrap everything so handler exceptions still translate to JSON errors.

- [ ] **Step 2: Build**

```bash
./build.ps1
```

Expected: build succeeds. (No `using` changes needed — the code uses types already imported.)

- [ ] **Step 3: Commit**

```bash
git add Server/Router.cs
git commit -m "$(cat <<'EOF'
refactor: reorder Router.Dispatch to match-then-auth

Routes flagged AllowAnonymous now skip the bearer-token check and the
write-gate. Behavior change for non-anonymous routes: unauthenticated
requests to unknown paths return 404 instead of 401 (accepted in spec
§5.2 — the OpenAPI document already enumerates routes).

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Convert AllowedPatchFields to Dictionary&lt;string, FieldShape&gt;

**Files:**
- Modify: `Controllers/GamesController.cs`

Replaces the `HashSet<string>` allow-list with a `Dictionary<string, FieldShape>`. The patch validator switches from `Contains` to `ContainsKey`. The dictionary is the single source of truth that `OpenApiSchemas.Game` (Task 8) consumes to render the schema.

- [ ] **Step 1: Replace the AllowedPatchFields declaration**

Open `Controllers/GamesController.cs`. Find the existing `AllowedPatchFields` declaration (around lines 23–39):

```csharp
private static readonly HashSet<string> AllowedPatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "name", "sortingName", "gameId", "description", "notes", "version",
    "installDirectory", "isInstalled", "hidden", "favorite",
    ...
};
```

Replace it with the dictionary version below. Note: `internal static readonly` (not `private`) because Task 8's `OpenApiSchemas` reads it from the same assembly.

```csharp
/// <summary>
/// Allow-list of patchable fields, paired with the JSON shape used by
/// both the patch validator (key lookup) and the OpenAPI schema builder
/// (value used to render the Game schema). Adding a field here updates
/// both surfaces in lockstep.
///
/// Nested observable collections (gameActions, links, roms) are
/// deliberately excluded.
/// </summary>
internal static readonly Dictionary<string, FieldShape> AllowedPatchFields = new Dictionary<string, FieldShape>(StringComparer.OrdinalIgnoreCase)
{
    // Identity / display
    { "name",                       FieldShape.Str("Display name") },
    { "sortingName",                FieldShape.Str("Override sort key") },
    { "gameId",                     FieldShape.Str("Library-plugin-specific identifier") },
    { "description",                FieldShape.Str("Long description / notes (HTML allowed)") },
    { "notes",                      FieldShape.Str("User-authored notes") },
    { "version",                    FieldShape.Str("Version string") },
    { "installDirectory",           FieldShape.Str("Absolute install path") },

    // Boolean flags
    { "isInstalled",                FieldShape.Bool("Marked installed") },
    { "hidden",                     FieldShape.Bool("Hidden in the library view") },
    { "favorite",                   FieldShape.Bool("Marked as favorite") },
    { "overrideInstallState",       FieldShape.Bool("Manual override of detected install state") },
    { "includeLibraryPluginAction", FieldShape.Bool("Show the library-plugin-provided action in the play menu") },
    { "enableSystemHdr",            FieldShape.Bool("Enable system HDR when launching") },
    { "useGlobalPostScript",        FieldShape.Bool("Use global post-launch script") },
    { "useGlobalPreScript",         FieldShape.Bool("Use global pre-launch script") },
    { "useGlobalGameStartedScript", FieldShape.Bool("Use global game-started script") },

    // Per-game scripts
    { "preScript",                  FieldShape.Str("Per-game pre-launch script (PowerShell)") },
    { "postScript",                 FieldShape.Str("Per-game post-launch script (PowerShell)") },
    { "gameStartedScript",          FieldShape.Str("Per-game game-started script (PowerShell)") },

    // Numeric metrics
    { "playtime",                   FieldShape.Long("Total play time in seconds") },
    { "playCount",                  FieldShape.Long("Number of times launched") },
    { "installSize",                FieldShape.LongNullable("On-disk size in bytes") },

    // Timestamps
    { "added",                      FieldShape.DateTimeNullable("When the game was added to the library (ISO 8601)") },
    { "modified",                   FieldShape.DateTimeNullable("When the game was last modified (ISO 8601)") },
    { "lastActivity",               FieldShape.DateTimeNullable("When the game was last played (ISO 8601)") },
    { "releaseDate",                FieldShape.Str("Release date as YYYY-MM-DD") },

    // Scores
    { "userScore",                  FieldShape.IntNullable("User score 0–100") },
    { "communityScore",             FieldShape.IntNullable("Community score 0–100") },
    { "criticScore",                FieldShape.IntNullable("Critic score 0–100") },

    // Media (paths or URLs the GetFullFilePath helper resolves)
    { "icon",                       FieldShape.Str("Icon path / URL / database id") },
    { "coverImage",                 FieldShape.Str("Cover image path / URL / database id") },
    { "backgroundImage",            FieldShape.Str("Background image path / URL / database id") },
    { "manual",                     FieldShape.Str("Manual path / URL / database id") },

    // Relationship arrays (uuid lists)
    { "platformIds",                FieldShape.UuidArray("Platform ids the game runs on") },
    { "genreIds",                   FieldShape.UuidArray("Genre ids") },
    { "developerIds",               FieldShape.UuidArray("Developer (Company) ids") },
    { "publisherIds",               FieldShape.UuidArray("Publisher (Company) ids") },
    { "categoryIds",                FieldShape.UuidArray("Category ids") },
    { "tagIds",                     FieldShape.UuidArray("Tag ids") },
    { "featureIds",                 FieldShape.UuidArray("Feature ids") },
    { "seriesIds",                  FieldShape.UuidArray("Series ids") },
    { "ageRatingIds",               FieldShape.UuidArray("AgeRating ids") },
    { "regionIds",                  FieldShape.UuidArray("Region ids") },

    // Single relationship ids
    { "sourceId",                   FieldShape.StrUuid("GameSource id") },
    { "completionStatusId",         FieldShape.StrUuid("CompletionStatus id") },
};
```

- [ ] **Step 2: Add the using import**

Near the top of `Controllers/GamesController.cs`, add:

```csharp
using PlayniteApiServer.Server.OpenApi;
```

Place it alphabetically after `using PlayniteApiServer.Server;`.

- [ ] **Step 3: Update the patch validator**

Find the patch validator loop in the `Patch` method (around lines 128–136):

```csharp
foreach (var prop in patch.Properties())
{
    if (!AllowedPatchFields.Contains(prop.Name))
    {
        throw new ApiException(400,
            "Field '" + prop.Name + "' is not patchable. " +
            "Nested collections (gameActions, links, roms) are read-only in v1.");
    }
}
```

Change `Contains` to `ContainsKey`:

```csharp
foreach (var prop in patch.Properties())
{
    if (!AllowedPatchFields.ContainsKey(prop.Name))
    {
        throw new ApiException(400,
            "Field '" + prop.Name + "' is not patchable. " +
            "Nested collections (gameActions, links, roms) are read-only in v1.");
    }
}
```

(One word: `Contains` → `ContainsKey`. Nothing else in the validator changes.)

- [ ] **Step 4: Build**

```bash
./build.ps1
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add Controllers/GamesController.cs
git commit -m "$(cat <<'EOF'
refactor: AllowedPatchFields → Dictionary<string, FieldShape>

Pairs each writable field with its JSON shape so the OpenAPI Game
schema (next task) and the patch validator share one source of truth.
Validator switches from Contains to ContainsKey; behavior unchanged.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Create OpenApiSchemas.cs (six component schemas + Schemas constants)

**Files:**
- Create: `Server/OpenApi/OpenApiSchemas.cs`
- Modify: `PlayniteApiServer.csproj`

Hand-authors the seven component schemas referenced by the route metadata (the spec said six, but we add a `NamedItemCreate` for the lookup POST bodies — see below). Also defines a `Schemas` constants class for `$ref` strings so `BuildRouter()` callsites use named constants instead of magic strings.

> **Note on schema count:** The design spec §5.4 lists six schemas. This task ships seven by adding `NamedItemCreate` (the body schema for `POST /platforms`, `POST /genres`, etc., which is `{name: string}` — structurally identical to `GameCreate` but conceptually distinct). The alternative was inlining the schema 12 times in the spec output. If this conflicts with your reading of the spec, escalate before proceeding — but the engineer-author of this plan considers it a minor and reasonable extension.

- [ ] **Step 1: Create the file**

Write `Server/OpenApi/OpenApiSchemas.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PlayniteApiServer.Controllers;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Hand-authored OpenAPI 3.0.3 component schemas. Each method returns a
    /// JObject fragment that the <see cref="OpenApiBuilder"/> drops into the
    /// document's <c>components.schemas</c> section.
    ///
    /// The Game schema is generated by walking
    /// <see cref="GamesController.AllowedPatchFields"/> — the same source of
    /// truth used by the patch validator. The result is that the spec's
    /// "writable" surface literally cannot drift from the validator.
    /// </summary>
    internal static class OpenApiSchemas
    {
        /// <summary>
        /// Constants for $ref strings. Use these from RouteBuilder calls
        /// instead of magic strings.
        /// </summary>
        public static class Schemas
        {
            public const string Error           = "#/components/schemas/Error";
            public const string Game            = "#/components/schemas/Game";
            public const string GameCreate      = "#/components/schemas/GameCreate";
            public const string GamePage        = "#/components/schemas/GamePage";
            public const string NamedItem       = "#/components/schemas/NamedItem";
            public const string NamedItemCreate = "#/components/schemas/NamedItemCreate";
            public const string Platform        = "#/components/schemas/Platform";
        }

        public static JObject BuildAll()
        {
            return new JObject
            {
                ["Error"]           = Error(),
                ["Game"]            = Game(),
                ["GameCreate"]      = GameCreate(),
                ["GamePage"]        = GamePage(),
                ["NamedItem"]       = NamedItem(),
                ["NamedItemCreate"] = NamedItemCreate(),
                ["Platform"]        = Platform(),
            };
        }

        // ─── individual schemas ──────────────────────────────────────────

        private static JObject Error() => new JObject
        {
            ["type"] = "object",
            ["required"] = new JArray("error", "message"),
            ["properties"] = new JObject
            {
                ["error"]   = StringProp("Short error code (e.g. \"not_found\", \"writes_disabled\")"),
                ["message"] = StringProp("Human-readable description"),
            },
        };

        private static JObject Game()
        {
            var properties = new JObject();

            // Read-only header — server-managed identity. These appear in
            // responses but are not in AllowedPatchFields.
            properties["id"] = StringProp("Server-assigned uuid", format: "uuid");

            // Walk the patch allow-list. The Dictionary preserves insertion
            // order (Newtonsoft does too), so the schema field order matches
            // the declaration order in GamesController.
            foreach (var kvp in GamesController.AllowedPatchFields)
            {
                properties[kvp.Key] = ShapeToJson(kvp.Value);
            }

            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = true,  // SDK exposes more fields than we document
                ["properties"] = properties,
                ["description"] = "A Playnite game. The documented properties cover the writable patch surface plus the server-assigned id; the SDK serializes additional read-only fields that are not enumerated here.",
            };
        }

        private static JObject GameCreate() => new JObject
        {
            ["type"] = "object",
            ["required"] = new JArray("name"),
            ["properties"] = new JObject
            {
                ["name"] = StringProp("Display name (required)"),
            },
        };

        private static JObject GamePage() => new JObject
        {
            ["type"] = "object",
            ["required"] = new JArray("total", "offset", "limit", "items"),
            ["properties"] = new JObject
            {
                ["total"]  = new JObject { ["type"] = "integer", ["description"] = "Total games matching the filter" },
                ["offset"] = new JObject { ["type"] = "integer", ["description"] = "Offset used for this page" },
                ["limit"]  = new JObject { ["type"] = "integer", ["description"] = "Page size used (capped at 1000)" },
                ["items"]  = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["$ref"] = Schemas.Game },
                },
            },
        };

        private static JObject NamedItem() => new JObject
        {
            ["type"] = "object",
            ["additionalProperties"] = true,
            ["required"] = new JArray("id", "name"),
            ["properties"] = new JObject
            {
                ["id"]   = StringProp("Server-assigned uuid", format: "uuid"),
                ["name"] = StringProp("Display name"),
            },
        };

        private static JObject NamedItemCreate() => new JObject
        {
            ["type"] = "object",
            ["required"] = new JArray("name"),
            ["properties"] = new JObject
            {
                ["name"] = StringProp("Display name (required)"),
            },
        };

        private static JObject Platform() => new JObject
        {
            ["type"] = "object",
            ["additionalProperties"] = true,
            ["required"] = new JArray("id", "name"),
            ["properties"] = new JObject
            {
                ["id"]              = StringProp("Server-assigned uuid", format: "uuid"),
                ["name"]            = StringProp("Display name"),
                ["specificationId"] = StringProp("Optional Playnite platform spec id (e.g. \"sony_playstation2\")"),
                ["icon"]            = StringProp("Icon path / URL / database id"),
                ["cover"]           = StringProp("Cover image path / URL / database id"),
                ["background"]      = StringProp("Background image path / URL / database id"),
            },
        };

        // ─── helpers ─────────────────────────────────────────────────────

        private static JObject StringProp(string description, string format = null)
        {
            var o = new JObject { ["type"] = "string", ["description"] = description };
            if (format != null)
            {
                o["format"] = format;
            }
            return o;
        }

        private static JObject ShapeToJson(FieldShape shape)
        {
            var o = new JObject { ["type"] = shape.Type };

            if (shape.Format != null)
            {
                o["format"] = shape.Format;
            }
            if (shape.Description != null)
            {
                o["description"] = shape.Description;
            }
            if (shape.Nullable)
            {
                o["nullable"] = true;
            }
            if (shape.Type == "array")
            {
                var items = new JObject { ["type"] = shape.ItemType ?? "string" };
                if (shape.ItemFormat != null)
                {
                    items["format"] = shape.ItemFormat;
                }
                o["items"] = items;
            }
            if (shape.EnumValues != null && shape.EnumValues.Count > 0)
            {
                o["enum"] = new JArray(shape.EnumValues);
            }
            return o;
        }
    }
}
```

> **Enum note:** None of the current `AllowedPatchFields` entries are enum-typed in the Playnite SDK. The `EnumValues` infrastructure exists for future fields. As of v1, no schema property in the emitted document will carry an `enum:` array. This honors the design decision ("yes for small enums") at the infrastructure level without manufacturing fictitious enum constraints.

- [ ] **Step 2: Add Compile entry to .csproj**

Add to the `<Compile>` ItemGroup:

```xml
    <Compile Include="Server\OpenApi\OpenApiSchemas.cs" />
```

- [ ] **Step 3: Build**

```bash
./build.ps1
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add Server/OpenApi/OpenApiSchemas.cs PlayniteApiServer.csproj
git commit -m "$(cat <<'EOF'
feat: hand-authored OpenAPI component schemas

Seven schemas (Error, Game, GameCreate, GamePage, NamedItem,
NamedItemCreate, Platform). Game is generated from
GamesController.AllowedPatchFields so the writable surface cannot drift
from the patch validator.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Create OpenApiBuilder.cs

**Files:**
- Create: `Server/OpenApi/OpenApiBuilder.cs`
- Modify: `Server/Router.cs` (add a `Routes` accessor)
- Modify: `PlayniteApiServer.csproj`

Walks the route table and emits a complete OpenAPI 3.0.3 document as a serialized JSON string.

- [ ] **Step 1: Add the Routes accessor to Router**

In `Server/Router.cs`, find the existing `routes` field:

```csharp
private readonly List<Route> routes = new List<Route>();
```

Add this property immediately below it (the field stays private; only the read-only view is exposed):

```csharp
public IReadOnlyList<Route> Routes => routes;
```

This single line is needed because `OpenApiBuilder.Build` takes the route list as input. Don't expose the mutable `List<Route>` directly.

- [ ] **Step 2: Create Server/OpenApi/OpenApiBuilder.cs**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Walks a route table and emits a serialized OpenAPI 3.0.3 document.
    /// The output is deterministic given a deterministic input — Newtonsoft
    /// preserves JObject insertion order, so the spec is byte-stable across
    /// runs as long as the route registrations don't move around.
    /// </summary>
    internal static class OpenApiBuilder
    {
        private static readonly Regex PathParamRegex = new Regex(@"\{([^}]+)\}", RegexOptions.Compiled);

        public static string Build(IReadOnlyList<Route> routes, string title, string version)
        {
            var doc = new JObject
            {
                ["openapi"] = "3.0.3",
                ["info"] = new JObject
                {
                    ["title"] = title,
                    ["version"] = version,
                    ["description"] = "Read/write access to the local Playnite library. All endpoints (except the documentation routes) require a Bearer token configured in the plugin settings; non-GET requests additionally require the EnableWrites toggle.",
                },
                ["servers"] = new JArray(
                    new JObject { ["url"] = "/" }
                ),
                ["components"] = new JObject
                {
                    ["securitySchemes"] = new JObject
                    {
                        ["bearerAuth"] = new JObject
                        {
                            ["type"] = "http",
                            ["scheme"] = "bearer",
                        },
                    },
                    ["schemas"] = OpenApiSchemas.BuildAll(),
                },
                ["paths"] = BuildPaths(routes),
                ["tags"] = BuildTagList(routes),
            };

            return JsonConvert.SerializeObject(doc, Formatting.Indented);
        }

        // ─── path building ───────────────────────────────────────────────

        private static JObject BuildPaths(IReadOnlyList<Route> routes)
        {
            var paths = new JObject();

            // Group routes by their path template, preserving registration order.
            var seen = new HashSet<string>();
            var ordered = new List<string>();
            foreach (var r in routes)
            {
                if (seen.Add(r.PathTemplate))
                {
                    ordered.Add(r.PathTemplate);
                }
            }

            foreach (var template in ordered)
            {
                var pathItem = new JObject();
                foreach (var route in routes.Where(r => r.PathTemplate == template))
                {
                    pathItem[route.Method.ToLowerInvariant()] = BuildOperation(route);
                }
                paths[template] = pathItem;
            }

            return paths;
        }

        private static JObject BuildOperation(Route route)
        {
            var op = new JObject();

            if (route.Tags != null && route.Tags.Length > 0)
            {
                op["tags"] = new JArray(route.Tags);
            }
            if (!string.IsNullOrEmpty(route.Summary))
            {
                op["summary"] = route.Summary;
            }
            if (!string.IsNullOrEmpty(route.Description))
            {
                op["description"] = route.Description;
            }

            // Parameters: auto-infer path params from {name} placeholders,
            // then merge any explicit overrides + query params.
            var parameters = BuildParameters(route);
            if (parameters.Count > 0)
            {
                op["parameters"] = new JArray(parameters);
            }

            if (route.RequestBody != null)
            {
                op["requestBody"] = BuildRequestBody(route.RequestBody);
            }

            op["responses"] = BuildResponses(route);

            // Security: every non-anonymous route requires bearerAuth.
            if (!route.AllowAnonymous)
            {
                op["security"] = new JArray(
                    new JObject { ["bearerAuth"] = new JArray() }
                );
            }
            else
            {
                // Anonymous routes get an explicit empty security list,
                // overriding any global default.
                op["security"] = new JArray();
            }

            return op;
        }

        private static List<JObject> BuildParameters(Route route)
        {
            var inferred = new List<JObject>();
            var overrides = new Dictionary<string, JObject>();

            // Auto-infer one path-param entry per {name} placeholder.
            foreach (Match m in PathParamRegex.Matches(route.PathTemplate))
            {
                var name = m.Groups[1].Value;
                inferred.Add(new JObject
                {
                    ["name"] = name,
                    ["in"] = "path",
                    ["required"] = true,
                    ["schema"] = new JObject { ["type"] = "string" },
                });
            }

            // Apply explicit overrides + add query params.
            var query = new List<JObject>();
            if (route.Parameters != null)
            {
                foreach (var p in route.Parameters)
                {
                    var entry = ParamToJson(p);
                    if (p.In == "path")
                    {
                        overrides[p.Name] = entry;
                    }
                    else
                    {
                        query.Add(entry);
                    }
                }
            }

            // Merge overrides into inferred path params (replace by name).
            for (int i = 0; i < inferred.Count; i++)
            {
                var name = (string)inferred[i]["name"];
                if (overrides.TryGetValue(name, out var ov))
                {
                    inferred[i] = ov;
                }
            }

            inferred.AddRange(query);
            return inferred;
        }

        private static JObject ParamToJson(OpenApiParameter p)
        {
            var schema = new JObject { ["type"] = p.Type ?? "string" };
            if (p.Format != null)
            {
                schema["format"] = p.Format;
            }
            var entry = new JObject
            {
                ["name"] = p.Name,
                ["in"] = p.In,
                ["required"] = p.Required,
                ["schema"] = schema,
            };
            if (!string.IsNullOrEmpty(p.Description))
            {
                entry["description"] = p.Description;
            }
            return entry;
        }

        private static JObject BuildRequestBody(OpenApiRequestBody body)
        {
            var schema = new JObject { ["$ref"] = body.SchemaRef };
            return new JObject
            {
                ["required"] = body.Required,
                ["description"] = body.Description ?? "",
                ["content"] = new JObject
                {
                    ["application/json"] = new JObject
                    {
                        ["schema"] = schema,
                    },
                },
            };
        }

        private static JObject BuildResponses(Route route)
        {
            var responses = new JObject();

            // Sort declared responses by status code for stable output.
            var declared = route.Responses ?? new List<OpenApiResponse>();
            foreach (var r in declared.OrderBy(x => x.Status))
            {
                responses[r.Status.ToString()] = BuildResponse(r);
            }

            // Auto-add a 401 if the route is non-anonymous and the author
            // didn't declare one explicitly.
            if (!route.AllowAnonymous && !responses.ContainsKey("401"))
            {
                responses["401"] = BuildResponse(new OpenApiResponse
                {
                    Status = 401,
                    Description = "Missing or invalid bearer token",
                    MediaType = "application/json",
                    SchemaRef = OpenApiSchemas.Schemas.Error,
                });
            }

            return responses;
        }

        private static JObject BuildResponse(OpenApiResponse r)
        {
            var entry = new JObject { ["description"] = r.Description };

            if (r.IsBinary)
            {
                entry["content"] = new JObject
                {
                    [r.MediaType] = new JObject
                    {
                        ["schema"] = new JObject
                        {
                            ["type"] = "string",
                            ["format"] = "binary",
                        },
                    },
                };
                return entry;
            }

            if (r.SchemaRef == null)
            {
                // No body — e.g. 204
                return entry;
            }

            JObject schema;
            if (r.IsArray)
            {
                schema = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["$ref"] = r.SchemaRef },
                };
            }
            else
            {
                schema = new JObject { ["$ref"] = r.SchemaRef };
            }

            entry["content"] = new JObject
            {
                [r.MediaType ?? "application/json"] = new JObject
                {
                    ["schema"] = schema,
                },
            };
            return entry;
        }

        // ─── tags ───────────────────────────────────────────────────────

        private static JArray BuildTagList(IReadOnlyList<Route> routes)
        {
            var seen = new HashSet<string>();
            var ordered = new List<string>();
            foreach (var r in routes)
            {
                if (r.Tags == null) continue;
                foreach (var t in r.Tags)
                {
                    if (seen.Add(t)) ordered.Add(t);
                }
            }
            return new JArray(ordered.Select(t => new JObject { ["name"] = t }));
        }
    }
}
```

- [ ] **Step 3: Add Compile entry to .csproj**

```xml
    <Compile Include="Server\OpenApi\OpenApiBuilder.cs" />
```

- [ ] **Step 4: Build**

```bash
./build.ps1
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add Server/OpenApi/OpenApiBuilder.cs Server/Router.cs PlayniteApiServer.csproj
git commit -m "$(cat <<'EOF'
feat: OpenApiBuilder generates the OpenAPI 3.0.3 document

Walks Router.Routes and emits a deterministic JSON document. Auto-adds
a 401 response to every non-anonymous operation. Path parameters are
inferred from {name} placeholders.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Create OpenApiHandler.cs

**Files:**
- Create: `Server/OpenApi/OpenApiHandler.cs`
- Modify: `PlayniteApiServer.csproj`

Trivial — serves the precomputed JSON string with `application/json`.

- [ ] **Step 1: Create the file**

```csharp
using System.Text;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Serves the OpenAPI document built once at plugin start. Anonymous —
    /// see Router.Dispatch and the route registration in BuildRouter.
    /// </summary>
    internal static class OpenApiHandler
    {
        public static void Serve(RequestContext r, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            r.Response.StatusCode = 200;
            r.Response.ContentType = "application/json; charset=utf-8";
            r.Response.ContentLength64 = bytes.Length;
            r.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
    }
}
```

- [ ] **Step 2: Add Compile entry to .csproj**

```xml
    <Compile Include="Server\OpenApi\OpenApiHandler.cs" />
```

- [ ] **Step 3: Build**

```bash
./build.ps1
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add Server/OpenApi/OpenApiHandler.cs PlayniteApiServer.csproj
git commit -m "$(cat <<'EOF'
feat: OpenApiHandler serves the precomputed spec JSON

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: Create SwaggerUiHandler.cs

**Files:**
- Create: `Server/OpenApi/SwaggerUiHandler.cs`
- Modify: `PlayniteApiServer.csproj`

Reads the embedded Swagger UI assets via `Assembly.GetManifestResourceStream` and writes them to the response. The `Resources` nested class is the single source of truth for the embedded resource names — these strings must exactly match the `<LogicalName>` values in the .csproj (Task 2).

- [ ] **Step 1: Create the file**

```csharp
using System.IO;
using System.Reflection;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Serves the embedded Swagger UI assets. The resource names below must
    /// exactly match the LogicalName values in PlayniteApiServer.csproj.
    /// All routes that use this handler are anonymous — see BuildRouter.
    /// </summary>
    internal static class SwaggerUiHandler
    {
        /// <summary>
        /// Single source of truth for embedded-resource names. The .csproj
        /// LogicalName entries reference these literal strings; do not
        /// rename one without renaming the other.
        /// </summary>
        public static class Resources
        {
            public const string IndexHtml          = "index.html";
            public const string Css                = "swagger-ui.css";
            public const string BundleJs           = "swagger-ui-bundle.js";
            public const string StandalonePresetJs = "swagger-ui-standalone-preset.js";
        }

        public static void Serve(RequestContext r, string resourceName, string contentType)
        {
            var asm = typeof(SwaggerUiHandler).Assembly;
            using (var stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new ApiException(404, "Asset missing: " + resourceName);
                }

                var bytes = ReadAllBytes(stream);
                r.Response.StatusCode = 200;
                r.Response.ContentType = contentType;
                r.Response.ContentLength64 = bytes.Length;
                r.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
        }

        private static byte[] ReadAllBytes(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
```

- [ ] **Step 2: Add Compile entry to .csproj**

```xml
    <Compile Include="Server\OpenApi\SwaggerUiHandler.cs" />
```

- [ ] **Step 3: Build**

```bash
./build.ps1
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add Server/OpenApi/SwaggerUiHandler.cs PlayniteApiServer.csproj
git commit -m "$(cat <<'EOF'
feat: SwaggerUiHandler serves embedded swagger-ui-dist assets

Resources nested class holds the single-source-of-truth resource names
shared with the csproj LogicalName entries.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 12: Wire it all up in BuildRouter()

**Files:**
- Modify: `PlayniteApiServerPlugin.cs:131-178` (the `BuildRouter` and `RegisterLookup` methods)

This is the largest task. It:
1. Adds `.Describes(...)` metadata to every existing route registration.
2. Refactors `RegisterLookup<T>` to take a tag name + singular label and emit metadata for all five lookup routes.
3. After all data routes are registered, builds the OpenAPI document and registers the five anonymous documentation routes.

- [ ] **Step 1: Add the using import**

Open `PlayniteApiServerPlugin.cs`. Near the top, add:

```csharp
using PlayniteApiServer.Server.OpenApi;
```

(Place it alphabetically after `using PlayniteApiServer.Server;`.)

- [ ] **Step 2: Replace the BuildRouter method**

Find the existing `BuildRouter` method (currently around lines 131–169) and the `RegisterLookup<T>` helper (currently around lines 171–178). Replace **both** with the following block:

```csharp
private Router BuildRouter()
{
    var db = PlayniteApi.Database;
    var ui = PlayniteApi.MainView.UIDispatcher;

    var router = new Router(() => settings.Live);

    // ─── Health ────────────────────────────────────────────────────
    var health = new HealthController(db);
    router.Add("GET", "/health", health.Get)
        .Summary("Health check")
        .Tags("system")
        .Description("Returns the plugin version and a count of every database collection.")
        .Response(200, "Server is running");

    // ─── Games ─────────────────────────────────────────────────────
    var games = new GamesController(db, ui);

    router.Add("GET", "/games", games.List)
        .Summary("List games")
        .Tags("games")
        .QueryParam("offset", "integer", "Pagination offset (default 0)")
        .QueryParam("limit",  "integer", "Page size (default 100, max 1000)")
        .QueryParam("q",      "string",  "Substring filter on Name (case-insensitive)")
        .Response(200, "Paged list of games", OpenApiSchemas.Schemas.GamePage);

    router.Add("POST", "/games", games.Create)
        .Summary("Create a game")
        .Tags("games")
        .Body(OpenApiSchemas.Schemas.GameCreate, "Minimum: name")
        .Response(201, "Created", OpenApiSchemas.Schemas.Game)
        .Response(400, "Validation error", OpenApiSchemas.Schemas.Error)
        .Response(403, "Writes disabled in plugin settings", OpenApiSchemas.Schemas.Error);

    router.Add("GET", "/games/{id}", games.Get)
        .Summary("Get a game by id")
        .Tags("games")
        .Response(200, "Game", OpenApiSchemas.Schemas.Game)
        .Response(404, "Game not found", OpenApiSchemas.Schemas.Error);

    router.Add("PATCH", "/games/{id}", games.Patch)
        .Summary("Patch a game")
        .Tags("games")
        .Description("Merge-style patch. Only fields in the writable allow-list may be set; nested observable collections (gameActions, links, roms) are read-only in v1.")
        .Body(OpenApiSchemas.Schemas.Game, "Subset of Game properties to update")
        .Response(200, "Updated game", OpenApiSchemas.Schemas.Game)
        .Response(400, "Invalid field or JSON", OpenApiSchemas.Schemas.Error)
        .Response(403, "Writes disabled in plugin settings", OpenApiSchemas.Schemas.Error)
        .Response(404, "Game not found", OpenApiSchemas.Schemas.Error)
        .Response(409, "Foreign key references unknown id", OpenApiSchemas.Schemas.Error);

    router.Add("DELETE", "/games/{id}", games.Delete)
        .Summary("Delete a game")
        .Tags("games")
        .Response(204, "Deleted")
        .Response(403, "Writes disabled in plugin settings", OpenApiSchemas.Schemas.Error)
        .Response(404, "Game not found", OpenApiSchemas.Schemas.Error);

    // ─── Game media ────────────────────────────────────────────────
    var media = new MediaController(db);
    router.Add("GET", "/games/{id}/media/{kind}", media.Get)
        .Summary("Get a game's image")
        .Tags("games")
        .Description("Streams the icon, cover, or background image for a game. The 'kind' segment must be one of: icon, cover, background.")
        .BinaryResponse(200, "Image bytes", "image/*")
        .Response(404, "Game or media not found", OpenApiSchemas.Schemas.Error);

    // ─── Lookup collections ────────────────────────────────────────
    RegisterLookup(router, "/platforms",          "platforms",          "platform",          new LookupController<Platform>(db.Platforms, ui),                 OpenApiSchemas.Schemas.Platform);
    RegisterLookup(router, "/genres",             "genres",             "genre",             new LookupController<Genre>(db.Genres, ui),                       OpenApiSchemas.Schemas.NamedItem);
    RegisterLookup(router, "/companies",          "companies",          "company",           new LookupController<Company>(db.Companies, ui),                  OpenApiSchemas.Schemas.NamedItem);
    RegisterLookup(router, "/features",           "features",           "feature",           new LookupController<GameFeature>(db.Features, ui),               OpenApiSchemas.Schemas.NamedItem);
    RegisterLookup(router, "/categories",         "categories",         "category",          new LookupController<Category>(db.Categories, ui),                OpenApiSchemas.Schemas.NamedItem);
    RegisterLookup(router, "/tags",               "tags",               "tag",               new LookupController<Tag>(db.Tags, ui),                           OpenApiSchemas.Schemas.NamedItem);
    RegisterLookup(router, "/series",             "series",             "series entry",      new LookupController<Series>(db.Series, ui),                      OpenApiSchemas.Schemas.NamedItem);
    RegisterLookup(router, "/ageratings",         "ageratings",         "age rating",        new LookupController<AgeRating>(db.AgeRatings, ui),               OpenApiSchemas.Schemas.NamedItem);
    RegisterLookup(router, "/regions",            "regions",            "region",            new LookupController<Region>(db.Regions, ui),                     OpenApiSchemas.Schemas.NamedItem);
    RegisterLookup(router, "/sources",            "sources",            "source",            new LookupController<GameSource>(db.Sources, ui),                 OpenApiSchemas.Schemas.NamedItem);
    RegisterLookup(router, "/completionstatuses", "completionstatuses", "completion status", new LookupController<CompletionStatus>(db.CompletionStatuses, ui), OpenApiSchemas.Schemas.NamedItem);
    RegisterLookup(router, "/emulators",          "emulators",          "emulator",          new LookupController<Emulator>(db.Emulators, ui),                 OpenApiSchemas.Schemas.NamedItem);

    // ─── Documentation routes ──────────────────────────────────────
    // Build the OpenAPI document NOW (after all data routes are registered)
    // and capture the JSON in a closure for the handler.
    var openApiJson = OpenApiBuilder.Build(router.Routes, "Playnite API Server", "0.1.0");

    router.Add("GET", "/openapi.json", r => OpenApiHandler.Serve(r, openApiJson))
        .AllowAnonymous();

    router.Add("GET", "/docs", r => SwaggerUiHandler.Serve(r, SwaggerUiHandler.Resources.IndexHtml, "text/html; charset=utf-8"))
        .AllowAnonymous();

    router.Add("GET", "/swagger-ui.css", r => SwaggerUiHandler.Serve(r, SwaggerUiHandler.Resources.Css, "text/css; charset=utf-8"))
        .AllowAnonymous();

    router.Add("GET", "/swagger-ui-bundle.js", r => SwaggerUiHandler.Serve(r, SwaggerUiHandler.Resources.BundleJs, "application/javascript; charset=utf-8"))
        .AllowAnonymous();

    router.Add("GET", "/swagger-ui-standalone-preset.js", r => SwaggerUiHandler.Serve(r, SwaggerUiHandler.Resources.StandalonePresetJs, "application/javascript; charset=utf-8"))
        .AllowAnonymous();

    return router;
}

private static void RegisterLookup<T>(
    Router router,
    string prefix,
    string tag,
    string singular,
    LookupController<T> c,
    string itemSchemaRef) where T : DatabaseObject
{
    router.Add("GET", prefix, c.List)
        .Summary("List " + tag)
        .Tags(tag)
        .ArrayResponse(200, "All " + tag, itemSchemaRef);

    router.Add("POST", prefix, c.Create)
        .Summary("Create a " + singular)
        .Tags(tag)
        .Body(OpenApiSchemas.Schemas.NamedItemCreate, "Minimum: name")
        .Response(201, "Created", itemSchemaRef)
        .Response(400, "Validation error", OpenApiSchemas.Schemas.Error)
        .Response(403, "Writes disabled in plugin settings", OpenApiSchemas.Schemas.Error);

    router.Add("GET", prefix + "/{id}", c.Get)
        .Summary("Get a " + singular + " by id")
        .Tags(tag)
        .Response(200, singular, itemSchemaRef)
        .Response(404, singular + " not found", OpenApiSchemas.Schemas.Error);

    router.Add("PATCH", prefix + "/{id}", c.Patch)
        .Summary("Rename a " + singular)
        .Tags(tag)
        .Body(OpenApiSchemas.Schemas.NamedItemCreate, "Only the 'name' field is patchable")
        .Response(200, "Updated", itemSchemaRef)
        .Response(400, "Validation error", OpenApiSchemas.Schemas.Error)
        .Response(403, "Writes disabled in plugin settings", OpenApiSchemas.Schemas.Error)
        .Response(404, singular + " not found", OpenApiSchemas.Schemas.Error);

    router.Add("DELETE", prefix + "/{id}", c.Delete)
        .Summary("Delete a " + singular)
        .Tags(tag)
        .Response(204, "Deleted")
        .Response(403, "Writes disabled in plugin settings", OpenApiSchemas.Schemas.Error)
        .Response(404, singular + " not found", OpenApiSchemas.Schemas.Error);
}
```

> **Why the OpenAPI build happens inside `BuildRouter` instead of after?** The doc must be built *after* all data routes are registered (so it can see them) and *before* the docs routes are registered (so they don't appear in the spec — they're infrastructure, not API). The closure captures `openApiJson` so the route handler returns it without re-building.

- [ ] **Step 3: Build**

```bash
./build.ps1
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add PlayniteApiServerPlugin.cs
git commit -m "$(cat <<'EOF'
feat: wire OpenAPI builder + Swagger UI routes into BuildRouter

Every existing route gets .Describes(...) metadata. After all data
routes are registered, the OpenAPI document is built once and the five
anonymous documentation routes (/openapi.json, /docs, plus three asset
routes) are added.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 13: Manual smoke test (spec §8)

**Files:** None (verification only)

This is the §8 smoke test from the design spec. It verifies the docs work end-to-end in a real Playnite instance and confirms the auth carve-out is correctly scoped. **Playnite must be restarted** between Task 12's deploy and this step so it picks up the new DLL.

If any step fails, the implementation is **not complete** — escalate with the failing step and the actual response.

- [ ] **Step 1: Restart Playnite**

Close Playnite if it's running, then start it again. Open the plugin settings (top menu → Add-ons → Generic → Playnite API Server) and note:
- The bind address (default `127.0.0.1`)
- The port (default `8088` or whatever was configured)
- The bearer **token** — copy it; you'll paste it into Swagger UI in step 2
- That **EnableWrites** is currently ON (turn it on if it isn't)

- [ ] **Step 2: Browser — Swagger UI loads cleanly**

Open `http://127.0.0.1:<port>/docs` in any modern browser.

Expected:
- Swagger UI renders with the title "Playnite API Server"
- The route list is grouped by tag: `system`, `games`, `platforms`, `genres`, ..., `emulators`
- The browser network tab shows 200s for `/docs`, `/swagger-ui.css`, `/swagger-ui-bundle.js`, `/swagger-ui-standalone-preset.js`, `/openapi.json`
- **No 404s** in the network tab. If anything 404s, the four-file vendoring isn't sufficient and you need to add the missing asset.

- [ ] **Step 3: Authorize**

Click the **Authorize** button (top-right). Paste the bearer token into the **Value** field for `bearerAuth`. Click **Authorize**, then **Close**.

Expected: the lock icons next to each operation flip to "locked" (closed padlock).

- [ ] **Step 4: GET /games — Try it out**

Expand `GET /games`, click **Try it out**, leave the params empty, click **Execute**.

Expected: HTTP 200, response body is a JSON object with `total`, `offset`, `limit`, `items[]`. The `items` array contains your real Playnite games.

- [ ] **Step 5: POST /games (writes ON)**

Expand `POST /games`, click **Try it out**, paste request body `{ "name": "Smoke test game" }`, click **Execute**.

Expected: HTTP 201, response body has the new game with a fresh `id`. Switch to Playnite's main window — the game **"Smoke test game"** should appear in the library. (Delete it after the test to avoid clutter.)

- [ ] **Step 6: POST /games (writes OFF)**

Open the plugin settings, set **EnableWrites** to OFF, save. Reload Swagger UI in the browser (the settings change is live — no Playnite restart required).

Repeat the POST /games "Try it out" with `{ "name": "should fail" }`.

Expected: HTTP 403, body `{ "error": "writes_disabled", "message": "Write operations are disabled in plugin settings." }`.

Re-enable **EnableWrites** before continuing.

- [ ] **Step 7: /openapi.json directly, no auth header**

In the browser address bar (or via curl), hit `http://127.0.0.1:<port>/openapi.json` directly with no `Authorization` header.

Expected: HTTP 200, valid JSON document. Verifies the `AllowAnonymous` carve-out works.

curl version:
```bash
curl -i http://127.0.0.1:<port>/openapi.json
```

- [ ] **Step 8: /games with no auth header → 401**

```bash
curl -i http://127.0.0.1:<port>/games
```

Expected: HTTP 401, `WWW-Authenticate: Bearer` header, body `{ "error": "unauthorized", "message": "Missing or invalid bearer token." }`. Verifies the carve-out is **scoped** to the docs routes — `/games` still requires auth.

- [ ] **Step 9: Unknown path → 404 (no auth required)**

```bash
curl -i http://127.0.0.1:<port>/totally-fake-path
```

Expected: HTTP 404 (not 401). Verifies the dispatch reorder — unknown paths return 404 even without an `Authorization` header.

- [ ] **Step 10: Mark complete**

If steps 1–9 all match expectations, the implementation is **complete**. No commit needed for this task — it's verification only.

If any step fails, debug:
- Asset 404 in step 2 → revisit Task 1 (vendoring) or Task 11 (resource names)
- Wrong status in step 6 → revisit the write-gate logic in Task 6
- 401 in step 7 or 9 → revisit Task 6 (`AllowAnonymous` bypass) or Task 12 (route registrations missing `.AllowAnonymous()`)
- Empty/wrong spec in step 7 → inspect the response, then revisit Task 9 (`OpenApiBuilder`) or Task 12 (the `.Describes(...)` metadata)

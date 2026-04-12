# `GET /games` — filters and sort

**Status:** approved (design)
**Date:** 2026-04-12
**Author:** Nathan + Claude

## 1. Goal

Grow the `GET /games` endpoint so clients can narrow and order the results
without pulling the full library. Today the endpoint supports only
pagination and a name-substring filter; after this change it also accepts a
curated set of boolean, ID, multi-ID, and range filters (including date
ranges), plus a sort parameter with a whitelisted field list and optional
descending prefix.

The response shape (`GamePage`) is unchanged. The existing three query
parameters (`offset`, `limit`, `q`) are preserved verbatim. A client that
hits the URL exactly as it does today gets exactly the same result.

## 2. Constraints

- **No changes outside `GET /games`.** No new controllers, no new schemas
  in `OpenApiSchemas`, no changes to PATCH/POST/DELETE behavior. Only
  `Controllers/GamesController.List`, its helpers, and the route
  registration in `PlayniteApiServerPlugin.BuildRouter` are touched.
- **Response shape is frozen.** Clients get the same `{total, offset, limit,
  items}` envelope they get today, just with more selective `items`.
- **Existing parsing patterns apply.** Query-string parsing goes through
  `RequestContext.Query`, parse errors throw `ApiException(400, ...)`, GUID
  parsing uses the same style as `HttpExtensions.ParseGuidOrThrow`.
- **OpenAPI metadata goes on the existing route registration.** No new
  schemas, no changes to `RouteBuilder` or `OpenApiBuilder`. Each new
  parameter is one `.QueryParam(...)` call.
- **Newtonsoft.Json 10.0 applies.** (No direct impact for this task — there
  is no JSON I/O change — but flagged so nothing else assumes newer APIs.)

## 3. Decisions

| Topic                              | Decision                                                                          |
|------------------------------------|-----------------------------------------------------------------------------------|
| Filter set size                    | **Curated wider set — 17 new filters plus a sort parameter**, covering everything Playnite's own UI typically reaches for. |
| Filter query-param naming          | **Field-name-first with explicit `Min`/`Max` suffixes for ranges.** One param per filter, names match JSON property names. |
| Multi-ID relationship semantics    | **Match-any (OR).** `genreIds=a,b` returns games that have genre A or genre B. |
| Sort syntax                        | **Single field, prefix `-` for descending.** `sort=-lastActivity`. Default `name` ascending. Whitelist of sortable fields. |
| Hidden-games default               | **No filter = all games.** `hidden` is just another filter; omitting it returns everything. |

## 4. Query parameters

After this change, `GET /games` accepts 21 query parameters. All are
optional; missing params mean "no filter applied".

### 4.1 Pagination (unchanged)

| Name     | Type    | Default | Notes                                           |
|----------|---------|---------|-------------------------------------------------|
| `offset` | integer | 0       | Non-negative; values < 0 clamped to 0.          |
| `limit`  | integer | 100     | Clamped to [1, 1000]; ≤0 becomes 100.           |
| `q`      | string  | —       | Substring filter on `Name`, case-insensitive.   |

### 4.2 Boolean filters

| Name          | Type    | Notes                                                         |
|---------------|---------|---------------------------------------------------------------|
| `isInstalled` | boolean | `true` / `false` (case-insensitive). Filter on `Game.IsInstalled`. |
| `favorite`    | boolean | Filter on `Game.Favorite`.                                    |
| `hidden`      | boolean | Filter on `Game.Hidden`. Omit to include all.                 |

### 4.3 Single-ID filters

| Name                 | Type        | Notes                                                         |
|----------------------|-------------|---------------------------------------------------------------|
| `sourceId`           | string uuid | Exact-match on `Game.SourceId`. A game whose SourceId is `Guid.Empty` never matches. |
| `completionStatusId` | string uuid | Exact-match on `Game.CompletionStatusId`.                     |

### 4.4 Multi-ID filters (match-any)

All seven take a comma-separated list of uuids and use **match-any** (OR)
semantics — a game passes the filter if any of its relationship IDs appears
in the filter value list. Empty elements in the list (from stray commas)
are silently dropped; any malformed uuid fails the whole request with a
`400`.

| Name           | Type              | Matches                                  |
|----------------|-------------------|------------------------------------------|
| `platformIds`  | comma-sep uuids   | `Game.PlatformIds ∩ filter ≠ ∅`          |
| `genreIds`     | comma-sep uuids   | `Game.GenreIds ∩ filter ≠ ∅`             |
| `developerIds` | comma-sep uuids   | `Game.DeveloperIds ∩ filter ≠ ∅`         |
| `publisherIds` | comma-sep uuids   | `Game.PublisherIds ∩ filter ≠ ∅`         |
| `categoryIds`  | comma-sep uuids   | `Game.CategoryIds ∩ filter ≠ ∅`          |
| `tagIds`       | comma-sep uuids   | `Game.TagIds ∩ filter ≠ ∅`               |
| `featureIds`   | comma-sep uuids   | `Game.FeatureIds ∩ filter ≠ ∅`           |

### 4.5 Range filters

Bounds are **inclusive** on both sides. Where both `Min` and `Max` are
specified and `Min > Max`, the whole request fails with `400`.

| Name                  | Type           | Notes                                                        |
|-----------------------|----------------|--------------------------------------------------------------|
| `playtimeMin`         | integer        | Seconds. Matches `Game.Playtime >= value`.                   |
| `playtimeMax`         | integer        | Seconds. Matches `Game.Playtime <= value`.                   |
| `userScoreMin`        | integer (0-100)| Matches `Game.UserScore >= value`. Games with null UserScore never match. |
| `lastActivityAfter`   | ISO 8601       | Matches `Game.LastActivity >= value`. Games with null LastActivity never match. |
| `lastActivityBefore`  | ISO 8601       | Matches `Game.LastActivity <= value`. Games with null LastActivity never match. |

### 4.6 Sort

| Name   | Type   | Default | Notes                                                |
|--------|--------|---------|------------------------------------------------------|
| `sort` | string | `name`  | Field name, optional `-` prefix for descending.      |

**Whitelist of sortable fields:**

```
name, added, modified, lastActivity, releaseDate,
playtime, playCount, userScore, communityScore, criticScore
```

Unknown field → `400 bad_request` with a message enumerating the whitelist.
For nullable fields (`added`, `modified`, `lastActivity`, `releaseDate`,
`userScore`, `communityScore`, `criticScore`), nulls sort **first** in
ascending order and **last** in descending order (.NET default and Playnite
UI behavior).

## 5. Parsing and error contract

All parsing errors raise `ApiException(400, "<specific message>")` which the
router translates into the standard `{error: "bad_request", message: "..."}`
JSON body. No silent coercion, no best-effort parsing.

**Boolean.** `"true"` / `"false"`, case-insensitive. Anything else →
`"Invalid boolean for '<name>': '<value>'. Use 'true' or 'false'."`

**Single uuid.** `Guid.TryParse`. On failure →
`"Invalid GUID for '<name>': '<value>'."`

**Multi uuid.** Split on `,`, trim whitespace from each element, drop empty
strings, parse each remainder as a uuid. On any malformed element →
`"Invalid GUID for '<name>'[<index>]: '<value>'."` where `<index>` is the
0-based position of the bad element in the filtered list.

**Integer / long.** `int.TryParse` / `ulong.TryParse` with
`CultureInfo.InvariantCulture`. On failure → `"Invalid integer for
'<name>': '<value>'."`

**Range validation.** After parsing both bounds, if `min > max` → `"Invalid
range: <minName> (<min>) is greater than <maxName> (<max>)."`. Bounds are
inclusive.

**Dates.** `DateTime.TryParse` with `CultureInfo.InvariantCulture` and
`DateTimeStyles.RoundtripKind`. Accepts `yyyy-MM-dd` or
`yyyy-MM-ddTHH:mm:ss[Z]`. On failure → `"Invalid ISO 8601 date for
'<name>': '<value>'."`

**Sort.** Strip at most one leading `-` from the value (more than one →
the leftover isn't a valid whitelist entry and the lookup fails). Look the
remaining field name up in the whitelist (case-insensitive). Unknown field
→ `"Unknown sort field '<name>'. Allowed: name, added, modified,
lastActivity, releaseDate, playtime, playCount, userScore, communityScore,
criticScore."`

**Empty values.** `?playtimeMin=` (present but blank) → `400`. Missing
entirely (`?` or `?other=foo`) → filter not applied.

**Fail-whole-request principle.** A single malformed parameter fails the
entire request. There is no "best-effort parse with partial results".

## 6. Filter composition and application order

Filters compose with **AND** — a game must pass every active filter to be
included. There is no top-level OR combinator, no `?or=...`, and no way to
express "favorite OR installed" in a single request. Clients needing that
make two requests and union client-side.

Inside `GamesController.List`, the execution order is:

```
1. Parse all query params via GamesQuery.Parse  → GamesQuery (validated)
2. Materialize db.Games.Cast<Game>() into List<Game>
3. Apply filters in this order (each reduces the working set):
   a. Boolean filters    (isInstalled, favorite, hidden)
   b. Single-ID filters  (sourceId, completionStatusId)
   c. Range filters      (playtime*, userScoreMin, lastActivity*)
   d. Multi-ID filters   (platformIds, genreIds, developerIds, publisherIds,
                          categoryIds, tagIds, featureIds)
   e. Substring filter   (q)
4. Apply sort (GamesQuerySort.Apply)
5. Apply offset/limit (Skip/Take)
6. Write the page (existing WriteJson path)
```

**Why that order?** Cheap O(1) field compares run first to shrink the set
before expensive multi-value and substring checks run. The order is
hand-written, not cost-model-driven — for personal libraries of
~1,000–10,000 games the difference is negligible and the predictable order
is easier to read.

**Null semantics inside filters.** Nullable fields (`LastActivity`,
`UserScore`, etc.) fail positive predicates. There is no way in v1 to
express "games where sourceId is null" or "games that have never been
played". If needed later, add a targeted flag like `hasSource=true|false`
or `unplayed=true`.

## 7. Code structure

### 7.1 New files

```
Controllers/
  GamesQuery.cs         POCO + Parse(Dictionary<string,string>) static
  GamesQueryFilter.cs   static Apply(IEnumerable<Game>, GamesQuery)
  GamesQuerySort.cs     static Apply(List<Game>, GamesQuery)
```

All three are `internal sealed` / `internal static` and live alongside the
existing controllers. They are pure functions over `GamesQuery` — no
dependencies on Playnite SDK types besides `Game`.

**`Controllers/GamesQuery.cs`** (sketch):

```csharp
internal enum SortField
{
    Name, Added, Modified, LastActivity, ReleaseDate,
    Playtime, PlayCount, UserScore, CommunityScore, CriticScore,
}

internal sealed class GamesQuery
{
    public int Offset { get; set; }
    public int Limit { get; set; }
    public string Q { get; set; }

    public bool? IsInstalled { get; set; }
    public bool? Favorite { get; set; }
    public bool? Hidden { get; set; }

    public Guid? SourceId { get; set; }
    public Guid? CompletionStatusId { get; set; }

    public List<Guid> PlatformIds { get; set; }
    public List<Guid> GenreIds { get; set; }
    public List<Guid> DeveloperIds { get; set; }
    public List<Guid> PublisherIds { get; set; }
    public List<Guid> CategoryIds { get; set; }
    public List<Guid> TagIds { get; set; }
    public List<Guid> FeatureIds { get; set; }

    public ulong? PlaytimeMin { get; set; }
    public ulong? PlaytimeMax { get; set; }
    public int? UserScoreMin { get; set; }
    public DateTime? LastActivityAfter { get; set; }
    public DateTime? LastActivityBefore { get; set; }

    public SortField SortField { get; set; }
    public bool SortDescending { get; set; }

    public static GamesQuery Parse(Dictionary<string, string> query);
}
```

**`Controllers/GamesQueryFilter.cs`**: one static method, one big chained
`IEnumerable<Game>` expression, clauses guarded by `if (q.Field.HasValue)`
or `if (q.Ids != null && q.Ids.Count > 0)` so missing filters are skipped
entirely.

**`Controllers/GamesQuerySort.cs`**: one static method, one switch on
`SortField` returning the right `OrderBy`/`OrderByDescending` expression.

### 7.2 Modified files

**`Controllers/GamesController.cs:List`** — becomes short again:

```csharp
public void List(RequestContext r)
{
    var q = GamesQuery.Parse(r.Query);

    IEnumerable<Game> source = db.Games.Cast<Game>();
    source = GamesQueryFilter.Apply(source, q);
    var materialized = source.ToList();

    var total = materialized.Count;
    var page = GamesQuerySort.Apply(materialized, q)
        .Skip(q.Offset)
        .Take(q.Limit)
        .ToList();

    r.WriteJson(200, new
    {
        total,
        offset = q.Offset,
        limit = q.Limit,
        items = page,
    });
}
```

The legacy `GetInt` helper local to `GamesController` is removed in this
change; it was only used by `List`, not by Patch/Delete (those parse ids
via `HttpExtensions.ParseGuidOrThrow`). The new query-string parsing
helpers live in `GamesQuery.Parse`.

**`PlayniteApiServerPlugin.cs:BuildRouter`** — the `router.Add("GET",
"/games", games.List)` block gains 18 new `.QueryParam(...)` calls plus a
reworded `sort` param. Full shape shown in §8.

**`PlayniteApiServer.csproj`** — 3 new `<Compile>` entries, one per new
file. No new `<EmbeddedResource>` or other item groups.

## 8. OpenAPI impact

The existing `GET /games` operation's `parameters` array grows from 3
entries to 21. No new component schemas are introduced. The `200` response
continues to reference `GamePage`. The operation's `security` and `tags`
are unchanged.

The full metadata block in `BuildRouter`:

```csharp
router.Add("GET", "/games", games.List)
    .Summary("List games")
    .Tags("games")
    // pagination (existing)
    .QueryParam("offset", "integer", "Pagination offset (default 0)")
    .QueryParam("limit",  "integer", "Page size (default 100, max 1000)")
    .QueryParam("q",      "string",  "Substring filter on Name (case-insensitive)")
    // boolean filters
    .QueryParam("isInstalled", "boolean", "Filter by install state")
    .QueryParam("favorite",    "boolean", "Filter by favorite flag")
    .QueryParam("hidden",      "boolean", "Filter by hidden flag. Omit to include all.")
    // single-ID filters
    .QueryParam("sourceId",           "string", "GameSource id (uuid)")
    .QueryParam("completionStatusId", "string", "CompletionStatus id (uuid)")
    // multi-ID filters (match-any)
    .QueryParam("platformIds",  "string", "Comma-separated platform uuids — match-any (OR)")
    .QueryParam("genreIds",     "string", "Comma-separated genre uuids — match-any (OR)")
    .QueryParam("developerIds", "string", "Comma-separated developer (Company) uuids — match-any (OR)")
    .QueryParam("publisherIds", "string", "Comma-separated publisher (Company) uuids — match-any (OR)")
    .QueryParam("categoryIds",  "string", "Comma-separated category uuids — match-any (OR)")
    .QueryParam("tagIds",       "string", "Comma-separated tag uuids — match-any (OR)")
    .QueryParam("featureIds",   "string", "Comma-separated feature uuids — match-any (OR)")
    // ranges
    .QueryParam("playtimeMin",        "integer", "Minimum total play time in seconds (inclusive)")
    .QueryParam("playtimeMax",        "integer", "Maximum total play time in seconds (inclusive)")
    .QueryParam("userScoreMin",       "integer", "Minimum user score 0-100 (inclusive)")
    .QueryParam("lastActivityAfter",  "string",  "ISO 8601 date or datetime (inclusive)")
    .QueryParam("lastActivityBefore", "string",  "ISO 8601 date or datetime (inclusive)")
    // sort
    .QueryParam("sort", "string", "Sort field. Prefix with '-' for descending. Default 'name'. Allowed: name, added, modified, lastActivity, releaseDate, playtime, playCount, userScore, communityScore, criticScore")
    .Response(200, "Paged list of games", OpenApiSchemas.Schemas.GamePage);
```

**One intentional fidelity gap.** The seven multi-ID filters are documented
as `string` rather than OpenAPI `array<string, format=uuid>`. The current
`RouteBuilder.QueryParam` API takes a single type string; supporting proper
array parameters would require a new `QueryParamArray` method on
`RouteBuilder` and new handling in `OpenApiBuilder.ParamToJson` — a real
improvement but scoped out of this task (see §10). The description on each
of those seven params explicitly says "comma-separated uuids", which gives
Swagger UI users the correct contract even if the `type` field is less
precise than it could be.

## 9. Out of scope

- **Multi-field sort** (e.g. `sort=-lastActivity,name`). Secondary sort
  keys are rarely meaningful in personal-use queries. Single-field sort is
  sufficient for v1.
- **Top-level OR / filter DSL** (e.g. `?filter=isInstalled eq true and
  playtime gt 3600`, OData, RSQL). Explicitly rejected in brainstorming —
  the implementation cost and fragile OpenAPI documentation aren't worth
  it at this scope.
- **`communityScoreMin/Max`, `criticScoreMin/Max`.** Filtering by community
  or critic score is rare in practice; `userScoreMin` alone covers the
  common case. Can be added later as a follow-up if you find yourself
  wanting it.
- **`playCountMin/Max`.** The common "unplayed" query is already expressed
  as `playtimeMax=0`. No need for a parallel play-count range.
- **`releaseDateAfter/Before`.** The Playnite `ReleaseDate` struct has
  optional month/day components, which makes range comparisons messier
  than the other date fields. Sorting by `releaseDate` is in v1; filtering
  is not.
- **`hasSource` / `unplayed` null-state flags.** Null-fails-positive is the
  rule in v1. If the null-state queries turn out to be common, a future
  task adds explicit flags.
- **Richer OpenAPI array parameters for multi-ID filters.** A follow-up
  task could add `RouteBuilder.QueryParamArray(name, itemType, itemFormat,
  description)` and the matching `OpenApiBuilder` logic so the seven
  multi-ID filters document as `array<string, format=uuid>` with proper
  form-style comma-delimited serialization. Out of scope here.
- **Pagination tokens / cursor-based pagination.** `offset`/`limit` stays.
- **Filter combinators on the same field.** E.g. "isInstalled true or
  favorite true" cannot be expressed. Two requests.

## 10. Risks and follow-ups

**Performance on large libraries.** The implementation materializes the
full game list, then filters sequentially. For ~10k games and ~18 filter
clauses this is still hundreds of microseconds — not a concern. If library
sizes ever grow to 100k+ and `/games` is hit frequently, profile before
adding more filters; a future task could introduce an index layer or push
filtering down through a smarter `IEnumerable` pipeline.

**Bogus filter GUIDs return zero results, not 400.** `?platformIds=<random>`
produces zero matches rather than validating the GUID against the
platforms table. This is consistent with the existing PATCH validator's
handling of unknown relationship IDs (they're rejected at PATCH time via a
separate `ValidateForeignKeys` step, but the List endpoint has no such
path). Clients expecting "relationship must exist" semantics should
validate IDs against the lookup endpoints before passing them to
`/games`.

**`releaseDate` sortable but not filterable.** Deliberate — see §9. The
sort uses the SDK's normalized `DateTime` field and handles the nullable
outer wrapper.

**Sort stability for ties.** .NET's `OrderBy` is stable, so ties fall back
to the database's internal iteration order. For `sort=name` ties are rare
(and typically on duplicates that the user already considers equivalent).
For `sort=playtime` or `sort=playCount`, ties on `0` are common and the
tie-break order is arbitrary. If this matters, a future task adds a
compound sort (e.g. `.ThenBy(g => g.Name)`) — one-line change in
`GamesQuerySort.Apply`.

**Fidelity gap in OpenAPI array params.** §8 note. Swagger UI renders each
multi-ID filter as a text box rather than a repeatable-uuid input. The
description compensates but this is a real future improvement.

## 11. Verification

Extend the existing spec §8 manual smoke test (the Swagger UI smoke test)
with the following additional steps, run after rebuilding the plugin and
restarting Playnite:

1. **Basic boolean filter.**
   `curl -H "Authorization: Bearer $TOKEN" "$BASE/games?isInstalled=true&limit=5"`
   Expected: 200, all returned `items` have `isInstalled: true`.

2. **Multi-ID match-any filter.** First get two platform IDs from
   `/platforms`. Then:
   `curl -H "Authorization: Bearer $TOKEN" "$BASE/games?platformIds=<guid1>,<guid2>&limit=5"`
   Expected: 200, returned games each have at least one of those platform
   IDs in their `platformIds` array.

3. **Range filter.**
   `curl -H "Authorization: Bearer $TOKEN" "$BASE/games?playtimeMin=3600&sort=-playtime&limit=5"`
   Expected: 200, returned games all have `playtime >= 3600` and are
   sorted in descending order by playtime.

4. **Date range filter.**
   `curl -H "Authorization: Bearer $TOKEN" "$BASE/games?lastActivityAfter=2026-01-01&sort=-lastActivity&limit=5"`
   Expected: 200, returned games all have `lastActivity` on or after
   2026-01-01, sorted descending.

5. **Unknown sort field.**
   `curl -H "Authorization: Bearer $TOKEN" "$BASE/games?sort=nope"`
   Expected: 400 with body
   `{"error":"bad_request","message":"Unknown sort field 'nope'. Allowed: name, added, ..."}`.

6. **Bad boolean.**
   `curl -H "Authorization: Bearer $TOKEN" "$BASE/games?isInstalled=yes"`
   Expected: 400 with body
   `{"error":"bad_request","message":"Invalid boolean for 'isInstalled': 'yes'. Use 'true' or 'false'."}`.

7. **Invalid range.**
   `curl -H "Authorization: Bearer $TOKEN" "$BASE/games?playtimeMin=1000&playtimeMax=500"`
   Expected: 400 with body
   `{"error":"bad_request","message":"Invalid range: playtimeMin (1000) is greater than playtimeMax (500)."}`.

8. **Bad uuid in multi-ID filter.**
   `curl -H "Authorization: Bearer $TOKEN" "$BASE/games?platformIds=valid-guid,not-a-guid"`
   Expected: 400 with body
   `{"error":"bad_request","message":"Invalid GUID for 'platformIds'[1]: 'not-a-guid'."}`.

9. **Unchanged base behavior — regression check.**
   `curl -H "Authorization: Bearer $TOKEN" "$BASE/games?limit=5"`
   Expected: 200, same 5 games that the previous version of the endpoint
   would have returned (sorted ascending by name, no filters).

10. **Swagger UI Try It Out.** Open `/docs`, expand `GET /games`, verify
    that 21 input fields are rendered, and do a Try It Out with
    `isInstalled=true` plus `sort=-lastActivity`. Response renders in the
    Swagger UI panel with the filtered + sorted payload.

If any step fails, the implementation is not complete.

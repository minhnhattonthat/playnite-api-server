# `GET /games` filters and sort — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `GET /games` to accept 17 new curated query-parameter filters (booleans, single IDs, match-any multi-ID lists, numeric ranges, date ranges) plus a whitelisted sort parameter, without breaking the existing `offset` / `limit` / `q` params or the `GamePage` response shape.

**Architecture:** The existing `GamesController.List` is refactored to call three new internal helpers in `Controllers/`: `GamesQuery` (parse + validate query string → value object), `GamesQueryFilter` (chain `Where` clauses per active filter), and `GamesQuerySort` (switch on sortable-field whitelist → `OrderBy` / `OrderByDescending`). Parsing errors all funnel through `ApiException(400, ...)`. OpenAPI metadata is extended in `PlayniteApiServerPlugin.BuildRouter()` with one `.QueryParam(...)` call per new parameter.

**Tech Stack:** .NET Framework 4.6.2, C# 7.3, classic `.csproj`, `System.Linq`, `System.Globalization`, existing `PlayniteApiServer.Server.ApiException`. No new NuGet, no new framework usings beyond what the existing `GamesController` already imports.

---

## Required reading before starting

- Spec: `docs/superpowers/specs/2026-04-12-games-filters-and-sort-design.md` — particularly §4 (parameter table), §5 (error contract), §6 (filter order), §7 (code structure), §11 (smoke test).
- Existing code: `Controllers/GamesController.cs` (the current `List` method you're refactoring; the `AllowedPatchFields` dictionary and the rest of the class stay untouched), `PlayniteApiServerPlugin.cs:BuildRouter` (the `/games` route block you're extending).
- `CLAUDE.md` at the repo root — particularly the "when adding a new route" checklist and the notes about Newtonsoft 10.0 quirks (no direct impact for this task, but worth knowing).

## File structure

### New files

```
Controllers/
  GamesQuery.cs         POCO + SortField enum + Parse(Dictionary<string,string>) static
  GamesQueryFilter.cs   static Apply(IEnumerable<Game>, GamesQuery) → IEnumerable<Game>
  GamesQuerySort.cs     static Apply(List<Game>, GamesQuery) → IOrderedEnumerable<Game>
```

All three are `internal sealed` / `internal static` in the existing `PlayniteApiServer.Controllers` namespace.

### Modified files

```
Controllers/GamesController.cs            List method replaced with slim version; dead GetInt helper removed.
PlayniteApiServerPlugin.cs                BuildRouter /games block extended with 18 new .QueryParam calls.
PlayniteApiServer.csproj                  +3 <Compile> items (one per new file).
```

## Notes that apply to every task

- **Build verification.** Each task ends with `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1`. Running `./build.ps1` directly from bash fails because bash tries to interpret the PowerShell script.
- **Close Playnite before building.** The script deploys into Playnite's extensions folder and cannot overwrite a locked DLL. If you only want to verify compilation without deploying, run MSBuild directly: `powershell -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' PlayniteApiServer.csproj /p:Configuration=Release /nologo /v:m"` (adjust the MSBuild path if your install differs).
- **Working directory** is `C:\Users\Nathan\Projects\PlayniteApiServer` for every command.
- **Commits** — one commit per task. Use the exact `git commit -m "..."` text shown in each task. Commit message convention: short imperative `type:` prefix matching the existing repo history (`feat:`, `refactor:`, etc.).
- **No TDD.** The project has no test framework; the spec explicitly excludes adding one. Verification is `build succeeds` for incremental tasks and the §11 manual smoke test for the final task.
- **No placeholders.** Every code block is the literal C# the engineer should produce.

---

## Task 1: Create `Controllers/GamesQuery.cs`

**Files:**
- Create: `Controllers/GamesQuery.cs`
- Modify: `PlayniteApiServer.csproj`

This task creates the POCO, the `SortField` enum, and the `Parse` static method that consumes the raw query-string dictionary. The file contains all the parsing helpers (`ParseBoolNullable`, `ParseGuidNullable`, etc.) as `private static` methods so the parsing logic lives in one place. The types are unused at this commit point — Task 2 and Task 3 consume them.

- [ ] **Step 1: Create the file**

Write `Controllers/GamesQuery.cs` with **exactly** the following content:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using PlayniteApiServer.Server;

namespace PlayniteApiServer.Controllers
{
    /// <summary>
    /// Whitelist of sortable fields for GET /games. Values are looked up
    /// by name in <see cref="GamesQuery.Parse"/> after stripping an optional
    /// leading '-' (descending prefix).
    /// </summary>
    internal enum SortField
    {
        Name,
        Added,
        Modified,
        LastActivity,
        ReleaseDate,
        Playtime,
        PlayCount,
        UserScore,
        CommunityScore,
        CriticScore,
    }

    /// <summary>
    /// Parsed and validated query parameters for GET /games. Built once per
    /// request via <see cref="Parse"/>; consumed by GamesQueryFilter and
    /// GamesQuerySort. Every filter is nullable (or an empty list) so the
    /// "no filter applied" state is explicit.
    /// </summary>
    internal sealed class GamesQuery
    {
        // Pagination
        public int Offset { get; set; }
        public int Limit { get; set; }
        public string Q { get; set; }

        // Boolean filters
        public bool? IsInstalled { get; set; }
        public bool? Favorite { get; set; }
        public bool? Hidden { get; set; }

        // Single-ID filters
        public Guid? SourceId { get; set; }
        public Guid? CompletionStatusId { get; set; }

        // Multi-ID filters (match-any OR)
        public List<Guid> PlatformIds { get; set; }
        public List<Guid> GenreIds { get; set; }
        public List<Guid> DeveloperIds { get; set; }
        public List<Guid> PublisherIds { get; set; }
        public List<Guid> CategoryIds { get; set; }
        public List<Guid> TagIds { get; set; }
        public List<Guid> FeatureIds { get; set; }

        // Range filters
        public ulong? PlaytimeMin { get; set; }
        public ulong? PlaytimeMax { get; set; }
        public int? UserScoreMin { get; set; }
        public DateTime? LastActivityAfter { get; set; }
        public DateTime? LastActivityBefore { get; set; }

        // Sort
        public SortField SortField { get; set; }
        public bool SortDescending { get; set; }

        public static GamesQuery Parse(Dictionary<string, string> query)
        {
            var q = new GamesQuery();

            // ── Pagination ──────────────────────────────────────────────
            q.Offset = ParseIntWithDefault(query, "offset", 0);
            if (q.Offset < 0) q.Offset = 0;

            q.Limit = ParseIntWithDefault(query, "limit", 100);
            if (q.Limit <= 0) q.Limit = 100;
            if (q.Limit > 1000) q.Limit = 1000;

            if (query.TryGetValue("q", out var qVal) && !string.IsNullOrEmpty(qVal))
            {
                q.Q = qVal;
            }

            // ── Boolean filters ─────────────────────────────────────────
            q.IsInstalled = ParseBoolNullable(query, "isInstalled");
            q.Favorite = ParseBoolNullable(query, "favorite");
            q.Hidden = ParseBoolNullable(query, "hidden");

            // ── Single-ID filters ───────────────────────────────────────
            q.SourceId = ParseGuidNullable(query, "sourceId");
            q.CompletionStatusId = ParseGuidNullable(query, "completionStatusId");

            // ── Multi-ID filters ────────────────────────────────────────
            q.PlatformIds = ParseGuidList(query, "platformIds");
            q.GenreIds = ParseGuidList(query, "genreIds");
            q.DeveloperIds = ParseGuidList(query, "developerIds");
            q.PublisherIds = ParseGuidList(query, "publisherIds");
            q.CategoryIds = ParseGuidList(query, "categoryIds");
            q.TagIds = ParseGuidList(query, "tagIds");
            q.FeatureIds = ParseGuidList(query, "featureIds");

            // ── Range filters ───────────────────────────────────────────
            q.PlaytimeMin = ParseULongNullable(query, "playtimeMin");
            q.PlaytimeMax = ParseULongNullable(query, "playtimeMax");
            q.UserScoreMin = ParseIntNullable(query, "userScoreMin");
            q.LastActivityAfter = ParseDateTimeNullable(query, "lastActivityAfter");
            q.LastActivityBefore = ParseDateTimeNullable(query, "lastActivityBefore");

            // Range consistency (inclusive bounds; min > max is an error)
            if (q.PlaytimeMin.HasValue && q.PlaytimeMax.HasValue && q.PlaytimeMin.Value > q.PlaytimeMax.Value)
            {
                throw new ApiException(400,
                    "Invalid range: playtimeMin (" + q.PlaytimeMin.Value +
                    ") is greater than playtimeMax (" + q.PlaytimeMax.Value + ").");
            }
            if (q.LastActivityAfter.HasValue && q.LastActivityBefore.HasValue &&
                q.LastActivityAfter.Value > q.LastActivityBefore.Value)
            {
                throw new ApiException(400,
                    "Invalid range: lastActivityAfter (" + q.LastActivityAfter.Value.ToString("o", CultureInfo.InvariantCulture) +
                    ") is greater than lastActivityBefore (" + q.LastActivityBefore.Value.ToString("o", CultureInfo.InvariantCulture) + ").");
            }

            // ── Sort ────────────────────────────────────────────────────
            ParseSort(query, q);

            return q;
        }

        // ─── private parsing helpers ────────────────────────────────────

        private static int ParseIntWithDefault(Dictionary<string, string> query, string name, int defaultValue)
        {
            if (!query.TryGetValue(name, out var raw)) return defaultValue;
            if (string.IsNullOrEmpty(raw))
            {
                throw new ApiException(400, "Empty value for '" + name + "'.");
            }
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                throw new ApiException(400, "Invalid integer for '" + name + "': '" + raw + "'.");
            }
            return v;
        }

        private static int? ParseIntNullable(Dictionary<string, string> query, string name)
        {
            if (!query.TryGetValue(name, out var raw)) return null;
            if (string.IsNullOrEmpty(raw))
            {
                throw new ApiException(400, "Empty value for '" + name + "'.");
            }
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                throw new ApiException(400, "Invalid integer for '" + name + "': '" + raw + "'.");
            }
            return v;
        }

        private static ulong? ParseULongNullable(Dictionary<string, string> query, string name)
        {
            if (!query.TryGetValue(name, out var raw)) return null;
            if (string.IsNullOrEmpty(raw))
            {
                throw new ApiException(400, "Empty value for '" + name + "'.");
            }
            if (!ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                throw new ApiException(400, "Invalid integer for '" + name + "': '" + raw + "'.");
            }
            return v;
        }

        private static bool? ParseBoolNullable(Dictionary<string, string> query, string name)
        {
            if (!query.TryGetValue(name, out var raw)) return null;
            if (string.IsNullOrEmpty(raw))
            {
                throw new ApiException(400, "Empty value for '" + name + "'.");
            }
            if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new ApiException(400, "Invalid boolean for '" + name + "': '" + raw + "'. Use 'true' or 'false'.");
        }

        private static Guid? ParseGuidNullable(Dictionary<string, string> query, string name)
        {
            if (!query.TryGetValue(name, out var raw)) return null;
            if (string.IsNullOrEmpty(raw))
            {
                throw new ApiException(400, "Empty value for '" + name + "'.");
            }
            if (!Guid.TryParse(raw, out var v))
            {
                throw new ApiException(400, "Invalid GUID for '" + name + "': '" + raw + "'.");
            }
            return v;
        }

        private static List<Guid> ParseGuidList(Dictionary<string, string> query, string name)
        {
            if (!query.TryGetValue(name, out var raw)) return null;
            if (string.IsNullOrEmpty(raw))
            {
                throw new ApiException(400, "Empty value for '" + name + "'.");
            }

            var parts = raw.Split(',');
            var result = new List<Guid>();
            // idx tracks position in the *filtered* list (after dropping empties),
            // matching the error-message contract in the spec.
            int idx = 0;
            foreach (var p in parts)
            {
                var trimmed = p.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (!Guid.TryParse(trimmed, out var g))
                {
                    throw new ApiException(400,
                        "Invalid GUID for '" + name + "'[" + idx + "]: '" + trimmed + "'.");
                }
                result.Add(g);
                idx++;
            }
            return result;
        }

        private static DateTime? ParseDateTimeNullable(Dictionary<string, string> query, string name)
        {
            if (!query.TryGetValue(name, out var raw)) return null;
            if (string.IsNullOrEmpty(raw))
            {
                throw new ApiException(400, "Empty value for '" + name + "'.");
            }
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v))
            {
                throw new ApiException(400,
                    "Invalid ISO 8601 date for '" + name + "': '" + raw + "'.");
            }
            return v;
        }

        private static void ParseSort(Dictionary<string, string> query, GamesQuery q)
        {
            // Missing sort → default (name ascending).
            if (!query.TryGetValue("sort", out var raw))
            {
                q.SortField = SortField.Name;
                q.SortDescending = false;
                return;
            }
            if (string.IsNullOrEmpty(raw))
            {
                throw new ApiException(400, "Empty value for 'sort'.");
            }

            bool desc = false;
            var field = raw;
            if (field.StartsWith("-", StringComparison.Ordinal))
            {
                desc = true;
                field = field.Substring(1);
            }

            switch (field.ToLowerInvariant())
            {
                case "name":           q.SortField = SortField.Name; break;
                case "added":          q.SortField = SortField.Added; break;
                case "modified":       q.SortField = SortField.Modified; break;
                case "lastactivity":   q.SortField = SortField.LastActivity; break;
                case "releasedate":    q.SortField = SortField.ReleaseDate; break;
                case "playtime":       q.SortField = SortField.Playtime; break;
                case "playcount":      q.SortField = SortField.PlayCount; break;
                case "userscore":      q.SortField = SortField.UserScore; break;
                case "communityscore": q.SortField = SortField.CommunityScore; break;
                case "criticscore":    q.SortField = SortField.CriticScore; break;
                default:
                    throw new ApiException(400,
                        "Unknown sort field '" + field + "'. Allowed: name, added, modified, " +
                        "lastActivity, releaseDate, playtime, playCount, userScore, " +
                        "communityScore, criticScore.");
            }
            q.SortDescending = desc;
        }
    }
}
```

Key details a reviewer should verify against the spec (§4 and §5):

- **`Parse` returns a fully-validated object or throws `ApiException(400, ...)`.** No partial results, no "silently ignore bad input".
- **The offset/limit clamping rules** (offset < 0 → 0; limit ≤ 0 → 100; limit > 1000 → 1000) preserve the existing `GamesController.List` behavior.
- **Empty-string values throw 400**, while missing keys return null/default. This is a behavior change from the legacy `GetInt` helper, which silently fell back to defaults on bad input.
- **`ParseGuidList` increments `idx` only on successful parses**, so the error message references the index in the filtered list (after dropping empty elements), matching §5's contract.
- **Sort parsing strips at most one leading `-`.** `--name` becomes `-name`, which isn't in the whitelist → 400.
- **Range validation uses inclusive bounds.** `min > max` (not `min >= max`) is the error condition.
- **All error-message string constants match §5 exactly** so the smoke test assertions line up.

- [ ] **Step 2: Add Compile entry to PlayniteApiServer.csproj**

In the existing Compile `<ItemGroup>`, add (next to the other `Controllers\...` entries, e.g. right after `<Compile Include="Controllers\GamesController.cs" />`):

```xml
    <Compile Include="Controllers\GamesQuery.cs" />
```

Use backslashes to match the rest of the file.

- [ ] **Step 3: Build**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
```

Expected: build succeeds. No warnings related to `GamesQuery.cs`. The new types are unused at this point — Tasks 2 and 3 will consume them.

- [ ] **Step 4: Commit**

```bash
git add Controllers/GamesQuery.cs PlayniteApiServer.csproj
git commit -m "$(cat <<'EOF'
feat: add GamesQuery parser for /games filters and sort

POCO + SortField enum + Parse(Dictionary) that consumes the raw query
string and returns a validated value object. All parse failures throw
ApiException(400, ...) with the error-message shapes defined in the
spec. Pagination clamping rules preserved from the legacy GetInt path;
empty values now 400 instead of silently falling back.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Create `Controllers/GamesQueryFilter.cs`

**Files:**
- Create: `Controllers/GamesQueryFilter.cs`
- Modify: `PlayniteApiServer.csproj`

Static helper that takes an `IEnumerable<Game>` and a `GamesQuery`, chains `Where` clauses per active filter in the order specified in the spec (cheapest predicates first), and returns the filtered enumerable. Pure function, no side effects, one method. The file is unused at this commit point — Task 4 wires it into `GamesController.List`.

- [ ] **Step 1: Create the file**

Write `Controllers/GamesQueryFilter.cs` with **exactly** the following content:

```csharp
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;

namespace PlayniteApiServer.Controllers
{
    /// <summary>
    /// Applies the active filters from a <see cref="GamesQuery"/> to a game
    /// sequence, in spec-defined order (cheapest predicates first). Missing
    /// filters are skipped entirely; each clause is guarded by a HasValue
    /// or non-empty-list check.
    /// </summary>
    internal static class GamesQueryFilter
    {
        public static IEnumerable<Game> Apply(IEnumerable<Game> source, GamesQuery q)
        {
            // ── 1. Boolean filters (O(1) per game) ──────────────────────
            if (q.IsInstalled.HasValue)
            {
                var v = q.IsInstalled.Value;
                source = source.Where(g => g.IsInstalled == v);
            }
            if (q.Favorite.HasValue)
            {
                var v = q.Favorite.Value;
                source = source.Where(g => g.Favorite == v);
            }
            if (q.Hidden.HasValue)
            {
                var v = q.Hidden.Value;
                source = source.Where(g => g.Hidden == v);
            }

            // ── 2. Single-ID filters (O(1) per game) ────────────────────
            if (q.SourceId.HasValue)
            {
                var v = q.SourceId.Value;
                source = source.Where(g => g.SourceId == v);
            }
            if (q.CompletionStatusId.HasValue)
            {
                var v = q.CompletionStatusId.Value;
                source = source.Where(g => g.CompletionStatusId == v);
            }

            // ── 3. Range filters ────────────────────────────────────────
            if (q.PlaytimeMin.HasValue)
            {
                var v = q.PlaytimeMin.Value;
                source = source.Where(g => g.Playtime >= v);
            }
            if (q.PlaytimeMax.HasValue)
            {
                var v = q.PlaytimeMax.Value;
                source = source.Where(g => g.Playtime <= v);
            }
            if (q.UserScoreMin.HasValue)
            {
                var v = q.UserScoreMin.Value;
                source = source.Where(g => g.UserScore.HasValue && g.UserScore.Value >= v);
            }
            if (q.LastActivityAfter.HasValue)
            {
                var v = q.LastActivityAfter.Value;
                source = source.Where(g => g.LastActivity.HasValue && g.LastActivity.Value >= v);
            }
            if (q.LastActivityBefore.HasValue)
            {
                var v = q.LastActivityBefore.Value;
                source = source.Where(g => g.LastActivity.HasValue && g.LastActivity.Value <= v);
            }

            // ── 4. Multi-ID filters (match-any OR) ──────────────────────
            if (q.PlatformIds != null && q.PlatformIds.Count > 0)
            {
                var set = q.PlatformIds;
                source = source.Where(g => g.PlatformIds != null && g.PlatformIds.Any(id => set.Contains(id)));
            }
            if (q.GenreIds != null && q.GenreIds.Count > 0)
            {
                var set = q.GenreIds;
                source = source.Where(g => g.GenreIds != null && g.GenreIds.Any(id => set.Contains(id)));
            }
            if (q.DeveloperIds != null && q.DeveloperIds.Count > 0)
            {
                var set = q.DeveloperIds;
                source = source.Where(g => g.DeveloperIds != null && g.DeveloperIds.Any(id => set.Contains(id)));
            }
            if (q.PublisherIds != null && q.PublisherIds.Count > 0)
            {
                var set = q.PublisherIds;
                source = source.Where(g => g.PublisherIds != null && g.PublisherIds.Any(id => set.Contains(id)));
            }
            if (q.CategoryIds != null && q.CategoryIds.Count > 0)
            {
                var set = q.CategoryIds;
                source = source.Where(g => g.CategoryIds != null && g.CategoryIds.Any(id => set.Contains(id)));
            }
            if (q.TagIds != null && q.TagIds.Count > 0)
            {
                var set = q.TagIds;
                source = source.Where(g => g.TagIds != null && g.TagIds.Any(id => set.Contains(id)));
            }
            if (q.FeatureIds != null && q.FeatureIds.Count > 0)
            {
                var set = q.FeatureIds;
                source = source.Where(g => g.FeatureIds != null && g.FeatureIds.Any(id => set.Contains(id)));
            }

            // ── 5. Substring filter (most expensive, last) ──────────────
            if (!string.IsNullOrWhiteSpace(q.Q))
            {
                var needle = q.Q.Trim();
                source = source.Where(g => g.Name != null &&
                    g.Name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return source;
        }
    }
}
```

**Why the local `var v = q.Field.Value;` pattern?** The lambdas close over `v`, not `q.Field`. Without this, C# 7.3 would re-evaluate `q.Field.Value` on every predicate call — not a correctness issue, but marginally slower, and the local makes the intent obvious. The multi-ID filters use `var set = q.PlatformIds;` for the same reason.

**Why no `HashSet` for the multi-ID lookups?** The filter lists are typically 1–3 elements. `List.Contains` at that size is ~as fast as `HashSet.Contains` and avoids the allocation. If you ever hit a case where `platformIds` has 100+ entries, swap to a `HashSet<Guid>` inline — it's a one-line change.

- [ ] **Step 2: Add Compile entry to PlayniteApiServer.csproj**

```xml
    <Compile Include="Controllers\GamesQueryFilter.cs" />
```

Place it right after the `GamesQuery.cs` entry.

- [ ] **Step 3: Build**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
```

Expected: build succeeds. The new static class is unused — Task 4 will be the first caller.

- [ ] **Step 4: Commit**

```bash
git add Controllers/GamesQueryFilter.cs PlayniteApiServer.csproj
git commit -m "$(cat <<'EOF'
feat: add GamesQueryFilter for /games filter chain

Static Apply(IEnumerable<Game>, GamesQuery) that chains Where clauses
in spec-defined order: booleans → single-ID → ranges → multi-ID → q.
Nullable filters fail positive predicates (games with null UserScore
never match userScoreMin, etc.). Missing filters are skipped entirely.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Create `Controllers/GamesQuerySort.cs`

**Files:**
- Create: `Controllers/GamesQuerySort.cs`
- Modify: `PlayniteApiServer.csproj`

Static helper that takes a `List<Game>` and a `GamesQuery`, switches on the `SortField` enum, and returns the correctly ordered sequence. Ten branches (one per whitelist entry), no special cases beyond that. The file is unused at this commit point — Task 4 wires it in.

- [ ] **Step 1: Create the file**

Write `Controllers/GamesQuerySort.cs` with **exactly** the following content:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;

namespace PlayniteApiServer.Controllers
{
    /// <summary>
    /// Applies the sort selection from a <see cref="GamesQuery"/> to a
    /// materialized game list. Ten branches map the <see cref="SortField"/>
    /// enum to <c>OrderBy</c> / <c>OrderByDescending</c>; .NET's default
    /// nullable comparer places nulls first in ascending order and last
    /// in descending order, matching Playnite's own UI behavior.
    /// </summary>
    internal static class GamesQuerySort
    {
        public static IOrderedEnumerable<Game> Apply(List<Game> source, GamesQuery q)
        {
            switch (q.SortField)
            {
                case SortField.Name:
                    return q.SortDescending
                        ? source.OrderByDescending(g => g.Name, StringComparer.OrdinalIgnoreCase)
                        : source.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase);

                case SortField.Added:
                    return q.SortDescending
                        ? source.OrderByDescending(g => g.Added)
                        : source.OrderBy(g => g.Added);

                case SortField.Modified:
                    return q.SortDescending
                        ? source.OrderByDescending(g => g.Modified)
                        : source.OrderBy(g => g.Modified);

                case SortField.LastActivity:
                    return q.SortDescending
                        ? source.OrderByDescending(g => g.LastActivity)
                        : source.OrderBy(g => g.LastActivity);

                case SortField.ReleaseDate:
                    // ReleaseDate is a Playnite struct; pull the normalized
                    // DateTime via the ?. operator so nulls sort correctly.
                    return q.SortDescending
                        ? source.OrderByDescending(g => g.ReleaseDate?.Date)
                        : source.OrderBy(g => g.ReleaseDate?.Date);

                case SortField.Playtime:
                    return q.SortDescending
                        ? source.OrderByDescending(g => g.Playtime)
                        : source.OrderBy(g => g.Playtime);

                case SortField.PlayCount:
                    return q.SortDescending
                        ? source.OrderByDescending(g => g.PlayCount)
                        : source.OrderBy(g => g.PlayCount);

                case SortField.UserScore:
                    return q.SortDescending
                        ? source.OrderByDescending(g => g.UserScore)
                        : source.OrderBy(g => g.UserScore);

                case SortField.CommunityScore:
                    return q.SortDescending
                        ? source.OrderByDescending(g => g.CommunityScore)
                        : source.OrderBy(g => g.CommunityScore);

                case SortField.CriticScore:
                    return q.SortDescending
                        ? source.OrderByDescending(g => g.CriticScore)
                        : source.OrderBy(g => g.CriticScore);

                default:
                    // Unreachable: SortField is validated in GamesQuery.Parse
                    // against the same whitelist. Guarding anyway so a future
                    // enum addition without a matching case fails loudly.
                    throw new InvalidOperationException(
                        "Unknown SortField value: " + q.SortField);
            }
        }
    }
}
```

**Why `g.ReleaseDate?.Date`?** `Game.ReleaseDate` is `Nullable<ReleaseDate>`, where `ReleaseDate` is a Playnite SDK struct with a public `Date` (DateTime) field. The `?.` operator on a nullable struct unwraps to the underlying value's field and re-wraps in `Nullable<DateTime>`. Nulls sort consistently against other nulls and before/after non-null dates per the default nullable comparer.

**Why `StringComparer.OrdinalIgnoreCase` for name?** Matches the existing `GamesController.List` ordering behavior (the legacy code explicitly passed the same comparer). This preserves the current sort order for clients that don't specify a `sort` param.

- [ ] **Step 2: Add Compile entry to PlayniteApiServer.csproj**

```xml
    <Compile Include="Controllers\GamesQuerySort.cs" />
```

Place it right after the `GamesQueryFilter.cs` entry.

- [ ] **Step 3: Build**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add Controllers/GamesQuerySort.cs PlayniteApiServer.csproj
git commit -m "$(cat <<'EOF'
feat: add GamesQuerySort with 10-field whitelist

Static Apply(List<Game>, GamesQuery) switching on SortField → OrderBy
or OrderByDescending. ReleaseDate uses g.ReleaseDate?.Date so the
nullable Playnite struct sorts via its underlying DateTime. Name sort
preserves the OrdinalIgnoreCase comparer the legacy List method used.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Refactor `GamesController.List` to use the new helpers

**Files:**
- Modify: `Controllers/GamesController.cs`

Replace the current `List` method with the slim 16-line version that delegates parsing, filtering, and sorting to the helpers from Tasks 1–3. Remove the now-unused `GetInt` private helper. No changes to any other method in the file (`Get`, `Create`, `Patch`, `Delete`, `ValidateForeignKeys`, `RequireAll`, `InvokeOnUi`, `AllowedPatchFields` — all untouched).

- [ ] **Step 1: Read the current List method for orientation**

```bash
grep -n "public void List" Controllers/GamesController.cs
grep -n "private static int GetInt" Controllers/GamesController.cs
```

Confirm both symbols exist. You'll be replacing one and deleting the other.

- [ ] **Step 2: Replace the `List` method body**

Open `Controllers/GamesController.cs`. Find the current `List` method — it starts around line 50 with `public void List(RequestContext r)` and runs to its closing `}` near line 84. The current body is:

```csharp
        public void List(RequestContext r)
        {
            var offset = GetInt(r.Query, "offset", 0);
            var limit = GetInt(r.Query, "limit", 100);
            if (offset < 0) offset = 0;
            if (limit <= 0) limit = 100;
            if (limit > 1000) limit = 1000;

            r.Query.TryGetValue("q", out var q);

            IEnumerable<Game> source = db.Games.Cast<Game>();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                source = source.Where(g => g.Name != null &&
                    g.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var materialized = source.ToList();
            var total = materialized.Count;

            var page = materialized
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Skip(offset)
                .Take(limit)
                .ToList();

            r.WriteJson(200, new
            {
                total,
                offset,
                limit,
                items = page,
            });
        }
```

Replace the entire method body with:

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

- [ ] **Step 3: Delete the unused `GetInt` helper**

Find the `private static int GetInt` method near the bottom of `GamesController.cs` (around line 211). It currently looks like this:

```csharp
        private static int GetInt(Dictionary<string, string> query, string key, int defaultValue)
        {
            if (query.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed))
            {
                return parsed;
            }
            return defaultValue;
        }
```

Delete the method entirely (including its trailing blank line). It was only called by `List`, which no longer uses it. Leaving it would be dead code.

**Do NOT touch any other method.** The `InvokeOnUi`, `ValidateForeignKeys`, `RequireAll`, `Get`, `Create`, `Patch`, `Delete`, and `AllowedPatchFields` declarations all stay byte-identical.

- [ ] **Step 4: Build**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
```

Expected: build succeeds. The new `List` method compiles against the three helpers created in Tasks 1–3. The `System.Linq` and `System.Collections.Generic` imports the controller already has cover the generic types used inline.

If the build fails with "GamesQuery does not exist in the current context" or similar, double-check that Tasks 1–3 committed correctly and that the three new files are in the `PlayniteApiServer.Controllers` namespace.

- [ ] **Step 5: Commit**

```bash
git add Controllers/GamesController.cs
git commit -m "$(cat <<'EOF'
refactor: GamesController.List delegates to GamesQuery helpers

Replaces the inline parse + filter + order chain with calls to
GamesQuery.Parse, GamesQueryFilter.Apply, and GamesQuerySort.Apply.
Removes the dead GetInt helper (only List used it). Behavior is
identical for unaware clients; offset/limit/q/default-sort all match
the previous output byte-for-byte.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Extend the `/games` route metadata in `BuildRouter`

**Files:**
- Modify: `PlayniteApiServerPlugin.cs`

Add 18 new `.QueryParam(...)` calls to the `router.Add("GET", "/games", games.List)` block so the OpenAPI spec advertises every new filter. No logic changes — this task only touches metadata.

- [ ] **Step 1: Find the existing `/games` metadata block**

In `PlayniteApiServerPlugin.cs`, find the `router.Add("GET", "/games", games.List)` block inside `BuildRouter()`. It currently looks like this (around lines 148–156):

```csharp
            router.Add("GET", "/games", games.List)
                .Summary("List games")
                .Tags("games")
                .QueryParam("offset", "integer", "Pagination offset (default 0)")
                .QueryParam("limit",  "integer", "Page size (default 100, max 1000)")
                .QueryParam("q",      "string",  "Substring filter on Name (case-insensitive)")
                .Response(200, "Paged list of games", OpenApiSchemas.Schemas.GamePage);
```

- [ ] **Step 2: Replace the block with the extended version**

Replace the block from Step 1 with:

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

**Constraints:**

- Don't touch any other route registration in `BuildRouter`. Only the `/games` block changes.
- Keep the metadata in the exact order shown above so the OpenAPI `parameters` array is stable: pagination → booleans → single-ID → multi-ID → ranges → sort. The comment dividers are intentional and help future readers navigate the 18-line chain.
- The `.Response(200, ...)` line stays last; it's unchanged from before.

- [ ] **Step 3: Build**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add PlayniteApiServerPlugin.cs
git commit -m "$(cat <<'EOF'
feat: advertise /games filters and sort in OpenAPI metadata

Adds 18 new .QueryParam(...) calls to the GET /games route so Swagger
UI's Try It Out surfaces the full filter + sort surface. Multi-ID
filters are documented as 'string' with 'comma-separated uuids' in the
description rather than OpenAPI array parameters — a known fidelity
gap that would require extending RouteBuilder/OpenApiBuilder, scoped
out of this task.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Manual smoke test (spec §11)

**Files:** None (verification only).

This executes the 10-step smoke test from the spec's §11. No code changes; no commit. If any step fails, the implementation is not complete and you need to go back and fix the root cause.

Playnite must be **restarted** between Task 5's deploy and this task so it picks up the new DLL. Before starting, note the plugin's bearer token (Add-ons → Generic → Playnite API Server → Settings → "Bearer Token") and its port (default `8083`). The commands below use environment variables:

```bash
TOKEN='paste-your-token-here'
BASE='http://127.0.0.1:8083'
```

- [ ] **Step 1: Restart Playnite**

Close Playnite if it's running, start it again. Confirm the plugin loads (no notification error about failing to bind to the port).

- [ ] **Step 2: Boolean filter**

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/games?isInstalled=true&limit=5" | head -c 500
```

Expected: HTTP 200, JSON body with `total`, `offset`, `limit`, `items[]`. Every item in `items` has `"isInstalled": true`.

- [ ] **Step 3: Multi-ID match-any filter**

First, get two platform IDs:

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/platforms?limit=2"
```

Pick any two `id` values from the response. Then:

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/games?platformIds=<guid1>,<guid2>&limit=5"
```

Expected: HTTP 200. Every returned game has at least one of those two platform IDs in its `platformIds` array.

- [ ] **Step 4: Range filter with sort**

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/games?playtimeMin=3600&sort=-playtime&limit=5"
```

Expected: HTTP 200. Every returned game has `playtime >= 3600`. Games are sorted descending by playtime (first item has the highest).

- [ ] **Step 5: Date range filter with sort**

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/games?lastActivityAfter=2026-01-01&sort=-lastActivity&limit=5"
```

Expected: HTTP 200. Every returned game has a `lastActivity` on or after `2026-01-01` (in your local timezone or UTC — `DateTime.TryParse` with `RoundtripKind` accepts either). Sorted descending by lastActivity.

- [ ] **Step 6: Unknown sort field → 400**

```bash
curl -i -H "Authorization: Bearer $TOKEN" "$BASE/games?sort=nope"
```

Expected: HTTP 400 with body exactly matching:

```json
{"error":"bad_request","message":"Unknown sort field 'nope'. Allowed: name, added, modified, lastActivity, releaseDate, playtime, playCount, userScore, communityScore, criticScore."}
```

- [ ] **Step 7: Bad boolean → 400**

```bash
curl -i -H "Authorization: Bearer $TOKEN" "$BASE/games?isInstalled=yes"
```

Expected: HTTP 400 with body:

```json
{"error":"bad_request","message":"Invalid boolean for 'isInstalled': 'yes'. Use 'true' or 'false'."}
```

- [ ] **Step 8: Invalid range → 400**

```bash
curl -i -H "Authorization: Bearer $TOKEN" "$BASE/games?playtimeMin=1000&playtimeMax=500"
```

Expected: HTTP 400 with body:

```json
{"error":"bad_request","message":"Invalid range: playtimeMin (1000) is greater than playtimeMax (500)."}
```

- [ ] **Step 9: Bad uuid in multi-ID filter → 400**

First get one valid platform uuid (same as Step 3). Then:

```bash
curl -i -H "Authorization: Bearer $TOKEN" "$BASE/games?platformIds=<valid-guid>,not-a-guid"
```

Expected: HTTP 400 with body (the index 1 refers to the second entry in the filtered list — `not-a-guid` is at position 1 after `<valid-guid>` at position 0):

```json
{"error":"bad_request","message":"Invalid GUID for 'platformIds'[1]: 'not-a-guid'."}
```

- [ ] **Step 10: Regression check — unchanged base behavior**

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/games?limit=5"
```

Expected: HTTP 200 with the same first five games the previous version of the endpoint would have returned (sorted ascending by name, no filters applied). This is the "existing clients don't break" check — if this step returns a different order or different games than before this task, something regressed.

- [ ] **Step 11: Swagger UI Try It Out**

Open `http://127.0.0.1:8083/docs` in any browser. Expand `GET /games`. Verify:

- The operation shows **21 input fields** (3 existing + 18 new).
- Each new field has a readable description in the Swagger UI panel.
- Click **Authorize**, paste the token.
- Click **Try it out**, set `isInstalled=true` and `sort=-lastActivity`, click **Execute**.
- The response panel shows a filtered + sorted payload matching what Step 4 returned via curl.

- [ ] **Step 12: Mark task complete**

If Steps 2–11 all match expectations, the implementation is **complete**. No commit for this task; verification is separate from the commit history.

**If any step fails:**

- Parse-error steps (6–9) failing usually means the error message string doesn't match exactly — check `GamesQuery.Parse` / `ParseSort` in Task 1.
- Filter steps (2–5) failing usually means a predicate is wrong in `GamesQueryFilter.Apply` — check the spec's §6 ordering and the null semantics in §4.
- Swagger UI step (11) failing with a missing field usually means a `.QueryParam` was dropped in Task 5.
- Regression step (10) failing means the default path through `GamesQuery.Parse` doesn't produce the equivalent of the old offset/limit/q/name-sort behavior — compare against the diff from Task 4 Step 2.

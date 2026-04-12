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

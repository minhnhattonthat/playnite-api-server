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

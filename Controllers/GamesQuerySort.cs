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

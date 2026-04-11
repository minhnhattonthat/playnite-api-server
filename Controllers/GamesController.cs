using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteApiServer.Dto;
using PlayniteApiServer.Server;

namespace PlayniteApiServer.Controllers
{
    /// <summary>
    /// Games CRUD with paginated listing and JObject-based PATCH merge.
    /// Relationships are ID-only in v1; name resolution is a client concern.
    /// Observable nested collections (GameActions, Links, Roms) are NOT patchable in v1.
    /// </summary>
    internal sealed class GamesController
    {
        // Allow-list for PATCH body keys. Case-insensitive match against JObject property names.
        // Nested observable collections are deliberately excluded.
        private static readonly HashSet<string> AllowedPatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "sortingName", "gameId", "description", "notes", "version",
            "installDirectory", "isInstalled", "hidden", "favorite",
            "overrideInstallState", "includeLibraryPluginAction", "enableSystemHdr",
            "useGlobalPostScript", "useGlobalPreScript", "useGlobalGameStartedScript",
            "preScript", "postScript", "gameStartedScript",
            "playtime", "playCount", "installSize",
            "added", "modified", "lastActivity", "releaseDate",
            "userScore", "communityScore", "criticScore",
            "icon", "coverImage", "backgroundImage",
            "manual",
            "platformIds", "genreIds", "developerIds", "publisherIds",
            "categoryIds", "tagIds", "featureIds", "seriesIds",
            "ageRatingIds", "regionIds",
            "sourceId", "completionStatusId",
        };

        private readonly IGameDatabaseAPI db;
        private readonly Dispatcher ui;

        public GamesController(IGameDatabaseAPI db, Dispatcher ui)
        {
            this.db = db;
            this.ui = ui;
        }

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

        public void Get(RequestContext r)
        {
            var id = HttpExtensions.ParseGuidOrThrow(r.PathVars["id"], "id");
            var game = db.Games.Get(id);
            if (game == null)
            {
                throw new ApiException(404, "Game not found: " + id);
            }
            r.WriteJson(200, game);
        }

        public void Create(RequestContext r)
        {
            var dto = r.ReadJson<GameCreateDto>();
            if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
            {
                throw new ApiException(400, "Field 'name' is required.");
            }

            var created = InvokeOnUi(() =>
            {
                var game = new Game(dto.Name.Trim());
                db.Games.Add(game);
                return game;
            });

            r.Response.AddHeader("Location", "/games/" + created.Id);
            r.WriteJson(201, created);
        }

        public void Patch(RequestContext r)
        {
            var id = HttpExtensions.ParseGuidOrThrow(r.PathVars["id"], "id");
            var existing = db.Games.Get(id);
            if (existing == null)
            {
                throw new ApiException(404, "Game not found: " + id);
            }

            var patch = r.ReadJObject();

            // Reject unknown / disallowed fields before doing anything expensive.
            foreach (var prop in patch.Properties())
            {
                if (!AllowedPatchFields.Contains(prop.Name))
                {
                    throw new ApiException(400,
                        "Field '" + prop.Name + "' is not patchable. " +
                        "Nested collections (gameActions, links, roms) are read-only in v1.");
                }
            }

            // Clone the game, populate the clone, validate, commit on UI thread.
            var clone = existing.GetCopy();
            var serializer = JsonSettings.CreateSerializer();
            using (var reader = patch.CreateReader())
            {
                try
                {
                    serializer.Populate(reader, clone);
                }
                catch (JsonException ex)
                {
                    throw new ApiException(400, "Patch failed: " + ex.Message);
                }
            }

            ValidateForeignKeys(clone);

            InvokeOnUi(() => db.Games.Update(clone));
            r.WriteJson(200, clone);
        }

        public void Delete(RequestContext r)
        {
            var id = HttpExtensions.ParseGuidOrThrow(r.PathVars["id"], "id");
            if (!db.Games.ContainsItem(id))
            {
                throw new ApiException(404, "Game not found: " + id);
            }

            InvokeOnUi(() => db.Games.Remove(id));
            r.Response.StatusCode = 204;
            r.Response.ContentLength64 = 0;
        }

        private void ValidateForeignKeys(Game g)
        {
            RequireAll(g.PlatformIds, id => db.Platforms.ContainsItem(id), "platformIds");
            RequireAll(g.GenreIds, id => db.Genres.ContainsItem(id), "genreIds");
            RequireAll(g.DeveloperIds, id => db.Companies.ContainsItem(id), "developerIds");
            RequireAll(g.PublisherIds, id => db.Companies.ContainsItem(id), "publisherIds");
            RequireAll(g.CategoryIds, id => db.Categories.ContainsItem(id), "categoryIds");
            RequireAll(g.TagIds, id => db.Tags.ContainsItem(id), "tagIds");
            RequireAll(g.FeatureIds, id => db.Features.ContainsItem(id), "featureIds");
            RequireAll(g.SeriesIds, id => db.Series.ContainsItem(id), "seriesIds");
            RequireAll(g.AgeRatingIds, id => db.AgeRatings.ContainsItem(id), "ageRatingIds");
            RequireAll(g.RegionIds, id => db.Regions.ContainsItem(id), "regionIds");

            if (g.SourceId != Guid.Empty && !db.Sources.ContainsItem(g.SourceId))
            {
                throw new ApiException(409, "sourceId references unknown source: " + g.SourceId);
            }

            if (g.CompletionStatusId != Guid.Empty && !db.CompletionStatuses.ContainsItem(g.CompletionStatusId))
            {
                throw new ApiException(409, "completionStatusId references unknown status: " + g.CompletionStatusId);
            }
        }

        private static void RequireAll(List<Guid> ids, Func<Guid, bool> exists, string fieldName)
        {
            if (ids == null)
            {
                return;
            }
            foreach (var id in ids)
            {
                if (!exists(id))
                {
                    throw new ApiException(409, fieldName + " references unknown id: " + id);
                }
            }
        }

        private static int GetInt(Dictionary<string, string> query, string key, int defaultValue)
        {
            if (query.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed))
            {
                return parsed;
            }
            return defaultValue;
        }

        private TResult InvokeOnUi<TResult>(Func<TResult> fn)
        {
            try
            {
                return (TResult)ui.Invoke(fn);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                throw new ApiException(503, "Playnite is shutting down.");
            }
        }

        private void InvokeOnUi(Action fn)
        {
            try
            {
                ui.Invoke(fn);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                throw new ApiException(503, "Playnite is shutting down.");
            }
        }
    }
}

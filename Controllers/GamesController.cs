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
using PlayniteApiServer.Server.OpenApi;

namespace PlayniteApiServer.Controllers
{
    /// <summary>
    /// Games CRUD with paginated listing and JObject-based PATCH merge.
    /// Relationships are ID-only in v1; name resolution is a client concern.
    /// Observable nested collections (GameActions, Links, Roms) are NOT patchable in v1.
    /// </summary>
    internal sealed class GamesController
    {
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
            { "releaseDate",                FieldShape.Str("Release date — nested object {year, month, day}; see Game schema for the full shape. Patching this field is discouraged in v1.") },

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

        private readonly IGameDatabaseAPI db;
        private readonly Dispatcher ui;

        public GamesController(IGameDatabaseAPI db, Dispatcher ui)
        {
            this.db = db;
            this.ui = ui;
        }

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

            r.Response.AddHeader("Location", "/api/games/" + created.Id);
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
                if (!AllowedPatchFields.ContainsKey(prop.Name))
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

using System;
using System.Net;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteApiServer.Controllers;
using PlayniteApiServer.Server;
using PlayniteApiServer.Server.OpenApi;
using PlayniteApiServer.Settings;

namespace PlayniteApiServer
{
    public class PlayniteApiServerPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("0a96c485-030a-4178-9c6c-6a9098fac2d5");

        private readonly PluginSettingsViewModel settings;
        private readonly object serverGate = new object();
        private ApiServer server;

        public PlayniteApiServerPlugin(IPlayniteAPI api) : base(api)
        {
            settings = new PluginSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new SettingsView();
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            StartServerInternal();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            StopServerInternal();
        }

        public override void Dispose()
        {
            StopServerInternal();
            base.Dispose();
        }

        /// <summary>
        /// Called by PluginSettingsViewModel after the port has changed so the
        /// listener can rebind without requiring a Playnite restart.
        /// </summary>
        public void RestartServer()
        {
            lock (serverGate)
            {
                StopServerInternal();
                StartServerInternal();
            }
        }

        private void StartServerInternal()
        {
            lock (serverGate)
            {
                if (server != null)
                {
                    return;
                }

                var current = settings.Live;
                var router = BuildRouter();
                var newServer = new ApiServer(router, current.Port, current.BindAddress);

                try
                {
                    newServer.Start();
                    server = newServer;
                }
                catch (HttpListenerException ex)
                {
                    logger.Error(ex, "Failed to start PlayniteApiServer on port " + current.Port);
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        Id.ToString() + "_port_failure",
                        "Playnite API Server could not bind to port " + current.Port + ": " + ex.Message +
                        " Change the port in plugin settings.",
                        NotificationType.Error));
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Unexpected error starting PlayniteApiServer.");
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        Id.ToString() + "_start_failure",
                        "Playnite API Server failed to start: " + ex.Message,
                        NotificationType.Error));
                }
            }
        }

        private void StopServerInternal()
        {
            lock (serverGate)
            {
                if (server == null)
                {
                    return;
                }

                try
                {
                    server.Stop();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error stopping PlayniteApiServer.");
                }
                finally
                {
                    server = null;
                }
            }
        }

        private static string PluginVersion()
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            // OpenAPI version field expects a semver-like string. Trim to
            // major.minor.patch (the AssemblyVersion fourth component is the
            // build number, not part of semver).
            return v == null ? "0.0.0" : v.ToString(3);
        }

        private Router BuildRouter()
        {
            var db = PlayniteApi.Database;
            var ui = PlayniteApi.MainView.UIDispatcher;

            var router = new Router(() => settings.Live);

            // ─── Health ────────────────────────────────────────────────────
            var health = new HealthController(db);
            router.Add("GET", "/api/health", health.Get)
                .Summary("Health check")
                .Tags("health")
                .Description("Returns the plugin version and a count of every database collection.")
                .Response(200, "Server is running");

            // ─── Games ─────────────────────────────────────────────────────
            var games = new GamesController(db, ui);

            router.Add("GET", "/api/games", games.List)
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

            router.Add("POST", "/api/games", games.Create)
                .Summary("Create a game")
                .Tags("games")
                .Body(OpenApiSchemas.Schemas.GameCreate, "Minimum: name")
                .Response(201, "Created", OpenApiSchemas.Schemas.Game)
                .Response(400, "Validation error", OpenApiSchemas.Schemas.Error)
                .Response(403, "Token lacks required scope", OpenApiSchemas.Schemas.Error);

            router.Add("GET", "/api/games/{id}", games.Get)
                .Summary("Get a game by id")
                .Tags("games")
                .Response(200, "Game", OpenApiSchemas.Schemas.Game)
                .Response(404, "Game not found", OpenApiSchemas.Schemas.Error);

            router.Add("PATCH", "/api/games/{id}", games.Patch)
                .Summary("Patch a game")
                .Tags("games")
                .Description("Merge-style patch. Only fields in the writable allow-list may be set; nested observable collections (gameActions, links, roms) are read-only in v1.")
                .Body(OpenApiSchemas.Schemas.Game, "Subset of Game properties to update")
                .Response(200, "Updated game", OpenApiSchemas.Schemas.Game)
                .Response(400, "Invalid field or JSON", OpenApiSchemas.Schemas.Error)
                .Response(403, "Token lacks required scope", OpenApiSchemas.Schemas.Error)
                .Response(404, "Game not found", OpenApiSchemas.Schemas.Error)
                .Response(409, "Foreign key references unknown id", OpenApiSchemas.Schemas.Error);

            router.Add("DELETE", "/api/games/{id}", games.Delete)
                .Summary("Delete a game")
                .Tags("games")
                .Response(204, "Deleted")
                .Response(403, "Token lacks required scope", OpenApiSchemas.Schemas.Error)
                .Response(404, "Game not found", OpenApiSchemas.Schemas.Error);

            // ─── Game media ────────────────────────────────────────────────
            var media = new MediaController(db);
            router.Add("GET", "/api/games/{id}/media/{kind}", media.Get)
                .Summary("Get a game's image")
                .Tags("games")
                .Description("Streams the icon, cover, or background image for a game. The 'kind' segment must be one of: icon, cover, background.")
                .BinaryResponse(200, "Image bytes", "image/*")
                .Response(404, "Game or media not found", OpenApiSchemas.Schemas.Error);

            // ─── Lookup collections ────────────────────────────────────────
            RegisterLookup(router, "/api/platforms",          "platforms",          "platform",          new LookupController<Platform>(db.Platforms, ui),                 OpenApiSchemas.Schemas.Platform);
            RegisterLookup(router, "/api/genres",             "genres",             "genre",             new LookupController<Genre>(db.Genres, ui),                       OpenApiSchemas.Schemas.NamedItem);
            RegisterLookup(router, "/api/companies",          "companies",          "company",           new LookupController<Company>(db.Companies, ui),                  OpenApiSchemas.Schemas.NamedItem);
            RegisterLookup(router, "/api/features",           "features",           "feature",           new LookupController<GameFeature>(db.Features, ui),               OpenApiSchemas.Schemas.NamedItem);
            RegisterLookup(router, "/api/categories",         "categories",         "category",          new LookupController<Category>(db.Categories, ui),                OpenApiSchemas.Schemas.NamedItem);
            RegisterLookup(router, "/api/tags",               "tags",               "tag",               new LookupController<Tag>(db.Tags, ui),                           OpenApiSchemas.Schemas.NamedItem);
            RegisterLookup(router, "/api/series",             "series",             "series entry",      new LookupController<Series>(db.Series, ui),                      OpenApiSchemas.Schemas.NamedItem);
            RegisterLookup(router, "/api/ageratings",         "ageratings",         "age rating",        new LookupController<AgeRating>(db.AgeRatings, ui),               OpenApiSchemas.Schemas.NamedItem);
            RegisterLookup(router, "/api/regions",            "regions",            "region",            new LookupController<Region>(db.Regions, ui),                     OpenApiSchemas.Schemas.NamedItem);
            RegisterLookup(router, "/api/sources",            "sources",            "source",            new LookupController<GameSource>(db.Sources, ui),                 OpenApiSchemas.Schemas.NamedItem);
            RegisterLookup(router, "/api/completionstatuses", "completionstatuses", "completion status", new LookupController<CompletionStatus>(db.CompletionStatuses, ui), OpenApiSchemas.Schemas.NamedItem);
            RegisterLookup(router, "/api/emulators",          "emulators",          "emulator",          new LookupController<Emulator>(db.Emulators, ui),                 OpenApiSchemas.Schemas.NamedItem);

            // ─── Documentation routes ──────────────────────────────────────
            // Build the OpenAPI document NOW (after all data routes are registered)
            // and capture the JSON in a closure for the handler. Each call to
            // BuildRouter produces an independent openApiJson binding;
            // RestartServer discards the old router so this stays correct.
            var openApiJson = OpenApiBuilder.Build(router.Routes, "Playnite API Server", PluginVersion());

            router.Add("GET", "/api/openapi.json", r => OpenApiHandler.Serve(r, openApiJson))
                .AllowAnonymous();

            router.Add("GET", "/api/docs", r => SwaggerUiHandler.Serve(r, SwaggerUiHandler.Resources.IndexHtml, "text/html; charset=utf-8"))
                .AllowAnonymous();

            router.Add("GET", "/api/swagger-ui.css", r => SwaggerUiHandler.Serve(r, SwaggerUiHandler.Resources.Css, "text/css; charset=utf-8"))
                .AllowAnonymous();

            router.Add("GET", "/api/swagger-ui-bundle.js", r => SwaggerUiHandler.Serve(r, SwaggerUiHandler.Resources.BundleJs, "application/javascript; charset=utf-8"))
                .AllowAnonymous();

            router.Add("GET", "/api/swagger-ui-standalone-preset.js", r => SwaggerUiHandler.Serve(r, SwaggerUiHandler.Resources.StandalonePresetJs, "application/javascript; charset=utf-8"))
                .AllowAnonymous();

            router.Add("GET", "/api/favicon.png", r => SwaggerUiHandler.Serve(r, SwaggerUiHandler.Resources.Favicon, "image/png"))
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
                .Response(403, "Token lacks required scope", OpenApiSchemas.Schemas.Error);

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
                .Response(403, "Token lacks required scope", OpenApiSchemas.Schemas.Error)
                .Response(404, singular + " not found", OpenApiSchemas.Schemas.Error);

            router.Add("DELETE", prefix + "/{id}", c.Delete)
                .Summary("Delete a " + singular)
                .Tags(tag)
                .Response(204, "Deleted")
                .Response(403, "Token lacks required scope", OpenApiSchemas.Schemas.Error)
                .Response(404, singular + " not found", OpenApiSchemas.Schemas.Error);
        }
    }
}

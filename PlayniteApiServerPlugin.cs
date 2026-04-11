using System;
using System.Net;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteApiServer.Controllers;
using PlayniteApiServer.Server;
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

        private Router BuildRouter()
        {
            var db = PlayniteApi.Database;
            var ui = PlayniteApi.MainView.UIDispatcher;

            var router = new Router(() => settings.Live);

            // Health.
            var health = new HealthController(db);
            router.Add("GET", "/health", health.Get);

            // Games — hand-written controller.
            var games = new GamesController(db, ui);
            router.Add("GET",    "/games",        games.List);
            router.Add("POST",   "/games",        games.Create);
            router.Add("GET",    "/games/{id}",   games.Get);
            router.Add("PATCH",  "/games/{id}",   games.Patch);
            router.Add("DELETE", "/games/{id}",   games.Delete);

            // Game media.
            var media = new MediaController(db);
            router.Add("GET", "/games/{id}/media/{kind}", media.Get);

            // Simple lookup collections — generic controller per type.
            RegisterLookup(router, "/platforms",         new LookupController<Platform>(db.Platforms, ui));
            RegisterLookup(router, "/genres",            new LookupController<Genre>(db.Genres, ui));
            RegisterLookup(router, "/companies",         new LookupController<Company>(db.Companies, ui));
            RegisterLookup(router, "/features",          new LookupController<GameFeature>(db.Features, ui));
            RegisterLookup(router, "/categories",        new LookupController<Category>(db.Categories, ui));
            RegisterLookup(router, "/tags",              new LookupController<Tag>(db.Tags, ui));
            RegisterLookup(router, "/series",            new LookupController<Series>(db.Series, ui));
            RegisterLookup(router, "/ageratings",        new LookupController<AgeRating>(db.AgeRatings, ui));
            RegisterLookup(router, "/regions",           new LookupController<Region>(db.Regions, ui));
            RegisterLookup(router, "/sources",           new LookupController<GameSource>(db.Sources, ui));
            RegisterLookup(router, "/completionstatuses",new LookupController<CompletionStatus>(db.CompletionStatuses, ui));
            RegisterLookup(router, "/emulators",         new LookupController<Emulator>(db.Emulators, ui));

            return router;
        }

        private static void RegisterLookup<T>(Router router, string prefix, LookupController<T> c) where T : DatabaseObject
        {
            router.Add("GET",    prefix,             c.List);
            router.Add("POST",   prefix,             c.Create);
            router.Add("GET",    prefix + "/{id}",   c.Get);
            router.Add("PATCH",  prefix + "/{id}",   c.Patch);
            router.Add("DELETE", prefix + "/{id}",   c.Delete);
        }
    }
}

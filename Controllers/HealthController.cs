using System.Reflection;
using Playnite.SDK;
using PlayniteApiServer.Server;

namespace PlayniteApiServer.Controllers
{
    internal sealed class HealthController
    {
        private readonly IGameDatabaseAPI db;

        public HealthController(IGameDatabaseAPI db)
        {
            this.db = db;
        }

        public void Get(RequestContext r)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

            r.WriteJson(200, new
            {
                ok = true,
                version,
                counts = new
                {
                    games = CountCollection(db.Games),
                    platforms = CountCollection(db.Platforms),
                    companies = CountCollection(db.Companies),
                    genres = CountCollection(db.Genres),
                    features = CountCollection(db.Features),
                    categories = CountCollection(db.Categories),
                    tags = CountCollection(db.Tags),
                    series = CountCollection(db.Series),
                    ageRatings = CountCollection(db.AgeRatings),
                    regions = CountCollection(db.Regions),
                    sources = CountCollection(db.Sources),
                    completionStatuses = CountCollection(db.CompletionStatuses),
                    emulators = CountCollection(db.Emulators),
                },
            });
        }

        private static int CountCollection(System.Collections.IEnumerable collection)
        {
            var count = 0;
            foreach (var _ in collection)
            {
                count++;
            }
            return count;
        }
    }
}

using System;
using System.IO;
using Playnite.SDK;
using PlayniteApiServer.Server;

namespace PlayniteApiServer.Controllers
{
    /// <summary>
    /// Streams a game's icon / cover / background image.
    /// Paths are resolved through IGameDatabaseAPI.GetFullFilePath which handles
    /// database ids, absolute paths, and URLs.
    /// </summary>
    internal sealed class MediaController
    {
        private readonly IGameDatabaseAPI db;

        public MediaController(IGameDatabaseAPI db)
        {
            this.db = db;
        }

        public void Get(RequestContext r)
        {
            var id = HttpExtensions.ParseGuidOrThrow(r.PathVars["id"], "id");
            var kind = r.PathVars["kind"];

            var game = db.Games.Get(id);
            if (game == null)
            {
                throw new ApiException(404, "Game not found: " + id);
            }

            string storedPath;
            switch (kind.ToLowerInvariant())
            {
                case "icon":
                    storedPath = game.Icon;
                    break;
                case "cover":
                    storedPath = game.CoverImage;
                    break;
                case "background":
                    storedPath = game.BackgroundImage;
                    break;
                default:
                    throw new ApiException(404, "Unknown media kind '" + kind + "'. Use icon, cover, or background.");
            }

            if (string.IsNullOrEmpty(storedPath))
            {
                throw new ApiException(404, "Game has no " + kind + " set.");
            }

            string fullPath;
            try
            {
                fullPath = db.GetFullFilePath(storedPath);
            }
            catch (Exception ex)
            {
                throw new ApiException(404, "Could not resolve media path: " + ex.Message);
            }

            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                throw new ApiException(404, "Media file not found on disk: " + fullPath);
            }

            var contentType = SniffContentType(fullPath);
            var bytes = File.ReadAllBytes(fullPath);

            r.Response.StatusCode = 200;
            r.Response.ContentType = contentType;
            r.Response.ContentLength64 = bytes.Length;
            r.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static string SniffContentType(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
            switch (ext)
            {
                case ".png":  return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                case ".gif":  return "image/gif";
                case ".bmp":  return "image/bmp";
                case ".ico":  return "image/x-icon";
                case ".svg":  return "image/svg+xml";
                default:      return "application/octet-stream";
            }
        }
    }
}

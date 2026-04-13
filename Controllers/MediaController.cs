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
        // Absolute ceiling on what we'll stream back. Individual files should be
        // well under this; the cap is a last-line defense against a PATCH that
        // redirects a media field at a huge file.
        private const long MaxMediaBytes = 64L * 1024L * 1024L;

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

            // A PATCH can set any string on icon/cover/background/manual. Require
            // the resolved path to live under the Playnite database root, so an
            // authenticated write-client can't turn this endpoint into a generic
            // file-read primitive.
            if (string.IsNullOrEmpty(fullPath) || !IsUnderDatabaseRoot(fullPath))
            {
                throw new ApiException(404, "Media file not found.");
            }

            if (!File.Exists(fullPath))
            {
                throw new ApiException(404, "Media file not found.");
            }

            var info = new FileInfo(fullPath);
            if (info.Length > MaxMediaBytes)
            {
                throw new ApiException(413, "Media file exceeds " + MaxMediaBytes + " byte limit.");
            }

            var etag = "\"" + info.LastWriteTimeUtc.Ticks.ToString("x") + "-" + info.Length.ToString("x") + "\"";

            var ifNoneMatch = r.Request.Headers["If-None-Match"];
            if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Trim() == etag)
            {
                r.Response.StatusCode = 304;
                r.Response.ContentLength64 = 0;
                r.Response.AddHeader("ETag", etag);
                r.Response.AddHeader("Cache-Control", "public, max-age=86400");
                return;
            }

            var contentType = SniffContentType(fullPath);

            r.Response.StatusCode = 200;
            r.Response.ContentType = contentType;
            r.Response.ContentLength64 = info.Length;
            r.Response.AddHeader("ETag", etag);
            r.Response.AddHeader("Cache-Control", "public, max-age=86400");
            using (var fs = File.OpenRead(fullPath))
            {
                fs.CopyTo(r.Response.OutputStream);
            }
        }

        private bool IsUnderDatabaseRoot(string fullPath)
        {
            var root = db.DatabasePath;
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            string normalizedRoot;
            string normalizedPath;
            try
            {
                normalizedRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                normalizedPath = Path.GetFullPath(fullPath);
            }
            catch (Exception)
            {
                return false;
            }

            // Windows filesystem comparison is case-insensitive.
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
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

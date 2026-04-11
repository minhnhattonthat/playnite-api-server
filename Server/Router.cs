using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Web;
using Playnite.SDK;
using PlayniteApiServer.Server.OpenApi;
using PlayniteApiServer.Settings;

namespace PlayniteApiServer.Server
{
    /// <summary>
    /// Owns the dispatch table and per-request pipeline:
    /// auth → route match → handler → exception translation.
    /// The router reads the live PluginSettings on every request so that
    /// token and EnableWrites changes take effect without a listener restart.
    /// </summary>
    internal sealed class Router
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly List<Route> routes = new List<Route>();
        private readonly Func<PluginSettings> settingsAccessor;

        public Router(Func<PluginSettings> settingsAccessor)
        {
            this.settingsAccessor = settingsAccessor;
        }

        public RouteBuilder Add(string method, string pathPattern, Action<RequestContext> handler)
        {
            var segments = SplitPath(pathPattern);
            var route = new Route(method, segments, handler, pathPattern);
            routes.Add(route);
            return new RouteBuilder(route);
        }

        public void Dispatch(HttpListenerContext http)
        {
            var settings = settingsAccessor();
            var req = http.Request;
            var resp = http.Response;

            try
            {
                // 1. Bearer auth — constant-time compare.
                var expected = settings.Token ?? "";
                var provided = ExtractBearerToken(req);
                if (string.IsNullOrEmpty(expected) || !TokenGen.ConstantTimeEquals(provided, expected))
                {
                    resp.AddHeader("WWW-Authenticate", "Bearer");
                    WriteError(resp, 401, "unauthorized", "Missing or invalid bearer token.");
                    return;
                }

                // 2. Route match.
                var pathSegments = SplitPath(req.Url.AbsolutePath);
                Route matchedOnPath = null;
                Dictionary<string, string> pathVars = null;

                foreach (var route in routes)
                {
                    if (!TryMatch(route.Segments, pathSegments, out var vars))
                    {
                        continue;
                    }

                    if (string.Equals(route.Method, req.HttpMethod, StringComparison.OrdinalIgnoreCase))
                    {
                        // Write-gate: non-GET requires EnableWrites.
                        if (!settings.EnableWrites && !IsReadMethod(route.Method))
                        {
                            WriteError(resp, 403, "writes_disabled", "Write operations are disabled in plugin settings.");
                            return;
                        }

                        var query = ParseQueryString(req.Url.Query);
                        var ctx = new RequestContext(http, vars, query);
                        route.Handler(ctx);
                        return;
                    }

                    if (matchedOnPath == null)
                    {
                        matchedOnPath = route;
                        pathVars = vars;
                    }
                }

                if (matchedOnPath != null)
                {
                    // 405: path matched, method did not — assemble Allow header.
                    var allowed = routes
                        .Where(r => SegmentsEqual(r.Segments, matchedOnPath.Segments))
                        .Select(r => r.Method.ToUpperInvariant())
                        .Distinct()
                        .ToArray();
                    resp.AddHeader("Allow", string.Join(", ", allowed));
                    WriteError(resp, 405, "method_not_allowed", "Method " + req.HttpMethod + " is not allowed on this resource.");
                    return;
                }

                WriteError(resp, 404, "not_found", "No route matches " + req.HttpMethod + " " + req.Url.AbsolutePath + ".");
            }
            catch (ApiException apiEx)
            {
                WriteError(resp, apiEx.StatusCode, ClassifyCode(apiEx.StatusCode), apiEx.Message);
            }
            catch (Exception ex)
            {
                var errorId = Guid.NewGuid().ToString();
                logger.Error(ex, "Unhandled exception in request " + req.HttpMethod + " " + req.Url.AbsolutePath + " (errorId=" + errorId + ")");
                WriteError(resp, 500, "internal", "Internal server error (id=" + errorId + ").");
            }
        }

        private static bool IsReadMethod(string method)
        {
            return string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractBearerToken(HttpListenerRequest req)
        {
            var header = req.Headers["Authorization"];
            if (string.IsNullOrEmpty(header))
            {
                return "";
            }

            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return header.Substring(prefix.Length).Trim();
        }

        private static string[] SplitPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                return new string[0];
            }
            return path.Trim('/').Split('/');
        }

        private static bool TryMatch(string[] routeSegments, string[] pathSegments, out Dictionary<string, string> vars)
        {
            vars = null;
            if (routeSegments.Length != pathSegments.Length)
            {
                return false;
            }

            Dictionary<string, string> captured = null;
            for (int i = 0; i < routeSegments.Length; i++)
            {
                var rs = routeSegments[i];
                var ps = pathSegments[i];

                if (rs.Length >= 2 && rs[0] == '{' && rs[rs.Length - 1] == '}')
                {
                    var name = rs.Substring(1, rs.Length - 2);
                    if (captured == null)
                    {
                        captured = new Dictionary<string, string>(StringComparer.Ordinal);
                    }
                    captured[name] = Uri.UnescapeDataString(ps);
                    continue;
                }

                if (!string.Equals(rs, ps, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            vars = captured ?? new Dictionary<string, string>(0, StringComparer.Ordinal);
            return true;
        }

        private static bool SegmentsEqual(string[] a, string[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query))
            {
                return result;
            }

            NameValueCollection parsed = HttpUtility.ParseQueryString(query);
            foreach (string key in parsed)
            {
                if (key != null)
                {
                    result[key] = parsed[key];
                }
            }
            return result;
        }

        private static void WriteError(HttpListenerResponse resp, int status, string code, string message)
        {
            try
            {
                resp.StatusCode = status;
                resp.ContentType = "application/json; charset=utf-8";
                var body = Newtonsoft.Json.JsonConvert.SerializeObject(
                    new Dto.ErrorDto { Error = code, Message = message },
                    JsonSettings.Default);
                var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                resp.ContentLength64 = bytes.Length;
                resp.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch
            {
                // Nothing useful to do if the error-write itself fails.
            }
        }

        private static string ClassifyCode(int status)
        {
            switch (status)
            {
                case 400: return "bad_request";
                case 401: return "unauthorized";
                case 403: return "forbidden";
                case 404: return "not_found";
                case 405: return "method_not_allowed";
                case 409: return "conflict";
                case 413: return "payload_too_large";
                case 503: return "unavailable";
                default:  return "error";
            }
        }
    }
}

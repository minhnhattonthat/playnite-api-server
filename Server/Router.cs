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
    /// route match → auth → handler → exception translation.
    /// Routes flagged AllowAnonymous skip the auth + write-gate steps.
    /// The router reads the live PluginSettings on every request so that
    /// token and EnableWrites changes take effect without a listener restart.
    /// </summary>
    internal sealed class Router
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly List<Route> routes = new List<Route>();
        public IReadOnlyList<Route> Routes => routes;
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

            // DNS-rebinding defense: when bound to loopback, reject Host
            // headers that don't resolve to a loopback name. Without this,
            // a page at attacker.com (whose DNS briefly resolves to 127.0.0.1)
            // could issue same-origin requests to this server. Done before
            // CORS so we don't advertise anything to a rebinding origin.
            if (IsLoopbackBind(settings.BindAddress) && !IsAllowedLoopbackHost(req.Url.Host))
            {
                resp.StatusCode = 421;
                resp.ContentLength64 = 0;
                return;
            }

            // CORS — every response gets these headers so cross-origin
            // callers can read the body.  Loopback-only, so wildcard is fine.
            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, PATCH, DELETE, OPTIONS");
            resp.AddHeader("Access-Control-Allow-Headers", "Authorization, Content-Type, If-None-Match");
            resp.AddHeader("Access-Control-Expose-Headers", "ETag");

            // Preflight: return 204 immediately, before auth / routing.
            if (string.Equals(req.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                resp.StatusCode = 204;
                resp.ContentLength64 = 0;
                return;
            }

            try
            {
                var pathSegments = SplitPath(req.Url.AbsolutePath);

                // 1. Walk the route table looking for a matching path. Track both
                //    the first method-match and any path-only match (for the 405).
                Route methodMatch = null;
                Dictionary<string, string> methodMatchVars = null;
                Route pathOnlyMatch = null;

                foreach (var route in routes)
                {
                    if (!TryMatch(route.Segments, pathSegments, out var vars))
                    {
                        continue;
                    }

                    if (string.Equals(route.Method, req.HttpMethod, StringComparison.OrdinalIgnoreCase))
                    {
                        methodMatch = route;
                        methodMatchVars = vars;
                        break;
                    }

                    if (pathOnlyMatch == null)
                    {
                        pathOnlyMatch = route;
                    }
                }

                // 2. No path match at all → 404 (no auth check; nothing to protect).
                if (methodMatch == null && pathOnlyMatch == null)
                {
                    WriteError(resp, 404, "not_found", "No route matches " + req.HttpMethod + " " + req.Url.AbsolutePath + ".");
                    return;
                }

                // 3. Path matched but method did not → 405 with Allow header.
                if (methodMatch == null)
                {
                    var allowed = routes
                        .Where(r => SegmentsEqual(r.Segments, pathOnlyMatch.Segments))
                        .Select(r => r.Method.ToUpperInvariant())
                        .Distinct()
                        .ToArray();
                    resp.AddHeader("Allow", string.Join(", ", allowed));
                    WriteError(resp, 405, "method_not_allowed", "Method " + req.HttpMethod + " is not allowed on this resource.");
                    return;
                }

                // 4. Auth + write-gate, skipped for anonymous routes.
                if (!methodMatch.AllowAnonymous)
                {
                    var expected = settings.Token ?? "";
                    var provided = ExtractBearerToken(req);
                    if (string.IsNullOrEmpty(expected) || !TokenGen.ConstantTimeEquals(provided, expected))
                    {
                        resp.AddHeader("WWW-Authenticate", "Bearer");
                        WriteError(resp, 401, "unauthorized", "Missing or invalid bearer token.");
                        return;
                    }

                    if (!settings.EnableWrites && !IsReadMethod(methodMatch.Method))
                    {
                        WriteError(resp, 403, "writes_disabled", "Write operations are disabled in plugin settings.");
                        return;
                    }
                }

                // 5. Invoke handler.
                var query = ParseQueryString(req.Url.Query);
                var ctx = new RequestContext(http, methodMatchVars, query);
                methodMatch.Handler(ctx);
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

        private static bool IsLoopbackBind(string bindAddress)
        {
            if (string.IsNullOrEmpty(bindAddress)) return true; // default = loopback
            return bindAddress == "127.0.0.1"
                || bindAddress == "::1"
                || bindAddress.StartsWith("127.", StringComparison.Ordinal)
                || string.Equals(bindAddress, "localhost", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAllowedLoopbackHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            return string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
                || string.Equals(host, "::1", StringComparison.Ordinal)
                || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
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

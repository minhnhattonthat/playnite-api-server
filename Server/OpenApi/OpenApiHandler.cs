using System.Text;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Serves the OpenAPI document built once at plugin start. Anonymous —
    /// see Router.Dispatch and the route registration in BuildRouter.
    /// </summary>
    internal static class OpenApiHandler
    {
        public static void Serve(RequestContext r, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            r.Response.StatusCode = 200;
            r.Response.ContentType = "application/json; charset=utf-8";
            r.Response.ContentLength64 = bytes.Length;
            r.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
    }
}

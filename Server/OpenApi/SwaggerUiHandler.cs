using System.IO;
using System.Reflection;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Serves the embedded Swagger UI assets. The resource names below must
    /// exactly match the LogicalName values in PlayniteApiServer.csproj.
    /// All routes that use this handler are anonymous — see BuildRouter.
    /// </summary>
    internal static class SwaggerUiHandler
    {
        /// <summary>
        /// Single source of truth for embedded-resource names. The .csproj
        /// LogicalName entries reference these literal strings; do not
        /// rename one without renaming the other.
        /// </summary>
        public static class Resources
        {
            public const string IndexHtml          = "index.html";
            public const string Css                = "swagger-ui.css";
            public const string BundleJs           = "swagger-ui-bundle.js";
            public const string StandalonePresetJs = "swagger-ui-standalone-preset.js";
            public const string Favicon            = "favicon.png";
        }

        public static void Serve(RequestContext r, string resourceName, string contentType)
        {
            var asm = typeof(SwaggerUiHandler).Assembly;
            using (var stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new ApiException(404, "Asset missing: " + resourceName);
                }

                var bytes = ReadAllBytes(stream);
                r.Response.StatusCode = 200;
                r.Response.ContentType = contentType;
                r.Response.ContentLength64 = bytes.Length;
                r.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
        }

        private static byte[] ReadAllBytes(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}

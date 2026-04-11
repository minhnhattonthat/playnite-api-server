using System;
using System.Collections.Generic;
using PlayniteApiServer.Server.OpenApi;

namespace PlayniteApiServer.Server
{
    /// <summary>
    /// One row in the routing table. Carries both routing data (method,
    /// segments, handler) and optional OpenAPI documentation metadata
    /// populated via <see cref="RouteBuilder"/>.
    /// </summary>
    internal sealed class Route
    {
        public string Method { get; }
        public string[] Segments { get; }
        public Action<RequestContext> Handler { get; }
        public string PathTemplate { get; }

        // Optional documentation metadata. All default to null/false so
        // existing routes registered without .Describes(...) still work.
        public bool AllowAnonymous { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string[] Tags { get; set; }
        public List<OpenApiParameter> Parameters { get; set; }
        public OpenApiRequestBody RequestBody { get; set; }
        public List<OpenApiResponse> Responses { get; set; }

        public Route(string method, string[] segments, Action<RequestContext> handler, string pathTemplate)
        {
            Method = method;
            Segments = segments;
            Handler = handler;
            PathTemplate = pathTemplate;
        }
    }
}

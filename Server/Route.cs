using System;

namespace PlayniteApiServer.Server
{
    /// <summary>
    /// A single entry in the router's dispatch table.
    /// Segments may contain literal path components or '{name}' placeholders that capture into RequestContext.PathVars.
    /// </summary>
    internal sealed class Route
    {
        public string Method { get; }
        public string[] Segments { get; }
        public Action<RequestContext> Handler { get; }

        public Route(string method, string[] segments, Action<RequestContext> handler)
        {
            Method = method;
            Segments = segments;
            Handler = handler;
        }
    }
}

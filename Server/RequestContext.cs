using System.Collections.Generic;
using System.Net;

namespace PlayniteApiServer.Server
{
    internal sealed class RequestContext
    {
        public HttpListenerContext Http { get; }
        public Dictionary<string, string> PathVars { get; }
        public Dictionary<string, string> Query { get; }

        public HttpListenerRequest Request => Http.Request;
        public HttpListenerResponse Response => Http.Response;

        public RequestContext(HttpListenerContext http, Dictionary<string, string> pathVars, Dictionary<string, string> query)
        {
            Http = http;
            PathVars = pathVars;
            Query = query;
        }
    }
}

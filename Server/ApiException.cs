using System;

namespace PlayniteApiServer.Server
{
    /// <summary>
    /// Thrown from controllers to produce a structured JSON error with a specific status code.
    /// Uncaught exceptions become 500.
    /// </summary>
    internal sealed class ApiException : Exception
    {
        public int StatusCode { get; }

        public ApiException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }
    }
}

using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayniteApiServer.Dto;

namespace PlayniteApiServer.Server
{
    internal static class HttpExtensions
    {
        // Cap request bodies at 8 MiB to avoid untrusted-size allocations.
        public const long MaxRequestBodyBytes = 8L * 1024L * 1024L;

        public static void WriteJson(this RequestContext r, int status, object body)
        {
            var resp = r.Response;
            resp.StatusCode = status;
            resp.ContentType = "application/json; charset=utf-8";

            if (body == null)
            {
                resp.ContentLength64 = 0;
                return;
            }

            var json = JsonConvert.SerializeObject(body, JsonSettings.Default);
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentLength64 = bytes.Length;
            resp.OutputStream.Write(bytes, 0, bytes.Length);
        }

        public static void WriteError(this RequestContext r, int status, string code, string message)
        {
            r.WriteJson(status, new ErrorDto { Error = code, Message = message });
        }

        public static string ReadBodyAsString(this RequestContext r)
        {
            var req = r.Request;
            if (req.ContentLength64 > MaxRequestBodyBytes)
            {
                throw new ApiException(413, "Request body exceeds 8 MiB limit.");
            }

            var encoding = req.ContentEncoding ?? Encoding.UTF8;

            // Read into a bounded buffer rather than trusting ContentLength64:
            // chunked-transfer requests report -1 and would bypass the fast
            // path above if we used StreamReader.ReadToEnd.
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[8 * 1024];
                long total = 0;
                int read;
                while ((read = req.InputStream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    total += read;
                    if (total > MaxRequestBodyBytes)
                    {
                        throw new ApiException(413, "Request body exceeds 8 MiB limit.");
                    }
                    buffer.Write(chunk, 0, read);
                }
                return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
        }

        public static T ReadJson<T>(this RequestContext r) where T : class
        {
            var text = r.ReadBodyAsString();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ApiException(400, "Request body is empty.");
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(text, JsonSettings.Default);
            }
            catch (JsonException ex)
            {
                throw new ApiException(400, "Invalid JSON: " + ex.Message);
            }
        }

        public static JObject ReadJObject(this RequestContext r)
        {
            var text = r.ReadBodyAsString();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ApiException(400, "Request body is empty.");
            }

            try
            {
                return JObject.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new ApiException(400, "Invalid JSON: " + ex.Message);
            }
        }

        public static Guid ParseGuidOrThrow(string s, string fieldName)
        {
            if (!Guid.TryParse(s, out var g))
            {
                throw new ApiException(400, "Invalid GUID for " + fieldName + ": '" + s + "'.");
            }
            return g;
        }
    }
}

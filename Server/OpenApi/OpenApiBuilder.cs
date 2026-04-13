using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Walks a route table and emits a serialized OpenAPI 3.0.3 document.
    /// The output is deterministic given a deterministic input — Newtonsoft
    /// preserves JObject insertion order, so the spec is byte-stable across
    /// runs as long as the route registrations don't move around.
    /// </summary>
    internal static class OpenApiBuilder
    {
        private static readonly Regex PathParamRegex = new Regex(@"\{([^}]+)\}", RegexOptions.Compiled);

        public static string Build(IReadOnlyList<Route> routes, string title, string version)
        {
            var doc = new JObject
            {
                ["openapi"] = "3.0.3",
                ["info"] = new JObject
                {
                    ["title"] = title,
                    ["version"] = version,
                    ["description"] = "Read/write access to the local Playnite library. All endpoints (except the documentation routes) require a Bearer token configured in the plugin settings. Each token carries a set of scopes — 'read' allows GET/HEAD, 'write' also allows POST/PUT/PATCH/DELETE.",
                },
                ["servers"] = new JArray(
                    new JObject { ["url"] = "/" }
                ),
                ["components"] = new JObject
                {
                    ["securitySchemes"] = new JObject
                    {
                        ["bearerAuth"] = new JObject
                        {
                            ["type"] = "http",
                            ["scheme"] = "bearer",
                        },
                    },
                    ["schemas"] = OpenApiSchemas.BuildAll(),
                },
                ["paths"] = BuildPaths(routes),
                ["tags"] = BuildTagList(routes),
            };

            return JsonConvert.SerializeObject(doc, Formatting.Indented);
        }

        // ─── path building ───────────────────────────────────────────────

        private static JObject BuildPaths(IReadOnlyList<Route> routes)
        {
            var paths = new JObject();

            // Group routes by their path template, preserving registration order.
            var seen = new HashSet<string>();
            var ordered = new List<string>();
            foreach (var r in routes)
            {
                if (seen.Add(r.PathTemplate))
                {
                    ordered.Add(r.PathTemplate);
                }
            }

            foreach (var template in ordered)
            {
                var pathItem = new JObject();
                foreach (var route in routes.Where(r => r.PathTemplate == template))
                {
                    pathItem[route.Method.ToLowerInvariant()] = BuildOperation(route);
                }
                paths[template] = pathItem;
            }

            return paths;
        }

        private static JObject BuildOperation(Route route)
        {
            var op = new JObject();

            if (route.Tags != null && route.Tags.Length > 0)
            {
                op["tags"] = new JArray(route.Tags);
            }
            if (!string.IsNullOrEmpty(route.Summary))
            {
                op["summary"] = route.Summary;
            }
            var description = route.Description ?? "";
            if (!route.AllowAnonymous)
            {
                var scope = IsReadMethod(route.Method) ? "read" : "write";
                var suffix = "Requires `" + scope + "` scope.";
                if (description.Length > 0)
                {
                    description += "\n\n" + suffix;
                }
                else
                {
                    description = suffix;
                }
            }
            if (description.Length > 0)
            {
                op["description"] = description;
            }

            // Parameters: auto-infer path params from {name} placeholders,
            // then merge any explicit overrides + query params.
            var parameters = BuildParameters(route);
            if (parameters.Count > 0)
            {
                op["parameters"] = new JArray(parameters);
            }

            if (route.RequestBody != null)
            {
                op["requestBody"] = BuildRequestBody(route.RequestBody);
            }

            op["responses"] = BuildResponses(route);

            // Security: every non-anonymous route requires bearerAuth.
            if (!route.AllowAnonymous)
            {
                op["security"] = new JArray(
                    new JObject { ["bearerAuth"] = new JArray() }
                );
            }
            else
            {
                // Anonymous routes get an explicit empty security list,
                // overriding any global default.
                op["security"] = new JArray();
            }

            return op;
        }

        private static List<JObject> BuildParameters(Route route)
        {
            var inferred = new List<JObject>();
            var overrides = new Dictionary<string, JObject>();

            // Auto-infer one path-param entry per {name} placeholder.
            foreach (Match m in PathParamRegex.Matches(route.PathTemplate))
            {
                var name = m.Groups[1].Value;
                inferred.Add(new JObject
                {
                    ["name"] = name,
                    ["in"] = "path",
                    ["required"] = true,
                    ["schema"] = new JObject { ["type"] = "string" },
                });
            }

            // Apply explicit overrides + add query params.
            var query = new List<JObject>();
            if (route.Parameters != null)
            {
                foreach (var p in route.Parameters)
                {
                    var entry = ParamToJson(p);
                    if (p.In == "path")
                    {
                        overrides[p.Name] = entry;
                    }
                    else
                    {
                        query.Add(entry);
                    }
                }
            }

            // Merge overrides into inferred path params (replace by name).
            for (int i = 0; i < inferred.Count; i++)
            {
                var name = (string)inferred[i]["name"];
                if (overrides.TryGetValue(name, out var ov))
                {
                    inferred[i] = ov;
                }
            }

            inferred.AddRange(query);
            return inferred;
        }

        private static JObject ParamToJson(OpenApiParameter p)
        {
            var schema = new JObject { ["type"] = p.Type ?? "string" };
            if (p.Format != null)
            {
                schema["format"] = p.Format;
            }
            var entry = new JObject
            {
                ["name"] = p.Name,
                ["in"] = p.In,
                ["required"] = p.Required,
                ["schema"] = schema,
            };
            if (!string.IsNullOrEmpty(p.Description))
            {
                entry["description"] = p.Description;
            }
            return entry;
        }

        private static JObject BuildRequestBody(OpenApiRequestBody body)
        {
            var schema = new JObject { ["$ref"] = body.SchemaRef };
            return new JObject
            {
                ["required"] = body.Required,
                ["description"] = body.Description ?? "",
                ["content"] = new JObject
                {
                    ["application/json"] = new JObject
                    {
                        ["schema"] = schema,
                    },
                },
            };
        }

        private static JObject BuildResponses(Route route)
        {
            // Build the working list first so the auto-401 (if needed) gets
            // sorted into numeric order alongside the declared responses,
            // not appended after the sort.
            var declared = route.Responses != null
                ? new List<OpenApiResponse>(route.Responses)
                : new List<OpenApiResponse>();

            // Auto-add a 401 if the route is non-anonymous and the author
            // didn't declare one explicitly.
            if (!route.AllowAnonymous && !declared.Any(r => r.Status == 401))
            {
                declared.Add(new OpenApiResponse
                {
                    Status = 401,
                    Description = "Missing or invalid bearer token",
                    MediaType = "application/json",
                    SchemaRef = OpenApiSchemas.Schemas.Error,
                });
            }

            var responses = new JObject();
            foreach (var r in declared.OrderBy(x => x.Status))
            {
                responses[r.Status.ToString()] = BuildResponse(r);
            }
            return responses;
        }

        private static JObject BuildResponse(OpenApiResponse r)
        {
            var entry = new JObject { ["description"] = r.Description };

            if (r.IsBinary)
            {
                entry["content"] = new JObject
                {
                    [r.MediaType] = new JObject
                    {
                        ["schema"] = new JObject
                        {
                            ["type"] = "string",
                            ["format"] = "binary",
                        },
                    },
                };
                return entry;
            }

            if (r.SchemaRef == null)
            {
                // No body — e.g. 204
                return entry;
            }

            JObject schema;
            if (r.IsArray)
            {
                schema = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["$ref"] = r.SchemaRef },
                };
            }
            else
            {
                schema = new JObject { ["$ref"] = r.SchemaRef };
            }

            entry["content"] = new JObject
            {
                [r.MediaType ?? "application/json"] = new JObject
                {
                    ["schema"] = schema,
                },
            };
            return entry;
        }

        private static bool IsReadMethod(string method)
        {
            return string.Equals(method, "GET", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "HEAD", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "OPTIONS", System.StringComparison.OrdinalIgnoreCase);
        }

        // ─── tags ───────────────────────────────────────────────────────

        private static JArray BuildTagList(IReadOnlyList<Route> routes)
        {
            var seen = new HashSet<string>();
            var ordered = new List<string>();
            foreach (var r in routes)
            {
                if (r.Tags == null) continue;
                foreach (var t in r.Tags)
                {
                    if (seen.Add(t)) ordered.Add(t);
                }
            }
            return new JArray(ordered.Select(t => new JObject { ["name"] = t }));
        }
    }
}

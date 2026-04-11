using System.Collections.Generic;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Fluent metadata builder returned by <see cref="Router.Add"/>. Each
    /// method mutates the underlying <see cref="Route"/> and returns
    /// <c>this</c> so calls can be chained.
    ///
    /// Path parameters are auto-inferred at OpenAPI build time from
    /// <c>{name}</c> placeholders in the route's <see cref="Route.PathTemplate"/>.
    /// Calling <see cref="PathParam"/> here only overrides the auto-inferred
    /// entry to set a non-string type or a description.
    /// </summary>
    internal sealed class RouteBuilder
    {
        private readonly Route route;

        public RouteBuilder(Route route)
        {
            this.route = route;
        }

        public RouteBuilder Summary(string summary)
        {
            route.Summary = summary;
            return this;
        }

        public RouteBuilder Description(string description)
        {
            route.Description = description;
            return this;
        }

        public RouteBuilder Tags(params string[] tags)
        {
            route.Tags = tags;
            return this;
        }

        public RouteBuilder QueryParam(string name, string type, string description, bool required = false)
        {
            EnsureParameters();
            route.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = "query",
                Type = type,
                Description = description,
                Required = required,
            });
            return this;
        }

        public RouteBuilder PathParam(string name, string type, string description)
        {
            EnsureParameters();
            route.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = "path",
                Type = type,
                Description = description,
                Required = true,
            });
            return this;
        }

        public RouteBuilder Body(string schemaRef, string description = null)
        {
            route.RequestBody = new OpenApiRequestBody
            {
                SchemaRef = schemaRef,
                Description = description,
                Required = true,
            };
            return this;
        }

        public RouteBuilder Response(int status, string description, string schemaRef = null)
        {
            EnsureResponses();
            route.Responses.Add(new OpenApiResponse
            {
                Status = status,
                Description = description,
                MediaType = "application/json",
                SchemaRef = schemaRef,
            });
            return this;
        }

        public RouteBuilder ArrayResponse(int status, string description, string itemSchemaRef)
        {
            EnsureResponses();
            route.Responses.Add(new OpenApiResponse
            {
                Status = status,
                Description = description,
                MediaType = "application/json",
                SchemaRef = itemSchemaRef,
                IsArray = true,
            });
            return this;
        }

        public RouteBuilder BinaryResponse(int status, string description, string mediaType)
        {
            EnsureResponses();
            route.Responses.Add(new OpenApiResponse
            {
                Status = status,
                Description = description,
                MediaType = mediaType,
                IsBinary = true,
            });
            return this;
        }

        public RouteBuilder AllowAnonymous()
        {
            route.AllowAnonymous = true;
            return this;
        }

        private void EnsureParameters()
        {
            if (route.Parameters == null)
            {
                route.Parameters = new List<OpenApiParameter>();
            }
        }

        private void EnsureResponses()
        {
            if (route.Responses == null)
            {
                route.Responses = new List<OpenApiResponse>();
            }
        }
    }
}

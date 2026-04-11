using System.Collections.Generic;

namespace PlayniteApiServer.Server.OpenApi
{
    /// <summary>
    /// Tiny data carriers consumed by the OpenAPI builder. They mirror the
    /// shapes of OpenAPI 3.0.3 objects but are *not* a complete OpenAPI model —
    /// only the parts the builder actually needs are represented here.
    /// </summary>
    internal sealed class OpenApiParameter
    {
        public string Name { get; set; }
        public string In { get; set; }            // "path" or "query"
        public string Description { get; set; }
        public bool Required { get; set; }
        public string Type { get; set; }          // "string", "integer", etc.
        public string Format { get; set; }        // optional, e.g. "uuid"
    }

    internal sealed class OpenApiRequestBody
    {
        public string Description { get; set; }
        public string SchemaRef { get; set; }     // "#/components/schemas/GameCreate"
        public bool Required { get; set; }
    }

    internal sealed class OpenApiResponse
    {
        public int Status { get; set; }
        public string Description { get; set; }
        public string MediaType { get; set; }     // defaults to "application/json"
        public string SchemaRef { get; set; }     // "#/components/schemas/Game" or null
        public bool IsArray { get; set; }         // true => schema is array<SchemaRef>
        public bool IsBinary { get; set; }        // true => string format=binary
    }

    /// <summary>
    /// JSON shape for a single field in a Game/Lookup schema. Consumed by both
    /// the OpenAPI builder (to render Game's properties) and the GamesController
    /// patch validator (to know which fields are writable). Single source of
    /// truth — adding a field here updates the docs and the validator together.
    /// </summary>
    internal sealed class FieldShape
    {
        public string Type { get; set; }          // "string", "integer", "boolean", "array"
        public string Format { get; set; }        // optional, e.g. "uuid", "date-time"
        public string ItemType { get; set; }      // for arrays: element type ("string")
        public string ItemFormat { get; set; }    // for arrays: element format ("uuid")
        public string Description { get; set; }
        public bool Nullable { get; set; }
        public List<string> EnumValues { get; set; }  // only set for ≤5-value enums; null otherwise

        public static FieldShape Str(string description = null) => new FieldShape { Type = "string", Description = description };
        public static FieldShape StrUuid(string description = null) => new FieldShape { Type = "string", Format = "uuid", Description = description };
        public static FieldShape Bool(string description = null) => new FieldShape { Type = "boolean", Description = description };
        public static FieldShape Int(string description = null) => new FieldShape { Type = "integer", Description = description };
        public static FieldShape IntNullable(string description = null) => new FieldShape { Type = "integer", Nullable = true, Description = description };
        public static FieldShape Long(string description = null) => new FieldShape { Type = "integer", Format = "int64", Description = description };
        public static FieldShape LongNullable(string description = null) => new FieldShape { Type = "integer", Format = "int64", Nullable = true, Description = description };
        public static FieldShape DateTimeNullable(string description = null) => new FieldShape { Type = "string", Format = "date-time", Nullable = true, Description = description };
        public static FieldShape UuidArray(string description = null) => new FieldShape { Type = "array", ItemType = "string", ItemFormat = "uuid", Nullable = true, Description = description };
    }
}

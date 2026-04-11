namespace PlayniteApiServer.Dto
{
    /// <summary>Minimal payload for POST /{collection} when adding a lookup item by name.</summary>
    public sealed class NamedDto
    {
        public string Name { get; set; }
    }
}

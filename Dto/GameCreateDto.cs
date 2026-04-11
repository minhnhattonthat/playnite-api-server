namespace PlayniteApiServer.Dto
{
    /// <summary>
    /// POST /games body. Only Name is required; everything else is optional and
    /// can be set in a follow-up PATCH. Relationships go through PATCH.
    /// </summary>
    public sealed class GameCreateDto
    {
        public string Name { get; set; }
    }
}

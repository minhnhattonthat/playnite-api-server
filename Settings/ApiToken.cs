using System.Collections.Generic;

namespace PlayniteApiServer.Settings
{
    /// <summary>
    /// A named bearer credential with a list of granted scopes.
    /// Scopes today: "read", "write". "write" implies "read".
    /// The scope list is open-ended so finer scopes can be added later
    /// without changing the settings-file schema.
    /// </summary>
    public sealed class ApiToken
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public List<string> Scopes { get; set; } = new List<string>();
    }
}

using System.Collections.Generic;
using System.Linq;

namespace PlayniteApiServer.Settings
{
    /// <summary>
    /// POCO persisted via Plugin.LoadPluginSettings / Plugin.SavePluginSettings.
    /// BindAddress is currently frozen to loopback in v1.
    /// Auth is via the Tokens list — each entry is a named bearer credential
    /// with its own scope set. See ApiToken.
    /// </summary>
    public sealed class PluginSettings
    {
        public int Port { get; set; } = 8083;
        public string BindAddress { get; set; } = "127.0.0.1";
        public List<ApiToken> Tokens { get; set; } = new List<ApiToken>();

        public PluginSettings Clone()
        {
            var source = Tokens ?? Enumerable.Empty<ApiToken>();
            return new PluginSettings
            {
                Port = Port,
                BindAddress = BindAddress,
                Tokens = source.Select(t => new ApiToken
                {
                    Name = t.Name,
                    Value = t.Value,
                    Scopes = t.Scopes != null ? new List<string>(t.Scopes) : new List<string>(),
                }).ToList(),
            };
        }
    }
}

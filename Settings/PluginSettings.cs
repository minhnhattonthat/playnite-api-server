namespace PlayniteApiServer.Settings
{
    /// <summary>
    /// POCO persisted via Plugin.LoadPluginSettings / Plugin.SavePluginSettings.
    /// BindAddress is currently frozen to loopback in v1.
    /// </summary>
    public sealed class PluginSettings
    {
        public int Port { get; set; } = 8083;
        public string Token { get; set; } = "";
        public bool EnableWrites { get; set; } = true;
        public string BindAddress { get; set; } = "127.0.0.1";

        public PluginSettings Clone()
        {
            return new PluginSettings
            {
                Port = Port,
                Token = Token,
                EnableWrites = EnableWrites,
                BindAddress = BindAddress,
            };
        }
    }
}

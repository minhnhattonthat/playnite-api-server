using System.Collections.Generic;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteApiServer.Server;

namespace PlayniteApiServer.Settings
{
    /// <summary>
    /// BeginEdit/CancelEdit/EndEdit pattern over an edit copy.
    /// Port change triggers a listener restart; token and EnableWrites apply live.
    /// </summary>
    public sealed class PluginSettingsViewModel : System.Collections.Generic.ObservableObject, ISettings
    {
        private readonly PlayniteApiServerPlugin plugin;
        private PluginSettings edit;
        private PluginSettings live;

        public PluginSettings Live => live;

        public int Port
        {
            get => edit.Port;
            set { edit.Port = value; OnPropertyChanged(nameof(Port)); }
        }

        public string Token
        {
            get => edit.Token;
            set { edit.Token = value; OnPropertyChanged(nameof(Token)); }
        }

        public bool EnableWrites
        {
            get => edit.EnableWrites;
            set { edit.EnableWrites = value; OnPropertyChanged(nameof(EnableWrites)); }
        }

        public string BindAddress => edit.BindAddress;

        public ICommand RegenerateTokenCommand => new RelayCommand(() =>
        {
            Token = TokenGen.NewToken();
        });

        public PluginSettingsViewModel(PlayniteApiServerPlugin plugin)
        {
            this.plugin = plugin;
            live = plugin.LoadPluginSettings<PluginSettings>() ?? new PluginSettings();

            // First-run token generation.
            if (string.IsNullOrEmpty(live.Token))
            {
                live.Token = TokenGen.NewToken();
                plugin.SavePluginSettings(live);
            }

            edit = live.Clone();
        }

        public void BeginEdit()
        {
            edit = live.Clone();
        }

        public void CancelEdit()
        {
            edit = live.Clone();
            OnPropertyChanged(nameof(Port));
            OnPropertyChanged(nameof(Token));
            OnPropertyChanged(nameof(EnableWrites));
        }

        public void EndEdit()
        {
            bool portChanged = edit.Port != live.Port;
            live = edit.Clone();
            plugin.SavePluginSettings(live);

            if (portChanged)
            {
                plugin.RestartServer();
            }
            // Token / EnableWrites changes apply live — no restart needed.
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (edit.Port < 1024 || edit.Port > 65535)
            {
                errors.Add("Port must be between 1024 and 65535.");
            }

            if (string.IsNullOrWhiteSpace(edit.Token) || edit.Token.Length < 16)
            {
                errors.Add("Token must be at least 16 characters.");
            }

            return errors.Count == 0;
        }
    }
}

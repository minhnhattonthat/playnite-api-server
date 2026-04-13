using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteApiServer.Server;

namespace PlayniteApiServer.Settings
{
    /// <summary>
    /// BeginEdit/CancelEdit/EndEdit pattern over an edit copy.
    /// Port change triggers a listener restart; token list changes apply live.
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

        public string BindAddress => edit.BindAddress;

        public ObservableCollection<ApiTokenRow> Tokens { get; }
            = new ObservableCollection<ApiTokenRow>();

        public ICommand AddTokenCommand => new RelayCommand(() =>
        {
            Tokens.Add(NewRow("", TokenGen.NewToken(), ApiTokenRow.ScopeReadWrite));
        });

        public PluginSettingsViewModel(PlayniteApiServerPlugin plugin)
        {
            this.plugin = plugin;
            live = plugin.LoadPluginSettings<PluginSettings>() ?? new PluginSettings();

            // First-run mint: if the settings file is missing, or if it
            // exists but no tokens are configured, create a default
            // read+write token so the plugin is usable out of the box.
            if (live.Tokens == null || live.Tokens.Count == 0)
            {
                live.Tokens = new List<ApiToken>
                {
                    new ApiToken
                    {
                        Name = "Default",
                        Value = TokenGen.NewToken(),
                        Scopes = new List<string> { "read", "write" },
                    },
                };
                plugin.SavePluginSettings(live);
            }

            edit = live.Clone();
            RebuildTokenRows();
        }

        public void BeginEdit()
        {
            edit = live.Clone();
            OnPropertyChanged(nameof(Port));
            RebuildTokenRows();
        }

        public void CancelEdit()
        {
            edit = live.Clone();
            OnPropertyChanged(nameof(Port));
            RebuildTokenRows();
        }

        public void EndEdit()
        {
            bool portChanged = edit.Port != live.Port;

            // Project the observable rows back onto edit.Tokens so that
            // live (= edit.Clone()) sees the user's intent.
            edit.Tokens = Tokens.Select(r => new ApiToken
            {
                Name = r.Name ?? "",
                Value = r.Value ?? "",
                Scopes = r.ScopeChoice == ApiTokenRow.ScopeReadWrite
                    ? new List<string> { "read", "write" }
                    : new List<string> { "read" },
            }).ToList();

            live = edit.Clone();
            plugin.SavePluginSettings(live);

            if (portChanged)
            {
                plugin.RestartServer();
            }
            // Token-list changes apply live — no restart needed.
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (edit.Port < 1024 || edit.Port > 65535)
            {
                errors.Add("Port must be between 1024 and 65535.");
            }

            for (int i = 0; i < Tokens.Count; i++)
            {
                var row = Tokens[i];
                var label = string.IsNullOrEmpty(row.Name) ? ("#" + (i + 1)) : row.Name;

                if (string.IsNullOrWhiteSpace(row.Value) || row.Value.Length < 16)
                {
                    errors.Add("Token " + label + ": value must be at least 16 characters.");
                }
                if (row.ScopeChoice != ApiTokenRow.ScopeRead &&
                    row.ScopeChoice != ApiTokenRow.ScopeReadWrite)
                {
                    errors.Add("Token " + label + ": scope must be read or read+write.");
                }
            }

            return errors.Count == 0;
        }

        private void RebuildTokenRows()
        {
            Tokens.Clear();
            foreach (var t in edit.Tokens)
            {
                var scope = t.Scopes != null && t.Scopes.Contains("write")
                    ? ApiTokenRow.ScopeReadWrite
                    : ApiTokenRow.ScopeRead;
                Tokens.Add(NewRow(t.Name ?? "", t.Value ?? "", scope));
            }
        }

        private ApiTokenRow NewRow(string name, string value, string scope)
        {
            var row = new ApiTokenRow
            {
                Name = name,
                Value = value,
                ScopeChoice = scope,
            };
            row.RegenerateCommand = new RelayCommand(() =>
            {
                row.Value = TokenGen.NewToken();
            });
            row.DeleteCommand = new RelayCommand(() =>
            {
                Tokens.Remove(row);
            });
            return row;
        }
    }

    /// <summary>
    /// Observable row wrapper around ApiToken for the settings DataGrid.
    /// ScopeChoice is the UI-facing string; the ViewModel projects it back
    /// onto ApiToken.Scopes at EndEdit.
    /// </summary>
    public sealed class ApiTokenRow : INotifyPropertyChanged
    {
        public const string ScopeRead = "read";
        public const string ScopeReadWrite = "read+write";

        public static IReadOnlyList<string> ScopeChoices { get; }
            = new[] { ScopeRead, ScopeReadWrite };

        private string name = "";
        private string valueText = "";
        private string scopeChoice = ScopeReadWrite;

        public string Name
        {
            get => name;
            set { if (name != value) { name = value; OnPropertyChanged(); } }
        }

        public string Value
        {
            get => valueText;
            set { if (valueText != value) { valueText = value; OnPropertyChanged(); } }
        }

        public string ScopeChoice
        {
            get => scopeChoice;
            set { if (scopeChoice != value) { scopeChoice = value; OnPropertyChanged(); } }
        }

        public ICommand RegenerateCommand { get; set; }
        public ICommand DeleteCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}

# Scoped Bearer Tokens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-token / global-`EnableWrites` auth model with a list of named, scoped bearer tokens. Two valid scopes today (`read`, `write`), `write` implies `read`, data model extensible to finer scopes later.

**Architecture:** One new POCO (`ApiToken`) stored as `List<ApiToken>` on `PluginSettings`. `Router.Dispatch` matches the incoming bearer value to a list entry via constant-time compare, then checks the token's scope list against a method-derived required scope. Settings UI replaces the single-token TextBox with a `DataGrid` of rows.

**Tech Stack:** .NET Framework 4.6.2, C# 7.3, classic csproj (no SDK style, no NuGet). Playnite SDK `GenericPlugin`. `System.Net.HttpListener`. WPF (`PresentationFramework`) for settings UI. Newtonsoft.Json 10 for settings (de)serialization.

**Spec reference:** `docs/superpowers/specs/2026-04-13-scoped-tokens-design.md`.

**No test project.** Verification is build + manual smoke test per spec §8. Full smoke requires closing Playnite, running `./build.ps1`, relaunching Playnite, then `curl`. Build-only verification can run with Playnite open:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Release
```

From bash on Windows, always invoke via `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1`; running `./build.ps1` directly in bash fails.

---

## Task 1: Add `ApiToken` POCO + register in csproj

**Files:**
- Create: `Settings/ApiToken.cs`
- Modify: `PlayniteApiServer.csproj` (add `<Compile Include="Settings\ApiToken.cs" />`)

This task is non-breaking: introduces the new type but doesn't yet reference it from existing code.

- [ ] **Step 1: Create `Settings/ApiToken.cs`**

```csharp
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
```

- [ ] **Step 2: Register the file in `PlayniteApiServer.csproj`**

Open `PlayniteApiServer.csproj`. Find the `<ItemGroup>` that starts with `<Compile Include="Properties\AssemblyInfo.cs" />` (around line 55). Immediately after the line `<Compile Include="Settings\PluginSettings.cs" />` (currently line 58), insert:

```xml
    <Compile Include="Settings\ApiToken.cs" />
```

So the block reads:

```xml
    <Compile Include="Settings\PluginSettings.cs" />
    <Compile Include="Settings\ApiToken.cs" />
    <Compile Include="Settings\PluginSettingsViewModel.cs" />
```

- [ ] **Step 3: Close Playnite if open** (required for `build.ps1` to succeed on deploy).

- [ ] **Step 4: Build to verify compilation**

Run:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Release
```

Expected: build succeeds, deploy step writes `PlayniteApiServer.dll` to `H:\Playnite\Extensions\PlayniteApiServer_<guid>\`.

- [ ] **Step 5: Commit**

```bash
git add Settings/ApiToken.cs PlayniteApiServer.csproj
git commit -m "feat: add ApiToken POCO for scoped bearer tokens"
```

---

## Task 2: Swap `PluginSettings` fields, migrate `Router` and `PluginSettingsViewModel` atomically

**Files:**
- Modify: `Settings/PluginSettings.cs` (remove `Token`, `EnableWrites`; add `Tokens`; update `Clone`)
- Modify: `Server/Router.cs` (rewrite auth block, add `FindToken` / `RequiredScope` / `Allows` helpers)
- Modify: `Settings/PluginSettingsViewModel.cs` (rewrite token section, add `ApiTokenRow`, commands, first-run mint, update `VerifySettings`)

All three files must change in one commit because any one of them alone leaves the project uncompilable (`PluginSettings.Token` is referenced by both `Router` and `ViewModel`). This task is the largest in the plan; execute its steps in order and build once at the end.

- [ ] **Step 1: Rewrite `Settings/PluginSettings.cs` completely**

Replace the entire file contents with:

```csharp
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
            return new PluginSettings
            {
                Port = Port,
                BindAddress = BindAddress,
                Tokens = Tokens.Select(t => new ApiToken
                {
                    Name = t.Name,
                    Value = t.Value,
                    Scopes = new List<string>(t.Scopes),
                }).ToList(),
            };
        }
    }
}
```

- [ ] **Step 2: Rewrite the auth block in `Server/Router.cs`**

In `Server/Router.cs`, locate the auth + write-gate block inside `Dispatch`. Currently (lines 124–141):

```csharp
// 4. Auth + write-gate, skipped for anonymous routes.
if (!methodMatch.AllowAnonymous)
{
    var expected = settings.Token ?? "";
    var provided = ExtractBearerToken(req);
    if (string.IsNullOrEmpty(expected) || !TokenGen.ConstantTimeEquals(provided, expected))
    {
        resp.AddHeader("WWW-Authenticate", "Bearer");
        WriteError(resp, 401, "unauthorized", "Missing or invalid bearer token.");
        return;
    }

    if (!settings.EnableWrites && !IsReadMethod(methodMatch.Method))
    {
        WriteError(resp, 403, "writes_disabled", "Write operations are disabled in plugin settings.");
        return;
    }
}
```

Replace that entire block with:

```csharp
// 4. Auth + scope check, skipped for anonymous routes.
if (!methodMatch.AllowAnonymous)
{
    var provided = ExtractBearerToken(req);
    var token = FindToken(settings.Tokens, provided);
    if (token == null)
    {
        resp.AddHeader("WWW-Authenticate", "Bearer");
        WriteError(resp, 401, "unauthorized", "Missing or invalid bearer token.");
        return;
    }

    var required = RequiredScope(methodMatch.Method);
    if (!Allows(token, required))
    {
        WriteError(resp, 403, "forbidden", "Token lacks required scope: " + required + ".");
        return;
    }
}
```

Then add three private static helpers at the bottom of the `Router` class, just before the closing brace of the class (after `ClassifyCode`):

```csharp
// GET/HEAD → "read"; everything else → "write". Route-level
// overrides would plug in here; none defined today.
private static string RequiredScope(string method)
{
    return IsReadMethod(method) ? "read" : "write";
}

// "write" implies "read". For any other required scope, exact match.
private static bool Allows(ApiToken token, string required)
{
    if (required == "read")
    {
        return token.Scopes.Contains("read") || token.Scopes.Contains("write");
    }
    return token.Scopes.Contains(required);
}

// Walk the token list with constant-time value comparison. Returns
// matching ApiToken or null. Short-circuits on empty input to avoid
// matching a blank-valued entry against a missing header. Walks the
// whole list on a match to keep timing uniform across list position —
// token counts are single-digit so the cost is negligible.
private static ApiToken FindToken(IReadOnlyList<ApiToken> tokens, string provided)
{
    if (string.IsNullOrEmpty(provided)) return null;
    ApiToken match = null;
    foreach (var t in tokens)
    {
        if (TokenGen.ConstantTimeEquals(provided, t.Value ?? "") && match == null)
        {
            match = t;
        }
    }
    return match;
}
```

Add a using directive at the top of `Router.cs` for `PlayniteApiServer.Settings` if not already present:

```csharp
using PlayniteApiServer.Settings;
```

(Check the existing `using` block; `PluginSettings` is already imported via the existing `using PlayniteApiServer.Settings;` at line 9, so this step may be a no-op. Confirm the line is present; if not, add it.)

Also update the class-level XML-doc comment on `Router` (lines 13–19) to reflect the new pipeline:

```csharp
/// <summary>
/// Owns the dispatch table and per-request pipeline:
/// route match → auth → scope check → handler → exception translation.
/// Routes flagged AllowAnonymous skip auth + scope checks.
/// The router reads the live PluginSettings on every request so that
/// token list and scope changes take effect without a listener restart.
/// </summary>
```

- [ ] **Step 3: Rewrite `Settings/PluginSettingsViewModel.cs` completely**

Replace the entire file contents with:

```csharp
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
```

- [ ] **Step 4: Close Playnite if open.**

- [ ] **Step 5: Build**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Release
```

Expected: build succeeds. If it fails, most likely culprits:
- Missing `using PlayniteApiServer.Settings;` in `Router.cs` — add it.
- The `Playnite.SDK` `ObservableObject` lives under `System.Collections.Generic` in the SDK — that's correct in the existing code (see the pre-edit `PluginSettingsViewModel`).
- `RelayCommand` is a Playnite SDK helper — imported via `using Playnite.SDK;` which is already in the template above.

- [ ] **Step 6: Commit**

```bash
git add Settings/PluginSettings.cs Settings/PluginSettingsViewModel.cs Server/Router.cs
git commit -m "feat: swap single-token auth for scoped token list"
```

---

## Task 3: Rewrite the settings UI (`SettingsView.xaml`)

**Files:**
- Modify: `Settings/SettingsView.xaml` (replace Bearer Token + EnableWrites block with DataGrid)

The code-behind file `SettingsView.xaml.cs` needs no change — it's a trivial `InitializeComponent()` partial class.

- [ ] **Step 1: Replace the entire contents of `Settings/SettingsView.xaml`**

```xml
<UserControl x:Class="PlayniteApiServer.Settings.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:local="clr-namespace:PlayniteApiServer.Settings"
             mc:Ignorable="d"
             d:DesignHeight="420" d:DesignWidth="640">
    <StackPanel Margin="20">
        <TextBlock Text="Playnite API Server"
                   FontSize="18" FontWeight="Bold" Margin="0,0,0,4" />
        <TextBlock TextWrapping="Wrap" Opacity="0.8" Margin="0,0,0,16"
                   Text="Exposes a bearer-authenticated HTTP REST API on 127.0.0.1 for reading and writing the Playnite game database." />

        <TextBlock Text="Port" Margin="0,0,0,4" />
        <TextBox Text="{Binding Port, UpdateSourceTrigger=PropertyChanged}"
                 Width="120" HorizontalAlignment="Left" Margin="0,0,0,16" />

        <TextBlock Text="API Tokens" FontWeight="Bold" Margin="0,0,0,4" />
        <TextBlock TextWrapping="Wrap" Opacity="0.7" Margin="0,0,0,8"
                   Text="Each token is a separate bearer credential. Scope 'read' allows GET/HEAD; 'read+write' also allows POST/PUT/PATCH/DELETE. Delete a token to revoke it immediately." />

        <DataGrid ItemsSource="{Binding Tokens}"
                  AutoGenerateColumns="False"
                  CanUserAddRows="False"
                  CanUserDeleteRows="False"
                  HeadersVisibility="Column"
                  GridLinesVisibility="Horizontal"
                  RowHeight="32"
                  Margin="0,0,0,8">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Name"
                                    Binding="{Binding Name, UpdateSourceTrigger=PropertyChanged}"
                                    Width="160" />
                <DataGridTemplateColumn Header="Scope" Width="140">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <ComboBox ItemsSource="{x:Static local:ApiTokenRow.ScopeChoices}"
                                      SelectedItem="{Binding ScopeChoice, UpdateSourceTrigger=PropertyChanged}"
                                      Margin="0,2" />
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
                <DataGridTextColumn Header="Token"
                                    Binding="{Binding Value}"
                                    Width="*"
                                    IsReadOnly="True"
                                    FontFamily="Consolas" />
                <DataGridTemplateColumn Header="" Width="90">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <Button Content="Regenerate"
                                    Command="{Binding RegenerateCommand}"
                                    Margin="2" Padding="6,2" />
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
                <DataGridTemplateColumn Header="" Width="70">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <Button Content="Delete"
                                    Command="{Binding DeleteCommand}"
                                    Margin="2" Padding="6,2" />
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>

        <Button Content="Add Token"
                Command="{Binding AddTokenCommand}"
                HorizontalAlignment="Left"
                Padding="12,4" Margin="0,0,0,12" />

        <TextBlock TextWrapping="Wrap" Opacity="0.7"
                   Text="The listener binds to http://127.0.0.1:{port}/ only. Token-list changes take effect immediately. Changing the port restarts the listener in place — no Playnite restart required." />
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Close Playnite, build, and deploy**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Release
```

Expected: build + deploy succeed.

- [ ] **Step 3: Visual check — launch Playnite and open the plugin settings**

Open Playnite → settings → extensions → Playnite API Server. Expected appearance:

- Port field at top.
- `API Tokens` heading + explanatory text.
- A DataGrid with one row (name: `Default`, scope: `read+write`, a long token value).
- Below the grid: `Add Token` button.
- Footer text.

Smoke-click: `Add Token` — a second row appears with a fresh token value, scope defaulted to `read+write`, name blank. Edit the name, set scope to `read` via ComboBox. Click Regenerate on the second row — the token value changes. Click Delete on the second row — the row disappears. Click OK to close the settings dialog; reopen and confirm persistence.

If the DataGrid doesn't render or ComboBox doesn't pick up the scope choices, check the `xmlns:local` namespace declaration and the `{x:Static local:ApiTokenRow.ScopeChoices}` binding.

- [ ] **Step 4: Commit**

```bash
git add Settings/SettingsView.xaml
git commit -m "feat: token list datagrid in settings view"
```

---

## Task 4: Advertise required scope in OpenAPI per-operation description, refresh top-level description

**Files:**
- Modify: `Server/OpenApi/OpenApiBuilder.cs` (append `Requires <scope> scope.` to each non-anonymous operation description; update the `info.description` to reflect the new auth model)

- [ ] **Step 1: Update the top-level `info.description` in `Build`**

In `Server/OpenApi/OpenApiBuilder.cs`, find the `Build` method (around line 19). The `info` block currently has:

```csharp
["description"] = "Read/write access to the local Playnite library. All endpoints (except the documentation routes) require a Bearer token configured in the plugin settings; non-GET requests additionally require the EnableWrites toggle.",
```

Replace that `description` value with:

```csharp
["description"] = "Read/write access to the local Playnite library. All endpoints (except the documentation routes) require a Bearer token configured in the plugin settings. Each token carries a set of scopes — 'read' allows GET/HEAD, 'write' also allows POST/PUT/PATCH/DELETE.",
```

- [ ] **Step 2: Append the scope-requirement sentence in `BuildOperation`**

In the same file, find `BuildOperation` (around line 82). The current body sets `op["description"]` unconditionally when `route.Description` is non-empty (line 94–97):

```csharp
if (!string.IsNullOrEmpty(route.Description))
{
    op["description"] = route.Description;
}
```

Replace that block with:

```csharp
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
```

Then add a private static helper at the bottom of the `OpenApiBuilder` class (just before the closing brace). Mirrors the one in `Router.cs` — we don't make `Router.IsReadMethod` internal to avoid widening a `Router` private:

```csharp
private static bool IsReadMethod(string method)
{
    return string.Equals(method, "GET", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "HEAD", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "OPTIONS", System.StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: Close Playnite, build, and deploy**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Release
```

Expected: build + deploy succeed.

- [ ] **Step 4: Relaunch Playnite and inspect `/openapi.json`**

With Playnite running, browse to `http://127.0.0.1:8083/openapi.json`. Search the response for `"Requires \`read\` scope."` and `"Requires \`write\` scope."` — both should appear. Search for `"EnableWrites"` — must not appear anywhere.

Also browse to `http://127.0.0.1:8083/docs` (Swagger UI). Expand a `GET` operation → description contains `Requires 'read' scope.` (rendered as code-fenced text). Expand a `PATCH`/`POST`/`DELETE` → description contains `Requires 'write' scope.`. The info-block description at the top of the page reflects the new scope-based wording.

- [ ] **Step 5: Commit**

```bash
git add Server/OpenApi/OpenApiBuilder.cs
git commit -m "docs: advertise required scope per operation in OpenAPI"
```

---

## Task 5: End-to-end smoke test per spec §8

**Files:** none modified (unless a bug is found, in which case fix inline and commit separately).

Execute each of the ten verifications below, in order. After step 1 you should already have a `Default` token with `read+write`. You will need a second `read`-only token starting at step 2.

You'll need two terminal variables:

```bash
RW="<paste the value of the Default token here>"
RO="<paste the value of the Read-Only token here — populate after step 2>"
```

Pick any existing game's GUID from Playnite for the PATCH tests:

```bash
curl -s -H "Authorization: Bearer $RW" http://127.0.0.1:8083/games | head -c 400
# Copy an "id" value from the first game in the response.
GID="<that id>"
```

- [ ] **Step 1: Default token minted on first run**

Open plugin settings, confirm exactly one row: Name `Default`, Scope `read+write`, Token non-empty. Copy the Token value into `$RW`.

- [ ] **Step 2: Add a second read-only token**

Click `Add Token`, set Name `Read-Only`, Scope `read`. Click OK. Reopen settings. Copy the new token's Value into `$RO`. Close settings.

- [ ] **Step 3: Read with read-only token → 200**

```bash
curl -s -o /dev/null -w "%{http_code}\n" \
  -H "Authorization: Bearer $RO" \
  http://127.0.0.1:8083/games
```

Expected output: `200`.

- [ ] **Step 4: Write with read-only token → 403 with scope message**

```bash
curl -s -w "\n%{http_code}\n" \
  -X PATCH \
  -H "Authorization: Bearer $RO" \
  -H "Content-Type: application/json" \
  -d '{"name":"scope-test-should-fail"}' \
  http://127.0.0.1:8083/games/$GID
```

Expected body: `{"error":"forbidden","message":"Token lacks required scope: write."}` and status `403`.

- [ ] **Step 5: Write with read+write token → 200**

```bash
curl -s -w "\n%{http_code}\n" \
  -X PATCH \
  -H "Authorization: Bearer $RW" \
  -H "Content-Type: application/json" \
  -d '{"name":"scope-test-via-rw"}' \
  http://127.0.0.1:8083/games/$GID
```

Expected status: `200`. Restore the original name afterwards if you care about not leaving test state behind:

```bash
curl -s -X PATCH -H "Authorization: Bearer $RW" -H "Content-Type: application/json" \
  -d '{"name":"<original name>"}' http://127.0.0.1:8083/games/$GID
```

- [ ] **Step 6: Invalid token → 401 + WWW-Authenticate: Bearer**

```bash
curl -s -D - -o /dev/null \
  -H "Authorization: Bearer deadbeef" \
  http://127.0.0.1:8083/games
```

Expected: `HTTP/1.1 401 Unauthorized` status line; a `WWW-Authenticate: Bearer` header; the JSON body carries `"error":"unauthorized"`.

- [ ] **Step 7: Missing Authorization header → 401 identical to step 6**

```bash
curl -s -D - -o /dev/null http://127.0.0.1:8083/games
```

Expected: same as step 6 (401, `WWW-Authenticate: Bearer`).

- [ ] **Step 8: Revocation by deletion**

Open settings, delete the `Read-Only` token row. Click OK. Re-run step 3 with `$RO`:

```bash
curl -s -o /dev/null -w "%{http_code}\n" \
  -H "Authorization: Bearer $RO" \
  http://127.0.0.1:8083/games
```

Expected: `401` (the value no longer matches any entry).

- [ ] **Step 9: Zero tokens → everything 401, docs still accessible**

Open settings, delete the `Default` token row too. Click OK (expect no validation block — the token list is allowed to be empty).

```bash
# Protected route: 401
curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer $RW" http://127.0.0.1:8083/games
# Health, OpenAPI, Swagger UI: 200
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8083/health
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8083/openapi.json
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8083/docs
```

Expected: first call `401`, remaining three `200`.

Reopen settings, click `Add Token` to mint a fresh replacement (or regenerate via any other flow). Click OK.

- [ ] **Step 10: Swagger UI end-to-end**

Mint a fresh token if you deleted them all at step 9. Browse to `http://127.0.0.1:8083/docs`. Click `Authorize` → paste the token value → `Authorize`. Try `GET /games` → 200. Try a `PATCH /games/{id}` with a minimal body — 200 if the token has `write`; 403 with the scope message if the token is `read`-only.

- [ ] **Step 11: If any verification failed**

Fix inline, rebuild, redeploy, rerun the failing step. Commit the fix with `fix:` prefix. If all steps passed, proceed to the wrap-up below.

- [ ] **Step 12: Wrap-up commit**

If you made any README or minor doc adjustments during smoke testing, commit them now. Otherwise this step is a no-op.

```bash
git status
# If clean, no commit needed.
```

---

## Notes on decisions deferred to implementation

These were explicit in the spec; flagged here so the implementer doesn't re-argue them:

- **No migration code** for the legacy `Token` + `EnableWrites` fields. Plugin has a single user who re-mints on first run.
- **Duplicate `Value` across rows** is not a validation error (astronomically unlikely with 32 bytes of randomness; `FindToken` returns the first match on the vanishing chance of collision).
- **Scope validation warnings for unknown strings** are not surfaced — forward-compat for future finer scopes. The UI ComboBox can't produce bad values, so the concern is only relevant if a newer-binary settings file is read by an older binary.
- **No audit logging** of which token authenticated which request. Can be added later without schema changes.

## Self-review notes

- Spec §3 data model → Task 1 + Task 2 Step 1.
- Spec §4 auth pipeline → Task 2 Step 2.
- Spec §5 settings UI (both ViewModel and XAML) → Task 2 Step 3 + Task 3.
- Spec §6 OpenAPI → Task 4.
- Spec §7 error semantics → enforced by Task 2 Step 2; validated by Task 5 Steps 4, 6, 7.
- Spec §8 verification → Task 5.
- Spec §9 files-touched list → matches the plan's Files lines across Tasks 1–4 (no controller changes, no Swagger asset changes, no Route.cs changes — confirmed).

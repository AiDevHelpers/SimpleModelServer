using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SimpleModelServer.Chat.Maui.Services;
using SimpleModelServer.Discovery;

namespace SimpleModelServer.Chat.Maui.Pages;

/// <summary>
/// Settings page for managing server connection and configuration.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsPageViewModel _viewModel;

    public SettingsPage(IServerConnectionStore connectionStore, IServerLocator serverLocator)
    {
        InitializeComponent();
        _viewModel = new SettingsPageViewModel(connectionStore, serverLocator);
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadSettingsAsync();
        UpdateManualFieldsVisibility();

        // Display app version
        AppVersionLabel.Text = AppInfo.Current.VersionString;

        // Load and display resolved server info
        await _viewModel.LoadResolvedServerInfoAsync();
        ResolvedServerLabel.Text = _viewModel.ResolvedBaseUrl ?? "Unknown";
        ResolvedViaLabel.Text = _viewModel.ResolvedVia ?? "Unknown";
    }

    private void OnConnectionModeChanged(object sender, EventArgs e)
    {
        UpdateManualFieldsVisibility();
    }

    private void UpdateManualFieldsVisibility()
    {
        var isManualMode = _viewModel.SelectedMode == ConnectionMode.Manual;
        ManualHostContainer.IsVisible = isManualMode;
        ManualPortContainer.IsVisible = isManualMode;
    }

    private async void OnTestConnectionClicked(object sender, EventArgs e)
    {
        try
        {
            ConnectionStatusLabel.Text = "Testing...";
            ConnectionStatusLabel.IsVisible = true;
            ConnectionStatusLabel.TextColor = Colors.Blue;

            var result = await _viewModel.TestConnectionAsync();

            if (result.Success)
            {
                ConnectionStatusLabel.Text = $"Success! Connected via {result.ResolvedVia} at {result.BaseUrl}";
                ConnectionStatusLabel.TextColor = Colors.Green;
            }
            else
            {
                ConnectionStatusLabel.Text = $"Connection failed: {result.ErrorMessage}";
                ConnectionStatusLabel.TextColor = Colors.Red;
            }
        }
        catch (Exception ex)
        {
            ConnectionStatusLabel.Text = $"Error: {ex.Message}";
            ConnectionStatusLabel.TextColor = Colors.Red;
            ConnectionStatusLabel.IsVisible = true;
        }
    }

    private async void OnSaveSettingsClicked(object sender, EventArgs e)
    {
        try
        {
            await _viewModel.SaveSettingsAsync();
            await DisplayAlert("Success", "Settings saved successfully", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save settings: {ex.Message}", "OK");
        }
    }

    /// <summary>Profile ids in the same order as the picker's items.</summary>
    private readonly List<int> _profileIds = [];

    private async void OnToggleConfigClicked(object sender, EventArgs e)
    {
        if (ConfigPanel.IsVisible)
        {
            ConfigPanel.IsVisible = false;
            ToggleConfigButton.Text = "Show Configuration";
        }
        else
        {
            ToggleConfigButton.Text = "Hide Configuration";
            ConfigPanel.IsVisible = true;
            await LoadAndDisplayConfigAsync();
        }
    }

    private async Task LoadAndDisplayConfigAsync()
    {
        try
        {
            ConfigStatusLabel.Text = "";
            ConfigStatusLabel.IsVisible = false;
            ConfigItemsContainer.Clear();

            var configData = await _viewModel.GetServerConfigAsync();
            if (configData != null && configData["global"] is JsonObject globalObj)
            {
                // Display all global config fields
                foreach (var kvp in globalObj)
                {
                    var grid = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitionCollection
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Auto)
                        },
                        ColumnSpacing = 8,
                        Padding = new Thickness(0, 2)
                    };

                    var label = new Label
                    {
                        Text = kvp.Key,
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold
                    };

                    var value = new Label
                    {
                        Text = kvp.Value?.ToString() ?? "null",
                        FontSize = 11,
                        HorizontalTextAlignment = TextAlignment.End
                    };

                    grid.Add(label, 0, 0);
                    grid.Add(value, 1, 0);
                    ConfigItemsContainer.Add(grid);
                }

                // Pre-populate routerPort if available
                if (globalObj["routerPort"]?.GetValue<int>() is int port)
                {
                    RouterPortEntry.Text = port.ToString();
                }

                PopulateProfiles(configData, globalObj);
            }
        }
        catch (Exception ex)
        {
            ConfigStatusLabel.Text = $"Error loading config: {ex.Message}";
            ConfigStatusLabel.TextColor = Colors.Red;
            ConfigStatusLabel.IsVisible = true;
        }
    }

    /// <summary>
    ///     Fills the profile picker from the server's <c>profiles</c> list and selects the active
    ///     <c>configurationId</c>. With no profiles (flat models schema) the picker is disabled.
    /// </summary>
    private void PopulateProfiles(JsonObject configData, JsonObject globalObj)
    {
        _profileIds.Clear();
        List<string> labels = [];

        if (configData["profiles"] is JsonArray profiles)
        {
            foreach (JsonObject profile in profiles.OfType<JsonObject>())
            {
                int id = profile["id"]?.GetValue<int>() ?? 0;
                string nickname = profile["nickname"]?.GetValue<string>() ?? $"Profile {id}";
                bool isDefault = profile["isDefault"]?.GetValue<bool>() ?? false;

                _profileIds.Add(id);
                labels.Add(isDefault ? $"{nickname} (default)" : nickname);
            }
        }

        ProfilePicker.ItemsSource = labels;
        ProfilePicker.IsEnabled = _profileIds.Count > 0;
        ProfileHintLabel.Text = _profileIds.Count > 0
            ? "Takes effect after the server restarts."
            : "This server uses a single model set; there are no profiles to switch between.";

        int active = globalObj["configurationId"]?.GetValue<int>() ?? -1;
        ProfilePicker.SelectedIndex = _profileIds.IndexOf(active);
    }

    private async void OnSaveProfileClicked(object sender, EventArgs e)
    {
        try
        {
            int index = ProfilePicker.SelectedIndex;
            if (index < 0 || index >= _profileIds.Count)
            {
                ConfigStatusLabel.Text = "Pick a model profile first";
                ConfigStatusLabel.TextColor = Colors.Red;
                ConfigStatusLabel.IsVisible = true;
                return;
            }

            ConfigStatusLabel.Text = "Saving...";
            ConfigStatusLabel.TextColor = Colors.Blue;
            ConfigStatusLabel.IsVisible = true;

            var result = await _viewModel.UpdateConfigurationIdAsync(_profileIds[index]);

            if (result.Success)
            {
                ConfigStatusLabel.Text = $"Saved! Restart the server to switch profiles (requires restart: {result.RequiresRestart})";
                ConfigStatusLabel.TextColor = Colors.Green;
            }
            else
            {
                ConfigStatusLabel.Text = $"Failed to save: {result.ErrorMessage}";
                ConfigStatusLabel.TextColor = Colors.Red;
            }
        }
        catch (Exception ex)
        {
            ConfigStatusLabel.Text = $"Error: {ex.Message}";
            ConfigStatusLabel.TextColor = Colors.Red;
            ConfigStatusLabel.IsVisible = true;
        }
    }

    private async void OnSaveRouterPortClicked(object sender, EventArgs e)
    {
        try
        {
            if (!int.TryParse(RouterPortEntry.Text, out var port))
            {
                ConfigStatusLabel.Text = "Invalid port number";
                ConfigStatusLabel.TextColor = Colors.Red;
                ConfigStatusLabel.IsVisible = true;
                return;
            }

            ConfigStatusLabel.Text = "Saving...";
            ConfigStatusLabel.TextColor = Colors.Blue;
            ConfigStatusLabel.IsVisible = true;

            var result = await _viewModel.UpdateRouterPortAsync(port);

            if (result.Success)
            {
                ConfigStatusLabel.Text = $"Saved! (Requires restart: {result.RequiresRestart})";
                ConfigStatusLabel.TextColor = Colors.Green;
            }
            else
            {
                ConfigStatusLabel.Text = $"Failed to save: {result.ErrorMessage}";
                ConfigStatusLabel.TextColor = Colors.Red;
            }
        }
        catch (Exception ex)
        {
            ConfigStatusLabel.Text = $"Error: {ex.Message}";
            ConfigStatusLabel.TextColor = Colors.Red;
            ConfigStatusLabel.IsVisible = true;
        }
    }
}

/// <summary>
/// ViewModel for the settings page.
/// </summary>
public class SettingsPageViewModel : INotifyPropertyChanged
{
    private readonly IServerConnectionStore _connectionStore;
    private readonly IServerLocator _serverLocator;
    private ConnectionMode _selectedMode;
    private string? _manualHost;
    private int _manualPort = 11435;
    private string? _apiKey;
    private string? _resolvedBaseUrl;
    private string? _resolvedVia;

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<ConnectionMode> AvailableModes { get; } = [ConnectionMode.Local, ConnectionMode.Mdns, ConnectionMode.Manual];

    public ConnectionMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (_selectedMode != value)
            {
                _selectedMode = value;
                OnPropertyChanged();
            }
        }
    }

    public string? ManualHost
    {
        get => _manualHost;
        set
        {
            if (_manualHost != value)
            {
                _manualHost = value;
                OnPropertyChanged();
            }
        }
    }

    public int ManualPort
    {
        get => _manualPort;
        set
        {
            if (_manualPort != value)
            {
                _manualPort = value;
                OnPropertyChanged();
            }
        }
    }

    public string? ApiKey
    {
        get => _apiKey;
        set
        {
            if (_apiKey != value)
            {
                _apiKey = value;
                OnPropertyChanged();
            }
        }
    }

    public string? ResolvedBaseUrl
    {
        get => _resolvedBaseUrl;
        set
        {
            if (_resolvedBaseUrl != value)
            {
                _resolvedBaseUrl = value;
                OnPropertyChanged();
            }
        }
    }

    public string? ResolvedVia
    {
        get => _resolvedVia;
        set
        {
            if (_resolvedVia != value)
            {
                _resolvedVia = value;
                OnPropertyChanged();
            }
        }
    }

    public SettingsPageViewModel(IServerConnectionStore connectionStore, IServerLocator serverLocator)
    {
        _connectionStore = connectionStore ?? throw new ArgumentNullException(nameof(connectionStore));
        _serverLocator = serverLocator ?? throw new ArgumentNullException(nameof(serverLocator));
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _connectionStore.LoadAsync();
            SelectedMode = settings.Mode;
            ManualHost = settings.ManualHost;
            ManualPort = settings.ManualPort;
            ApiKey = settings.ApiKey;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex}");
        }
    }

    public async Task SaveSettingsAsync()
    {
        var settings = new ServerConnectionSettings
        {
            Mode = SelectedMode,
            ManualHost = ManualHost,
            ManualPort = ManualPort,
            ApiKey = ApiKey
        };

        await _connectionStore.SaveAsync(settings);
    }

    public async Task LoadResolvedServerInfoAsync()
    {
        try
        {
            var resolved = await _serverLocator.ResolveAsync();
            ResolvedBaseUrl = resolved.BaseUrl;
            ResolvedVia = resolved.ResolvedVia.ToString();
        }
        catch (Exception ex)
        {
            ResolvedBaseUrl = $"Error: {ex.Message}";
            ResolvedVia = "Unknown";
        }
    }

    public async Task<TestConnectionResult> TestConnectionAsync()
    {
        try
        {
            var resolved = await _serverLocator.RediscoverAsync();
            return new TestConnectionResult
            {
                Success = true,
                BaseUrl = resolved.BaseUrl,
                ResolvedVia = resolved.ResolvedVia.ToString()
            };
        }
        catch (Exception ex)
        {
            return new TestConnectionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<JsonObject?> GetServerConfigAsync()
    {
        try
        {
            var resolved = await _serverLocator.ResolveAsync();
            using var httpClient = AuthenticatedHttpClientFactory.Create(resolved);

            var configUri = new Uri(resolved.BaseUrl).AppendPath("/admin/config");
            var configData = await httpClient.GetFromJsonAsync<JsonObject>(configUri);
            return configData;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting server config: {ex}");
            return null;
        }
    }

    public async Task<UpdateConfigResult> UpdateConfigurationIdAsync(int configurationId)
    {
        try
        {
            var resolved = await _serverLocator.ResolveAsync();
            using var httpClient = AuthenticatedHttpClientFactory.Create(resolved);

            var updateUri = new Uri(resolved.BaseUrl).AppendPath("/admin/config");
            var updateBody = new { setting = "global.configurationId", value = configurationId };

            using var response = await httpClient.PostAsJsonAsync(updateUri, updateBody);
            var responseText = await response.Content.ReadAsStringAsync();
            var body = JsonNode.Parse(responseText) as JsonObject;

            if (response.IsSuccessStatusCode)
            {
                return new UpdateConfigResult
                {
                    Success = true,
                    RequiresRestart = body?["requiresRestart"]?.GetValue<bool>() ?? true,
                    Message = body?["message"]?.GetValue<string>() ?? "Update successful"
                };
            }

            return new UpdateConfigResult
            {
                Success = false,
                ErrorMessage = body?["message"]?.GetValue<string>() ?? "Unknown error"
            };
        }
        catch (Exception ex)
        {
            return new UpdateConfigResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<UpdateConfigResult> UpdateRouterPortAsync(int port)
    {
        try
        {
            var resolved = await _serverLocator.ResolveAsync();
            using var httpClient = AuthenticatedHttpClientFactory.Create(resolved);

            var updateUri = new Uri(resolved.BaseUrl).AppendPath("/admin/config");
            var updateBody = new { setting = "global.routerPort", value = port };

            using var response = await httpClient.PostAsJsonAsync(updateUri, updateBody);

            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var resultData = JsonNode.Parse(responseText) as JsonObject;
                var requiresRestart = resultData?["requiresRestart"]?.GetValue<bool>() ?? false;
                var message = resultData?["message"]?.GetValue<string>() ?? "Update successful";

                return new UpdateConfigResult
                {
                    Success = true,
                    RequiresRestart = requiresRestart,
                    Message = message
                };
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                var errorData = JsonNode.Parse(errorText) as JsonObject;
                var errorMessage = errorData?["message"]?.GetValue<string>() ?? "Unknown error";

                return new UpdateConfigResult
                {
                    Success = false,
                    ErrorMessage = errorMessage
                };
            }
        }
        catch (Exception ex)
        {
            return new UpdateConfigResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class TestConnectionResult
{
    public bool Success { get; set; }
    public string? BaseUrl { get; set; }
    public string? ResolvedVia { get; set; }
    public string? ErrorMessage { get; set; }
}

public class UpdateConfigResult
{
    public bool Success { get; set; }
    public bool RequiresRestart { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
}

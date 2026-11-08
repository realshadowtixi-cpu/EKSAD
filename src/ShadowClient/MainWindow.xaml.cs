using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.QuiltMC;
using CmlLib.Core.ProcessBuilder;
using ShadowClient.Services;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ShadowClient;

public partial class MainWindow : Window
{
    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShadowClient");
    private readonly string _settingsFile;
    private readonly string _profilesFile;
    private LauncherSettings _settings;
    private List<GameProfile> _profiles = new();
    private MinecraftPath _minecraftPath;
    private MinecraftLauncher _launcher;
    private readonly ModrinthService _modrinth = new();
    private readonly HttpClient _skinHttp = new() { Timeout = TimeSpan.FromSeconds(40) };
    private MSession? _session;
    private bool _isMicrosoftSession;
    private List<ModrinthProject> _modResults = new();

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(_dataDirectory);
        _settingsFile = Path.Combine(_dataDirectory, "settings.json");
        _profilesFile = Path.Combine(_dataDirectory, "profiles.json");
        _settings = LoadJson(_settingsFile, new LauncherSettings());
        _profiles = LoadJson(_profilesFile, new List<GameProfile>());
        if (_profiles.Count == 0)
            _profiles.Add(new GameProfile { Name = "Default 1.21.1", VersionId = "1.21.1" });

        _minecraftPath = new MinecraftPath(_settings.GameDirectory);
        _launcher = CreateLauncher(_minecraftPath);
        ApplySettingsToUi();
        RefreshProfileUi();
        RefreshVersionsAsync();
        RefreshLocalFiles();
        _skinHttp.DefaultRequestHeaders.UserAgent.ParseAdd("ShadowClient/0.1.0");
    }

    private MinecraftLauncher CreateLauncher(MinecraftPath path)
    {
        var launcher = new MinecraftLauncher(path);
        launcher.FileProgressChanged += (_, e) => Dispatcher.Invoke(() =>
            SetStatus($"Downloading {e.Name}  ·  {e.ProgressedTasks}/{e.TotalTasks}"));
        launcher.ByteProgressChanged += (_, e) => Dispatcher.Invoke(() =>
            SetStatus($"Downloading game files  ·  {FormatBytes(e.ProgressedBytes)} / {FormatBytes(e.TotalBytes)}"));
        return launcher;
    }

    private static T LoadJson<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? fallback;
        }
        catch { return fallback; }
    }

    private void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ApplySettingsToUi()
    {
        GameDirectoryInput.Text = _settings.GameDirectory;
        JavaPathInput.Text = _settings.JavaPath;
        OfflineUsernameInput.Text = _settings.OfflineUsername;
        WidthInput.Text = _settings.ScreenWidth.ToString();
        HeightInput.Text = _settings.ScreenHeight.ToString();
        RamCombo.SelectedIndex = _settings.MaximumRamMb switch { <= 1024 => 0, <= 1536 => 1, _ => 2 };
        HomeRamText.Text = $"{_settings.MaximumRamMb} MB";
        HomeRamNote.Text = $"{_settings.MaximumRamMb} MB  ·  balanced";
    }

    private void RefreshProfileUi()
    {
        ProfilesList.ItemsSource = null;
        ProfilesList.ItemsSource = _profiles;
        HomeProfileCombo.ItemsSource = null;
        HomeProfileCombo.ItemsSource = _profiles;
        if (_profiles.Count > 0)
        {
            HomeProfileCombo.SelectedIndex = 0;
            ProfilesList.SelectedIndex = 0;
        }
    }

    private async void RefreshVersionsAsync()
    {
        try
        {
            SetStatus("Fetching official Minecraft version list...");
            var versions = await _launcher.GetAllVersionsAsync();
            var names = versions.Select(v => v.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            VersionCombo.ItemsSource = names;
            if (names.Contains("1.21.1")) VersionCombo.SelectedItem = "1.21.1";
            else if (names.Count > 0) VersionCombo.SelectedIndex = 0;

            ModVersionCombo.ItemsSource = names.Where(n => !n.Contains('_')).ToList();
            ModVersionCombo.SelectedItem = names.Contains("1.21.1") ? "1.21.1" : VersionCombo.SelectedItem;
            SetStatus($"Ready  ·  {names.Count} Minecraft versions available");
        }
        catch (Exception ex)
        {
            SetStatus("Could not load version manifest");
            ShowError("Could not fetch the Minecraft version list. Check your internet connection.\n\n" + ex.Message);
        }
    }

    private static string FormatBytes(long value)
    {
        if (value < 1024) return value + " B";
        if (value < 1024 * 1024) return (value / 1024d).ToString("0.0") + " KB";
        return (value / 1024d / 1024d).ToString("0.0") + " MB";
    }

    private void SetStatus(string message) => StatusText.Text = message;
    private void SetBusy(bool busy)
    {
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.IsEnabled = !busy;
        InstallVersionButton.IsEnabled = !busy;
    }

    private void ShowError(string message) => MessageBox.Show(this, message, "Shadow Client", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string page) return;
        ShowPage(page);
    }

    private void ShowPage(string page)
    {
        HomePage.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        VersionsPage.Visibility = page == "Versions" ? Visibility.Visible : Visibility.Collapsed;
        ModsPage.Visibility = page == "Mods" ? Visibility.Visible : Visibility.Collapsed;
        SkinsPage.Visibility = page == "Skins" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        PageTitle.Text = page switch
        {
            "Home" => "Dashboard",
            "Versions" => "Versions & profiles",
            "Mods" => "Mod browser",
            "Skins" => "Player skins",
            _ => "Settings"
        };
        PageSubtitle.Text = page switch
        {
            "Home" => "Your game. Your setup. No clutter.",
            "Versions" => "Install game versions and create named profiles.",
            "Mods" => "Find compatible content and manage installed files.",
            "Skins" => "View and update the skin on your Minecraft account.",
            _ => "Configure the game directory, Java and performance."
        };
        var buttons = new[] { NavHome, NavVersions, NavMods, NavSkins, NavSettings };
        foreach (var b in buttons)
        {
            bool active = Equals(b.Tag?.ToString(), page);
            b.Background = active ? new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#27313B")) :
                System.Windows.Media.Brushes.Transparent;
            b.Foreground = active ? System.Windows.Media.Brushes.White :
                new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#CAD2DA"));
        }
        if (page == "Mods") RefreshLocalFiles();
        if (page == "Skins" && _isMicrosoftSession && _session != null) RefreshSkinAsync();
    }

    private void GoVersions_Click(object sender, RoutedEventArgs e) => ShowPage("Versions");
    private void GoSettings_Click(object sender, RoutedEventArgs e) => ShowPage("Settings");

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            SetStatus("Opening Microsoft sign-in...");
            var loginHandler = JELoginHandlerBuilder.BuildDefault();
            var authenticatedSession = await loginHandler.Authenticate();
            if (authenticatedSession == null) throw new InvalidOperationException("Microsoft sign-in did not return a Minecraft session.");

            _session = authenticatedSession;
            _isMicrosoftSession = true;
            UpdateAccountUi();
            SkinAccountName.Text = _session.Username;
            SetStatus($"Signed in with Microsoft as {_session.Username}");
        }
        catch (Exception ex)
        {
            ShowError("Microsoft sign-in failed. Make sure the account owns Minecraft: Java Edition.\n\n" + ex.Message);
            SetStatus("Sign-in failed");
        }
        finally { SetBusy(false); }
    }

    private void UseOffline_Click(object sender, RoutedEventArgs e)
    {
        string username = OfflineUsernameInput.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[A-Za-z0-9_]{3,16}$"))
        {
            ShowError("Enter a Minecraft offline username containing 3–16 letters, numbers or underscores.");
            return;
        }

        _session = MSession.CreateOfflineSession(username);
        _isMicrosoftSession = false;
        _settings.OfflineUsername = username;
        SaveJson(_settingsFile, _settings);
        UpdateAccountUi();
        SkinAccountName.Text = $"{username} · Offline profile";
        SkinOfflineNote.Visibility = Visibility.Visible;
        CurrentSkinImage.Source = null;
        SkinVariantText.Text = "No authenticated skin in offline mode";
        SetStatus($"Offline profile active · {username} · offline-mode servers only");
        ShowPage("Home");
    }

    private void UpdateAccountUi()
    {
        bool connected = _session != null;
        LoginButton.Visibility = _isMicrosoftSession ? Visibility.Collapsed : Visibility.Visible;
        UseOfflineButton.Visibility = _isMicrosoftSession ? Visibility.Collapsed : Visibility.Visible;
        OfflineUsernameInput.Visibility = _isMicrosoftSession ? Visibility.Collapsed : Visibility.Visible;
        LogoutButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        if (_isMicrosoftSession && _session != null)
        {
            AccountStatusText.Text = "MICROSOFT ACCOUNT";
            AccountModeText.Text = "Authenticated Microsoft account";
            SidebarAccountText.Text = _session.Username;
            SkinAccountName.Text = _session.Username;
            SkinOfflineNote.Visibility = Visibility.Collapsed;
        }
        else if (_session != null)
        {
            AccountStatusText.Text = "OFFLINE PROFILE";
            AccountModeText.Text = "Offline-mode servers only";
            SidebarAccountText.Text = _session.Username;
            SkinAccountName.Text = $"{_session.Username} · Offline profile";
            SkinOfflineNote.Visibility = Visibility.Visible;
        }
        else
        {
            AccountStatusText.Text = "ACCOUNT NOT CONNECTED";
            AccountModeText.Text = "Choose Microsoft or offline profile";
            SidebarAccountText.Text = "Not signed in";
            SkinAccountName.Text = "Sign in with Microsoft to manage your skin";
            SkinOfflineNote.Visibility = Visibility.Collapsed;
        }
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        _session = null;
        _isMicrosoftSession = false;
        UpdateAccountUi();
        CurrentSkinImage.Source = null;
        SkinVariantText.Text = "Not loaded";
        SetStatus("Signed out");
    }

    private async void Play_Click(object sender, RoutedEventArgs e) => await LaunchSelectedAsync(HomeProfileCombo.SelectedItem as GameProfile);
    private async void LaunchSelectedProfile_Click(object sender, RoutedEventArgs e) => await LaunchSelectedAsync(ProfilesList.SelectedItem as GameProfile);

    private async Task LaunchSelectedAsync(GameProfile? profile)
    {
        if (_session == null)
        {
            ShowError("Choose Microsoft login or activate an offline username before launching.");
            return;
        }
        if (profile == null)
        {
            ShowError("Create or select a game profile first.");
            return;
        }

        try
        {
            SetBusy(true);
            SetStatus($"Preparing {profile.VersionId}...");
            await _launcher.InstallAsync(profile.VersionId);
            var version = await _launcher.GetVersionAsync(profile.VersionId);
            string? javaPath = string.IsNullOrWhiteSpace(_settings.JavaPath)
                ? await _launcher.GetJavaPath(version)
                : _settings.JavaPath;
            if (string.IsNullOrWhiteSpace(javaPath)) javaPath = await _launcher.GetDefaultJavaPath();
            if (string.IsNullOrWhiteSpace(javaPath))
                throw new InvalidOperationException("Java was not found. Install the matching Java runtime or choose javaw.exe in Settings.");

            var options = new MLaunchOption
            {
                Session = _session,
                JavaPath = javaPath,
                MaximumRamMb = _settings.MaximumRamMb,
                MinimumRamMb = Math.Min(512, _settings.MaximumRamMb),
                ScreenWidth = _settings.ScreenWidth,
                ScreenHeight = _settings.ScreenHeight,
                FullScreen = false,
                GameLauncherName = "Shadow Client",
                GameLauncherVersion = "0.1.0"
            };

            SetStatus("Starting Minecraft...");
            var process = await _launcher.BuildProcessAsync(profile.VersionId, options);
            if (!process.Start()) throw new InvalidOperationException("Windows could not start the Minecraft process.");
            SetStatus($"Minecraft started  ·  {profile.Name}");
        }
        catch (Exception ex)
        {
            SetStatus("Launch failed");
            ShowError("Minecraft could not be launched. Confirm that the selected version and Java runtime are compatible.\n\n" + ex.Message);
        }
        finally { SetBusy(false); }
    }

    private async void RefreshVersions_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try { await RefreshVersionsCoreAsync(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private async Task RefreshVersionsCoreAsync()
    {
        SetStatus("Refreshing Minecraft metadata...");
        var versions = await _launcher.GetAllVersionsAsync();
        var names = versions.Select(v => v.Name).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        VersionCombo.ItemsSource = names;
        if (names.Contains("1.21.1")) VersionCombo.SelectedItem = "1.21.1";
        ModVersionCombo.ItemsSource = names;
        ModVersionCombo.SelectedItem = names.Contains("1.21.1") ? "1.21.1" : names.FirstOrDefault();
        SetStatus($"Version list refreshed  ·  {names.Count} entries");
    }

    private async void InstallProfile_Click(object sender, RoutedEventArgs e)
    {
        if (VersionCombo.SelectedItem is not string mcVersion)
        {
            ShowError("Choose a Minecraft version first.");
            return;
        }
        string profileName = ProfileNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            ShowError("Enter a profile name.");
            return;
        }

        try
        {
            SetBusy(true);
            SetStatus($"Installing Minecraft {mcVersion}...");
            await _launcher.InstallAsync(mcVersion);
            string versionId = mcVersion;
            string loader = (LoaderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Vanilla";

            if (loader == "Fabric")
            {
                SetStatus("Installing Fabric loader...");
                var installer = new FabricInstaller(new HttpClient());
                versionId = await installer.Install(mcVersion, _minecraftPath);
            }
            else if (loader == "Quilt")
            {
                SetStatus("Installing Quilt loader...");
                var installer = new QuiltInstaller(new HttpClient());
                versionId = await installer.Install(mcVersion, _minecraftPath);
            }

            var existing = _profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.VersionId = versionId;
            }
            else
            {
                _profiles.Add(new GameProfile { Name = profileName, VersionId = versionId });
            }
            SaveJson(_profilesFile, _profiles);
            RefreshProfileUi();
            var newProfile = _profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
            HomeProfileCombo.SelectedItem = newProfile;
            ProfilesList.SelectedItem = newProfile;
            SetStatus($"Profile ready  ·  {profileName} ({versionId})");
        }
        catch (Exception ex)
        {
            SetStatus("Installation failed");
            ShowError("The game version or mod loader could not be installed.\n\n" + ex.Message);
        }
        finally { SetBusy(false); }
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesList.SelectedItem is GameProfile profile)
        {
            SelectedProfileText.Text = $"{profile.Name}\nMinecraft version ID: {profile.VersionId}\n\nThis profile uses the selected game version and your global performance settings.";
            ProfileNameInput.Text = profile.Name;
        }
    }

    private void HomeProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HomeProfileCombo.SelectedItem is GameProfile profile)
        {
            HomeProfileName.Text = profile.Name;
            HomeVersionName.Text = profile.VersionId;
        }
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not GameProfile profile)
        {
            ShowError("Select a profile first.");
            return;
        }
        string newName = ProfileNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            ShowError("Enter the new profile name in the Profile name box.");
            return;
        }
        if (_profiles.Any(p => p != profile && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError("A profile with that name already exists.");
            return;
        }
        profile.Name = newName;
        SaveJson(_profilesFile, _profiles);
        RefreshProfileUi();
        ProfilesList.SelectedItem = profile;
        HomeProfileCombo.SelectedItem = profile;
        SetStatus("Profile renamed");
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not GameProfile profile) return;
        if (MessageBox.Show(this, $"Remove the profile entry '{profile.Name}'? The installed game files will stay on disk.",
                "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _profiles.Remove(profile);
        SaveJson(_profilesFile, _profiles);
        RefreshProfileUi();
        SetStatus("Profile entry removed; game files were kept");
    }

    private async void SearchMods_Click(object sender, RoutedEventArgs e)
    {
        string query = ModSearchInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowError("Enter a search term.");
            return;
        }
        string gameVersion = ModVersionCombo.SelectedItem?.ToString() ?? "1.21.1";
        string loader = GetSelectedModLoader();
        string projectType = GetProjectType();
        try
        {
            SetBusy(true);
            SetStatus("Searching Modrinth...");
            _modResults = await _modrinth.SearchAsync(query, gameVersion, loader, projectType);
            ModResultsList.ItemsSource = null;
            ModResultsList.ItemsSource = _modResults;
            SetStatus($"Found {_modResults.Count} compatible projects");
        }
        catch (Exception ex)
        {
            SetStatus("Mod search failed");
            ShowError("Modrinth search failed.\n\n" + ex.Message);
        }
        finally { SetBusy(false); }
    }

    private async void InstallPerformancePack_Click(object sender, RoutedEventArgs e)
    {
        var profile = HomeProfileCombo.SelectedItem as GameProfile;
        if (profile == null || !profile.VersionId.Contains("fabric", StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Create or select a Fabric profile first, then choose its matching Minecraft version in the Mod Browser.");
            return;
        }
        string gameVersion = ModVersionCombo.SelectedItem?.ToString() ?? "1.21.1";
        string modsFolder = Path.Combine(_settings.GameDirectory, "mods");
        string[] pack = { "Sodium", "Lithium", "FerriteCore", "ImmediatelyFast", "Entity Culling" };
        var installedNames = new List<string>();
        var notFound = new List<string>();
        try
        {
            SetBusy(true);
            ModTypeCombo.SelectedIndex = 0;
            ModLoaderCombo.SelectedIndex = 0;
            SetStatus("Finding compatible low-end performance mods...");
            foreach (string modName in pack)
            {
                var matches = await _modrinth.SearchAsync(modName, gameVersion, "fabric", "mod");
                var match = matches.FirstOrDefault(m => m.Title.Contains(modName, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    notFound.Add(modName);
                    continue;
                }
                SetStatus($"Installing {match.Title}...");
                await _modrinth.InstallLatestAsync(match.ProjectId, modsFolder, gameVersion, "fabric", true);
                installedNames.Add(match.Title);
            }
            RefreshLocalFiles();
            string summary = installedNames.Count == 0 ? "No mods were installed." : "Installed: " + string.Join(", ", installedNames);
            if (notFound.Count > 0) summary += "\nNot found for this version: " + string.Join(", ", notFound);
            SetStatus($"Performance pack finished  ·  {installedNames.Count}/{pack.Length} selected");
            MessageBox.Show(this, summary + "\n\nRestart Minecraft to load the updated mods folder.", "Low-end performance pack", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus("Performance pack installation failed");
            ShowError("The performance pack could not be fully installed. Check that your selected Fabric profile and Minecraft version match.\n\n" + ex.Message);
        }
        finally { SetBusy(false); }
    }

    private void ModTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized) RefreshLocalFiles();
    }

    private string GetProjectType() => ModTypeCombo.SelectedIndex switch
    {
        1 => "resourcepack",
        2 => "shader",
        _ => "mod"
    };

    private string GetSelectedModLoader()
    {
        if (GetProjectType() != "mod") return "minecraft";
        return ((ModLoaderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Fabric").ToLowerInvariant();
    }

    private string GetTargetContentDirectory()
    {
        return GetProjectType() switch
        {
            "resourcepack" => Path.Combine(_settings.GameDirectory, "resourcepacks"),
            "shader" => Path.Combine(_settings.GameDirectory, "shaderpacks"),
            _ => Path.Combine(_settings.GameDirectory, "mods")
        };
    }

    private async void InstallSelectedMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModResultsList.SelectedItem is not ModrinthProject project)
        {
            ShowError("Select a search result first.");
            return;
        }
        string gameVersion = ModVersionCombo.SelectedItem?.ToString() ?? "1.21.1";
        string loader = GetSelectedModLoader();
        string targetDirectory = GetTargetContentDirectory();
        bool resolveDependencies = GetProjectType() == "mod";
        try
        {
            SetBusy(true);
            SetStatus($"Installing {project.Title}...");
            int installedCount = await _modrinth.InstallLatestAsync(project.ProjectId, targetDirectory,
                gameVersion, loader, resolveDependencies);
            RefreshLocalFiles();
            SetStatus($"Installed {project.Title}  ·  {installedCount} file(s) added");
        }
        catch (Exception ex)
        {
            SetStatus("Content installation failed");
            ShowError("The selected project could not be installed. Check the version and loader filters.\n\n" + ex.Message);
        }
        finally { SetBusy(false); }
    }

    private void RefreshLocalFiles()
    {
        string folder = GetTargetContentDirectory();
        Directory.CreateDirectory(folder);
        LocalFilesPathText.Text = folder;
        string[] patterns = GetProjectType() == "mod" ? new[] { "*.jar", "*.jar.disabled" } : new[] { "*.zip", "*.zip.disabled", "*.jar", "*.jar.disabled" };
        var files = patterns.SelectMany(pattern => Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new LocalGameFile { FullPath = path })
            .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        LocalFilesList.ItemsSource = files;
    }

    private void AddLocalFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Minecraft content file",
            Filter = GetProjectType() == "mod" ? "Mod files (*.jar)|*.jar" : "Content files (*.zip;*.jar)|*.zip;*.jar",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        string folder = GetTargetContentDirectory();
        Directory.CreateDirectory(folder);
        int copied = 0;
        foreach (string file in dialog.FileNames)
        {
            string destination = Path.Combine(folder, Path.GetFileName(file));
            if (File.Exists(destination)) continue;
            File.Copy(file, destination);
            copied++;
        }
        RefreshLocalFiles();
        SetStatus($"Added {copied} file(s)");
    }

    private void ToggleLocalFile_Click(object sender, RoutedEventArgs e)
    {
        if (LocalFilesList.SelectedItem is not LocalGameFile file) return;
        string destination = file.Enabled ? file.FullPath + ".disabled" : file.FullPath[..^".disabled".Length];
        if (File.Exists(destination))
        {
            ShowError("A file with the target name already exists. Remove the duplicate first.");
            return;
        }
        File.Move(file.FullPath, destination);
        RefreshLocalFiles();
        SetStatus(file.Enabled ? "Content disabled" : "Content enabled");
    }

    private void RemoveLocalFile_Click(object sender, RoutedEventArgs e)
    {
        if (LocalFilesList.SelectedItem is not LocalGameFile file) return;
        if (MessageBox.Show(this, $"Remove {file.FileName}?", "Remove content", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        File.Delete(file.FullPath);
        RefreshLocalFiles();
        SetStatus("Content file removed");
    }

    private void OpenFilesFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = GetTargetContentDirectory();
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private async void RefreshSkin_Click(object sender, RoutedEventArgs e) => await RefreshSkinAsync();

    private async Task RefreshSkinAsync()
    {
        if (!_isMicrosoftSession || _session == null)
        {
            ShowError("Minecraft skin services require an authenticated Microsoft account. Offline profiles only provide a username for offline-mode servers.");
            return;
        }
        try
        {
            SetStatus("Loading current Minecraft skin...");
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var response = await _skinHttp.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("skins", out var skins) || skins.GetArrayLength() == 0)
                throw new InvalidOperationException("No skin was returned for this Minecraft profile.");

            var active = skins.EnumerateArray().FirstOrDefault(s =>
                s.TryGetProperty("state", out var state) && state.GetString() == "ACTIVE");
            if (active.ValueKind == JsonValueKind.Undefined) active = skins[0];
            string? skinUrl = active.TryGetProperty("url", out var url) ? url.GetString() : null;
            string variant = active.TryGetProperty("variant", out var v) ? v.GetString() ?? "unknown" : "unknown";
            SkinVariantText.Text = $"Active skin · {variant}";
            SkinAccountName.Text = _session.Username;
            if (!string.IsNullOrWhiteSpace(skinUrl))
            {
                byte[] bytes = await _skinHttp.GetByteArrayAsync(skinUrl);
                var bitmap = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                CurrentSkinImage.Source = bitmap;
            }
            SetStatus("Skin loaded");
        }
        catch (Exception ex)
        {
            SetStatus("Could not load skin");
            ShowError("The current skin could not be loaded.\n\n" + ex.Message);
        }
    }

    private async void UploadSkin_Click(object sender, RoutedEventArgs e)
    {
        if (!_isMicrosoftSession || _session == null)
        {
            ShowError("Skin upload requires an authenticated Microsoft account. Offline profiles cannot upload an official Minecraft skin.");
            return;
        }
        var dialog = new OpenFileDialog { Title = "Choose a Minecraft skin PNG", Filter = "PNG images (*.png)|*.png" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SetBusy(true);
            SetStatus("Uploading skin to your Minecraft profile...");
            string variant = SkinVariantCombo.SelectedIndex == 1 ? "slim" : "classic";
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(variant), "variant");
            await using var fileStream = File.OpenRead(dialog.FileName);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(fileContent, "file", Path.GetFileName(dialog.FileName));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/minecraft/profile/skins")
            {
                Content = form
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var response = await _skinHttp.SendAsync(request);
            response.EnsureSuccessStatusCode();
            SetStatus("Skin uploaded successfully");
            await RefreshSkinAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Skin upload failed");
            ShowError("The skin could not be uploaded. Confirm it is a valid Minecraft skin PNG and that your account owns Java Edition.\n\n" + ex.Message);
        }
        finally { SetBusy(false); }
    }

    private void BrowseGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Minecraft game directory", InitialDirectory = Directory.Exists(GameDirectoryInput.Text) ? GameDirectoryInput.Text : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) };
        if (dialog.ShowDialog(this) == true) GameDirectoryInput.Text = dialog.FolderName;
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select Java executable", Filter = "Java executable (javaw.exe;java.exe)|javaw.exe;java.exe|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) JavaPathInput.Text = dialog.FileName;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        string folder = GameDirectoryInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            ShowError("Choose a game directory.");
            return;
        }
        if (!int.TryParse(WidthInput.Text, out int width) || width < 640 || width > 7680 ||
            !int.TryParse(HeightInput.Text, out int height) || height < 360 || height > 4320)
        {
            ShowError("Enter a valid display size. Width must be 640–7680 and height 360–4320.");
            return;
        }
        _settings.GameDirectory = Path.GetFullPath(folder);
        _settings.JavaPath = JavaPathInput.Text.Trim();
        _settings.MaximumRamMb = RamCombo.SelectedIndex switch { 0 => 1024, 2 => 2048, _ => 1536 };
        _settings.ScreenWidth = width;
        _settings.ScreenHeight = height;
        Directory.CreateDirectory(_settings.GameDirectory);
        SaveJson(_settingsFile, _settings);
        _minecraftPath = new MinecraftPath(_settings.GameDirectory);
        _launcher = CreateLauncher(_minecraftPath);
        RefreshLocalFiles();
        ApplySettingsToUi();
        RefreshVersionsAsync();
        SetStatus("Settings saved");
    }
}

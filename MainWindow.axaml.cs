using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UE4SSInstaller.Services;

namespace UE4SSInstaller;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType ZipType = new("Zip archive")
    {
        Patterns = ["*.zip"],
        MimeTypes = ["application/zip"]
    };

    private readonly List<DetectedGame> _allGames = [];
    private string? _win64Path;
    private bool _busy;
    private bool _applyingSelection;
    private InstalledMod? _pendingModUninstall;

    public MainWindow()
    {
        InitializeComponent();
        ApplyChromeInset(WindowDecorationMargin);
        PropertyChanged += OnWindowPropertyChanged;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowDecorationMarginProperty)
            ApplyChromeInset(WindowDecorationMargin);
    }

    private void ApplyChromeInset(Thickness chrome)
    {
        ContentRoot.Margin = new Thickness(
            Math.Max(20, chrome.Left + 16),
            chrome.Top + 10,
            Math.Max(20, chrome.Right + 16),
            16);
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        SetStatus("Scanning Steam library...");
        EmptyGamesText.Text = "Scanning Steam library...";

        try
        {
            var games = await Task.Run(SteamScanner.FindUnrealGames);
            _allGames.Clear();
            _allGames.AddRange(games);
            ApplyGameFilter();
            SetStatus($"Found {games.Count} Unreal games.");
        }
        catch (Exception ex)
        {
            _allGames.Clear();
            GamesListBox.ItemsSource = Array.Empty<DetectedGame>();
            EmptyGamesText.Text = "Steam scan failed. Add a game manually using the button below.";
            EmptyGamesText.IsVisible = true;
            SetStatus($"Steam scan failed: {ex.Message}");
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        => ApplyGameFilter();

    private void OnGamesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingSelection || _busy)
            return;

        if (GamesListBox.SelectedItem is not DetectedGame game)
            return;

        _applyingSelection = true;
        try
        {
            PathTextBox.Text = game.Win64Path;
            ApplyGameFolder(game.Win64Path);
        }
        finally
        {
            _applyingSelection = false;
        }
    }

    private async void OnAddGameManuallyClick(object? sender, RoutedEventArgs e)
    {
        ManualAddPanel.IsVisible = true;
        await PickGameFolderAsync();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
        => await PickGameFolderAsync();

    private async Task PickGameFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select game install folder",
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return;

        var folderPath = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(folderPath))
        {
            SetStatus("Could not resolve a local path for that folder.");
            return;
        }

        ClearGameSelection();
        PathTextBox.Text = folderPath;
        ApplyGameFolder(folderPath);
    }

    private void OnPathTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        ClearGameSelection();
        ApplyGameFolder(PathTextBox.Text);
        e.Handled = true;
    }

    private void OnPathTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_applyingSelection)
            return;

        ApplyGameFolder(PathTextBox.Text);
    }

    private void ApplyGameFolder(string? gameInstallPath)
    {
        if (_busy)
            return;

        if (string.IsNullOrWhiteSpace(gameInstallPath))
        {
            if (_win64Path is not null)
            {
                _win64Path = null;
                SyncListSelection();
                RefreshInstalledMods();
                UpdateActionButtons();
                SetStatus("Ready");
            }

            return;
        }

        if (string.Equals(gameInstallPath.Trim().Trim('"'), _win64Path, StringComparison.OrdinalIgnoreCase))
        {
            RefreshInstalledMods();
            ShowInstallStatus();
            return;
        }

        var win64 = PathDetector.FindWin64Directory(gameInstallPath);
        if (win64 is null)
        {
            _win64Path = null;
            SyncListSelection();
            RefreshInstalledMods();
            UpdateActionButtons();
            SetStatus("No Binaries/Win64 folder was found. Select the game's Steam folder (Manage → Browse local files).");
            return;
        }

        _win64Path = win64;
        PathTextBox.Text = win64;
        SyncListSelection();
        RefreshInstalledMods();
        UpdateActionButtons();
        ShowInstallStatus();
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (_win64Path is null || _busy)
            return;

        var win64Path = _win64Path;
        var channel = VersionComboBox.SelectedIndex == 1
            ? Ue4ssChannel.ZDev
            : Ue4ssChannel.Release;

        await RunBusyAsync("Downloading...", async () =>
        {
            var zipPath = await GitHubFetcher.DownloadAsync(channel);
            try
            {
                SetStatus("Extracting...");
                await Task.Run(() => ZipInstaller.InstallUe4ss(zipPath, win64Path, channel));
            }
            finally
            {
                TryDelete(zipPath);
            }

            MarkManagedChannel(win64Path, channel);

            var pack = FindSignaturePack(win64Path);
            if (pack is null)
            {
                SetStatus($"Installed {FormatChannel(channel)}. {InstallTracker.Detect(win64Path).StatusText}");
                return;
            }

            if (!ZipInstaller.TryGetSignaturesDirectory(win64Path, out _))
            {
                SetStatus($"Installed {FormatChannel(channel)}, but ue4ss/ was missing so signatures were not copied.");
                return;
            }

            try
            {
                SetStatus($"Applying {pack.DisplayName}...");
                var signatureZip = await GitHubFetcher.DownloadLatestReleaseZipAsync(pack.Owner, pack.Repo);
                try
                {
                    var dest = await Task.Run(() => ZipInstaller.InstallSignaturePack(signatureZip, win64Path));
                    if (pack.IniPatches.Length > 0)
                    {
                        await Task.Run(() => SettingsIniPatcher.ApplyPatches(win64Path, pack.IniPatches));
                    }

                    if (pack.HasEngineVersionOverride)
                    {
                        await Task.Run(() => SettingsIniPatcher.ApplyEngineVersion(
                            win64Path,
                            pack.EngineMajorVersion!.Value,
                            pack.EngineMinorVersion!.Value));
                    }

                    SetStatus($"Installed {FormatChannel(channel)} and {pack.DisplayName} into {dest}.");
                }
                finally
                {
                    TryDelete(signatureZip);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Installed {FormatChannel(channel)}, but the signature pack failed: {ex.Message}");
            }
        });
    }

    private void OnUninstallClick(object? sender, RoutedEventArgs e)
    {
        if (_win64Path is null || _busy)
            return;

        _pendingModUninstall = null;
        ConfirmTitle.Text = "Uninstall UE4SS?";
        ConfirmBody.Text =
            "This deletes the ue4ss folder (including mods and signatures) and UE4SS DLLs in Win64 such as dwmapi.dll.";
        UninstallConfirmOverlay.IsVisible = true;
    }

    private void OnUninstallCancelClick(object? sender, RoutedEventArgs e)
    {
        UninstallConfirmOverlay.IsVisible = false;
        _pendingModUninstall = null;
    }

    private async void OnUninstallConfirmClick(object? sender, RoutedEventArgs e)
    {
        UninstallConfirmOverlay.IsVisible = false;
        if (_win64Path is null || _busy)
            return;

        var win64Path = _win64Path;
        var mod = _pendingModUninstall;
        _pendingModUninstall = null;

        if (mod is not null)
        {
            var name = mod.Name;
            await RunBusyAsync($"Removing {name}...", async () =>
            {
                await Task.Run(() => ZipInstaller.UninstallMod(win64Path, mod.Id));
                RefreshInstalledMods();
                SetStatus($"Removed {name}.");
            });
            return;
        }

        await RunBusyAsync("Uninstalling UE4SS...", async () =>
        {
            await Task.Run(() => ZipInstaller.UninstallUe4ss(win64Path));
            ClearManagedChannel(win64Path);
            RefreshInstalledMods();
            SetStatus("UE4SS was removed from this game.");
        });
    }

    private async void OnInstallModClick(object? sender, RoutedEventArgs e)
    {
        if (_win64Path is null || _busy)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Mod Zip",
            AllowMultiple = false,
            FileTypeFilter = [ZipType]
        });

        if (files.Count == 0)
            return;

        var zipPath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(zipPath))
        {
            SetStatus("Could not resolve a local path for that zip.");
            return;
        }

        var win64Path = _win64Path;
        await RunBusyAsync("Extracting...", async () =>
        {
            var result = await Task.Run(() => ZipInstaller.InstallMod(zipPath, win64Path));
            RefreshInstalledMods();
            var where = result.Kind == ModPackageKind.GameDirectory
                ? "the game folder (UE4SS pack / overlay)"
                : "the Mods folder";
            SetStatus($"Installed {result.Name} into {where}.");
        });
    }

    private void OnUninstallModClick(object? sender, RoutedEventArgs e)
    {
        if (_win64Path is null || _busy)
            return;

        if (InstalledModsCombo.SelectedItem is not InstalledMod mod)
            return;

        _pendingModUninstall = mod;
        ConfirmTitle.Text = $"Remove {mod.Name}?";
        ConfirmBody.Text =
            "This deletes the files this app installed for that mod. UE4SS itself is left alone.";
        UninstallConfirmOverlay.IsVisible = true;
    }

    private async Task RunBusyAsync(string status, Func<Task> work)
    {
        _busy = true;
        UpdateActionButtons();
        BusyBar.IsVisible = true;
        SetStatus(status);

        try
        {
            await work();
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}");
        }
        finally
        {
            _busy = false;
            BusyBar.IsVisible = false;
            UpdateActionButtons();
        }
    }

    private void OnInstalledModsSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => UpdateActionButtons();

    private void ApplyGameFilter()
    {
        var query = SearchTextBox.Text?.Trim() ?? string.Empty;
        IEnumerable<DetectedGame> filtered = _allGames;
        if (query.Length > 0)
            filtered = _allGames.Where(game => game.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        var list = filtered.ToList();
        var selected = _win64Path is null
            ? null
            : list.FirstOrDefault(game =>
                string.Equals(game.Win64Path, _win64Path, StringComparison.OrdinalIgnoreCase));

        _applyingSelection = true;
        try
        {
            GamesListBox.ItemsSource = list;
            GamesListBox.SelectedItem = selected;
        }
        finally
        {
            _applyingSelection = false;
        }

        EmptyGamesText.IsVisible = list.Count == 0;
        if (list.Count > 0)
            return;

        EmptyGamesText.Text = _allGames.Count > 0
            ? "No matching games."
            : "No Unreal games found in Steam. Add one manually using the button below.";
    }

    private void MarkManagedChannel(string win64Path, Ue4ssChannel channel)
    {
        var label = FormatChannel(channel);
        foreach (var game in _allGames)
        {
            if (string.Equals(game.Win64Path, win64Path, StringComparison.OrdinalIgnoreCase))
                game.ChannelLabel = label;
        }

        ApplyGameFilter();
    }

    private void ClearManagedChannel(string win64Path)
    {
        foreach (var game in _allGames)
        {
            if (string.Equals(game.Win64Path, win64Path, StringComparison.OrdinalIgnoreCase))
                game.ChannelLabel = null;
        }

        ApplyGameFilter();
    }

    private void SyncListSelection()
    {
        DetectedGame? match = null;
        if (_win64Path is not null)
        {
            match = _allGames.FirstOrDefault(game =>
                string.Equals(game.Win64Path, _win64Path, StringComparison.OrdinalIgnoreCase));
        }

        if (ReferenceEquals(GamesListBox.SelectedItem, match))
            return;

        _applyingSelection = true;
        try
        {
            GamesListBox.SelectedItem = match;
        }
        finally
        {
            _applyingSelection = false;
        }
    }

    private void ClearGameSelection()
    {
        if (GamesListBox.SelectedItem is null)
            return;

        _applyingSelection = true;
        try
        {
            GamesListBox.SelectedItem = null;
        }
        finally
        {
            _applyingSelection = false;
        }
    }

    private void UpdateActionButtons()
    {
        var canAct = !_busy && _win64Path is not null;
        var canUninstall = canAct
                           && InstallTracker.Detect(_win64Path!).Kind != InstallKind.None;
        InstallButton.IsEnabled = canAct;
        InstallModButton.IsEnabled = canAct;
        UninstallButton.IsEnabled = canUninstall;
        BrowseButton.IsEnabled = !_busy;
        AddGameManuallyButton.IsEnabled = !_busy;
        VersionComboBox.IsEnabled = !_busy;
        GamesListBox.IsEnabled = !_busy;
        SearchTextBox.IsEnabled = !_busy;
        PathTextBox.IsEnabled = !_busy;
        UninstallModButton.IsEnabled = canAct
                                       && InstalledModsCombo.SelectedItem is InstalledMod;
        InstalledModsCombo.IsEnabled = !_busy;
    }

    private void RefreshInstalledMods()
    {
        InstalledModsCombo.SelectedItem = null;
        InstalledModsCombo.ItemsSource = null;

        if (_win64Path is null)
        {
            InstalledModsPanel.IsVisible = false;
            return;
        }

        InstalledModsPanel.IsVisible = true;
        var mods = ModTracker.List(_win64Path).ToList();
        InstalledModsCombo.ItemsSource = mods;
        InstalledModsCombo.SelectedIndex = mods.Count > 0 ? 0 : -1;
    }

    private void ShowInstallStatus()
    {
        if (_win64Path is null)
        {
            SetStatus("Ready");
            return;
        }

        SetStatus(InstallTracker.Detect(_win64Path).StatusText);
        var pack = FindSignaturePack(_win64Path);
        if (pack is not null)
            SetStatus($"{StatusText.Text} {pack.DisplayName} will be applied on install.");
    }

    private KnownSignaturePack? FindSignaturePack(string win64Path)
    {
        var game = _allGames.FirstOrDefault(g =>
            string.Equals(g.Win64Path, win64Path, StringComparison.OrdinalIgnoreCase));
        return KnownSignatureCatalog.Find(game?.AppId, game?.Name, game?.InstallPath, win64Path);
    }

    private static string FormatChannel(Ue4ssChannel channel)
        => channel == Ue4ssChannel.ZDev ? "zDev" : "Release";

    private void SetStatus(string message) => StatusText.Text = message;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }
}

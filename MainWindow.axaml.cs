using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
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
    private bool _applyingSettings;
    private bool _devTabSelected;
    private InstalledMod? _pendingModUninstall;
    private InstalledMod? _pendingEditMod;
    private string? _pendingModZip;
    private bool _isHandheld;
    private bool _handheldShowActions;

    public MainWindow()
    {
        InitializeComponent();
        ApplyChromeInset(WindowDecorationMargin);
        if (HandheldLayout.ShouldForceExpanded(HandheldLayout.TryMeasurePrimaryDiagonalInches()))
            WindowState = WindowState.Maximized;
        PropertyChanged += OnWindowPropertyChanged;
        ApplyLayoutMode();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowDecorationMarginProperty)
            ApplyChromeInset(WindowDecorationMargin);
        else if (e.Property == WindowStateProperty)
            ApplyLayoutMode();
    }

    private void ApplyChromeInset(Thickness chrome)
    {
        ContentRoot.Margin = new Thickness(
            Math.Max(20, chrome.Left + 16),
            chrome.Top + 10,
            Math.Max(20, chrome.Right + 16),
            16);
    }

    private void ApplyLayoutMode()
    {
        var decision = HandheldLayout.Detect(WindowState);
        _isHandheld = decision.IsHandheld;
        Classes.Set("handheld", _isHandheld);

        if (GamesHint is not null)
        {
            GamesHint.Text = _isHandheld
                ? "Tap a game. If it isn't listed, add it manually."
                : "Click a game below. If it isn't listed, add it manually.";
        }

        ApplyHandheldPanes();
        ApplyChromeInset(WindowDecorationMargin);
        ApplyConfirmLayout();
        ApplyEditModLayout();
    }

    private void ApplyHandheldPanes()
    {
        if (GamesSection is null)
            return;

        if (!_isHandheld)
            _handheldShowActions = false;

        var showActions = _isHandheld && _handheldShowActions && _win64Path is not null;
        var showGames = !showActions;

        if (HandheldChrome is not null)
            HandheldChrome.IsVisible = showActions;
        GamesSection.IsVisible = showGames;
        if (AddGameSection is not null)
            AddGameSection.IsVisible = showGames;
        AddGameManuallyButton.HorizontalAlignment = _isHandheld
            ? Avalonia.Layout.HorizontalAlignment.Stretch
            : Avalonia.Layout.HorizontalAlignment.Right;

        VersionSection.IsVisible = !_isHandheld || showActions;
        StatusSection.IsVisible = !_isHandheld || showActions;

        if (showActions)
            ManualAddPanel.IsVisible = false;

        ContentRoot.RowDefinitions = showActions
            ? new RowDefinitions("Auto,Auto,Auto,Auto")
            : new RowDefinitions("*,Auto,Auto,Auto");

        ApplyInstalledModsVisibility();
        RefreshUe4ssOptions();
        if (showActions)
            RefreshSelectedGameTitle();
    }

    private void ApplyInstalledModsVisibility()
    {
        if (InstalledModsPanel is null)
            return;

        var showActions = _isHandheld && _handheldShowActions && _win64Path is not null;
        if (_isHandheld && !showActions)
        {
            InstalledModsPanel.IsVisible = false;
            return;
        }

        InstalledModsPanel.IsVisible = _win64Path is not null;
    }

    private void ApplyConfirmLayout()
    {
        if (ConfirmCard is null || ConfirmButtons is null)
            return;

        if (_isHandheld)
        {
            ConfirmCard.Width = double.NaN;
            ConfirmCard.Margin = new Thickness(16);
            ConfirmCard.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            ConfirmButtons.Orientation = Avalonia.Layout.Orientation.Vertical;
            ConfirmButtons.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            foreach (var child in ConfirmButtons.Children)
            {
                if (child is Control control)
                    control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            }
        }
        else
        {
            ConfirmCard.Width = 380;
            ConfirmCard.Margin = new Thickness(0);
            ConfirmCard.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            ConfirmButtons.Orientation = Avalonia.Layout.Orientation.Horizontal;
            ConfirmButtons.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            foreach (var child in ConfirmButtons.Children)
            {
                if (child is Control control)
                    control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            }
        }
    }

    private void ApplyEditModLayout()
    {
        if (EditModCard is null || EditModButtons is null)
            return;

        if (_isHandheld)
        {
            EditModCard.Width = double.NaN;
            EditModCard.Margin = new Thickness(16);
            EditModCard.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            EditModButtons.Orientation = Avalonia.Layout.Orientation.Vertical;
            EditModButtons.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            foreach (var child in EditModButtons.Children)
            {
                if (child is Control control)
                    control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            }
        }
        else
        {
            EditModCard.Width = 380;
            EditModCard.Margin = new Thickness(0);
            EditModCard.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            EditModButtons.Orientation = Avalonia.Layout.Orientation.Horizontal;
            EditModButtons.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            foreach (var child in EditModButtons.Children)
            {
                if (child is Control control)
                    control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            }
        }
    }

    private void OnHandheldBackClick(object? sender, RoutedEventArgs e)
    {
        _handheldShowActions = false;
        ApplyHandheldPanes();
    }

    private void ShowHandheldActions()
    {
        if (!_isHandheld || _win64Path is null)
            return;

        _handheldShowActions = true;
        RefreshSelectedGameTitle();
        ApplyHandheldPanes();
    }

    private void RefreshSelectedGameTitle()
    {
        if (SelectedGameTitle is null)
            return;

        var game = _win64Path is null
            ? null
            : _allGames.FirstOrDefault(g =>
                string.Equals(g.Win64Path, _win64Path, StringComparison.OrdinalIgnoreCase));
        SelectedGameTitle.Text = game?.Name ?? "Selected game";

        var hasIcon = game?.Icon is not null;
        SelectedGameIcon.Source = game?.Icon;
        SelectedGameIconBorder.IsVisible = hasIcon;
        SelectedGameInitialBorder.IsVisible = !hasIcon;
        SelectedGameInitial.Text = game?.Initial ?? "?";
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

    private void OnGamesListTapped(object? sender, TappedEventArgs e)
    {
        if (!_isHandheld || _busy || _applyingSelection)
            return;

        if (e.Source is not Visual visual
            || visual.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
        {
            return;
        }

        if (GamesListBox.SelectedItem is DetectedGame)
            ShowHandheldActions();
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
                _handheldShowActions = false;
                SyncListSelection();
                RefreshInstalledMods();
                UpdateActionButtons();
                SetStatus("Ready");
                ApplyHandheldPanes();
            }

            return;
        }

        if (string.Equals(gameInstallPath.Trim().Trim('"'), _win64Path, StringComparison.OrdinalIgnoreCase))
        {
            RefreshInstalledMods();
            ShowInstallStatus();
            ShowHandheldActions();
            return;
        }

        var win64 = PathDetector.FindWin64Directory(gameInstallPath);
        if (win64 is null)
        {
            _win64Path = null;
            _handheldShowActions = false;
            SyncListSelection();
            RefreshInstalledMods();
            UpdateActionButtons();
            SetStatus("No Binaries/Win64 folder was found. Select the game's Steam folder (Manage → Browse local files).");
            ApplyHandheldPanes();
            return;
        }

        _win64Path = win64;
        PathTextBox.Text = win64;
        EnsureGameInList(gameInstallPath, win64);
        SyncListSelection();
        RefreshInstalledMods();
        UpdateActionButtons();
        ShowInstallStatus();
        ShowHandheldActions();
    }

    private void EnsureGameInList(string pickedPath, string win64Path)
    {
        var existing = _allGames.FirstOrDefault(game =>
            string.Equals(game.Win64Path, win64Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return;

        var identity = ManualGameResolver.Resolve(pickedPath, win64Path);
        var exePath = PathDetector.FindGameExecutable(win64Path);
        var steamPath = SteamScanner.TryFindSteamPath();
        var artwork = steamPath is null
            ? null
            : GameIconLoader.FindSteamArtwork(steamPath, identity.AppId);
        var state = InstallTracker.Detect(win64Path);
        _allGames.Add(new DetectedGame
        {
            Name = identity.Name,
            InstallPath = identity.InstallPath,
            Win64Path = win64Path,
            ExePath = exePath,
            AppId = string.IsNullOrEmpty(identity.AppId) ? null : identity.AppId,
            Icon = GameIconLoader.Load(exePath, artwork),
            ChannelLabel = state.GameBadge,
            ChannelLabelTip = state.GameBadgeTip
        });
        _allGames.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
            SearchTextBox.Text = string.Empty;

        ApplyGameFilter();
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (_win64Path is null || _busy)
            return;

        HideEditModOverlay();
        var win64Path = _win64Path;
        var channel = VersionComboBox.SelectedIndex == 1
            ? Ue4ssChannel.ZDev
            : Ue4ssChannel.Release;

        await RunBusyAsync("Downloading...", async () =>
        {
            var pack = FindSignaturePack(win64Path);
            var zipPath = await GitHubFetcher.DownloadAsync(channel, pack?.PinnedUe4ssGitSha, pack?.Ue4ssSource);
            try
            {
                SetStatus("Extracting...");
                await Task.Run(() => ZipInstaller.InstallUe4ss(zipPath, win64Path, channel));
            }
            finally
            {
                TryDelete(zipPath);
            }

            ApplyInstallBadge(win64Path);

            if (pack is null)
            {
                SetStatus($"Installed {FormatChannel(channel)}. {InstallTracker.Detect(win64Path).StatusText}");
                return;
            }

            if (!pack.HasSignaturePack)
            {
                var source = pack.Ue4ssSource?.Tag ?? pack.DisplayName;
                var hint = string.IsNullOrWhiteSpace(pack.InstallHint) ? "" : $" {pack.InstallHint}";
                SetStatus($"Installed {FormatChannel(channel)} from {source}.{hint}");
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

                    var pinNote = pack.HasPinnedUe4ss
                        ? $" (UE4SS {pack.PinnedUe4ssGitSha})"
                        : "";
                    SetStatus($"Installed {FormatChannel(channel)}{pinNote} and {pack.DisplayName} into {dest}.");
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
        _pendingModZip = null;
        HideEditModOverlay();
        ConfirmTitle.Text = "Uninstall UE4SS?";
        ConfirmBody.Text =
            "This deletes the ue4ss folder (including mods and signatures) and UE4SS DLLs in Win64 such as dwmapi.dll.";
        ConfirmActionButton.Content = "Uninstall";
        UninstallConfirmOverlay.IsVisible = true;
    }

    private void OnConfirmCancelClick(object? sender, RoutedEventArgs e)
    {
        UninstallConfirmOverlay.IsVisible = false;
        _pendingModUninstall = null;
        _pendingModZip = null;
    }

    private async void OnConfirmActionClick(object? sender, RoutedEventArgs e)
    {
        UninstallConfirmOverlay.IsVisible = false;
        if (_win64Path is null || _busy)
            return;

        var win64Path = _win64Path;
        var zipPath = _pendingModZip;
        var mod = _pendingModUninstall;
        _pendingModZip = null;
        _pendingModUninstall = null;

        if (zipPath is not null)
        {
            var reinstall = ZipInstaller.WouldReinstall(zipPath, win64Path);
            await RunBusyAsync(reinstall ? "Reinstalling..." : "Installing...", async () =>
            {
                var result = await Task.Run(() => ZipInstaller.InstallMod(zipPath, win64Path));
                RefreshInstalledMods();
                ApplyInstallBadge(win64Path);
                SetStatus(ZipInstaller.FormatModInstallStatus(result));
            });
            return;
        }

        if (mod is not null)
        {
            var name = mod.DisplayName;
            await RunBusyAsync($"Removing {name}...", async () =>
            {
                await Task.Run(() => ZipInstaller.UninstallMod(win64Path, mod.Id));
                RefreshInstalledMods();
                ApplyInstallBadge(win64Path);
                SetStatus($"Removed {name}.");
            });
            return;
        }

        await RunBusyAsync("Uninstalling UE4SS...", async () =>
        {
            await Task.Run(() => ZipInstaller.UninstallUe4ss(win64Path));
            ApplyInstallBadge(win64Path);
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

        _pendingModUninstall = null;
        _pendingModZip = null;
        HideEditModOverlay();
        string name;
        var reinstall = false;
        ModPackageKind? kind = null;
        try
        {
            var preview = ZipInstaller.PreviewModInstall(zipPath, _win64Path);
            name = preview.Name;
            reinstall = preview.WouldReinstall;
            kind = preview.Kind;
        }
        catch (InvalidOperationException ex) when (ex.Message == ZipInstaller.Ue4ssNotInstalledMessage)
        {
            SetStatus(ZipInstaller.Ue4ssNotInstalledMessage);
            return;
        }
        catch
        {
            name = ZipInstaller.CleanZipStem(zipPath);
        }

        _pendingModZip = zipPath;

        ConfirmTitle.Text = reinstall ? $"Reinstall {name}?" : $"Install {name}?";
        ConfirmBody.Text = DescribeModZip(zipPath, name, reinstall, kind);
        ConfirmActionButton.Content = reinstall ? "Reinstall" : "Install";
        UninstallConfirmOverlay.IsVisible = true;
    }

    internal static string DescribeModZip(string zipPath, string name, bool reinstall, ModPackageKind? kind = null)
    {
        if (kind is null)
        {
            try
            {
                kind = ZipInstaller.PeekModZipKind(zipPath);
            }
            catch
            {
                // Fall through with a generic destination.
            }
        }

        var target = kind switch
        {
            ModPackageKind.GameDirectory => "the game folder",
            ModPackageKind.ModsFolder => "the Mods folder",
            _ => "the selected game"
        };

        if (reinstall)
            return $"This replaces the existing {name} install. Old files are removed first, then the new zip is copied into {target}.";

        return $"This installs {name} into {target}. You can uninstall it later from Installed mods.";
    }

    private void OnUninstallModClick(object? sender, RoutedEventArgs e)
    {
        if (_win64Path is null || _busy)
            return;

        if (InstalledModsCombo.SelectedItem is not InstalledMod mod)
            return;

        _pendingModZip = null;
        _pendingModUninstall = mod;
        HideEditModOverlay();
        ConfirmTitle.Text = $"Remove {mod.DisplayName}?";
        ConfirmBody.Text =
            "This deletes the files this app installed for that mod. UE4SS itself is left alone.";
        ConfirmActionButton.Content = "Uninstall";
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

    private void ApplyInstallBadge(string win64Path)
    {
        var state = InstallTracker.Detect(win64Path);
        foreach (var game in _allGames)
        {
            if (string.Equals(game.Win64Path, win64Path, StringComparison.OrdinalIgnoreCase))
                game.ApplyInstallState(state);
        }

        ApplyGameFilter();
        UpdateCustomUe4ssNote();
        RefreshUe4ssOptions();
    }

    private void UpdateCustomUe4ssNote()
    {
        if (CustomUe4ssNote is null)
            return;

        if (_win64Path is null)
        {
            CustomUe4ssNote.IsVisible = false;
            return;
        }

        var state = InstallTracker.Detect(_win64Path);
        if (state.Kind == InstallKind.CustomMod && !string.IsNullOrEmpty(state.GameBadgeTip))
        {
            CustomUe4ssNote.Text = state.GameBadgeTip;
            CustomUe4ssNote.IsVisible = true;
            return;
        }

        CustomUe4ssNote.IsVisible = false;
    }

    private void OnInstallTabClick(object? sender, RoutedEventArgs e)
    {
        _devTabSelected = false;
        ApplyActionPanes();
    }

    private void OnDevTabClick(object? sender, RoutedEventArgs e)
    {
        _devTabSelected = true;
        ApplyActionPanes();
    }

    private void RefreshUe4ssOptions()
    {
        if (Ue4ssOptionsPanel is null)
            return;

        var showActions = _isHandheld && _handheldShowActions && _win64Path is not null;
        if (_isHandheld && !showActions)
        {
            if (ActionTabStrip is not null)
                ActionTabStrip.IsVisible = false;
            ApplyActionPanes();
            return;
        }

        var settings = _win64Path is not null
                       && InstallTracker.Detect(_win64Path).Kind != InstallKind.None
            ? SettingsIniPatcher.TryReadRuntimeSettings(_win64Path)
            : null;

        if (settings is null)
            _devTabSelected = false;

        if (ActionTabStrip is not null)
            ActionTabStrip.IsVisible = settings is not null;

        if (settings is not null)
        {
            _applyingSettings = true;
            try
            {
                LoggingCheckBox.IsChecked = settings.LoggingEnabled;
                CacheCheckBox.IsChecked = settings.UseUObjectArrayCache;
            }
            finally
            {
                _applyingSettings = false;
            }
        }

        ApplyActionPanes();
    }

    private void ApplyActionPanes()
    {
        var showDev = _devTabSelected && ActionTabStrip is { IsVisible: true };
        SetPaneActive(InstallPane, !showDev);
        SetPaneActive(Ue4ssOptionsPanel, showDev);

        InstallTabButton?.Classes.Set("selected", !showDev);
        DevTabButton?.Classes.Set("selected", showDev);
        RefreshUe4ssOptionsEnabled();
    }

    private static void SetPaneActive(Control? pane, bool active)
    {
        if (pane is null)
            return;

        pane.Opacity = active ? 1 : 0;
        pane.IsHitTestVisible = active;
        pane.IsEnabled = active;
    }

    private void RefreshUe4ssOptionsEnabled()
    {
        var enabled = !_busy && _devTabSelected && ActionTabStrip is { IsVisible: true };
        if (LoggingCheckBox is not null)
            LoggingCheckBox.IsEnabled = enabled;
        if (CacheCheckBox is not null)
            CacheCheckBox.IsEnabled = enabled;
    }

    private void OnLoggingCheckedChanged(object? sender, RoutedEventArgs e)
        => ApplyUe4ssOption(
            () => SettingsIniPatcher.SetLoggingEnabled(_win64Path!, LoggingCheckBox.IsChecked == true),
            LoggingCheckBox.IsChecked == true
                ? "Enabled UE4SS logging."
                : "Disabled UE4SS logging.");

    private void OnCacheCheckedChanged(object? sender, RoutedEventArgs e)
        => ApplyUe4ssOption(
            () => SettingsIniPatcher.SetUObjectArrayCache(_win64Path!, CacheCheckBox.IsChecked == true),
            CacheCheckBox.IsChecked == true
                ? "Enabled Live View Search."
                : "Disabled Live View Search.");

    private void ApplyUe4ssOption(Action write, string status)
    {
        if (_applyingSettings || _busy || _win64Path is null)
            return;

        try
        {
            write();
            SetStatus(status);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}");
            RefreshUe4ssOptions();
        }
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
        var hasSelectedMod = InstalledModsCombo.SelectedItem is InstalledMod;
        UninstallModButton.IsEnabled = canAct && hasSelectedMod;
        EditModButton.IsEnabled = canAct && hasSelectedMod;
        InstalledModsCombo.IsEnabled = !_busy;
        if (InstallTabButton is not null)
            InstallTabButton.IsEnabled = !_busy;
        if (DevTabButton is not null)
            DevTabButton.IsEnabled = !_busy;
        RefreshUe4ssOptionsEnabled();
    }

    private void OnEditModClick(object? sender, RoutedEventArgs e)
    {
        if (_win64Path is null || _busy)
            return;

        if (InstalledModsCombo.SelectedItem is not InstalledMod mod)
            return;

        _pendingEditMod = mod;
        UninstallConfirmOverlay.IsVisible = false;
        EditModNameBox.Text = mod.DisplayName;
        EditModNameBox.PlaceholderText = mod.Name;
        EditModNoteBox.Text = mod.Note ?? "";
        EditModOverlay.IsVisible = true;
    }

    private void OnEditModCancelClick(object? sender, RoutedEventArgs e)
        => HideEditModOverlay();

    private void OnEditModSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_win64Path is null || _pendingEditMod is null)
        {
            HideEditModOverlay();
            return;
        }

        var id = _pendingEditMod.Id;
        var label = EditModNameBox.Text;
        var note = EditModNoteBox.Text;
        HideEditModOverlay();

        if (!ModTracker.UpdateDisplay(_win64Path, id, label, note))
        {
            SetStatus("Could not update that mod. It may have been uninstalled.");
            RefreshInstalledMods();
            return;
        }

        RefreshInstalledMods(id);
        ApplyInstallBadge(_win64Path);
        var updated = ModTracker.List(_win64Path).FirstOrDefault(m => m.Id == id);
        SetStatus(updated is null
            ? "Updated the installed mod list."
            : $"Updated {updated.DisplayName}.");
    }

    private void HideEditModOverlay()
    {
        if (EditModOverlay is not null)
            EditModOverlay.IsVisible = false;
        _pendingEditMod = null;
    }

    internal readonly record struct InstalledModsState(
        IReadOnlyList<InstalledMod> Mods,
        InstalledMod? Selected);

    /// <summary>
    /// List and selection for the Installed mods combo. After UE4SS uninstall the
    /// tracker is empty, so Selected is null and the combo must not keep a stale item.
    /// </summary>
    internal static InstalledModsState GetInstalledModsState(string? win64Path, string? selectId = null)
    {
        if (string.IsNullOrWhiteSpace(win64Path))
            return new InstalledModsState([], null);

        var mods = ModTracker.List(win64Path);
        if (mods.Count == 0)
            return new InstalledModsState(mods, null);

        if (!string.IsNullOrWhiteSpace(selectId))
        {
            var match = mods.FirstOrDefault(m => m.Id == selectId);
            if (match is not null)
                return new InstalledModsState(mods, match);
        }

        return new InstalledModsState(mods, mods[0]);
    }

    private void RefreshInstalledMods(string? selectId = null)
    {
        if (selectId is null)
            HideEditModOverlay();

        InstalledModsCombo.SelectedItem = null;
        InstalledModsCombo.SelectedIndex = -1;
        InstalledModsCombo.ItemsSource = null;

        if (_win64Path is null)
        {
            InstalledModsPanel.IsVisible = false;
            UpdateActionButtons();
            UpdateCustomUe4ssNote();
            RefreshUe4ssOptions();
            return;
        }

        var state = GetInstalledModsState(_win64Path, selectId);
        if (state.Mods.Count == 0)
        {
            ApplyInstalledModsVisibility();
            UpdateActionButtons();
            UpdateCustomUe4ssNote();
            RefreshUe4ssOptions();
            return;
        }

        InstalledModsCombo.ItemsSource = state.Mods;
        InstalledModsCombo.SelectedItem = state.Selected;
        ApplyInstalledModsVisibility();
        UpdateActionButtons();
        UpdateCustomUe4ssNote();
        RefreshUe4ssOptions();
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
        {
            var note = string.IsNullOrWhiteSpace(pack.InstallHint)
                ? $"{pack.DisplayName} will be applied on install."
                : pack.InstallHint;
            SetStatus($"{StatusText.Text} {note}");
        }
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

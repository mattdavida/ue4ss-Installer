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
                UpdateActionButtons();
                SetStatus("Ready");
            }

            return;
        }

        if (string.Equals(gameInstallPath.Trim().Trim('"'), _win64Path, StringComparison.OrdinalIgnoreCase))
        {
            ShowInstallStatus();
            return;
        }

        var win64 = PathDetector.FindWin64Directory(gameInstallPath);
        if (win64 is null)
        {
            _win64Path = null;
            UpdateActionButtons();
            SetStatus("No Binaries/Win64 folder was found. Select the game's Steam folder (Manage → Browse local files).");
            return;
        }

        _win64Path = win64;
        PathTextBox.Text = win64;
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
            SetStatus($"Installed {FormatChannel(channel)}. {InstallTracker.Detect(win64Path).StatusText}");
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
            var where = result.Kind == ModPackageKind.GameDirectory
                ? "the game folder (UE4SS pack / overlay)"
                : "the Mods folder";
            SetStatus($"Installed into {where}: {result.Destination}");
        });
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
        InstallButton.IsEnabled = canAct;
        InstallModButton.IsEnabled = canAct;
        BrowseButton.IsEnabled = !_busy;
        AddGameManuallyButton.IsEnabled = !_busy;
        VersionComboBox.IsEnabled = !_busy;
        GamesListBox.IsEnabled = !_busy;
        SearchTextBox.IsEnabled = !_busy;
        PathTextBox.IsEnabled = !_busy;
    }

    private void ShowInstallStatus()
    {
        if (_win64Path is null)
        {
            SetStatus("Ready");
            return;
        }

        SetStatus(InstallTracker.Detect(_win64Path).StatusText);
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

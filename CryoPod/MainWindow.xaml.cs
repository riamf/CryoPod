using System;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using CryoPod.Interop;
using CryoPod.Models;
using CryoPod.Services.GameExplorer;
using CryoPod.Services.Input;
using CryoPod.Services.Launch;
using CryoPod.Services.Steam;
using CryoPod.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CryoPod
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private sealed record SuspendedGame(Guid Id, InstalledGame InstalledGame, string Name, Process Process, SuspendedProcessState? SuspendedState, DateTimeOffset SuspendedAt);

        private const int GamesPerRow = 3;
        private const double GameCardAspectRatio = 420d / 203d;
        private const double GameCardTextHeight = 56;
        private const double GameCardVerticalSpacing = 8;
        private const double GameCardHorizontalMargin = 16;
        private const double FocusedGameCardScale = 1.03;
        private const float FocusGlowOpacity = 1f;
        private const float UnfocusedGlowOpacity = 0f;
        private const float FocusedTitleOpacity = 1f;
        private const float UnfocusedTitleOpacity = 0.82f;
        private static readonly TimeSpan FocusAnimationDuration = TimeSpan.FromMilliseconds(180);

        private static readonly TimeSpan SteamMetadataRefreshInterval = TimeSpan.FromHours(24);
        private static readonly HashSet<string> KnownAntiCheatProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "EasyAntiCheat",
            "EasyAntiCheat_EOS",
            "BEService",
            "vgtray",
            "FACEIT",
        };

        private IReadOnlyList<InstalledGame> _installedGames = [];
        private IReadOnlyList<SteamAppDetailsResponse> _steamAppDetails = [];
        private readonly ForegroundProcessControlService _foregroundProcessControlService = new ForegroundProcessControlService();
        private readonly ISteamAppDetailsService _steamAppDetailsService = new SteamAppDetailsService();
        private readonly ISteamAppDetailsCacheService _steamAppDetailsCacheService = new SteamAppDetailsCacheService();
        private readonly GameLaunchService _gameLaunchService = new GameLaunchService();
        private readonly GameLibraryNavigationController _gameLibraryNavigationController;
        private readonly Dictionary<int, bool> _safeHotkeyPathByProcessId = [];
        private readonly List<SuspendedGame> _suspendedGames = [];
        private GlobalKeyboardShortcutService? _globalKeyboardShortcutService;
        private Process? _activeGame;
        private string? _activeGameName;
        private InstalledGame? _activeInstalledGame;
        private GameLibraryItemViewModel? _activeGameDetailsItem;
        private nint _windowHandle;
        private bool _windowInitialized;

        public ObservableCollection<SuspendedGameViewModel> SuspendedGamesView { get; } = [];

        public MainWindow()
        {
            InitializeComponent();
            Activated += MainWindow_Activated;
            Closed += MainWindow_Closed;
            _gameLibraryNavigationController = new GameLibraryNavigationController(
                RootGrid,
                GamesGridView,
                GameDetailsBackButton,
                GameDetailsRunButton,
                GameDetailsScreenshotsListView,
                ExitPromptYesButton,
                ExitPromptNoButton,
                DispatcherQueue,
                CanProcessMainViewInput);
            _gameLibraryNavigationController.DetailsRequested += GameLibraryNavigationController_DetailsRequested;
            _gameLibraryNavigationController.DetailsClosed += GameLibraryNavigationController_DetailsClosed;
            _gameLibraryNavigationController.DetailsRunRequested += GameLibraryNavigationController_DetailsRunRequested;
            _gameLibraryNavigationController.ExitRequested += GameLibraryNavigationController_ExitRequested;
            _gameLibraryNavigationController.ExitConfirmed += GameLibraryNavigationController_ExitConfirmed;
            _gameLibraryNavigationController.ExitCanceled += GameLibraryNavigationController_ExitCanceled;
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_windowInitialized)
            {
                RefreshTrackedGameState();
                UpdateGameDetailsActionButton();
                return;
            }

            _windowHandle = WindowNative.GetWindowHandle(this);
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            _globalKeyboardShortcutService = new GlobalKeyboardShortcutService(_windowHandle);
            _globalKeyboardShortcutService.ShortcutPressed += GlobalKeyboardShortcutService_ShortcutPressed;
            SetFullScreen();
            _windowInitialized = true;
            RefreshTrackedGameState();
            UpdateGameDetailsActionButton();
        }

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            await RunStartupWorkAsync();
        }

        private void GamesGridView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateGameGridLayout();
        }

        private void GamesGridView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateGameGridLayout();
        }

        private async void GamesGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is GameLibraryItemViewModel gameLibraryItem)
            {
                await ShowGameDetailsAsync(gameLibraryItem);
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            Activated -= MainWindow_Activated;

            if (_activeGame is not null)
            {
                _activeGame.Exited -= GameProcess_Exited;
                _activeGame.Dispose();
                _activeGame = null;
            }

            foreach (var suspendedGame in _suspendedGames)
            {
                suspendedGame.Process.Exited -= GameProcess_Exited;
                _foregroundProcessControlService.ResumeSuspendedProcessForCleanup(suspendedGame.SuspendedState);
                suspendedGame.Process.Dispose();
            }

            _suspendedGames.Clear();
            _safeHotkeyPathByProcessId.Clear();

            if (_globalKeyboardShortcutService is not null)
            {
                _globalKeyboardShortcutService.ShortcutPressed -= GlobalKeyboardShortcutService_ShortcutPressed;
                _globalKeyboardShortcutService.Dispose();
            }
            _gameLibraryNavigationController.DetailsRequested -= GameLibraryNavigationController_DetailsRequested;
            _gameLibraryNavigationController.DetailsClosed -= GameLibraryNavigationController_DetailsClosed;
            _gameLibraryNavigationController.DetailsRunRequested -= GameLibraryNavigationController_DetailsRunRequested;
            _gameLibraryNavigationController.ExitRequested -= GameLibraryNavigationController_ExitRequested;
            _gameLibraryNavigationController.ExitConfirmed -= GameLibraryNavigationController_ExitConfirmed;
            _gameLibraryNavigationController.ExitCanceled -= GameLibraryNavigationController_ExitCanceled;
            _gameLibraryNavigationController.Dispose();
            Closed -= MainWindow_Closed;
        }

        private void GamesGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is not GridViewItem itemContainer)
            {
                return;
            }

            EnsureGameGridItemTransform(itemContainer);

            itemContainer.GotFocus -= GameGridItem_GotFocus;
            itemContainer.LostFocus -= GameGridItem_LostFocus;
            itemContainer.SizeChanged -= GameGridItem_SizeChanged;

            if (args.InRecycleQueue)
            {
                SetGameGridItemFocusState(itemContainer, false);
                return;
            }

            itemContainer.GotFocus += GameGridItem_GotFocus;
            itemContainer.LostFocus += GameGridItem_LostFocus;
            itemContainer.SizeChanged += GameGridItem_SizeChanged;

            SetGameGridItemFocusState(itemContainer, itemContainer.FocusState != FocusState.Unfocused);
        }

        private void GameDetailsScreenshotsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is not ListViewItem itemContainer)
            {
                return;
            }

            EnsureScreenshotListItemTransform(itemContainer);

            itemContainer.GotFocus -= ScreenshotListItem_GotFocus;
            itemContainer.LostFocus -= ScreenshotListItem_LostFocus;
            itemContainer.SizeChanged -= ScreenshotListItem_SizeChanged;

            if (args.InRecycleQueue)
            {
                SetScreenshotListItemFocusState(itemContainer, false);
                return;
            }

            itemContainer.GotFocus += ScreenshotListItem_GotFocus;
            itemContainer.LostFocus += ScreenshotListItem_LostFocus;
            itemContainer.SizeChanged += ScreenshotListItem_SizeChanged;

            SetScreenshotListItemFocusState(itemContainer, itemContainer.FocusState != FocusState.Unfocused);
        }

        private void SetFullScreen()
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }

        private void GlobalKeyboardShortcutService_ShortcutPressed(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                var (actionSucceeded, usedSafePath, antiCheatDetected) = await SuspendActiveGameAsync(useSafePathForOnlineTitles: true);
                if (actionSucceeded)
                {
                    BringLauncherToFront();
                    UpdateSuspendedStateUi(_suspendedGames.Count > 0);
                    Debug.WriteLine(usedSafePath
                        ? antiCheatDetected
                            ? "Global hotkey used the safe minimize-only path because anti-cheat was detected."
                            : "Global hotkey used the safe minimize-only path for an online multiplayer title."
                        : "Global hotkey suspended the tracked game.");
                    return;
                }

                Debug.WriteLine("Global suspend shortcut was ignored, unavailable, or failed.");
            });
        }

        private async Task RunStartupWorkAsync()
        {
            StartupLoaderPanel.Visibility = Visibility.Visible;
            StartupProgressBar.IsIndeterminate = true;
            StartupProgressBar.Value = 0;
            StartupStatusText.Text = "Searching for installed games...";

            var coordinator = new GameExplorerCoordinator(new IGameExplorerService[]
            {
                new SteamGameExplorerService(),
            });

            _installedGames = await coordinator.FindInstalledGamesAsync();

            var steamGamesByAppId = _installedGames
                .Where(game => game.AppId is > 0)
                .GroupBy(game => game.AppId!.Value)
                .ToDictionary(group => group.Key, group => group.First());

            var steamAppIds = steamGamesByAppId.Keys.ToList();
            var cachedAppDetails = await _steamAppDetailsCacheService.GetCachedAppDetailsAsync(steamAppIds);

            _steamAppDetails = cachedAppDetails.Values
                .Where(entry => entry.AppDetails?.Success == true)
                .Select(entry => entry.AppDetails!)
                .ToList();

            var missingSteamAppIds = steamAppIds
                .Where(appId => !cachedAppDetails.TryGetValue(appId, out var cachedEntry) || cachedEntry.AppDetails?.Success != true)
                .ToList();

            var staleSteamAppIds = steamAppIds
                .Where(appId => cachedAppDetails.TryGetValue(appId, out var cachedEntry)
                    && cachedEntry.AppDetails?.Success == true
                    && DateTimeOffset.UtcNow - cachedEntry.LastUpdatedUtc >= SteamMetadataRefreshInterval)
                .ToList();

            if (missingSteamAppIds.Count > 0)
            {
                StartupStatusText.Text = "Loading Steam metadata...";
                var loadedAppDetails = await LoadSteamAppDetailsAsync(missingSteamAppIds, steamGamesByAppId);
                _steamAppDetails = MergeSteamAppDetails(_steamAppDetails, loadedAppDetails);
            }

            UpdateGameLibrary();

            StartupLoaderPanel.Visibility = Visibility.Collapsed;

            await _gameLibraryNavigationController.FocusFirstItemAsync();

            _ = RefreshSteamAppDetailsInBackgroundAsync(staleSteamAppIds, steamGamesByAppId);
        }

        private async Task<IReadOnlyList<SteamAppDetailsResponse>> LoadSteamAppDetailsAsync(
            IReadOnlyList<int> steamAppIds,
            IReadOnlyDictionary<int, InstalledGame> steamGamesByAppId)
        {
            if (steamAppIds.Count == 0)
            {
                StartupProgressBar.IsIndeterminate = false;
                StartupProgressBar.Maximum = 1;
                StartupProgressBar.Value = 1;
                return [];
            }

            StartupProgressBar.IsIndeterminate = false;
            StartupProgressBar.Minimum = 0;
            StartupProgressBar.Maximum = steamAppIds.Count;
            StartupProgressBar.Value = 0;

            var progress = new Progress<SteamAppDetailsProgress>(progressInfo =>
            {
                var gameName = steamGamesByAppId.TryGetValue(progressInfo.SteamAppId, out var game)
                    ? game.Name
                    : progressInfo.SteamAppId.ToString();

                StartupStatusText.Text = $"Loading {gameName} ({progressInfo.SteamAppId}) data...";
                StartupProgressBar.Value = progressInfo.CurrentItemIndex;

                Debug.WriteLine($"[{progressInfo.CurrentItemIndex}/{progressInfo.TotalItems}] Loading {gameName} ({progressInfo.SteamAppId}) data");
            });

            try
            {
                var appDetails = await _steamAppDetailsService.GetAppDetailsAsync(steamAppIds, progress);
                await _steamAppDetailsCacheService.SaveAppDetailsAsync(appDetails);
                StartupStatusText.Text = $"Loaded Steam metadata for {appDetails.Count} game(s).";
                StartupProgressBar.Value = steamAppIds.Count;
                Debug.WriteLine($"Loaded Steam metadata for {appDetails.Count} game(s).");
                return appDetails;
            }
            catch
            {
                StartupStatusText.Text = "Failed to load Steam metadata.";
                return [];
            }
        }

        private async Task RefreshSteamAppDetailsInBackgroundAsync(
            IReadOnlyList<int> staleSteamAppIds,
            IReadOnlyDictionary<int, InstalledGame> steamGamesByAppId)
        {
            if (staleSteamAppIds.Count == 0)
            {
                return;
            }

            Debug.WriteLine($"Refreshing cached Steam metadata for {staleSteamAppIds.Count} game(s) in the background.");

            var progress = new Progress<SteamAppDetailsProgress>(progressInfo =>
            {
                var gameName = steamGamesByAppId.TryGetValue(progressInfo.SteamAppId, out var game)
                    ? game.Name
                    : progressInfo.SteamAppId.ToString();

                Debug.WriteLine($"[Background {progressInfo.CurrentItemIndex}/{progressInfo.TotalItems}] Refreshing {gameName} ({progressInfo.SteamAppId}) data");
            });

            try
            {
                var refreshedAppDetails = await _steamAppDetailsService.GetAppDetailsAsync(staleSteamAppIds, progress);
                await _steamAppDetailsCacheService.SaveAppDetailsAsync(refreshedAppDetails);
                _steamAppDetails = MergeSteamAppDetails(_steamAppDetails, refreshedAppDetails);
                UpdateGameLibrary();
                Debug.WriteLine($"Background Steam metadata refresh completed for {refreshedAppDetails.Count} game(s).");
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Background Steam metadata refresh failed: {exception}");
            }
        }

        private static IReadOnlyList<SteamAppDetailsResponse> MergeSteamAppDetails(
            IEnumerable<SteamAppDetailsResponse> existingAppDetails,
            IEnumerable<SteamAppDetailsResponse> newAppDetails)
        {
            return existingAppDetails
                .Concat(newAppDetails)
                .Where(details => details.Data?.SteamAppId > 0)
                .GroupBy(details => details.Data!.SteamAppId)
                .Select(group => group.Last())
                .ToList();
        }

        private void UpdateGameLibrary()
        {
            var steamAppDetailsByAppId = _steamAppDetails
                .Where(details => details.Data?.SteamAppId > 0)
                .GroupBy(details => details.Data!.SteamAppId)
                .ToDictionary(group => group.Key, group => group.Last());

            var gameLibraryItems = _installedGames
                .Where(game => game.AppId is > 0 && steamAppDetailsByAppId.ContainsKey(game.AppId.Value))
                .Select(game =>
                {
                    var appDetails = steamAppDetailsByAppId[game.AppId!.Value];
                    return new GameLibraryItemViewModel(game, appDetails);
                })
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            GamesGridView.ItemsSource = gameLibraryItems;
            UpdateGameGridLayout();
        }

        private void UpdateGameGridLayout()
        {
            if (GamesGridView.ItemsPanelRoot is not ItemsWrapGrid itemsWrapGrid)
            {
                return;
            }

            var availableWidth = GamesGridView.ActualWidth
                - GamesGridView.Padding.Left
                - GamesGridView.Padding.Right
                - (GameCardHorizontalMargin * GamesPerRow);

            var itemWidth = Math.Max(220, availableWidth / GamesPerRow);
            var imageHeight = itemWidth / GameCardAspectRatio;

            itemsWrapGrid.ItemWidth = itemWidth;
            itemsWrapGrid.ItemHeight = imageHeight + GameCardTextHeight + GameCardVerticalSpacing;
        }

        private void GameGridItem_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewItem itemContainer)
            {
                SetGameGridItemFocusState(itemContainer, true);
            }
        }

        private void GameGridItem_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewItem itemContainer)
            {
                SetGameGridItemFocusState(itemContainer, false);
            }
        }

        private void GameGridItem_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is GridViewItem itemContainer)
            {
                UpdateGameGridItemCenterPoint(itemContainer);
            }
        }

        private void ScreenshotListItem_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ListViewItem itemContainer)
            {
                SetScreenshotListItemFocusState(itemContainer, true);
            }
        }

        private void ScreenshotListItem_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ListViewItem itemContainer)
            {
                SetScreenshotListItemFocusState(itemContainer, false);
            }
        }

        private void ScreenshotListItem_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ListViewItem itemContainer)
            {
                UpdateListItemCenterPoint(itemContainer);
            }
        }

        private bool CanProcessMainViewInput()
        {
            return StartupLoaderPanel.Visibility != Visibility.Visible;
        }

        private async void GameLibraryNavigationController_DetailsRequested(object? sender, GameLibraryItemInvokedEventArgs e)
        {
            if (e.Item is not GameLibraryItemViewModel gameLibraryItem)
            {
                return;
            }

            await ShowGameDetailsAsync(gameLibraryItem);
        }

        private async void GameLibraryNavigationController_DetailsClosed(object? sender, EventArgs e)
        {
            await HideGameDetailsAsync();
        }

        private async void GameLibraryNavigationController_DetailsRunRequested(object? sender, EventArgs e)
        {
            await ExecuteActiveDetailsActionAsync();
        }

        private async void GameLibraryNavigationController_ExitRequested(object? sender, EventArgs e)
        {
            await ShowExitPromptAsync();
        }

        private void GameLibraryNavigationController_ExitConfirmed(object? sender, EventArgs e)
        {
            App.Current.Exit();
        }

        private async void GameLibraryNavigationController_ExitCanceled(object? sender, EventArgs e)
        {
            await HideExitPromptAsync();
        }

        private async void ExitPromptYesButton_Click(object sender, RoutedEventArgs e)
        {
            App.Current.Exit();
        }

        private async void ExitPromptNoButton_Click(object sender, RoutedEventArgs e)
        {
            await HideExitPromptAsync();
        }

        private async void GameDetailsBackButton_Click(object sender, RoutedEventArgs e)
        {
            await HideGameDetailsAsync();
        }

        private async void GameDetailsRunButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteActiveDetailsActionAsync();
        }

        private async void OnResumeClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            Guid suspendedGameId;
            if (button.Tag is Guid tagGuid)
            {
                suspendedGameId = tagGuid;
            }
            else if (button.Tag is string tagText && Guid.TryParse(tagText, out var parsedGuid))
            {
                suspendedGameId = parsedGuid;
            }
            else
            {
                return;
            }

            await ResumeSuspendedGameAsync(suspendedGameId);
        }

        private async Task ShowExitPromptAsync()
        {
            if (ExitPromptOverlay.Visibility == Visibility.Visible)
            {
                return;
            }

            ExitPromptOverlay.Visibility = Visibility.Visible;
            await _gameLibraryNavigationController.ActivateExitPromptAsync();
        }

        private async Task HideExitPromptAsync()
        {
            if (ExitPromptOverlay.Visibility != Visibility.Visible)
            {
                return;
            }

            ExitPromptOverlay.Visibility = Visibility.Collapsed;
            await _gameLibraryNavigationController.DeactivateExitPromptAsync();
        }

        private async Task ShowGameDetailsAsync(GameLibraryItemViewModel gameLibraryItem)
        {
            RefreshTrackedGameState();
            _activeGameDetailsItem = gameLibraryItem;
            BindGameDetails(gameLibraryItem);
            UpdateGameDetailsActionButton();
            GameDetailsOverlay.Visibility = Visibility.Visible;
            await _gameLibraryNavigationController.ActivateDetailsAsync();
        }

        private async Task HideGameDetailsAsync()
        {
            if (GameDetailsOverlay.Visibility != Visibility.Visible)
            {
                return;
            }

            GameDetailsOverlay.Visibility = Visibility.Collapsed;
            ClearGameDetails();
            _activeGameDetailsItem = null;
            UpdateGameDetailsActionButton();
            await _gameLibraryNavigationController.DeactivateDetailsAsync();
        }

        private void BindGameDetails(GameLibraryItemViewModel gameLibraryItem)
        {
            GameDetailsBackgroundImage.Source = CreateImageSource(gameLibraryItem.BackgroundUrl);
            GameDetailsTitleTextBlock.Text = gameLibraryItem.Name;
            GameDetailsMetadataTextBlock.Text = BuildGameDetailsMetadata(gameLibraryItem);
            GameDetailsMetadataTextBlock.Visibility = string.IsNullOrWhiteSpace(GameDetailsMetadataTextBlock.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;

            var descriptionText = gameLibraryItem.DetailedDescription ?? gameLibraryItem.ShortDescription ?? string.Empty;
            GameDetailsDescriptionTextBlock.Text = descriptionText;
            GameDetailsDescriptionSection.Visibility = string.IsNullOrWhiteSpace(descriptionText)
                ? Visibility.Collapsed
                : Visibility.Visible;

            GameDetailsAboutTextBlock.Text = gameLibraryItem.AboutTheGame ?? string.Empty;
            GameDetailsAboutSection.Visibility = string.IsNullOrWhiteSpace(gameLibraryItem.AboutTheGame)
                ? Visibility.Collapsed
                : Visibility.Visible;

            var screenshotImages = gameLibraryItem.ScreenshotUrls
                .Select(CreateImageSource)
                .Where(imageSource => imageSource is not null)
                .Cast<ImageSource>()
                .ToList();

            GameDetailsScreenshotsListView.ItemsSource = screenshotImages;
            GameDetailsScreenshotsListView.SelectedIndex = -1;
            GameDetailsScreenshotsSection.Visibility = screenshotImages.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ClearGameDetails()
        {
            GameDetailsBackgroundImage.Source = null;
            GameDetailsTitleTextBlock.Text = string.Empty;
            GameDetailsMetadataTextBlock.Text = string.Empty;
            GameDetailsDescriptionTextBlock.Text = string.Empty;
            GameDetailsAboutTextBlock.Text = string.Empty;
            GameDetailsScreenshotsListView.ItemsSource = null;
            GameDetailsScreenshotsListView.SelectedIndex = -1;
        }

        private async Task ExecuteActiveDetailsActionAsync()
        {
            if (TryGetSuspendedGame(_activeGameDetailsItem?.InstalledGame, out var suspendedGameId))
            {
                Debug.WriteLine($"Details action requested resume for {_activeGameDetailsItem?.Name ?? "unknown game"}.");
                await ResumeSuspendedGameAsync(suspendedGameId);
                return;
            }

            if (IsActiveDetailsGameRunning())
            {
                Debug.WriteLine($"Details action requested suspend for {_activeGameDetailsItem?.Name ?? "unknown game"}. Active tracked game: {_activeGameName ?? _activeGame?.ProcessName ?? "none"}.");
                var suspendResult = await SuspendActiveGameAsync(useSafePathForOnlineTitles: false);
                if (!suspendResult.Success)
                {
                    Debug.WriteLine("Suspend button could not pause the selected running game.");
                    return;
                }

                BringLauncherToFront();
                UpdateSuspendedStateUi(_suspendedGames.Count > 0);
                UpdateGameDetailsActionButton();
                return;
            }

            await LaunchActiveDetailsGameAsync();
        }

        private async Task LaunchActiveDetailsGameAsync()
        {
            if (_activeGameDetailsItem is null)
            {
                return;
            }

            if (_activeGame is not null && !HasProcessExited(_activeGame))
            {
                var suspendResult = await SuspendActiveGameAsync(useSafePathForOnlineTitles: false);
                if (!suspendResult.Success)
                {
                    Debug.WriteLine("Launching a new game was blocked because the active game could not be paused.");
                    return;
                }

                BringLauncherToFront();
                UpdateSuspendedStateUi(_suspendedGames.Count > 0);
            }

            var (launchSucceeded, gameProcess) = await _gameLaunchService.LaunchAsync(_activeGameDetailsItem.InstalledGame);
            if (gameProcess is not null)
            {
                TrackActiveGame(gameProcess, _activeGameDetailsItem.InstalledGame, _activeGameDetailsItem.Name, _activeGameDetailsItem.IsOnlineMultiplayer);
            }

            Debug.WriteLine(!launchSucceeded
                ? $"Failed to launch {_activeGameDetailsItem.Name}."
                : gameProcess is null
                    ? $"Launched {_activeGameDetailsItem.Name}, but the game process could not be identified."
                    : $"Launched {_activeGameDetailsItem.Name} with process {gameProcess.ProcessName} ({gameProcess.Id}).");
        }

        private void TrackActiveGame(Process gameProcess, InstalledGame installedGame, string gameName, bool requiresSafeHotkeyPath)
        {
            if (_activeGame is not null && _activeGame.Id != gameProcess.Id)
            {
                _activeGame.Exited -= GameProcess_Exited;
                _activeGame.Dispose();
            }

            _activeGame = gameProcess;
            _activeGameName = gameName;
            _activeInstalledGame = installedGame;
            _safeHotkeyPathByProcessId[gameProcess.Id] = requiresSafeHotkeyPath;
            _activeGame.EnableRaisingEvents = true;
            _activeGame.Exited -= GameProcess_Exited;
            _activeGame.Exited += GameProcess_Exited;
            UpdateSuspendedStateUi(_suspendedGames.Count > 0);
            UpdateGameDetailsActionButton();
        }

        private void GameProcess_Exited(object? sender, EventArgs e)
        {
            if (sender is not Process exitedProcess)
            {
                return;
            }

            var exitedProcessId = exitedProcess.Id;
            var exitedProcessName = GetTrackedGameName(exitedProcess);

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_activeGame?.Id == exitedProcessId)
                {
                    _activeGame.Exited -= GameProcess_Exited;
                    _activeGame.Dispose();
                    _activeGame = null;
                    _activeGameName = null;
                    _activeInstalledGame = null;
                }
                else if (TryRemoveSuspendedGame(exitedProcessId, out var suspendedGame))
                {
                    suspendedGame.Process.Exited -= GameProcess_Exited;
                    _foregroundProcessControlService.CloseSuspendedProcessHandles(suspendedGame.SuspendedState);
                    suspendedGame.Process.Dispose();
                }

                _safeHotkeyPathByProcessId.Remove(exitedProcessId);

                Debug.WriteLine($"Tracked game process exited: {exitedProcessName} ({exitedProcessId}).");
                UpdateSuspendedStateUi(_suspendedGames.Count > 0);
                UpdateGameDetailsActionButton();
            });
        }

        private async Task ResumeSuspendedGameAsync(Guid suspendedGameId)
        {
            // This method updates WinUI controls and must run on the UI thread.
            if (!TryGetSuspendedGame(suspendedGameId, out var suspendedGame, out var suspendedGameIndex))
            {
                UpdateSuspendedStateUi(_suspendedGames.Count > 0);
                return;
            }

            if (HasProcessExited(suspendedGame.Process))
            {
                var suspendedProcessId = suspendedGame.Process.Id;

                _suspendedGames.RemoveAt(suspendedGameIndex);
                suspendedGame.Process.Exited -= GameProcess_Exited;
                _foregroundProcessControlService.CloseSuspendedProcessHandles(suspendedGame.SuspendedState);
                suspendedGame.Process.Dispose();
                _safeHotkeyPathByProcessId.Remove(suspendedProcessId);
                UpdateSuspendedStateUi(_suspendedGames.Count > 0);
                Debug.WriteLine($"Resume skipped because {suspendedGame.Name} had already exited.");
                return;
            }

            if (_activeGame is not null && !HasProcessExited(_activeGame))
            {
                var suspendResult = await SuspendActiveGameAsync(useSafePathForOnlineTitles: false);
                if (!suspendResult.Success)
                {
                    Debug.WriteLine("Resume button could not pause the current active game.");
                    return;
                }

                BringLauncherToFront();
            }

            var resumedWindowHandle = await WaitForMainWindowHandleAsync(suspendedGame.Process);
            if (resumedWindowHandle != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(resumedWindowHandle, NativeMethods.SW_SHOW);
                NativeMethods.SetForegroundWindow(resumedWindowHandle);
                NativeMethods.BringWindowToTop(resumedWindowHandle);
            }

            var resumeSucceeded = suspendedGame.SuspendedState is null;
            if (suspendedGame.SuspendedState is not null)
            {
                resumeSucceeded = _foregroundProcessControlService.TryResumeSuspendedProcess(suspendedGame.SuspendedState);
            }

            if (resumeSucceeded)
            {
                _suspendedGames.RemoveAt(suspendedGameIndex);
                _foregroundProcessControlService.CloseSuspendedProcessHandles(suspendedGame.SuspendedState);

                TrackActiveGame(
                    suspendedGame.Process,
                    suspendedGame.InstalledGame,
                    suspendedGame.Name,
                    _safeHotkeyPathByProcessId.TryGetValue(suspendedGame.Process.Id, out var requiresSafeHotkeyPath) && requiresSafeHotkeyPath);
            }

            UpdateSuspendedStateUi(_suspendedGames.Count > 0);
            UpdateGameDetailsActionButton();
            Debug.WriteLine(resumeSucceeded
                ? $"Resume button resumed {suspendedGame.Name}."
                : "Resume button could not resume the suspended game.");
        }

        private void UpdateSuspendedStateUi(bool isSuspended)
        {
            // This method updates WinUI controls and must run on the UI thread.
            RefreshSuspendedGamesList();
            SuspendedGamesList.Visibility = isSuspended ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task<(bool Success, bool UsedSafePath, bool AntiCheatDetected)> SuspendActiveGameAsync(bool useSafePathForOnlineTitles)
        {
            var usedSafePath = false;
            var antiCheatDetected = false;

            if (_activeGame is null || HasProcessExited(_activeGame))
            {
                Debug.WriteLine(_activeGame is null
                    ? "SuspendActiveGameAsync aborted because there is no tracked active game."
                    : $"SuspendActiveGameAsync aborted because the tracked active game has already exited: {_activeGame.ProcessName} ({_activeGame.Id}).");
                return (false, usedSafePath, antiCheatDetected);
            }

            var activeGame = _activeGame;
            antiCheatDetected = IsKnownAntiCheatRunning();
            var onlineTitleRequiresSafePath = RequiresSafeHotkeyPath(activeGame);
            usedSafePath = antiCheatDetected || (useSafePathForOnlineTitles && onlineTitleRequiresSafePath);

            string activeGameName;
            IntPtr activeWindowHandle;

            try
            {
                activeGame.Refresh();
                activeGameName = activeGame.ProcessName;
                activeWindowHandle = activeGame.MainWindowHandle;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"SuspendActiveGameAsync could not inspect the active game before suspend: {exception}");
                return (false, usedSafePath, antiCheatDetected);
            }

            Debug.WriteLine(
                $"SuspendActiveGameAsync starting for {GetTrackedGameName(activeGame)} ({activeGameName}/{activeGame.Id}). " +
                $"WindowHandle=0x{activeWindowHandle.ToInt64():X}. UsedSafePath={usedSafePath}. " +
                $"UseSafePathForOnlineTitles={useSafePathForOnlineTitles}. OnlineTitleRequiresSafePath={onlineTitleRequiresSafePath}. " +
                $"AntiCheatDetected={antiCheatDetected}. TrackedMetadataAvailable={_activeInstalledGame is not null}.");

            SuspendedProcessState? suspendedState = null;
            var actionSucceeded = usedSafePath
                ? _foregroundProcessControlService.TryMinimizeTrackedOrForegroundProcessAndActivateWindow(_windowHandle, Environment.ProcessId, activeGame)
                : _foregroundProcessControlService.TrySuspendTrackedProcessAndActivateWindow(_windowHandle, activeGame, out suspendedState);

            if (!actionSucceeded)
            {
                Debug.WriteLine(
                    $"SuspendActiveGameAsync failed for {GetTrackedGameName(activeGame)} ({activeGame.Id}). " +
                    $"Path={(usedSafePath ? "safe-minimize" : "suspend")}. SuspendedProcessCount={suspendedState?.ProcessIds.Count ?? 0}.");
                return (false, usedSafePath, antiCheatDetected);
            }

            if (_activeInstalledGame is null)
            {
                Debug.WriteLine("Suspending the active game was skipped because no tracked game metadata was available.");
                return (false, usedSafePath, antiCheatDetected);
            }

            _suspendedGames.Add(new SuspendedGame(Guid.NewGuid(), _activeInstalledGame, GetTrackedGameName(activeGame), activeGame, suspendedState, DateTimeOffset.Now));
            Debug.WriteLine(
                $"SuspendActiveGameAsync succeeded for {GetTrackedGameName(activeGame)} ({activeGame.Id}). " +
                $"Path={(usedSafePath ? "safe-minimize" : "suspend")}. SuspendedProcessIds=[{string.Join(", ", suspendedState?.ProcessIds ?? [])}]. TotalSuspendedGames={_suspendedGames.Count}.");
            _activeGame = null;
            _activeGameName = null;
            _activeInstalledGame = null;
            UpdateSuspendedStateUi(_suspendedGames.Count > 0);
            UpdateGameDetailsActionButton();
            return (true, usedSafePath, antiCheatDetected);
        }

        private void BringLauncherToFront()
        {
            NativeMethods.ShowWindow(_windowHandle, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(_windowHandle);
            NativeMethods.BringWindowToTop(_windowHandle);
        }

        private string GetTrackedGameName(Process gameProcess)
        {
            if (_activeGame?.Id == gameProcess.Id && !string.IsNullOrWhiteSpace(_activeGameName))
            {
                return _activeGameName;
            }

            try
            {
                return string.IsNullOrWhiteSpace(gameProcess.ProcessName) ? "Game" : gameProcess.ProcessName;
            }
            catch
            {
                return "Game";
            }
        }

        private static async Task<IntPtr> WaitForMainWindowHandleAsync(Process process)
        {
            const int maxAttempts = 4;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (HasProcessExited(process))
                {
                    return IntPtr.Zero;
                }

                try
                {
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        return process.MainWindowHandle;
                    }
                }
                catch
                {
                    return IntPtr.Zero;
                }

                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(200);
                }
            }

            return IntPtr.Zero;
        }

        private bool RequiresSafeHotkeyPath(Process gameProcess)
        {
            return _safeHotkeyPathByProcessId.TryGetValue(gameProcess.Id, out var requiresSafeHotkeyPath)
                && requiresSafeHotkeyPath;
        }

        private bool TryRemoveSuspendedGame(int processId, out SuspendedGame suspendedGame)
        {
            for (var index = 0; index < _suspendedGames.Count; index++)
            {
                if (_suspendedGames[index].Process.Id == processId)
                {
                    suspendedGame = _suspendedGames[index];
                    _suspendedGames.RemoveAt(index);
                    return true;
                }
            }

            suspendedGame = null!;
            return false;
        }

        private bool TryGetSuspendedGame(Guid suspendedGameId, out SuspendedGame suspendedGame, out int suspendedGameIndex)
        {
            for (var index = 0; index < _suspendedGames.Count; index++)
            {
                if (_suspendedGames[index].Id == suspendedGameId)
                {
                    suspendedGame = _suspendedGames[index];
                    suspendedGameIndex = index;
                    return true;
                }
            }

            suspendedGame = null!;
            suspendedGameIndex = -1;
            return false;
        }

        private void RefreshSuspendedGamesList()
        {
            SuspendedGamesView.Clear();

            foreach (var suspendedGame in _suspendedGames)
            {
                SuspendedGamesView.Add(new SuspendedGameViewModel(
                    suspendedGame.Id,
                    suspendedGame.Name,
                    suspendedGame.SuspendedAt));
            }

            SuspendedGamesList.Visibility = _suspendedGames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateGameDetailsActionButton()
        {
            GameDetailsRunButton.Content = TryGetSuspendedGame(_activeGameDetailsItem?.InstalledGame, out _)
                ? "Resume"
                : IsActiveDetailsGameRunning()
                    ? "Suspend"
                    : "Run";
        }

        private void RefreshTrackedGameState()
        {
            if (_activeGame is not null && HasProcessExited(_activeGame))
            {
                var exitedProcessId = _activeGame.Id;
                _activeGame.Exited -= GameProcess_Exited;
                _activeGame.Dispose();
                _activeGame = null;
                _activeGameName = null;
                _activeInstalledGame = null;
                _safeHotkeyPathByProcessId.Remove(exitedProcessId);
            }

            var removedSuspendedGame = false;
            for (var index = _suspendedGames.Count - 1; index >= 0; index--)
            {
                var suspendedGame = _suspendedGames[index];
                if (!HasProcessExited(suspendedGame.Process))
                {
                    continue;
                }

                _suspendedGames.RemoveAt(index);
                suspendedGame.Process.Exited -= GameProcess_Exited;
                _foregroundProcessControlService.CloseSuspendedProcessHandles(suspendedGame.SuspendedState);
                suspendedGame.Process.Dispose();
                _safeHotkeyPathByProcessId.Remove(suspendedGame.Process.Id);
                removedSuspendedGame = true;
            }

            if (removedSuspendedGame)
            {
                UpdateSuspendedStateUi(_suspendedGames.Count > 0);
            }
        }

        private bool IsActiveDetailsGameRunning()
        {
            return _activeGameDetailsItem is not null
                && _activeInstalledGame is not null
                && _activeGame is not null
                && !HasProcessExited(_activeGame)
                && AreSameGame(_activeGameDetailsItem.InstalledGame, _activeInstalledGame);
        }

        private static bool AreSameGame(InstalledGame left, InstalledGame right)
        {
            if (left.AppId is > 0 && right.AppId is > 0)
            {
                return left.AppId == right.AppId;
            }

            if (!string.IsNullOrWhiteSpace(left.InstallPath)
                && !string.IsNullOrWhiteSpace(right.InstallPath)
                && string.Equals(left.InstallPath, right.InstallPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetSuspendedGame(InstalledGame? installedGame, out Guid suspendedGameId)
        {
            if (installedGame is not null)
            {
                for (var index = 0; index < _suspendedGames.Count; index++)
                {
                    if (AreSameGame(installedGame, _suspendedGames[index].InstalledGame))
                    {
                        suspendedGameId = _suspendedGames[index].Id;
                        return true;
                    }
                }
            }

            suspendedGameId = Guid.Empty;
            return false;
        }

        private static bool HasProcessExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsKnownAntiCheatRunning()
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (KnownAntiCheatProcessNames.Contains(process.ProcessName))
                    {
                        return true;
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }

            return false;
        }

        private static string BuildGameDetailsMetadata(GameLibraryItemViewModel gameLibraryItem)
        {
            var metadataParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(gameLibraryItem.ReleaseDate))
            {
                metadataParts.Add($"Release date: {gameLibraryItem.ReleaseDate}");
            }

            if (!string.IsNullOrWhiteSpace(gameLibraryItem.Developers))
            {
                metadataParts.Add($"Developers: {gameLibraryItem.Developers}");
            }

            if (!string.IsNullOrWhiteSpace(gameLibraryItem.Publishers))
            {
                metadataParts.Add($"Publishers: {gameLibraryItem.Publishers}");
            }

            if (!string.IsNullOrWhiteSpace(gameLibraryItem.Genres))
            {
                metadataParts.Add($"Genres: {gameLibraryItem.Genres}");
            }

            return string.Join("   •   ", metadataParts);
        }

        private static ImageSource? CreateImageSource(string? imageUrl)
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
            {
                return null;
            }

            return new BitmapImage(imageUri);
        }

        private static void EnsureGameGridItemTransform(GridViewItem itemContainer)
        {
            var visual = ElementCompositionPreview.GetElementVisual(itemContainer);

            if (visual.ImplicitAnimations is null)
            {
                visual.ImplicitAnimations = CreateGameGridItemImplicitAnimations(visual.Compositor);
            }

            UpdateGameGridItemCenterPoint(itemContainer);

            if (visual.Scale == Vector3.Zero)
            {
                visual.Scale = Vector3.One;
            }

            if (GetFocusGlowBorder(itemContainer) is Border focusGlowBorder)
            {
                EnsureOpacityAnimation(focusGlowBorder);
            }

            if (GetGameNameTextBlock(itemContainer) is TextBlock gameNameTextBlock)
            {
                EnsureOpacityAnimation(gameNameTextBlock);
            }
        }

        private static void EnsureScreenshotListItemTransform(ListViewItem itemContainer)
        {
            var visual = ElementCompositionPreview.GetElementVisual(itemContainer);

            if (visual.ImplicitAnimations is null)
            {
                visual.ImplicitAnimations = CreateGameGridItemImplicitAnimations(visual.Compositor);
            }

            UpdateListItemCenterPoint(itemContainer);

            if (visual.Scale == Vector3.Zero)
            {
                visual.Scale = Vector3.One;
            }

            if (GetScreenshotFocusBorder(itemContainer) is Border focusBorder)
            {
                EnsureOpacityAnimation(focusBorder);
            }
        }

        private static void SetGameGridItemFocusState(GridViewItem itemContainer, bool isFocused)
        {
            EnsureGameGridItemTransform(itemContainer);

            var visual = ElementCompositionPreview.GetElementVisual(itemContainer);
            var scale = isFocused ? FocusedGameCardScale : 1d;
            var scaleValue = (float)scale;

            visual.Scale = new Vector3(scaleValue, scaleValue, 1f);

            if (GetFocusGlowBorder(itemContainer) is Border focusGlowBorder)
            {
                ElementCompositionPreview.GetElementVisual(focusGlowBorder).Opacity = isFocused
                    ? FocusGlowOpacity
                    : UnfocusedGlowOpacity;
            }

            if (GetGameNameTextBlock(itemContainer) is TextBlock gameNameTextBlock)
            {
                ElementCompositionPreview.GetElementVisual(gameNameTextBlock).Opacity = isFocused
                    ? FocusedTitleOpacity
                    : UnfocusedTitleOpacity;
            }

            Canvas.SetZIndex(itemContainer, isFocused ? 1 : 0);
        }

        private static void UpdateGameGridItemCenterPoint(GridViewItem itemContainer)
        {
            var visual = ElementCompositionPreview.GetElementVisual(itemContainer);
            visual.CenterPoint = new Vector3(
                (float)(itemContainer.ActualWidth / 2),
                (float)(itemContainer.ActualHeight / 2),
                0f);
        }

        private static void SetScreenshotListItemFocusState(ListViewItem itemContainer, bool isFocused)
        {
            EnsureScreenshotListItemTransform(itemContainer);

            var visual = ElementCompositionPreview.GetElementVisual(itemContainer);
            var scale = isFocused ? FocusedGameCardScale : 1d;
            var scaleValue = (float)scale;

            visual.Scale = new Vector3(scaleValue, scaleValue, 1f);

            if (GetScreenshotFocusBorder(itemContainer) is Border focusBorder)
            {
                ElementCompositionPreview.GetElementVisual(focusBorder).Opacity = isFocused
                    ? FocusGlowOpacity
                    : UnfocusedGlowOpacity;
            }

            Canvas.SetZIndex(itemContainer, isFocused ? 1 : 0);
        }

        private static void UpdateListItemCenterPoint(ListViewItem itemContainer)
        {
            var visual = ElementCompositionPreview.GetElementVisual(itemContainer);
            visual.CenterPoint = new Vector3(
                (float)(itemContainer.ActualWidth / 2),
                (float)(itemContainer.ActualHeight / 2),
                0f);
        }

        private static ImplicitAnimationCollection CreateGameGridItemImplicitAnimations(Compositor compositor)
        {
            var animations = compositor.CreateImplicitAnimationCollection();

            var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
            scaleAnimation.InsertExpressionKeyFrame(1f, "this.FinalValue");
            scaleAnimation.Duration = FocusAnimationDuration;
            scaleAnimation.Target = nameof(Visual.Scale);

            animations[nameof(Visual.Scale)] = scaleAnimation;

            return animations;
        }

        private static void EnsureOpacityAnimation(UIElement element)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);

            if (visual.ImplicitAnimations is not null)
            {
                return;
            }

            var animations = visual.Compositor.CreateImplicitAnimationCollection();
            var opacityAnimation = visual.Compositor.CreateScalarKeyFrameAnimation();
            opacityAnimation.InsertExpressionKeyFrame(1f, "this.FinalValue");
            opacityAnimation.Duration = FocusAnimationDuration;
            opacityAnimation.Target = nameof(Visual.Opacity);

            animations[nameof(Visual.Opacity)] = opacityAnimation;
            visual.ImplicitAnimations = animations;
        }

        private static Border? GetFocusGlowBorder(GridViewItem itemContainer)
        {
            return (itemContainer.ContentTemplateRoot as FrameworkElement)?.FindName("FocusGlowBorder") as Border;
        }

        private static TextBlock? GetGameNameTextBlock(GridViewItem itemContainer)
        {
            return (itemContainer.ContentTemplateRoot as FrameworkElement)?.FindName("GameNameTextBlock") as TextBlock;
        }

        private static Border? GetScreenshotFocusBorder(ListViewItem itemContainer)
        {
            return (itemContainer.ContentTemplateRoot as FrameworkElement)?.FindName("ScreenshotFocusBorder") as Border;
        }

    }
}

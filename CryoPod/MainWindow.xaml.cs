using System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using CryoPod.Models;
using CryoPod.Services.GameExplorer;
using CryoPod.Services.Steam;
using CryoPod.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
        private const int GamesPerRow = 3;
        private const double GameCardAspectRatio = 420d / 203d;
        private const double GameCardTextHeight = 56;
        private const double GameCardVerticalSpacing = 8;
        private const double GameCardHorizontalMargin = 16;

        private static readonly TimeSpan SteamMetadataRefreshInterval = TimeSpan.FromHours(24);

        private IReadOnlyList<InstalledGame> _installedGames = [];
        private IReadOnlyList<SteamAppDetailsResponse> _steamAppDetails = [];
        private readonly ISteamAppDetailsService _steamAppDetailsService = new SteamAppDetailsService();
        private readonly ISteamAppDetailsCacheService _steamAppDetailsCacheService = new SteamAppDetailsCacheService();

        public MainWindow()
        {
            InitializeComponent();
            SetFullScreen();
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

        private void SetFullScreen()
        {
            var windowHandle = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
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
                .Select(game =>
                {
                    SteamAppDetailsResponse? appDetails = null;
                    if (game.AppId is > 0)
                    {
                        steamAppDetailsByAppId.TryGetValue(game.AppId.Value, out appDetails);
                    }

                    var thumbnailUrl = appDetails?.Data?.HeaderImage ?? appDetails?.Data?.CapsuleImage;
                    return new GameLibraryItemViewModel(game, thumbnailUrl);
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
    }
}

using System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using CryoPod.Models;
using CryoPod.Services.GameExplorer;
using CryoPod.Services.Input;
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
        private const double FocusedGameCardScale = 1.03;
        private const float FocusGlowOpacity = 1f;
        private const float UnfocusedGlowOpacity = 0f;
        private const float FocusedTitleOpacity = 1f;
        private const float UnfocusedTitleOpacity = 0.82f;
        private static readonly TimeSpan FocusAnimationDuration = TimeSpan.FromMilliseconds(180);

        private static readonly TimeSpan SteamMetadataRefreshInterval = TimeSpan.FromHours(24);

        private IReadOnlyList<InstalledGame> _installedGames = [];
        private IReadOnlyList<SteamAppDetailsResponse> _steamAppDetails = [];
        private readonly ISteamAppDetailsService _steamAppDetailsService = new SteamAppDetailsService();
        private readonly ISteamAppDetailsCacheService _steamAppDetailsCacheService = new SteamAppDetailsCacheService();
        private readonly GameLibraryNavigationController _gameLibraryNavigationController;

        public MainWindow()
        {
            InitializeComponent();
            Closed += MainWindow_Closed;
            _gameLibraryNavigationController = new GameLibraryNavigationController(
                RootGrid,
                GamesGridView,
                ExitPromptYesButton,
                ExitPromptNoButton,
                DispatcherQueue,
                CanProcessMainViewInput);
            _gameLibraryNavigationController.ExitRequested += GameLibraryNavigationController_ExitRequested;
            _gameLibraryNavigationController.ExitConfirmed += GameLibraryNavigationController_ExitConfirmed;
            _gameLibraryNavigationController.ExitCanceled += GameLibraryNavigationController_ExitCanceled;
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

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
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

        private bool CanProcessMainViewInput()
        {
            return StartupLoaderPanel.Visibility != Visibility.Visible;
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

    }
}

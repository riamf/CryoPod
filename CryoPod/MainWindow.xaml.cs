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
        private IReadOnlyList<InstalledGame> _installedGames = [];
        private IReadOnlyList<SteamAppDetailsResponse> _steamAppDetails = [];
        private readonly ISteamAppDetailsService _steamAppDetailsService = new SteamAppDetailsService();

        public MainWindow()
        {
            InitializeComponent();
            SetFullScreen();
        }

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            await RunStartupWorkAsync();
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

            StartupStatusText.Text = "Loading Steam metadata...";
            _steamAppDetails = await LoadSteamAppDetailsAsync();

            StartupLoaderPanel.Visibility = Visibility.Collapsed;
        }

        private async Task<IReadOnlyList<SteamAppDetailsResponse>> LoadSteamAppDetailsAsync()
        {
            var steamGamesByAppId = _installedGames
                .Where(game => game.AppId is > 0)
                .GroupBy(game => game.AppId!.Value)
                .ToDictionary(group => group.Key, group => group.First());

            var steamAppIds = steamGamesByAppId.Keys.ToList();

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
    }
}

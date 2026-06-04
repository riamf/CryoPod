using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using CryoPod.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CryoPod.ViewModels
{
    public sealed class GameLibraryItemViewModel
    {
        public GameLibraryItemViewModel(InstalledGame installedGame, SteamAppDetailsResponse? appDetails)
        {
            InstalledGame = installedGame;
            Name = installedGame.Name;
            var appData = appDetails?.Data;

            Thumbnail = CreateImageSource(appData?.HeaderImage ?? appData?.CapsuleImage);
            BackgroundUrl = appData?.BackgroundRaw ?? appData?.Background;
            ShortDescription = appData?.ShortDescription?.Trim();
            DetailedDescription = NormalizeHtmlText(appData?.DetailedDescription);
            AboutTheGame = NormalizeHtmlText(appData?.AboutTheGame);
            ReleaseDate = appData?.ReleaseDate?.Date?.Trim();
            Developers = JoinValues(appData?.Developers);
            Publishers = JoinValues(appData?.Publishers);
            Genres = JoinValues(appData?.Genres.Select(genre => genre.Description));
            IsOnlineMultiplayer = DetectOnlineMultiplayer(appData?.Categories.Select(category => category.Description));
            ScreenshotUrls = appData?.Screenshots
                .Select(screenshot => screenshot.PathFull ?? screenshot.PathThumbnail)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Cast<string>()
                .ToList()
                ?? [];
        }

        public InstalledGame InstalledGame { get; }

        public string Name { get; }

        public ImageSource? Thumbnail { get; }

        public string? BackgroundUrl { get; }

        public string? ShortDescription { get; }

        public string? DetailedDescription { get; }

        public string? AboutTheGame { get; }

        public string? ReleaseDate { get; }

        public string? Developers { get; }

        public string? Publishers { get; }

        public string? Genres { get; }

        public bool IsOnlineMultiplayer { get; }

        public IReadOnlyList<string> ScreenshotUrls { get; }

        private static ImageSource? CreateImageSource(string? imageUrl)
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
            {
                return null;
            }

            return new BitmapImage(imageUri);
        }

        private static string? JoinValues(IEnumerable<string?>? values)
        {
            if (values is null)
            {
                return null;
            }

            var items = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToList();

            return items.Count > 0 ? string.Join(", ", items) : null;
        }

        private static string? NormalizeHtmlText(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var formatted = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            formatted = Regex.Replace(formatted, @"</p>|</div>|</h\d>", "\n\n", RegexOptions.IgnoreCase);
            formatted = Regex.Replace(formatted, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
            formatted = Regex.Replace(formatted, @"</li>", "\n", RegexOptions.IgnoreCase);
            formatted = Regex.Replace(formatted, @"<[^>]+>", string.Empty);
            formatted = WebUtility.HtmlDecode(formatted);
            formatted = Regex.Replace(formatted, @"\r\n?", "\n");
            formatted = Regex.Replace(formatted, @"\n{3,}", "\n\n");
            formatted = Regex.Replace(formatted, @"[ \t]+\n", "\n");

            return formatted.Trim();
        }

        private static bool DetectOnlineMultiplayer(IEnumerable<string?>? categories)
        {
            if (categories is null)
            {
                return false;
            }

            foreach (var category in categories)
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    continue;
                }

                if (category.Contains("Online", StringComparison.OrdinalIgnoreCase)
                    || category.Contains("PvP", StringComparison.OrdinalIgnoreCase)
                    || category.Contains("MMO", StringComparison.OrdinalIgnoreCase)
                    || category.Contains("Cross-Platform Multiplayer", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

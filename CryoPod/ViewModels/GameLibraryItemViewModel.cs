using System;
using CryoPod.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CryoPod.ViewModels
{
    public sealed class GameLibraryItemViewModel
    {
        public GameLibraryItemViewModel(InstalledGame installedGame, string? thumbnailUrl, string? backgroundUrl)
        {
            InstalledGame = installedGame;
            Name = installedGame.Name;

            if (Uri.TryCreate(thumbnailUrl, UriKind.Absolute, out var thumbnailUri))
            {
                Thumbnail = new BitmapImage(thumbnailUri);
            }

            if (Uri.TryCreate(backgroundUrl, UriKind.Absolute, out var backgroundUri))
            {
                Background = new BitmapImage(backgroundUri);
            }
        }

        public InstalledGame InstalledGame { get; }

        public string Name { get; }

        public ImageSource? Thumbnail { get; }

        public ImageSource? Background { get; }
    }
}

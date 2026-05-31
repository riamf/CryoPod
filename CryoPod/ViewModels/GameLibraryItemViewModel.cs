using System;
using CryoPod.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CryoPod.ViewModels
{
    public sealed class GameLibraryItemViewModel
    {
        public GameLibraryItemViewModel(InstalledGame installedGame, string? thumbnailUrl)
        {
            InstalledGame = installedGame;
            Name = installedGame.Name;

            if (Uri.TryCreate(thumbnailUrl, UriKind.Absolute, out var thumbnailUri))
            {
                Thumbnail = new BitmapImage(thumbnailUri);
            }
        }

        public InstalledGame InstalledGame { get; }

        public string Name { get; }

        public ImageSource? Thumbnail { get; }
    }
}

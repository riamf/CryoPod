using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CryoPod.Models;
using Windows.System;

namespace CryoPod.Services.Launch
{
    internal sealed class GameLaunchService
    {
        public async Task<bool> LaunchAsync(InstalledGame installedGame)
        {
            if (installedGame.AppId is not > 0)
            {
                return false;
            }

            var launchUri = new Uri($"steam://rungameid/{installedGame.AppId.Value}");

            try
            {
                if (await Launcher.LaunchUriAsync(launchUri))
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to launch Steam URI for {installedGame.Name} ({installedGame.AppId}): {exception}");
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = launchUri.ToString(),
                    UseShellExecute = true,
                });
                return true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to start Steam shell launch for {installedGame.Name} ({installedGame.AppId}): {exception}");
                return false;
            }
        }
    }
}

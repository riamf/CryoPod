namespace CryoPod.Models
{
    public sealed record InstalledGame(string Name, string Source, string? InstallPath = null);
}

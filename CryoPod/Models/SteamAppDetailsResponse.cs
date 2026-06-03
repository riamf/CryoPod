using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CryoPod.Models
{
    public sealed class SteamAppDetailsResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public SteamAppData? Data { get; set; }
    }

    public sealed class SteamAppData
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("steam_appid")]
        public int SteamAppId { get; set; }

        [JsonPropertyName("is_free")]
        public bool IsFree { get; set; }

        [JsonPropertyName("short_description")]
        public string? ShortDescription { get; set; }

        [JsonPropertyName("detailed_description")]
        public string? DetailedDescription { get; set; }

        [JsonPropertyName("about_the_game")]
        public string? AboutTheGame { get; set; }

        [JsonPropertyName("header_image")]
        public string? HeaderImage { get; set; }

        [JsonPropertyName("capsule_image")]
        public string? CapsuleImage { get; set; }

        [JsonPropertyName("developers")]
        public List<string> Developers { get; set; } = [];

        [JsonPropertyName("publishers")]
        public List<string> Publishers { get; set; } = [];

        [JsonPropertyName("platforms")]
        public SteamPlatforms? Platforms { get; set; }

        [JsonPropertyName("categories")]
        public List<SteamCategory> Categories { get; set; } = [];

        [JsonPropertyName("genres")]
        public List<SteamGenre> Genres { get; set; } = [];

        [JsonPropertyName("screenshots")]
        public List<SteamScreenshot> Screenshots { get; set; } = [];

        [JsonPropertyName("release_date")]
        public SteamReleaseDate? ReleaseDate { get; set; }

        [JsonPropertyName("background")]
        public string? Background { get; set; }

        [JsonPropertyName("background_raw")]
        public string? BackgroundRaw { get; set; }
    }

    public sealed class SteamPlatforms
    {
        [JsonPropertyName("windows")]
        public bool Windows { get; set; }

        [JsonPropertyName("mac")]
        public bool Mac { get; set; }

        [JsonPropertyName("linux")]
        public bool Linux { get; set; }
    }

    public sealed class SteamCategory
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public sealed class SteamGenre
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public sealed class SteamScreenshot
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("path_thumbnail")]
        public string? PathThumbnail { get; set; }

        [JsonPropertyName("path_full")]
        public string? PathFull { get; set; }
    }

    public sealed class SteamReleaseDate
    {
        [JsonPropertyName("coming_soon")]
        public bool ComingSoon { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }
    }
}

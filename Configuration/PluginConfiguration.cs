using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.VideoStore.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Anthropic API key for Staff Picks themes and blurbs (empty = picks without Claude).</summary>
    public string AnthropicApiKey { get; set; } = string.Empty;

    /// <summary>Claude model for Staff Picks.</summary>
    public string Model { get; set; } = "claude-haiku-5-5";

    public bool StaffPicks { get; set; } = true;

    public bool NewReleases { get; set; } = true;

    public bool HiddenGems { get; set; } = true;

    public bool LastCopy { get; set; } = true;

    /// <summary>Titles per shelf.</summary>
    public int PerShelf { get; set; } = 12;

    public bool IncludeMovies { get; set; } = true;

    public bool IncludeShows { get; set; } = true;

    /// <summary>Hidden Gems: lowest community rating that counts as "good".</summary>
    public double HiddenGemMinRating { get; set; } = 7.0;

    /// <summary>Days between restocking the weekly shelves.</summary>
    public int RestockDays { get; set; } = 7;

    // ---- bookkeeping (not shown on the settings page)

    /// <summary>The collections this plugin manages, so renames by the user don't create duplicates.</summary>
    public List<ShelfRecord> Shelves { get; set; } = new();

    /// <summary>Recent Staff Picks themes, so they don't repeat.</summary>
    public List<string> RecentThemes { get; set; } = new();

    /// <summary>Everything that was on a weekly shelf recently, so a restock brings new titles.</summary>
    public List<Guid> RecentlyShelved { get; set; } = new();
}

public class ShelfRecord
{
    public string Key { get; set; } = string.Empty;

    public Guid CollectionId { get; set; }

    public DateTime LastStocked { get; set; }
}

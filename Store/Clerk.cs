using Jellyfin.Data.Enums;
using Jellyfin.Plugin.VideoStore.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.VideoStore.Store;

/// <summary>One shelf: a collection the plugin keeps stocked.</summary>
public sealed record Shelf(string Key, string Name, string Image, bool Weekly);

/// <summary>
/// Stocks the shelves. Each shelf is a locked collection with its own cover art; restocking swaps its titles
/// (and, for Staff Picks, its description). New Releases restock daily, the rest every RestockDays.
/// </summary>
public sealed class Clerk
{
    public static readonly Shelf[] AllShelves =
    {
        new("staff-picks", "Staff Picks", "staff-picks.png", true),
        new("new-releases", "New Releases", "new-releases.png", false),
        new("hidden-gems", "Hidden Gems", "hidden-gems.png", true),
        new("last-copy", "Last Copy", "last-copy.png", true)
    };

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ILibraryManager _library;
    private readonly ICollectionManager _collections;
    private readonly IUserManager _users;
    private readonly IProviderManager _providers;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<Clerk> _logger;

    public Clerk(ILibraryManager library, ICollectionManager collections, IUserManager users, IProviderManager providers, IHttpClientFactory http, ILogger<Clerk> logger)
    {
        _library = library;
        _collections = collections;
        _users = users;
        _providers = providers;
        _http = http;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance!.Configuration;

    /// <summary>Restock shelves that are due (or all of them when <paramref name="force"/>). Returns a summary per shelf.</summary>
    public async Task<List<string>> RestockAsync(bool force, IProgress<double>? progress, CancellationToken ct)
    {
        var report = new List<string>();
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cfg = Config;
            var stock = Stock();
            var watched = Watched();
            var recent = new HashSet<Guid>(cfg.RecentlyShelved);
            var shelves = AllShelves.Where(Enabled).ToList();
            var done = 0;

            foreach (var shelf in shelves)
            {
                ct.ThrowIfCancellationRequested();
                var record = cfg.Shelves.FirstOrDefault(s => s.Key == shelf.Key);
                var due = force || record is null || !shelf.Weekly
                    || DateTime.UtcNow - record.LastStocked > TimeSpan.FromDays(Math.Clamp(cfg.RestockDays, 1, 60) - 0.25);
                if (!due)
                {
                    report.Add($"{shelf.Name}: not due yet");
                    continue;
                }

                var n = Math.Clamp(cfg.PerShelf, 3, 40);
                List<BaseItem> picks;
                string? overview = null;
                switch (shelf.Key)
                {
                    case "staff-picks":
                        var staff = await StaffPicker.PickAsync(stock, watched, recent, n, _http, _logger, ct).ConfigureAwait(false);
                        picks = staff.Items;
                        overview = staff.Overview;
                        break;
                    case "new-releases":
                        picks = stock.OrderByDescending(Added).Take(n).ToList();
                        overview = "Fresh on the shelf: the latest arrivals.";
                        break;
                    case "hidden-gems":
                        picks = Shuffle(stock.Where(i => !watched.Contains(i.Id) && (i.CommunityRating ?? 0) >= cfg.HiddenGemMinRating)
                                .Where(i => !recent.Contains(i.Id)).ToList())
                            .Take(n).ToList();
                        overview = "Highly rated, and nobody in the house has watched them yet.";
                        break;
                    default: // last-copy
                        picks = stock.Where(i => !watched.Contains(i.Id) && !recent.Contains(i.Id))
                            .OrderBy(Added).Take(n).ToList();
                        overview = "The tapes that have been sitting on the shelf the longest. Somebody give them a chance.";
                        break;
                }

                if (picks.Count == 0)
                {
                    report.Add($"{shelf.Name}: nothing fits right now");
                    continue;
                }

                await Shelve(shelf, picks, overview, ct).ConfigureAwait(false);
                if (shelf.Weekly)
                {
                    foreach (var p in picks)
                    {
                        recent.Add(p.Id);
                    }
                }

                report.Add($"{shelf.Name}: {picks.Count} titles");
                progress?.Report(100.0 * ++done / Math.Max(1, shelves.Count));
            }

            // Remember about a month of weekly shelves so restocks bring new titles.
            cfg.RecentlyShelved = recent.TakeLast(Math.Clamp(cfg.PerShelf, 3, 40) * 3 * 4).ToList();
            Plugin.Instance!.SaveConfiguration();
        }
        finally
        {
            Gate.Release();
        }

        _logger.LogInformation("Video Store restocked: {Report}", string.Join("; ", report));
        return report;
    }

    private static bool Enabled(Shelf s) => s.Key switch
    {
        "staff-picks" => Config.StaffPicks,
        "new-releases" => Config.NewReleases,
        "hidden-gems" => Config.HiddenGems,
        _ => Config.LastCopy
    };

    /// <summary>Movies and shows the store can put on a shelf.</summary>
    private List<BaseItem> Stock()
    {
        var kinds = new List<BaseItemKind>();
        if (Config.IncludeMovies)
        {
            kinds.Add(BaseItemKind.Movie);
        }

        if (Config.IncludeShows)
        {
            kinds.Add(BaseItemKind.Series);
        }

        if (kinds.Count == 0)
        {
            return new List<BaseItem>();
        }

        return _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = kinds.ToArray(),
            IsVirtualItem = false,
            Recursive = true
        }).Where(i => !string.IsNullOrEmpty(i.Name)).ToList();
    }

    /// <summary>Movies anyone has watched, and shows anyone has watched an episode of.</summary>
    private HashSet<Guid> Watched()
    {
        var seen = new HashSet<Guid>();
        foreach (var user in _users.GetUsers())
        {
            var played = _library.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
                IsPlayed = true,
                Recursive = true
            });
            foreach (var item in played)
            {
                seen.Add(item is Episode ep ? ep.SeriesId : item.Id);
            }
        }

        return seen;
    }

    private static DateTime Added(BaseItem item)
        => item is Series s && s.DateLastMediaAdded is DateTime d ? d : item.DateCreated;

    internal static List<T> Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    /// <summary>Put exactly these titles on the shelf's collection (creating it, with its cover, if needed).</summary>
    private async Task Shelve(Shelf shelf, List<BaseItem> picks, string? overview, CancellationToken ct)
    {
        var cfg = Config;
        var record = cfg.Shelves.FirstOrDefault(s => s.Key == shelf.Key);
        var box = record is null ? null : _library.GetItemById(record.CollectionId) as BoxSet;

        if (box is null)
        {
            box = await _collections.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = shelf.Name,
                IsLocked = true,
                ItemIdList = picks.Select(p => p.Id.ToString("N")).ToArray()
            }).ConfigureAwait(false);
            record ??= new ShelfRecord { Key = shelf.Key };
            record.CollectionId = box.Id;
            if (!cfg.Shelves.Contains(record))
            {
                cfg.Shelves.Add(record);
            }
        }
        else
        {
            var current = box.GetLinkedChildren().Select(c => c.Id).ToHashSet();
            var wanted = picks.Select(p => p.Id).ToHashSet();
            var remove = current.Where(id => !wanted.Contains(id)).ToList();
            var add = wanted.Where(id => !current.Contains(id)).ToList();
            if (remove.Count > 0)
            {
                await _collections.RemoveFromCollectionAsync(box.Id, remove).ConfigureAwait(false);
            }

            if (add.Count > 0)
            {
                await _collections.AddToCollectionAsync(box.Id, add).ConfigureAwait(false);
            }

            box = _library.GetItemById(box.Id) as BoxSet ?? box;
        }

        record!.LastStocked = DateTime.UtcNow;

        // Cover art (only if the collection has none, so a cover you picked yourself stays).
        if (!box.HasImage(ImageType.Primary, 0))
        {
            var res = typeof(Plugin).Assembly.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith("." + shelf.Image, StringComparison.OrdinalIgnoreCase));
            if (res is not null)
            {
                await using var art = typeof(Plugin).Assembly.GetManifestResourceStream(res)!;
                await _providers.SaveImage(box, art, "image/png", ImageType.Primary, null, ct).ConfigureAwait(false);
            }
        }

        if (overview is not null)
        {
            box.Overview = overview;
            if (!box.LockedFields.Contains(MetadataField.Overview))
            {
                box.LockedFields = box.LockedFields.Append(MetadataField.Overview).ToArray();
            }
        }

        await box.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
    }
}

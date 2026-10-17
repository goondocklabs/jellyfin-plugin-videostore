using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.VideoStore.Store;

/// <summary>Daily restock (Dashboard > Scheduled Tasks > Video Store). Weekly shelves only change when they're due.</summary>
public sealed class RestockTask : IScheduledTask
{
    private readonly Clerk _clerk;

    public RestockTask(Clerk clerk)
    {
        _clerk = clerk;
    }

    public string Name => "Restock shelves";

    public string Key => "VideoStoreRestock";

    public string Description => "Updates New Releases, and restocks Staff Picks, Hidden Gems and Last Copy when they're due.";

    public string Category => "Video Store";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _clerk.RestockAsync(false, progress, cancellationToken);

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(4).Add(TimeSpan.FromMinutes(10)).Ticks
        };
    }
}

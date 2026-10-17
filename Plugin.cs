using System.Globalization;
using Jellyfin.Plugin.VideoStore.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.VideoStore;

/// <summary>Video Store: rotating video-store shelves (collections) built from your own library.</summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string PluginGuid = "22636043-47a7-4333-9892-a91ecb14861e";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Video Store";

    public override Guid Id => Guid.Parse(PluginGuid);

    public override string Description => "Rotating video store shelves from your own library: Staff Picks, New Releases, Hidden Gems and Last Copy.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        };
    }
}

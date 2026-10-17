# 📼 Video Store for Jellyfin

Rotating video store shelves built from your own library. Each shelf is a Jellyfin collection with its own neon
cover art, so it shows up in every Jellyfin app, Apple TV and Roku included.

| Shelf | What's on it | Restocked |
|---|---|---|
| **Staff Picks** | A theme of the week ("Rainy Saturday", "Under 90 Minutes"...) with a hand-written style card for every title | Weekly |
| **New Releases** | The latest additions to the library | Daily |
| **Hidden Gems** | Well rated titles nobody in the house has watched yet | Weekly |
| **Last Copy** | The titles that have sat unwatched the longest | Weekly |

Staff Picks uses Claude when you add an Anthropic API key in the plugin settings (well under a cent a week).
Without a key it picks a genre of the week.

## Install

1. Jellyfin → Dashboard → Plugins → Repositories → **+** and add
   `https://raw.githubusercontent.com/goondocklabs/jellyfin-plugin-videostore/main/manifest.json`
2. Catalog → **Video Store** → Install, then restart Jellyfin.
3. Dashboard → Plugins → Video Store → **Restock all shelves now**.

Shelves restock every morning at 4:10 (Dashboard → Scheduled Tasks → Video Store). Weekly shelves avoid titles
they've shown in the last month. Your own cover art on a shelf is kept.

## Releases

Bump the version in `meta.json` and `Jellyfin.Plugin.VideoStore.csproj` and push to `main`; GitHub Actions builds
the plugin, makes the release and updates `manifest.json`.

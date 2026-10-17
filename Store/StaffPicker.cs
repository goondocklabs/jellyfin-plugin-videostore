using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.VideoStore.Store;

/// <summary>
/// This week's Staff Picks: Claude (if there's an API key) picks a theme and titles from your library and writes
/// a short shelf-talker for each, like the cards taped to video store shelves. Without a key: a genre of the week.
/// </summary>
public static class StaffPicker
{
    private const int CandidateCount = 220;

    public sealed record Result(List<BaseItem> Items, string Overview);

    public static async Task<Result> PickAsync(List<BaseItem> stock, HashSet<Guid> watched, HashSet<Guid> recent, int n, IHttpClientFactory http, ILogger logger, CancellationToken ct)
    {
        var cfg = Plugin.Instance!.Configuration;
        var fresh = stock.Where(i => !recent.Contains(i.Id)).ToList();
        if (fresh.Count < n)
        {
            fresh = stock.ToList();
        }

        if (!string.IsNullOrWhiteSpace(cfg.AnthropicApiKey))
        {
            try
            {
                var picked = await AskClaude(fresh, watched, n, http, ct).ConfigureAwait(false);
                if (picked is not null && picked.Items.Count >= Math.Min(3, n))
                {
                    return picked;
                }

                logger.LogWarning("Video Store: Claude's Staff Picks didn't come back usable; picking a genre of the week instead");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Video Store: couldn't reach Claude for Staff Picks; picking a genre of the week instead");
            }
        }

        return GenreOfTheWeek(fresh, n);
    }

    private static Result GenreOfTheWeek(List<BaseItem> stock, int n)
    {
        var cfg = Plugin.Instance!.Configuration;
        var genres = stock.SelectMany(i => i.Genres ?? Array.Empty<string>())
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= n)
            .Select(g => g.Key)
            .Where(g => !cfg.RecentThemes.Contains(g + " Week", StringComparer.OrdinalIgnoreCase))
            .ToList();
        var genre = genres.Count > 0 ? genres[Random.Shared.Next(genres.Count)] : null;
        var pool = genre is null ? stock : stock.Where(i => (i.Genres ?? Array.Empty<string>()).Contains(genre, StringComparer.OrdinalIgnoreCase)).ToList();
        var items = Clerk.Shuffle(pool.OrderByDescending(i => i.CommunityRating ?? 0).Take(n * 3).ToList()).Take(n).ToList();
        var theme = genre is null ? "Clerk's Choice" : genre + " Week";
        Remember(theme);
        return new Result(items, $"This week's theme: {theme}");
    }

    private static async Task<Result?> AskClaude(List<BaseItem> stock, HashSet<Guid> watched, int n, IHttpClientFactory http, CancellationToken ct)
    {
        var cfg = Plugin.Instance!.Configuration;
        var inv = CultureInfo.InvariantCulture;

        // A random slice of the library (a big library won't fit, and a slice keeps weeks different).
        var candidates = Clerk.Shuffle(stock.ToList()).Take(CandidateCount).ToList();
        var list = new StringBuilder();
        for (var i = 0; i < candidates.Count; i++)
        {
            var it = candidates[i];
            var mins = it.RunTimeTicks is long t && t > 0 ? $", {TimeSpan.FromTicks(t).TotalMinutes:0} min" : string.Empty;
            var genres = string.Join("/", (it.Genres ?? Array.Empty<string>()).Take(3));
            var rating = it.CommunityRating is float r ? string.Format(inv, ", rated {0:0.0}", r) : string.Empty;
            var kind = it is Series ? "TV show" : "movie";
            var seen = watched.Contains(it.Id) ? ", watched before" : ", never watched";
            var about = (it.Overview ?? string.Empty).Replace('\n', ' ');
            if (about.Length > 110)
            {
                about = about[..107] + "...";
            }

            list.Append(inv, $"{i}. {it.Name} ({it.ProductionYear?.ToString(inv) ?? "?"}) [{kind}; {genres}{rating}{mins}{seen}] {about}\n");
        }

        var avoid = cfg.RecentThemes.Count > 0 ? "Don't reuse these recent themes: " + string.Join("; ", cfg.RecentThemes) + "." : string.Empty;
        var prompt = $$"""
            You're the clerk at a neighborhood video store in 1995, stocking this week's "Staff Picks" shelf from the store's
            inventory below. Pick a fun, specific theme for the week (like "Rainy Saturday", "Movies Your Parents Fell Asleep To",
            "Under 90 Minutes", "Guilty Pleasures", "Road Trips"), then choose {{n}} titles from the list that fit it.
            Mix in some never-watched titles. {{avoid}}

            For each pick, write a shelf-talker: one short, enthusiastic, handwritten-card style line (at most 90 characters),
            based on what you know about the title. No spoilers, no emoji.

            Answer with only JSON like:
            {"theme": "Rainy Saturday", "picks": [{"n": 12, "card": "Pure cozy. Bring a blanket."}]}
            using the numbers from the list.

            Inventory:
            {{list}}
            """;

        var body = JsonSerializer.Serialize(new
        {
            model = string.IsNullOrWhiteSpace(cfg.Model) ? "claude-haiku-5-5" : cfg.Model.Trim(),
            max_tokens = 4000,
            messages = new[] { new { role = "user", content = prompt } }
        });

        using var client = http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(90);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", cfg.AnthropicApiKey.Trim());
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("JellyfinVideoStore", "1.0"));

        using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Anthropic API said {(int)res.StatusCode}: {text}");
        }

        var answer = new StringBuilder();
        using (var doc = JsonDocument.Parse(text))
        {
            foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text" && block.TryGetProperty("text", out var t))
                {
                    answer.Append(t.GetString());
                }
            }
        }

        var json = Regex.Match(answer.ToString(), @"\{.*\}", RegexOptions.Singleline).Value;
        if (json.Length == 0)
        {
            return null;
        }

        using var picks = JsonDocument.Parse(json);
        var theme = picks.RootElement.TryGetProperty("theme", out var th) ? th.GetString() ?? "Staff Picks" : "Staff Picks";
        var items = new List<BaseItem>();
        var lines = new StringBuilder();
        lines.Append(inv, $"This week's theme: {theme}\n");
        foreach (var p in picks.RootElement.GetProperty("picks").EnumerateArray())
        {
            if (!p.TryGetProperty("n", out var num) || !num.TryGetInt32(out var idx) || idx < 0 || idx >= candidates.Count)
            {
                continue;
            }

            var item = candidates[idx];
            if (items.Contains(item))
            {
                continue;
            }

            items.Add(item);
            var card = p.TryGetProperty("card", out var c) ? (c.GetString() ?? string.Empty).Trim() : string.Empty;
            lines.Append(inv, $"\n{item.Name}: {card}");
            if (items.Count >= n)
            {
                break;
            }
        }

        Remember(theme);
        return new Result(items, lines.ToString());
    }

    private static void Remember(string theme)
    {
        var cfg = Plugin.Instance!.Configuration;
        cfg.RecentThemes.Add(theme);
        cfg.RecentThemes = cfg.RecentThemes.TakeLast(10).ToList();
    }
}

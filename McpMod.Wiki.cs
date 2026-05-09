using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int DefaultWikiSearchLimit = 10;
    private const int MaxWikiSearchLimit = 50;

    private static void HandleGetWiki(HttpListenerRequest request, HttpListenerResponse response)
    {
        var query = request.QueryString["query"] ?? request.QueryString["q"] ?? "";
        var itemType = request.QueryString["type"] ?? request.QueryString["item_type"] ?? "all";
        var limit = ParseWikiLimit(request.QueryString["limit"]);

        if (string.IsNullOrWhiteSpace(query))
        {
            SendError(response, 400, "query is required; wiki search does not return the full profile catalog.");
            return;
        }

        try
        {
            var dataTask = RunOnMainThread(() => BuildWikiSearch(query, itemType, limit));
            SendJson(response, dataTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to search wiki: {ex.Message}");
        }
    }

    internal static object SearchWiki(string query, string itemType = "all", int? limit = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Error("query is required; wiki search does not return the full profile catalog.");

        return BuildWikiSearch(query, itemType, NormalizeWikiLimit(limit ?? DefaultWikiSearchLimit));
    }

    private static Dictionary<string, object?> BuildWikiSearch(string query, string itemType, int limit)
    {
        var progress = SaveManager.Instance?.Progress;
        var saveManager = SaveManager.Instance;
        if (progress == null || saveManager == null)
            return Error("No profile data available.");

        var normalizedItemType = NormalizeWikiItemType(itemType);
        if (normalizedItemType == null)
            return Error("item_type must be one of: all, card, relic.");

        var discoveredCards = progress.DiscoveredCards
            .Select(id => id.Entry)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var discoveredRelics = progress.DiscoveredRelics
            .Select(id => id.Entry)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<WikiCandidate>();
        if (normalizedItemType is "all" or "card")
            candidates.AddRange(BuildCardWikiCandidates(discoveredCards));
        if (normalizedItemType is "all" or "relic")
            candidates.AddRange(BuildRelicWikiCandidates(discoveredRelics));

        var matches = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = ScoreWikiCandidate(query, candidate)
            })
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(match => BuildWikiResult(match.Candidate, match.Score))
            .ToList();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["profile_id"] = saveManager.CurrentProfileId,
            ["query"] = query,
            ["item_type"] = normalizedItemType,
            ["limit"] = limit,
            ["scope"] = "active_profile_discovered_cards_and_relics",
            ["selection_policy"] = "Searches only cards and relics discovered by the active profile, then returns the best fuzzy matches instead of exposing the full catalog.",
            ["counts"] = new Dictionary<string, object?>
            {
                ["discovered_cards"] = discoveredCards.Count,
                ["discovered_relics"] = discoveredRelics.Count,
                ["searched"] = candidates.Count,
                ["returned"] = matches.Count
            },
            ["results"] = matches
        };
    }

    private static int ParseWikiLimit(string? rawLimit)
    {
        if (!int.TryParse(rawLimit, out var limit))
            return DefaultWikiSearchLimit;
        return NormalizeWikiLimit(limit);
    }

    private static int NormalizeWikiLimit(int limit)
        => Math.Clamp(limit, 1, MaxWikiSearchLimit);

    private static string? NormalizeWikiItemType(string? itemType)
    {
        var value = (itemType ?? "all").Trim().ToLowerInvariant();
        return value switch
        {
            "" or "all" or "any" => "all",
            "card" or "cards" => "card",
            "relic" or "relics" => "relic",
            _ => null
        };
    }

    private static IEnumerable<WikiCandidate> BuildCardWikiCandidates(HashSet<string> discoveredIds)
    {
        var byId = new Dictionary<string, WikiCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in GetLoadableTypes(typeof(CardModel).Assembly))
        {
            if (type == null || type.IsAbstract || !typeof(CardModel).IsAssignableFrom(type))
                continue;

            var card = TryCreateModel<CardModel>(type);
            var id = SafeGetText(() => card?.Id.Entry);
            if (card == null || string.IsNullOrWhiteSpace(id) || !discoveredIds.Contains(id))
                continue;

            byId.TryAdd(id, new WikiCandidate(
                Kind: "card",
                Id: id,
                Name: SafeGetText(() => card.Title) ?? id,
                SearchText: BuildWikiSearchText("card", id, SafeGetText(() => card.Title), SafeGetText(() => card.Type), SafeGetText(() => card.Rarity)),
                Card: card,
                Relic: null));
        }

        return byId.Values;
    }

    private static IEnumerable<WikiCandidate> BuildRelicWikiCandidates(HashSet<string> discoveredIds)
    {
        var byId = new Dictionary<string, WikiCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in GetLoadableTypes(typeof(RelicModel).Assembly))
        {
            if (type == null || type.IsAbstract || !typeof(RelicModel).IsAssignableFrom(type))
                continue;

            var relic = TryCreateModel<RelicModel>(type);
            var id = SafeGetText(() => relic?.Id.Entry);
            if (relic == null || string.IsNullOrWhiteSpace(id) || !discoveredIds.Contains(id))
                continue;

            byId.TryAdd(id, new WikiCandidate(
                Kind: "relic",
                Id: id,
                Name: SafeGetText(() => relic.Title) ?? id,
                SearchText: BuildWikiSearchText("relic", id, SafeGetText(() => relic.Title), SafeGetText(() => relic.Rarity)),
                Card: null,
                Relic: relic));
        }

        return byId.Values;
    }

    private static IEnumerable<Type?> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null);
        }
    }

    private static T? TryCreateModel<T>(Type type) where T : class
    {
        try { return Activator.CreateInstance(type, nonPublic: true) as T; }
        catch { return null; }
    }

    private static Dictionary<string, object?> BuildWikiResult(WikiCandidate candidate, double score)
    {
        if (candidate.Card != null)
            return BuildWikiCardResult(candidate, score);
        if (candidate.Relic != null)
            return BuildWikiRelicResult(candidate, score);

        return new Dictionary<string, object?>
        {
            ["item_type"] = candidate.Kind,
            ["id"] = candidate.Id,
            ["name"] = candidate.Name,
            ["score"] = Math.Round(score, 3)
        };
    }

    private static Dictionary<string, object?> BuildWikiCardResult(WikiCandidate candidate, double score)
    {
        var card = candidate.Card!;
        var upgraded = SafeBuildUpgradedCardPreview(card);
        var upgradedVariant = upgraded != null
            ? BuildWikiCardVariant(upgraded, "upgraded")
            : card.IsUpgradable
                ? new Dictionary<string, object?>
                {
                    ["variant"] = "upgraded",
                    ["description"] = SafeGetCardUpgradePreviewDescription(card),
                    ["preview_available"] = false
                }
                : null;

        var result = new Dictionary<string, object?>
        {
            ["item_type"] = "card",
            ["id"] = candidate.Id,
            ["name"] = candidate.Name,
            ["score"] = Math.Round(score, 3),
            ["rarity"] = SafeGetText(() => card.Rarity),
            ["type"] = SafeGetText(() => card.Type),
            ["is_upgradable"] = card.IsUpgradable,
            ["current_upgrade_level"] = card.CurrentUpgradeLevel,
            ["max_upgrade_level"] = card.MaxUpgradeLevel,
            ["base"] = BuildWikiCardVariant(card, "base"),
            ["upgraded"] = upgradedVariant
        };

        return result;
    }

    private static Dictionary<string, object?> BuildWikiCardVariant(CardModel card, string variant)
    {
        return new Dictionary<string, object?>
        {
            ["variant"] = variant,
            ["id"] = SafeGetText(() => card.Id.Entry),
            ["name"] = SafeGetText(() => card.Title),
            ["type"] = SafeGetText(() => card.Type),
            ["rarity"] = SafeGetText(() => card.Rarity),
            ["cost"] = GetCostDisplay(card),
            ["star_cost"] = GetStarCostDisplay(card),
            ["description"] = SafeGetCardDescription(card),
            ["is_upgraded"] = card.IsUpgraded,
            ["is_upgradable"] = card.IsUpgradable,
            ["current_upgrade_level"] = card.CurrentUpgradeLevel,
            ["max_upgrade_level"] = card.MaxUpgradeLevel,
            ["keywords"] = BuildHoverTips(card.HoverTips)
        };
    }

    private static Dictionary<string, object?> BuildWikiRelicResult(WikiCandidate candidate, double score)
    {
        var relic = candidate.Relic!;
        return new Dictionary<string, object?>
        {
            ["item_type"] = "relic",
            ["id"] = candidate.Id,
            ["name"] = candidate.Name,
            ["score"] = Math.Round(score, 3),
            ["rarity"] = SafeGetText(() => relic.Rarity),
            ["description"] = SafeGetText(() => relic.DynamicDescription),
            ["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic)
        };
    }

    private static string BuildWikiSearchText(params string?[] values)
        => string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static double ScoreWikiCandidate(string query, WikiCandidate candidate)
    {
        var queryNormalized = NormalizeSearchText(query);
        var candidateNormalized = NormalizeSearchText(candidate.SearchText);
        if (string.IsNullOrWhiteSpace(queryNormalized) || string.IsNullOrWhiteSpace(candidateNormalized))
            return 0;

        var queryCompact = CompactSearchText(queryNormalized);
        var candidateCompact = CompactSearchText(candidateNormalized);
        var nameCompact = CompactSearchText(candidate.Name);
        var idCompact = CompactSearchText(candidate.Id);

        if (queryCompact.Equals(nameCompact, StringComparison.OrdinalIgnoreCase)
            || queryCompact.Equals(idCompact, StringComparison.OrdinalIgnoreCase))
            return 1000;

        if (nameCompact.Contains(queryCompact, StringComparison.OrdinalIgnoreCase)
            || idCompact.Contains(queryCompact, StringComparison.OrdinalIgnoreCase))
            return 850;

        var queryTokens = TokenizeSearchText(queryNormalized);
        var candidateTokens = TokenizeSearchText(candidateNormalized);
        if (queryTokens.Count == 0 || candidateTokens.Count == 0)
            return 0;

        double total = 0;
        var strongMatches = 0;
        foreach (var queryToken in queryTokens)
        {
            var best = candidateTokens.Max(candidateToken => TokenSimilarity(queryToken, candidateToken));
            total += best;
            if (best >= 0.72)
                strongMatches++;
        }

        var average = total / queryTokens.Count;
        var score = average * 700;
        if (strongMatches > 0)
            score += strongMatches * 35;

        return score >= 300 ? score : 0;
    }

    private static string NormalizeSearchText(string value)
    {
        var sb = new StringBuilder(value.Length);
        var lastWasSpace = true;
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        return sb.ToString().Trim();
    }

    private static string CompactSearchText(string value)
        => NormalizeSearchText(value).Replace(" ", "", StringComparison.Ordinal);

    private static List<string> TokenizeSearchText(string value)
        => NormalizeSearchText(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static double TokenSimilarity(string queryToken, string candidateToken)
    {
        if (queryToken.Equals(candidateToken, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        if (candidateToken.Contains(queryToken, StringComparison.OrdinalIgnoreCase)
            || queryToken.Contains(candidateToken, StringComparison.OrdinalIgnoreCase))
            return 0.88;

        var distance = LevenshteinDistance(queryToken, candidateToken);
        var maxLength = Math.Max(queryToken.Length, candidateToken.Length);
        return maxLength == 0 ? 0 : 1.0 - ((double)distance / maxLength);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private sealed record WikiCandidate(
        string Kind,
        string Id,
        string Name,
        string SearchText,
        CardModel? Card,
        RelicModel? Relic);
}

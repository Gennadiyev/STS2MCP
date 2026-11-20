using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Localization;

namespace Sts2Localization;

internal static class GameNameLocalization
{
    internal const string SimplifiedChinese = "zhs";
    private static readonly Dictionary<(string Language, string Table), Dictionary<string, string>?> Tables = new();

    private static string? TableFor(string kind) => kind switch
    {
        "act" => "acts",
        "affliction" => "afflictions",
        "card" => "cards",
        "character" => "characters",
        "enchantment" => "enchantments",
        "encounter" => "encounters",
        "enemy" => "monsters",
        "event" => "events",
        "orb" => "orbs",
        "potion" => "potions",
        "power" => "powers",
        "relic" => "relics",
        _ => null
    };

    internal static bool Supports(string kind) => TableFor(kind) != null;

    internal static string? Find(LocString? text, string language = SimplifiedChinese) =>
        text is { } loc ? Read(loc.LocTable, loc.LocEntryKey, language) : null;

    internal static string? Find(string kind, string id)
    {
        var table = TableFor(kind);
        if (table == null)
            return null;

        string entry = id[(id.LastIndexOf('.') + 1)..].ToUpperInvariant();
        if (kind == "enemy")
        {
            var name = Read(table, $"{entry}.name");
            int suffix = entry.LastIndexOf('_');
            return name ?? (suffix > 0 && int.TryParse(entry[(suffix + 1)..], out _)
                ? Read(table, $"{entry[..suffix]}.name")
                : null);
        }

        string key = $"{entry}.title";
        return Read(table, key) ?? (kind == "event" ? Read("ancients", key) : null);
    }

    private static string? Read(string table, string key, string language = SimplifiedChinese)
    {
        if (!Tables.TryGetValue((language, table), out var names))
        {
            string path = $"res://localization/{language}/{table}.json";
            if (Godot.FileAccess.FileExists(path))
            {
                try { names = JsonSerializer.Deserialize<Dictionary<string, string>>(Godot.FileAccess.GetFileAsString(path)); }
                catch (JsonException) { }
            }
            Tables[(language, table)] = names;
        }

        return names?.TryGetValue(key, out var title) == true && !string.IsNullOrWhiteSpace(title)
            ? title
            : null;
    }
}

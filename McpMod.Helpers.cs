using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private static string? SafeGetCardDescription(CardModel card, PileType pile = PileType.Hand)
    {
        try { return StripRichTextTags(card.GetDescriptionForPile(pile)).Replace("\n", " "); }
        catch { return SafeGetText(() => card.Description)?.Replace("\n", " "); }
    }

    internal static string? SafeGetText(Func<object?> getter)
    {
        try
        {
            var result = getter();
            if (result == null) return null;
            // If it's a LocString, call GetFormattedText
            if (result is MegaCrit.Sts2.Core.Localization.LocString locString)
                return StripRichTextTags(locString.GetFormattedText());
            return result.ToString();
        }
        catch { return null; }
    }

    internal static string StripRichTextTags(string text)
    {
        // Remove BBCode-style tags like [color=red], [/color], etc.
        // Special case: [img]res://path/to/file.png[/img] → [file.png]
        var sb = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                // Check for [img]...[/img] pattern
                if (text.AsSpan(i).StartsWith("[img]"))
                {
                    int contentStart = i + 5; // length of "[img]"
                    int closeTag = text.IndexOf("[/img]", contentStart, StringComparison.Ordinal);
                    if (closeTag >= 0)
                    {
                        string path = text[contentStart..closeTag];
                        int lastSlash = path.LastIndexOf('/');
                        string filename = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
                        sb.Append('[').Append(filename).Append(']');
                        i = closeTag + 6; // length of "[/img]"
                        continue;
                    }
                }

                int end = text.IndexOf(']', i);
                if (end >= 0) { i = end + 1; continue; }
            }
            sb.Append(text[i]);
            i++;
        }
        return sb.ToString();
    }

    internal static void SendJson(HttpListenerResponse response, object data)
    {
        string json = JsonSerializer.Serialize(data, _jsonOptions);
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    internal static void SendText(HttpListenerResponse response, string text, string contentType = "text/plain")
    {
        byte[] buffer = Encoding.UTF8.GetBytes(text);
        response.ContentType = $"{contentType}; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    internal static void SendError(HttpListenerResponse response, int statusCode, string message)
    {
        response.StatusCode = statusCode;
        SendJson(response, new Dictionary<string, object?> { ["error"] = message });
    }

    private static Dictionary<string, object?> Error(string message)
    {
        return new Dictionary<string, object?> { ["status"] = "error", ["error"] = message };
    }

    private static object? GetInstanceFieldValue(object source, string fieldName)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly;

        for (var type = source.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(fieldName, Flags);
            if (field != null)
                return field.GetValue(source);
        }

        return null;
    }

    private static void AddMenuOptionIfVisible(
        List<Dictionary<string, object?>> options,
        object owner,
        string fieldName,
        string label)
    {
        try
        {
            var btn = GetInstanceFieldValue(owner, fieldName);
            if (btn is Control ctrl && IsNodeVisible(ctrl))
            {
                var isEnabled = btn.GetType().GetProperty("IsEnabled")?.GetValue(btn) as bool?;
                options.Add(new Dictionary<string, object?>
                {
                    ["name"] = label,
                    ["enabled"] = isEnabled ?? true
                });
            }
        }
        catch { }
    }

    internal static List<T> FindAll<T>(Node start) where T : Node
    {
        var list = new List<T>();
        if (IsLiveNode(start))
            FindAllRecursive(start, list);
        return list;
    }

    /// <summary>
    /// FindAll variant that sorts results by visual position (row-major: top-to-bottom, left-to-right).
    /// NGridCardHolder.OnFocus() calls MoveToFront() which scrambles child order for z-rendering.
    /// Sorting by GlobalPosition restores the correct visual order for both single-row (card rewards,
    /// choose-a-card) and multi-row (deck selection grids) layouts.
    /// </summary>
    internal static List<T> FindAllSortedByPosition<T>(Node start) where T : Control
    {
        var list = FindAll<T>(start);
        list.Sort((a, b) =>
        {
            int cmp = a.GlobalPosition.Y.CompareTo(b.GlobalPosition.Y);
            return cmp != 0 ? cmp : a.GlobalPosition.X.CompareTo(b.GlobalPosition.X);
        });
        return list;
    }

    private static void FindAllRecursive<T>(Node node, List<T> found) where T : Node
    {
        if (!IsLiveNode(node))
            return;
        if (node is T item)
            found.Add(item);
        try
        {
            foreach (var child in node.GetChildren())
                FindAllRecursive(child, found);
        }
        catch (ObjectDisposedException) { }
    }

    private static List<Dictionary<string, object?>> BuildHoverTips(IEnumerable<IHoverTip> tips)
    {
        var result = new List<Dictionary<string, object?>>();
        try
        {
            var seen = new HashSet<string>();
            foreach (var tip in IHoverTip.RemoveDupes(tips))
            {
                try
                {
                    string? title = null;
                    string? description = null;

                    if (tip is HoverTip ht)
                    {
                        title = ht.Title != null ? StripRichTextTags(ht.Title) : null;
                        description = StripRichTextTags(ht.Description);
                    }
                    else if (tip is CardHoverTip cardTip)
                    {
                        title = SafeGetText(() => cardTip.Card.Title);
                        description = SafeGetCardDescription(cardTip.Card);
                    }

                    if (title == null && description == null) continue;

                    string key = title ?? description!;
                    if (!seen.Add(key)) continue;

                    result.Add(new Dictionary<string, object?>
                    {
                        ["name"] = title,
                        ["description"] = description
                    });
                }
                catch { /* skip individual tip on error */ }
            }
        }
        catch { /* return partial results */ }
        return result;
    }

    internal static T? FindFirst<T>(Node start) where T : Node
    {
        if (!IsLiveNode(start))
            return null;
        if (start is T result)
            return result;
        Godot.Collections.Array<Node> children;
        try
        {
            children = start.GetChildren();
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        foreach (var child in children)
        {
            var val = FindFirst<T>(child);
            if (val != null) return val;
        }
        return null;
    }

    internal static bool IsLiveNode(Node? node)
    {
        try
        {
            return node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    internal static bool IsNodeVisible(CanvasItem? node)
    {
        try
        {
            return node != null && IsLiveNode(node) && node.Visible && node.IsVisibleInTree();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}

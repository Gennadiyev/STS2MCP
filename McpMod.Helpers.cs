using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

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

    private static bool IsMapScreenOpenOrVisible()
    {
        var mapScreen = NMapScreen.Instance;
        return mapScreen != null && (mapScreen.IsOpen || IsNodeVisible(mapScreen));
    }

    private static bool IsControlVisibleInTree(NClickableControl? control)
    {
        try
        {
            return control != null &&
                   IsLiveNode(control) &&
                   IsNodeVisible(control);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static Node? GetOpenModalNode()
    {
        try
        {
            return NModalContainer.Instance?.OpenModal as Node;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private static bool IsDescendantOf(Node? node, Node ancestor)
    {
        try
        {
            for (var current = node; current != null && IsLiveNode(current); current = current.GetParent())
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
            }
        }
        catch (ObjectDisposedException) { }

        return false;
    }

    private static bool IsControlVisibleInOpenModal(NClickableControl? control, Node? openModal = null)
    {
        openModal ??= GetOpenModalNode();
        if (control == null || openModal == null || !IsLiveNode(control) || !IsLiveNode(openModal))
            return false;

        try
        {
            return control.Visible && IsDescendantOf(control, openModal);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool IsPopupButtonActionable(NClickableControl? control)
    {
        if (control == null || !IsLiveNode(control))
            return false;

        try
        {
            return control.IsEnabled &&
                   (IsControlVisibleInTree(control) || IsControlVisibleInOpenModal(control));
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool IsControlVisibleOrActionable(NClickableControl? control)
    {
        return IsControlVisibleInTree(control) && control!.IsEnabled;
    }

    private static bool IsFtueNodeActive(Node node)
    {
        if (node is not CanvasItem canvas || !IsLiveNode(node))
            return false;

        if (IsNodeVisible(canvas))
            return true;

        if (ReferenceEquals(GetOpenModalNode(), node))
            return true;

        try
        {
            return GetInstanceFieldValue(node, "_confirmButton") is NClickableControl confirmButton &&
                   (IsControlVisibleInTree(confirmButton) || IsControlVisibleInOpenModal(confirmButton));
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static Dictionary<string, object?>? BuildVisibleFtueState(Node root)
    {
        var tutorialFtue = FindVisibleAcceptTutorialsFtue(root);
        if (tutorialFtue != null && IsFtueNodeActive(tutorialFtue))
        {
            return new Dictionary<string, object?>
            {
                ["state_type"] = "menu",
                ["menu_screen"] = "tutorial_prompt",
                ["message"] = "Enable Tutorials? Choose yes or no.",
                ["options"] = new List<Dictionary<string, object?>>
                {
                    new() { ["name"] = "no", ["enabled"] = true },
                    new() { ["name"] = "yes", ["enabled"] = true }
                }
            };
        }

        var ftue = FindVisibleGenericFtue(root);
        if (ftue != null)
        {
            var canAdvance = FindFtueAdvanceButton(ftue) != null;
            return new Dictionary<string, object?>
            {
                ["state_type"] = "menu",
                ["menu_screen"] = "tutorial",
                ["message"] = "Tutorial popup active. Use advance to dismiss.",
                ["options"] = new List<Dictionary<string, object?>>
                {
                    new() { ["name"] = "advance", ["enabled"] = canAdvance },
                    new() { ["name"] = "proceed", ["enabled"] = canAdvance }
                }
            };
        }

        var popup = BuildVisiblePopupState(root);
        if (popup != null)
            return popup;

        return null;
    }

    private static Dictionary<string, object?>? BuildVisiblePopupState(Node root)
    {
        var popup = FindVisibleVerticalPopup(root);
        var options = popup != null
            ? GetPopupOptions(popup)
            : GetVisiblePopupButtonOptions(root);

        var stateOptions = new List<Dictionary<string, object?>>();
        foreach (var option in options)
        {
            stateOptions.Add(new Dictionary<string, object?>
            {
                ["name"] = option.Name,
                ["enabled"] = option.Button.IsEnabled
            });
        }

        if (stateOptions.Count == 0)
            return null;

        return new Dictionary<string, object?>
        {
            ["state_type"] = "menu",
            ["menu_screen"] = "popup",
            ["message"] = popup != null ? GetVerticalPopupText(popup, "TitleLabel") ?? "Popup active." : "Popup active.",
            ["body"] = popup != null ? GetVerticalPopupText(popup, "BodyLabel") : null,
            ["options"] = stateOptions
        };
    }

    private static MegaCrit.Sts2.Core.Nodes.Ftue.NAcceptTutorialsFtue? FindVisibleAcceptTutorialsFtue(Node root)
    {
        var openModal = GetOpenModalNode();
        if (openModal is MegaCrit.Sts2.Core.Nodes.Ftue.NAcceptTutorialsFtue openFtue &&
            IsFtueNodeActive(openFtue))
        {
            return openFtue;
        }

        foreach (var ftue in FindAll<MegaCrit.Sts2.Core.Nodes.Ftue.NAcceptTutorialsFtue>(root))
        {
            if (IsFtueNodeActive(ftue) || ReferenceEquals(openModal, ftue))
                return ftue;
        }
        return null;
    }

    private static bool IsAnyFtueVisible(Node root)
    {
        return BuildVisibleFtueState(root) != null;
    }

    private static NVerticalPopup? FindVisibleVerticalPopup(Node root)
    {
        var openModal = GetOpenModalNode();
        if (openModal is NVerticalPopup openPopup && GetPopupOptions(openPopup).Count > 0)
            return openPopup;

        if (openModal != null)
        {
            foreach (var popup in FindAll<NVerticalPopup>(openModal))
            {
                if (GetPopupOptions(popup).Count > 0)
                    return popup;
            }
        }

        foreach (var popup in FindAll<NVerticalPopup>(root))
        {
            if ((IsNodeVisible(popup) || (openModal != null && IsDescendantOf(popup, openModal))) &&
                GetPopupOptions(popup).Count > 0)
            {
                return popup;
            }
        }
        return null;
    }

    private static List<(string Name, NClickableControl Button)> GetPopupOptions(NVerticalPopup popup)
    {
        var options = new List<(string Name, NClickableControl Button)>();
        AddPopupOption(options, popup.YesButton, "yes");
        AddPopupOption(options, popup.NoButton, "no");
        return options;
    }

    private static List<(string Name, NClickableControl Button)> GetVisiblePopupButtonOptions(Node root)
    {
        var options = new List<(string Name, NClickableControl Button)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var openModal = GetOpenModalNode();
        if (openModal != null)
        {
            foreach (var button in FindAll<NClickableControl>(openModal))
                AddVisiblePopupButtonOption(options, seen, button, null, openModal);
        }

        foreach (var button in FindAll<NPopupYesNoButton>(root))
        {
            AddVisiblePopupButtonOption(options, seen, button, button.IsYes ? "yes" : "no", null);
        }

        foreach (var modal in FindAll<NModalContainer>(root))
        {
            if (!IsNodeVisible(modal))
                continue;

            foreach (var button in FindAll<NClickableControl>(modal))
                AddVisiblePopupButtonOption(options, seen, button, null, null);
        }

        return options;
    }

    private static void AddVisiblePopupButtonOption(
        List<(string Name, NClickableControl Button)> options,
        HashSet<string> seen,
        NClickableControl? button,
        string? fallback,
        Node? openModal)
    {
        if (!(IsControlVisibleInTree(button) || IsControlVisibleInOpenModal(button, openModal)))
            return;

        var label = button is NPopupYesNoButton popupButton
            ? GetPopupButtonLabel(popupButton)
            : GetClickableControlLabel(button!);
        var name = NormalizeMenuOptionName(label) ?? fallback;
        if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            return;

        options.Add((name, button!));
    }

    private static void AddPopupOption(
        List<(string Name, NClickableControl Button)> options,
        NPopupYesNoButton? button,
        string fallback)
    {
        if (!IsPopupButtonActionable(button))
            return;

        var label = GetPopupButtonLabel(button!);
        var name = NormalizeMenuOptionName(label) ?? fallback;
        options.Add((name, button!));
    }

    private static string? GetVerticalPopupText(NVerticalPopup popup, string propertyName)
    {
        var label = popup.GetType().GetProperty(propertyName)?.GetValue(popup);
        return GetNodeText(label);
    }

    private static string? GetPopupButtonLabel(NPopupYesNoButton button)
    {
        var label = GetInstanceFieldValue(button, "_label");
        return GetNodeText(label);
    }

    private static string? GetClickableControlLabel(NClickableControl button)
    {
        foreach (var fieldName in new[] { "_label", "_textLabel", "_title", "_buttonLabel" })
        {
            var label = GetNodeText(GetInstanceFieldValue(button, fieldName));
            if (!string.IsNullOrWhiteSpace(label))
                return label;
        }

        foreach (var propName in new[] { "Text", "Label", "Title" })
        {
            var label = SafeGetText(() => button.GetType().GetProperty(propName)?.GetValue(button));
            if (!string.IsNullOrWhiteSpace(label))
                return label;
        }

        var childText = FindNodeTextRecursive(button);
        if (!string.IsNullOrWhiteSpace(childText))
            return childText;

        var nodeName = button.Name.ToString();
        if (!string.IsNullOrWhiteSpace(nodeName))
            return nodeName.EndsWith("Button", StringComparison.OrdinalIgnoreCase)
                ? nodeName[..^"Button".Length]
                : nodeName;

        return null;
    }

    private static string? FindNodeTextRecursive(Node start)
    {
        if (!IsLiveNode(start))
            return null;

        var text = GetNodeText(start);
        if (!string.IsNullOrWhiteSpace(text))
            return text;

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
            text = FindNodeTextRecursive(child);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static string? GetNodeText(object? node)
    {
        if (node == null)
            return null;

        foreach (var propName in new[] { "Text", "BbcodeText" })
        {
            var text = SafeGetText(() => node.GetType().GetProperty(propName)?.GetValue(node));
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static string? NormalizeMenuOptionName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var sb = new StringBuilder();
        foreach (var ch in StripRichTextTags(text).Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
        }

        var normalized = sb.ToString().Trim('_');
        return normalized.Length == 0 ? null : normalized;
    }

    private static Node? FindVisibleGenericFtue(Node start)
    {
        if (ReferenceEquals(start, (Godot.Engine.GetMainLoop() as SceneTree)?.Root))
        {
            var openModal = GetOpenModalNode();
            if (openModal != null)
            {
                var openFtue = FindVisibleGenericFtue(openModal);
                if (openFtue != null)
                    return openFtue;
            }
        }

        if (!IsLiveNode(start))
            return null;

        var typeName = start.GetType().FullName ?? "";
        if (typeName.StartsWith("MegaCrit.Sts2.Core.Nodes.Ftue.", StringComparison.Ordinal) &&
            !typeName.EndsWith(".NAcceptTutorialsFtue", StringComparison.Ordinal) &&
            IsFtueNodeActive(start))
        {
            return start;
        }

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
            var val = FindVisibleGenericFtue(child);
            if (val != null)
                return val;
        }

        return null;
    }

    private static NClickableControl? FindFtueAdvanceButton(Node ftue)
    {
        foreach (var fieldName in new[]
        {
            "_confirmButton",
            "_advanceButton",
            "_nextButton",
            "_proceedButton",
            "_acknowledgeButton",
            "_arrowButton",
            "_rightArrowButton",
            "_rightButton"
        })
        {
            if (GetInstanceFieldValue(ftue, fieldName) is NClickableControl fieldButton &&
                IsPopupButtonActionable(fieldButton))
            {
                return fieldButton;
            }
        }

        try
        {
            foreach (var field in ftue.GetType().GetFields(
                         System.Reflection.BindingFlags.Public |
                         System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.Instance))
            {
                if (field.GetValue(ftue) is NClickableControl fieldButton &&
                    IsPopupButtonActionable(fieldButton))
                {
                    return fieldButton;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        foreach (var button in FindAll<NClickableControl>(ftue))
        {
            if (IsPopupButtonActionable(button))
                return button;
        }

        return null;
    }
}

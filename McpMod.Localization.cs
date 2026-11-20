using System.Collections.Generic;
using System.Linq;
using System.Net;
using MegaCrit.Sts2.Core.Combat;
using Sts2Localization;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void HandleGetChineseNames(HttpListenerRequest request, HttpListenerResponse response)
    {
        var entries = request.QueryString.GetValues("entry");
        if (entries is not { Length: >= 1 and <= 24 })
        {
            SendError(response, 400, "Provide 1–24 entry parameters as kind:id.");
            return;
        }

        var references = new List<(string Kind, string Id)>();
        foreach (string entry in entries)
        {
            int separator = entry.IndexOf(':');
            if (separator <= 0)
            {
                SendError(response, 400, "Provide each entry as kind:id.");
                return;
            }
            string kind = entry[..separator];
            string id = entry[(separator + 1)..].Trim();
            if (!GameNameLocalization.Supports(kind) || id.Length is < 1 or > 100)
            {
                SendError(response, 400, "Each entry needs a supported kind and a 1–100 character game ID.");
                return;
            }
            references.Add((kind, id));
        }

        var result = RunOnMainThread(() => references.Select(reference =>
        {
            var (kind, id) = reference;
            var target = kind == "enemy" && CombatManager.Instance.DebugOnlyGetState() is { } combat
                ? ResolveTarget(combat, id)
                : null;
            return new
            {
                kind,
                id,
                name = GameNameLocalization.Find(target?.Monster?.Title) ?? GameNameLocalization.Find(kind, id)
            };
        }).ToArray()).GetAwaiter().GetResult();
        SendJson(response, new { status = "ok", language = GameNameLocalization.SimplifiedChinese, entries = result });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Func<string, string, string>? _dispatchCoordination;

    private static Func<string, string, string>? FindCoordinationBridge()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => candidate.GetName().Name == "STS2_Coordination");
        return assembly?.GetType("Sts2Coordination.CoordinationMod")
            ?.GetMethod("Dispatch", new[] { typeof(string), typeof(string) })
            ?.CreateDelegate<Func<string, string, string>>();
    }

    private static Dictionary<string, object?> ExecuteCoordination(string action, Dictionary<string, JsonElement> data)
    {
        if (action != "coordination_prompt")
            return CallCoordination(action, data);
        if (!data.TryGetValue("options", out var input) || input.ValueKind != JsonValueKind.Array ||
            input.GetArrayLength() is < 2 or > 4)
            return Error("Provide 2–4 coordination choices.");

        var combat = CombatManager.Instance.DebugOnlyGetState();
        var options = new List<object>();
        foreach (var option in input.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object ||
                (option.TryGetProperty("label", out var label) &&
                    label.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)))
                return Error("A coordination choice label must be text when supplied.");
            var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(option.GetRawText())!;
            if (option.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String &&
                kind.GetString() == "focus_enemy")
            {
                if (combat == null || !option.TryGetProperty("target", out var target) ||
                    target.ValueKind != JsonValueKind.String ||
                    ResolveTarget(combat, target.GetString()!)?.CombatId is not uint id)
                    return Error("Each focus_enemy choice needs a live enemy target.");
                fields["combat_target_id"] = JsonSerializer.SerializeToElement(id);
            }
            else
                fields.Remove("combat_target_id");
            options.Add(fields);
        }

        data["options"] = JsonSerializer.SerializeToElement(options, _compactJsonOptions);
        return CallCoordination(action, data);
    }

    private static Dictionary<string, object?>? ValidateCoordinationAction(Dictionary<string, JsonElement> data, JsonElement promptId)
    {
        if (promptId.ValueKind != JsonValueKind.String || !Guid.TryParseExact(promptId.GetString(), "N", out _))
            return Error("Invalid coordination_prompt_id.");
        uint? combatTargetId = null;
        if (data.TryGetValue("target", out var target) && target.ValueKind == JsonValueKind.String &&
            CombatManager.Instance.DebugOnlyGetState() is { } combat)
            combatTargetId = ResolveTarget(combat, target.GetString()!)?.CombatId;
        var result = CallCoordination("coordination_validate", new
        {
            prompt_id = promptId.GetString(),
            combat_target_id = combatTargetId
        });
        return result.GetValueOrDefault("status") is JsonElement { ValueKind: JsonValueKind.String } status &&
            status.GetString() == "ok" ? null : result;
    }

    private static Dictionary<string, object?> CallCoordination(string action, object data)
    {
        _dispatchCoordination ??= FindCoordinationBridge();
        if (_dispatchCoordination == null)
            return BlockedAction(new("coordination_mod_unavailable", "Enable the STS2 Co-op Coordination companion on each seat.", false));
        string json = _dispatchCoordination(action, JsonSerializer.Serialize(data, _compactJsonOptions));
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, _compactJsonOptions)
            ?? Error("The coordination companion returned no result.");
    }
}

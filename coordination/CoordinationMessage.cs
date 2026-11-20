using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace Sts2Coordination;

public struct CoordinationMessage : INetMessage, IPacketSerializable
{
    internal const int MaxLength = 4096;
    internal string Json;

    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public void Serialize(PacketWriter writer) => writer.WriteString(Json);
    public void Deserialize(PacketReader reader) => Json = reader.ReadString();
}

internal sealed record Choice(string? Label, string Kind = "choice", string? Target = null, uint? CombatTargetId = null)
{
    internal bool IsValid => CoordinationMod.IsText(Label, 40) && (Kind switch
    {
        "choice" or "agent_first" or "teammate_first" or "no_preference" =>
            Target == null && CombatTargetId == null,
        "focus_enemy" => CoordinationMod.IsText(Target, 100) && CombatTargetId != null,
        _ => false
    });
}

internal sealed record CombatScope(int Act, int Floor, int Round, int Turn);

internal sealed record Question(string Text, Choice[] Options, bool AllowText, int LifetimeSeconds, CombatScope Scope)
{
    internal bool IsValid => CoordinationMod.IsText(Text, 140) && LifetimeSeconds is >= 5 and <= 60 &&
        Scope != null && Options is { Length: >= 2 and <= 4 } &&
        Options.All(option => option?.IsValid == true);
}

internal sealed record Reply(int? ChoiceIndex = null, string? Text = null, bool GoAhead = false, int? CompletedPlays = null)
{
    internal bool IsValid => ((ChoiceIndex.HasValue ? 1 : 0) + (Text != null ? 1 : 0) + (GoAhead ? 1 : 0)) == 1 &&
        (ChoiceIndex == null || ChoiceIndex is >= 0 and < 4) &&
        (CompletedPlays == null || (ChoiceIndex != null && CompletedPlays >= 0)) &&
        (Text == null || CoordinationMod.IsText(Text, 200));
}

internal sealed record Envelope(string Kind, ulong To, ulong From = 0, string? PromptId = null,
    Question? Question = null, Reply? Reply = null, string? Text = null, string? Language = null)
{
    internal bool IsValid => To != From && (Kind switch
    {
        "message" => CoordinationMod.IsText(Text, 200),
        "prompt" => HasPromptId && Question?.IsValid == true,
        "reply" => HasPromptId && Reply?.IsValid == true,
        "cancel" or "complete" => HasPromptId,
        "locale_hello" or "locale" => Language is { Length: 3 } && Language.All(char.IsAsciiLetterLower),
        _ => false
    });

    private bool HasPromptId => Guid.TryParseExact(PromptId, "N", out _);
}

using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared.Magic.Events;

public sealed partial class TransformSpellEvent : EntityTargetActionEvent
{
    /// <summary>
    /// Components added or removed from the target
    /// </summary>
    [DataField]
    [AlwaysPushInheritance]
    public ComponentRegistry ToAdd = new();

    [DataField]
    [AlwaysPushInheritance]
    public HashSet<string> ToRemove = new();

    /// <summary>
    /// Outfit forced onto the target
    /// </summary>
    [DataField]
    public string Loadout;

    /// <summary>
    /// Whether our event was cancelled or not by invalid targeting etc
    /// </summary>
    public bool Cancelled = false;

    [DataField]
    public string FailureMessage = "spell-target-immune-transformed";
}

using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared.Magic.Events;

public sealed partial class ChangeStatusEffectEvent : EntityTargetActionEvent
{
    [DataField]
    public string StatusEffect;

    [DataField]
    public float Duration = 60;
}

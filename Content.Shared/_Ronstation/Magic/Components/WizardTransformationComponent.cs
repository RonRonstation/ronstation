using Robust.Shared.GameStates;

namespace Content.Shared.Magic.Components;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedMagicSystem))]
public sealed partial class WizardTransformationComponent : Component;

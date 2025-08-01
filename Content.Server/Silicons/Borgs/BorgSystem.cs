// Modifications ported by Ronstation from CorvaxNext, therefore this file is licensed as MIT sublicensed with AGPL-v3.0.
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Actions;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Hands.Systems;
using Content.Server.PowerCell;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared.Alert;
using Content.Shared.Body.Events;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Pointing;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Roles;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StationAi;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;
using Content.Shared.Wires;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Silicons.Borgs;

/// <inheritdoc/>
public sealed partial class BorgSystem : SharedBorgSystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IBanManager _banManager = default!;
    [Dependency] private readonly IConfigurationManager _cfgManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;

    public static readonly ProtoId<JobPrototype> BorgJobId = "Borg";

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BorgChassisComponent, AfterInteractUsingEvent>(OnChassisInteractUsing);
        SubscribeLocalEvent<BorgChassisComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<BorgChassisComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<BorgChassisComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<BorgChassisComponent, BeingGibbedEvent>(OnBeingGibbed);
        SubscribeLocalEvent<BorgChassisComponent, PowerCellChangedEvent>(OnPowerCellChanged);
        SubscribeLocalEvent<BorgChassisComponent, PowerCellSlotEmptyEvent>(OnPowerCellSlotEmpty);
        SubscribeLocalEvent<BorgChassisComponent, GetCharactedDeadIcEvent>(OnGetDeadIC);
        SubscribeLocalEvent<BorgChassisComponent, GetCharacterUnrevivableIcEvent>(OnGetUnrevivableIC);
        SubscribeLocalEvent<BorgChassisComponent, ItemToggledEvent>(OnToggled);

        SubscribeLocalEvent<BorgBrainComponent, MindAddedMessage>(OnBrainMindAdded);
        SubscribeLocalEvent<BorgBrainComponent, PointAttemptEvent>(OnBrainPointAttempt);

        InitializeModules();
        InitializeMMI();
        InitializeUI();
        InitializeTransponder();
    }

    private void OnMapInit(EntityUid uid, BorgChassisComponent component, MapInitEvent args)
    {
        UpdateBatteryAlert((uid, component));
        _movementSpeedModifier.RefreshMovementSpeedModifiers(uid);
    }

    private void OnChassisInteractUsing(EntityUid uid, BorgChassisComponent component, AfterInteractUsingEvent args)
    {
        if (!args.CanReach || args.Handled || uid == args.User)
            return;

        var used = args.Used;
        TryComp<BorgBrainComponent>(used, out var brain);
        TryComp<BorgModuleComponent>(used, out var module);
        TryComp<AiRemoteBrainComponent>(used, out var aiBrain); // Corvax-Next-AiRemoteControl

        if (TryComp<WiresPanelComponent>(uid, out var panel) && !panel.Open)
        {
            if (brain != null || module != null)
            {
                Popup.PopupEntity(Loc.GetString("borg-panel-not-open"), uid, args.User);
            }
            return;
        }

        if (component.BrainEntity == null && brain != null &&
            _whitelistSystem.IsWhitelistPassOrNull(component.BrainWhitelist, used))
        {
            if (_mind.TryGetMind(used, out _, out var mind) &&
                _player.TryGetSessionById(mind.UserId, out var session))
            {
                if (!CanPlayerBeBorged(session))
                {
                    Popup.PopupEntity(Loc.GetString("borg-player-not-allowed"), used, args.User);
                    return;
                }
            }

            _container.Insert(used, component.BrainContainer);
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(args.User):player} installed brain {ToPrettyString(used)} into borg {ToPrettyString(uid)}");
            args.Handled = true;
            UpdateUI(uid, component);
        }

        if (module != null && CanInsertModule(uid, used, component, module, args.User))
        {
            InsertModule((uid, component), used);
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(args.User):player} installed module {ToPrettyString(used)} into borg {ToPrettyString(uid)}");
            args.Handled = true;
            UpdateUI(uid, component);
        }

        // Corvax-Next-AiRemoteControl-Start
        if (component.BrainEntity == null && aiBrain != null &&
    _whitelistSystem.IsWhitelistPassOrNull(component.BrainWhitelist, used))
        {
            EnsureComp<AiRemoteControllerComponent>(uid);
            _container.Insert(used, component.BrainContainer);
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(args.User):player} installed ai remote brain {ToPrettyString(used)} into borg {ToPrettyString(uid)}");
            args.Handled = true;
            BorgActivate(uid, component);

            UpdateUI(uid, component);
        }
        // Corvax-Next-AiRemoteControl-End
    }

    /// <summary>
    /// Inserts a new module into a borg, the same as if a player inserted it manually.
    /// </summary>
    /// <para>
    /// This does not run checks to see if the borg is actually allowed to be inserted, such as whitelists.
    /// </para>
    /// <param name="ent">The borg to insert into.</param>
    /// <param name="module">The module to insert.</param>
    public void InsertModule(Entity<BorgChassisComponent> ent, EntityUid module)
    {
        _container.Insert(module, ent.Comp.ModuleContainer);
    }

    // todo: consider transferring over the ghost role? managing that might suck.
    protected override void OnInserted(EntityUid uid, BorgChassisComponent component, EntInsertedIntoContainerMessage args)
    {
        base.OnInserted(uid, component, args);

        if (HasComp<BorgBrainComponent>(args.Entity) && _mind.TryGetMind(args.Entity, out var mindId, out var mind) && args.Container == component.BrainContainer)
        {
            _mind.TransferTo(mindId, uid, mind: mind);
        }
    }

    protected override void OnRemoved(EntityUid uid, BorgChassisComponent component, EntRemovedFromContainerMessage args)
    {
        base.OnRemoved(uid, component, args);

        if (HasComp<BorgBrainComponent>(args.Entity) && _mind.TryGetMind(uid, out var mindId, out var mind) && args.Container == component.BrainContainer)
        {
            _mind.TransferTo(mindId, args.Entity, mind: mind);
        }

        // Corvax-Next-AiRemoteControl-Start
        if (HasComp<AiRemoteBrainComponent>(args.Entity))
        {
            BorgDeactivate(uid, component);
            RemComp<AiRemoteControllerComponent>(uid);
            RemComp<StationAiVisionComponent>(uid);
        }
        // Corvax-Next-AiRemoteControl-End
    }

    private void OnMindAdded(EntityUid uid, BorgChassisComponent component, MindAddedMessage args)
    {
        BorgActivate(uid, component);
    }

    private void OnMindRemoved(EntityUid uid, BorgChassisComponent component, MindRemovedMessage args)
    {
        BorgDeactivate(uid, component);
    }

    private void OnMobStateChanged(EntityUid uid, BorgChassisComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
        {
            if (_mind.TryGetMind(uid, out _, out _))
                _powerCell.SetDrawEnabled(uid, true);
        }
        else
        {
            _powerCell.SetDrawEnabled(uid, false);
        }
    }

    private void OnBeingGibbed(EntityUid uid, BorgChassisComponent component, ref BeingGibbedEvent args)
    {
        TryEjectPowerCell(uid, component, out var _);

        _container.EmptyContainer(component.BrainContainer);
        _container.EmptyContainer(component.ModuleContainer);
    }

    private void OnPowerCellChanged(EntityUid uid, BorgChassisComponent component, PowerCellChangedEvent args)
    {
        UpdateBatteryAlert((uid, component));

        // if we aren't drawing and suddenly get enough power to draw again, reeanble.
        if (_powerCell.HasDrawCharge(uid))
        {
            Toggle.TryActivate(uid);
        }

        UpdateUI(uid, component);
    }

    private void OnPowerCellSlotEmpty(EntityUid uid, BorgChassisComponent component, ref PowerCellSlotEmptyEvent args)
    {
        Toggle.TryDeactivate(uid);
        UpdateUI(uid, component);
    }

    private void OnGetDeadIC(EntityUid uid, BorgChassisComponent component, ref GetCharactedDeadIcEvent args)
    {
        args.Dead = true;
    }

    private void OnGetUnrevivableIC(EntityUid uid, BorgChassisComponent component, ref GetCharacterUnrevivableIcEvent args)
    {
        args.Unrevivable = true;
    }

    private void OnToggled(Entity<BorgChassisComponent> ent, ref ItemToggledEvent args)
    {
        var (uid, comp) = ent;
        if (args.Activated)
            InstallAllModules(uid, comp);
        else
            DisableAllModules(uid, comp);

        // only enable the powerdraw if there is a player in the chassis
        var drawing = _mind.TryGetMind(uid, out _, out _) && _mobState.IsAlive(ent);
        _powerCell.SetDrawEnabled(uid, drawing);

        UpdateUI(uid, comp);

        _movementSpeedModifier.RefreshMovementSpeedModifiers(uid);
    }

    private void OnBrainMindAdded(EntityUid uid, BorgBrainComponent component, MindAddedMessage args)
    {
        if (!Container.TryGetOuterContainer(uid, Transform(uid), out var container))
            return;

        var containerEnt = container.Owner;

        if (!TryComp<BorgChassisComponent>(containerEnt, out var chassisComponent) ||
            container.ID != chassisComponent.BrainContainerId)
            return;

        if (!_mind.TryGetMind(uid, out var mindId, out var mind) ||
            !_player.TryGetSessionById(mind.UserId, out var session))
            return;

        if (!CanPlayerBeBorged(session))
        {
            Popup.PopupEntity(Loc.GetString("borg-player-not-allowed-eject"), uid);
            Container.RemoveEntity(containerEnt, uid);
            _throwing.TryThrow(uid, _random.NextVector2() * 5, 5f);
            return;
        }

        _mind.TransferTo(mindId, containerEnt, mind: mind);
    }

    private void OnBrainPointAttempt(EntityUid uid, BorgBrainComponent component, PointAttemptEvent args)
    {
        args.Cancel();
    }

    private void UpdateBatteryAlert(Entity<BorgChassisComponent> ent, PowerCellSlotComponent? slotComponent = null)
    {
        if (!_powerCell.TryGetBatteryFromSlot(ent, out var battery, slotComponent))
        {
            _alerts.ClearAlert(ent, ent.Comp.BatteryAlert);
            _alerts.ShowAlert(ent, ent.Comp.NoBatteryAlert);
            return;
        }

        var chargePercent = (short) MathF.Round(battery.CurrentCharge / battery.MaxCharge * 10f);

        // we make sure 0 only shows if they have absolutely no battery.
        // also account for floating point imprecision
        if (chargePercent == 0 && _powerCell.HasDrawCharge(ent, cell: slotComponent))
        {
            chargePercent = 1;
        }

        _alerts.ClearAlert(ent, ent.Comp.NoBatteryAlert);
        _alerts.ShowAlert(ent, ent.Comp.BatteryAlert, chargePercent);
    }

    public bool TryEjectPowerCell(EntityUid uid, BorgChassisComponent component, [NotNullWhen(true)] out List<EntityUid>? ents)
    {
        ents = null;

        if (!TryComp<PowerCellSlotComponent>(uid, out var slotComp) ||
            !Container.TryGetContainer(uid, slotComp.CellSlotId, out var container) ||
            !container.ContainedEntities.Any())
                return false;

        ents = Container.EmptyContainer(container);

        return true;
    }

    /// <summary>
    /// Activates a borg when a player occupies it
    /// </summary>
    public void BorgActivate(EntityUid uid, BorgChassisComponent component)
    {
        Popup.PopupEntity(Loc.GetString("borg-mind-added", ("name", Identity.Name(uid, EntityManager))), uid);
        if (_powerCell.HasDrawCharge(uid))
        {
            Toggle.TryActivate(uid);
            _powerCell.SetDrawEnabled(uid, _mobState.IsAlive(uid));
        }
        _appearance.SetData(uid, BorgVisuals.HasPlayer, true);
    }

    /// <summary>
    /// Deactivates a borg when a player leaves it.
    /// </summary>
    public void BorgDeactivate(EntityUid uid, BorgChassisComponent component)
    {
        Popup.PopupEntity(Loc.GetString("borg-mind-removed", ("name", Identity.Name(uid, EntityManager))), uid);
        Toggle.TryDeactivate(uid);
        _powerCell.SetDrawEnabled(uid, false);
        _appearance.SetData(uid, BorgVisuals.HasPlayer, false);
    }

    /// <summary>
    /// Checks that a player has fulfilled the requirements for the borg job.
    /// If they don't have enough hours, they cannot be placed into a chassis.
    /// </summary>
    public bool CanPlayerBeBorged(ICommonSession session)
    {
        if (_banManager.GetJobBans(session.UserId)?.Contains(BorgJobId) == true)
            return false;

        return true;
    }
}

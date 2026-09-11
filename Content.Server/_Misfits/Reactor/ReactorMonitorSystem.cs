using Content.Server.Popups;
using Content.Shared._Misfits.Reactor;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Reactor;

public sealed class ReactorMonitorSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly ProtoId<AccessLevelPrototype> RequiredAccess = "VaultEngineer";

    private const float UpdateInterval = 1f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReactorMonitorComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<ReactorMonitorComponent, ComponentRemove>(OnComponentRemove);
        SubscribeLocalEvent<ReactorMonitorComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
        SubscribeLocalEvent<ReactorMonitorComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
        SubscribeLocalEvent<ReactorMonitorComponent, BoundUIOpenedEvent>(OnOpened);

        Subs.BuiEvents<ReactorMonitorComponent>(ReactorMonitorUiKey.Key, subs =>
        {
            subs.Event<ReactorStartMsg>(Forward);
            subs.Event<ReactorShutdownMsg>(Forward);
            subs.Event<ReactorScramMsg>(Forward);
            subs.Event<ReactorSetModeMsg>(Forward);
            subs.Event<ReactorSetFuelingRateMsg>(Forward);
            subs.Event<ReactorSetHeatingPowerMsg>(Forward);
            subs.Event<ReactorSetPlasmaCurrentMsg>(Forward);
            subs.Event<ReactorSetDivertorRateMsg>(Forward);
            subs.Event<ReactorSetCoilTargetMsg>(Forward);
            subs.Event<ReactorMonitorSelectMsg>(OnSelect);
            subs.Event<ReactorMonitorAllMsg>(OnAll);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<ReactorMonitorComponent>();
        while (query.MoveNext(out var uid, out var comp))
            RefreshUi((uid, comp));
    }

    private void OnComponentInit(Entity<ReactorMonitorComponent> ent, ref ComponentInit args)
    {
        _itemSlots.AddItemSlot(ent, ReactorMonitorComponent.IdCardSlotId, ent.Comp.IdCardSlot);
    }

    private void OnComponentRemove(Entity<ReactorMonitorComponent> ent, ref ComponentRemove args)
    {
        _itemSlots.RemoveItemSlot(ent, ent.Comp.IdCardSlot);
    }

    private void OnItemInserted(Entity<ReactorMonitorComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ReactorMonitorComponent.IdCardSlotId)
            return;

        if (!HasRequiredAccess(args.Entity))
        {
            _itemSlots.TryEject(ent, ent.Comp.IdCardSlot, null, out _);
            return;
        }

        RefreshIdCard(ent);
    }

    private void OnItemRemoved(Entity<ReactorMonitorComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ReactorMonitorComponent.IdCardSlotId)
            return;

        RefreshIdCard(ent);
    }

    private bool HasRequiredAccess(EntityUid card)
    {
        var tags = _access.FindAccessTags(card, new HashSet<EntityUid> { card });
        return tags.Contains(RequiredAccess);
    }

    private void RefreshIdCard(Entity<ReactorMonitorComponent> ent)
    {
        ent.Comp.InsertedIdName = ent.Comp.IdCardSlot.Item is { Valid: true } item
            ? MetaData(item).EntityName
            : null;
        RefreshUi(ent);
    }

    private void OnOpened(Entity<ReactorMonitorComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (ent.Comp.Selected == null || !HasComp<ReactorComponent>(ent.Comp.Selected))
        {
            var reactors = GetTrackedReactors(ent.Comp);
            ent.Comp.Selected = reactors.Count > 0 ? reactors[0] : null;
        }

        RefreshUi(ent);
    }

    private bool IsLoggedIn(Entity<ReactorMonitorComponent> ent, EntityUid actor)
    {
        if (ent.Comp.InsertedIdName != null)
            return true;

        _popup.PopupEntity(Loc.GetString("reactor-popup-not-logged-in"), ent, actor);
        return false;
    }

    private void Forward<T>(Entity<ReactorMonitorComponent> ent, ref T msg) where T : BoundUserInterfaceMessage
    {
        if (!IsLoggedIn(ent, msg.Actor))
            return;

        if (ent.Comp.Selected is not { } reactor || !HasComp<ReactorComponent>(reactor))
        {
            _popup.PopupEntity(Loc.GetString("reactor-popup-no-reactor-selected"), ent, msg.Actor);
            return;
        }

        msg.UiKey = ReactorUiKey.Key;

        RaiseLocalEvent(reactor, (object) msg);
        RefreshUi(ent);
    }

    private void OnSelect(Entity<ReactorMonitorComponent> ent, ref ReactorMonitorSelectMsg msg)
    {
        var reactors = GetTrackedReactors(ent.Comp);
        if (msg.Index < 1 || msg.Index > reactors.Count)
            return;

        ent.Comp.Selected = reactors[msg.Index - 1];
        RefreshUi(ent);
    }

    private void OnAll(Entity<ReactorMonitorComponent> ent, ref ReactorMonitorAllMsg msg)
    {
        if (!IsLoggedIn(ent, msg.Actor))
            return;

        foreach (var uid in GetTrackedReactors(ent.Comp))
        {
            BoundUserInterfaceMessage? forward = msg.Command switch
            {
                ReactorMonitorAllCommand.Start => new ReactorStartMsg(),
                ReactorMonitorAllCommand.Shutdown => new ReactorShutdownMsg(),
                ReactorMonitorAllCommand.Scram => new ReactorScramMsg(),
                ReactorMonitorAllCommand.SetMode => new ReactorSetModeMsg(msg.Mode),
                ReactorMonitorAllCommand.SetFuelingRate => new ReactorSetFuelingRateMsg(msg.Value),
                ReactorMonitorAllCommand.SetHeatingPower => new ReactorSetHeatingPowerMsg(msg.Value),
                ReactorMonitorAllCommand.SetPlasmaCurrent => new ReactorSetPlasmaCurrentMsg(msg.Value),
                ReactorMonitorAllCommand.SetDivertorRate => new ReactorSetDivertorRateMsg(msg.Value),
                _ => null,
            };

            if (forward == null)
                continue;

            forward.UiKey = ReactorUiKey.Key;
            forward.Actor = msg.Actor;
            RaiseLocalEvent(uid, (object) forward);
        }

        RefreshUi(ent);
    }

    private List<EntityUid> GetTrackedReactors(ReactorMonitorComponent comp)
    {
        var reactors = new List<EntityUid>();
        if (comp.TrackedTags.Count == 0)
            return reactors;

        var query = EntityQueryEnumerator<ReactorComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            foreach (var tag in comp.TrackedTags)
            {
                if (_tag.HasTag(uid, tag))
                {
                    reactors.Add(uid);
                    break;
                }
            }
        }

        reactors.Sort((a, b) => string.Compare(MetaData(a).EntityName, MetaData(b).EntityName, StringComparison.OrdinalIgnoreCase));
        return reactors;
    }

    private void RefreshUi(Entity<ReactorMonitorComponent> ent)
    {
        _ui.SetUiState(ent.Owner, ReactorMonitorUiKey.Key, BuildState(ent));
    }

    private ReactorMonitorState BuildState(Entity<ReactorMonitorComponent> ent)
    {
        var tracked = GetTrackedReactors(ent.Comp);
        var entries = new List<ReactorMonitorEntry>();
        var selectedIndex = 0;

        for (var i = 0; i < tracked.Count; i++)
        {
            var uid = tracked[i];
            if (!TryComp<ReactorComponent>(uid, out var reactor))
                continue;

            entries.Add(new ReactorMonitorEntry(
                MetaData(uid).EntityName,
                reactor.State,
                reactor.Integrity,
                reactor.PowerOutput,
                reactor.MaxOutput,
                reactor.Alarm));

            if (uid == ent.Comp.Selected)
                selectedIndex = i + 1;
        }

        if (ent.Comp.Selected is { } selectedUid && TryComp<ReactorComponent>(selectedUid, out var sel))
        {
            var lockedOutSeconds = sel.LockedUntil is { } lockedUntil && lockedUntil > _timing.CurTime
                ? (float) (lockedUntil - _timing.CurTime).TotalSeconds
                : 0f;

            return new ReactorMonitorState(
                insertedIdName: ent.Comp.InsertedIdName,
                reactors: entries,
                selectedIndex: selectedIndex,
                hasSelection: true,
                mode: sel.Mode,
                state: sel.State,
                startupStep: sel.StartupStep,
                shutdownStep: sel.ShutdownStep,
                stepElapsed: sel.StepElapsed,
                stepDurationSeconds: (float) sel.StepDuration.TotalSeconds,
                fuelingRate: sel.FuelingRate,
                fuelingTarget: sel.FuelingTarget,
                heatingPower: sel.HeatingPower,
                heatingTarget: sel.HeatingTarget,
                plasmaCurrent: sel.PlasmaCurrent,
                plasmaCurrentTarget: sel.PlasmaCurrentTarget,
                divertorRate: sel.DivertorRate,
                divertorTarget: sel.DivertorTarget,
                coilTargets: new List<float>(sel.CoilTargets),
                coilStrength: new List<float>(sel.CoilStrength),
                coilLocalHeat: new List<float>(sel.CoilLocalHeat),
                density: sel.Density,
                temperature: sel.Temperature,
                beta: sel.Beta,
                ashLevel: sel.AshLevel,
                integrity: sel.Integrity,
                powerOutput: sel.PowerOutput,
                loadFactor: sel.LoadFactor,
                maxOutput: sel.MaxOutput,
                alarm: sel.Alarm,
                lockedOutSeconds: lockedOutSeconds);
        }

        return new ReactorMonitorState(
            insertedIdName: ent.Comp.InsertedIdName,
            reactors: entries,
            selectedIndex: 0,
            hasSelection: false,
            mode: ReactorMode.Automatic,
            state: ReactorState.Offline,
            startupStep: ReactorStartupStep.None,
            shutdownStep: ReactorShutdownStep.None,
            stepElapsed: 0f,
            stepDurationSeconds: 0f,
            fuelingRate: 0f,
            fuelingTarget: 0f,
            heatingPower: 0f,
            heatingTarget: 0f,
            plasmaCurrent: 0f,
            plasmaCurrentTarget: 0f,
            divertorRate: 0f,
            divertorTarget: 0f,
            coilTargets: new List<float>(),
            coilStrength: new List<float>(),
            coilLocalHeat: new List<float>(),
            density: 0f,
            temperature: 0f,
            beta: 0f,
            ashLevel: 0f,
            integrity: 0f,
            powerOutput: 0f,
            loadFactor: 0f,
            maxOutput: 0f,
            alarm: null);
    }
}

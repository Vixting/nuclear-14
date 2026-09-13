using Content.Server.GameTicking;
using Content.Server.Paper;
using Content.Shared._Misfits.Reactor;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.Reactor;

public sealed class ReactorMonitorSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

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
        SubscribeLocalEvent<ReactorComponent, ReactorCommandRejectedEvent>(OnCommandRejected);

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
            subs.Event<ReactorMonitorPrintMsg>(OnPrint);
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

    private void Notify(Entity<ReactorMonitorComponent> ent, EntityUid actor, string locId, bool isError = true,
        Dictionary<string, string>? locArgs = null)
    {
        _ui.ServerSendUiMessage(ent.Owner, ReactorMonitorUiKey.Key, new ReactorMonitorNoticeMsg(locId, isError, locArgs), actor);
    }

    private void OnCommandRejected(Entity<ReactorComponent> ent, ref ReactorCommandRejectedEvent args)
    {
        var consoles = EntityQueryEnumerator<ReactorMonitorComponent>();
        while (consoles.MoveNext(out var uid, out var comp))
        {
            if (comp.Selected == ent.Owner)
                Notify((uid, comp), args.Actor, args.LocId, locArgs: args.LocArgs);
        }
    }

    private bool IsLoggedIn(Entity<ReactorMonitorComponent> ent, EntityUid actor)
    {
        if (ent.Comp.InsertedIdName != null)
            return true;

        Notify(ent, actor, "reactor-popup-not-logged-in");
        return false;
    }

    private void Forward<T>(Entity<ReactorMonitorComponent> ent, ref T msg) where T : BoundUserInterfaceMessage
    {
        if (!IsLoggedIn(ent, msg.Actor))
            return;

        if (ent.Comp.Selected is not { } reactor || !HasComp<ReactorComponent>(reactor))
        {
            Notify(ent, msg.Actor, "reactor-popup-no-reactor-selected");
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

    private void OnPrint(Entity<ReactorMonitorComponent> ent, ref ReactorMonitorPrintMsg msg)
    {
        if (!IsLoggedIn(ent, msg.Actor))
            return;

        if (ent.Comp.LinkedPrinter is not { } printer || !TryComp<ReactorPrinterComponent>(printer, out var printerComp))
        {
            Notify(ent, msg.Actor, "reactor-popup-no-printer-linked");
            return;
        }

        if (ent.Comp.Selected is not { } reactorUid || !TryComp<ReactorComponent>(reactorUid, out var reactor))
        {
            Notify(ent, msg.Actor, "reactor-popup-no-reactor-selected");
            return;
        }

        var content = msg.Report switch
        {
            ReactorPrintReport.Status => BuildStatusReportContent(reactorUid, reactor),
            _ => null,
        };

        if (content == null)
            return;

        var paper = Spawn(printerComp.PaperPrototype, Transform(printer).Coordinates);
        _paper.SetContent(paper, content);

        Notify(ent, msg.Actor, "reactor-popup-printed", isError: false);
    }

    private string BuildStatusReportContent(EntityUid uid, ReactorComponent reactor)
    {
        var name = MetaData(uid).EntityName;
        var outputPct = reactor.MaxOutput > 0f ? (int) (reactor.PowerOutput / reactor.MaxOutput * 100) : 0;
        var roundTime = _gameTicker.RoundDuration().ToString(@"hh\:mm\:ss");

        var msg = new FormattedMessage();
        msg.AddMarkupOrThrow("[head=2]Reactor Status Report[/head]\n");
        msg.AddMarkupOrThrow("[color=#1b4f9c]▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬[/color]\n");
        msg.AddMarkupOrThrow($"[bold]Form:[/bold] [color=#e8c547]VT-14-240[/color]\n");
        msg.AddMarkupOrThrow("[color=#AAAAAA][italic]Vault 14 — Office of Reactor Commissioning[/italic][/color]\n");
        msg.AddMarkupOrThrow("[color=#1b4f9c]▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬[/color]\n\n");

        msg.AddMarkupOrThrow($"[bold]Reactor:[/bold] {FormattedMessage.EscapeText(name)}\n");
        msg.AddMarkupOrThrow($"[bold]State:[/bold] {reactor.State} ({reactor.Mode})\n");
        msg.AddMarkupOrThrow($"[bold]Integrity:[/bold] {(int) reactor.Integrity}%\n");
        msg.AddMarkupOrThrow($"[bold]Output:[/bold] {(int) reactor.PowerOutput}/{(int) reactor.MaxOutput} ({outputPct}%)\n\n");

        msg.AddMarkupOrThrow("[bold]Readouts:[/bold]\n");
        msg.AddMarkupOrThrow($"[bullet/] Fuel: {Pct(reactor.FuelingRate)}/{Pct(reactor.FuelingTarget)}\n");
        msg.AddMarkupOrThrow($"[bullet/] Heat: {Pct(reactor.HeatingPower)}/{Pct(reactor.HeatingTarget)}\n");
        msg.AddMarkupOrThrow($"[bullet/] Current: {Pct(reactor.PlasmaCurrent)}/{Pct(reactor.PlasmaCurrentTarget)}\n");
        msg.AddMarkupOrThrow($"[bullet/] Divertor: {Pct(reactor.DivertorRate)}/{Pct(reactor.DivertorTarget)}\n");
        msg.AddMarkupOrThrow($"[bullet/] Beta: {Pct(reactor.Beta)}   Density: {Pct(reactor.Density)}   Ash: {Pct(reactor.AshLevel)}\n\n");

        if (reactor.ActiveFaults.Count > 0)
        {
            msg.AddMarkupOrThrow("[bold][color=#ff0000]Active Faults:[/color][/bold]\n");
            foreach (var fault in reactor.ActiveFaults)
                msg.AddMarkupOrThrow($"[bullet/] [color=#ff0000]{FormattedMessage.EscapeText(fault)}[/color]\n");
            msg.AddMarkupOrThrow("\n");
        }

        msg.AddMarkupOrThrow($"[italic][color=#AAAAAA]Printed at T+{roundTime}. Vault-Tec accepts no liability for conditions that changed between printing and reading.[/color][/italic]");

        return msg.ToMarkup();
    }

    private static string Pct(float value) => $"{(int) (value * 100)}%";

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
                autoDerated: sel.AutoDerated,
                activeFaults: new List<string>(sel.ActiveFaults),
                ignitionTemperature: sel.IgnitionTemperature,
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

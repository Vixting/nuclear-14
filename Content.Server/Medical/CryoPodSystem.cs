using Content.Server.Administration.Logs;
using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.Unary.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Medical.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Atmos;
using Content.Shared._Misfits.Special;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Systems;
using Content.Shared.UserInterface;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Climbing.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Medical.Cryogenics;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Power;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using SharedToolSystem = Content.Shared.Tools.Systems.SharedToolSystem;

namespace Content.Server.Medical;

public sealed partial class CryoPodSystem : SharedCryoPodSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly GasCanisterSystem _gasCanisterSystem = default!;
    [Dependency] private readonly ClimbSystem _climbSystem = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private readonly SolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SharedToolSystem _toolSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly MetaDataSystem _metaDataSystem = default!;
    [Dependency] private readonly ReactiveSystem _reactiveSystem = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly TemperatureSystem _temperatureSystem = default!;
    [Dependency] private readonly SharedBodySystem _bodySystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CryoPodComponent, ComponentInit>(OnCryoPodComponentInit);
        SubscribeLocalEvent<CryoPodComponent, GetVerbsEvent<AlternativeVerb>>(AddAlternativeVerbs);
        SubscribeLocalEvent<CryoPodComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<CryoPodComponent, CryoPodDragFinished>(OnDragFinished);
        SubscribeLocalEvent<CryoPodComponent, CryoPodPryFinished>(OnCryoPodPryFinished);

        SubscribeLocalEvent<CryoPodComponent, AtmosDeviceUpdateEvent>(OnCryoPodUpdateAtmosphere);
        SubscribeLocalEvent<CryoPodComponent, DragDropTargetEvent>(HandleDragDropOn);
        SubscribeLocalEvent<CryoPodComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CryoPodComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<CryoPodComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<CryoPodComponent, GasAnalyzerScanEvent>(OnGasAnalyzed);
        SubscribeLocalEvent<CryoPodComponent, ActivatableUIOpenAttemptEvent>(OnActivateUIAttempt);
        SubscribeLocalEvent<CryoPodComponent, AfterActivatableUIOpenEvent>(OnActivateUI);
        SubscribeLocalEvent<CryoPodComponent, EntRemovedFromContainerMessage>(OnEjected);
        SubscribeLocalEvent<CryoPodComponent, EntInsertedIntoContainerMessage>(OnBeakerChanged);
        SubscribeLocalEvent<CryoPodComponent, CryoPodTransferReagentMessage>(OnTransferReagentMessage);
        SubscribeLocalEvent<CryoPodComponent, CryoPodSetRunningMessage>(OnSetRunningMessage);
    }

    private void OnCryoPodComponentInit(EntityUid uid, CryoPodComponent cryoPod, ComponentInit args)
    {
        OnComponentInit(uid, cryoPod, args);

        _solutionContainerSystem.EnsureSolution((uid, null), CryoPodComponent.BufferSolutionName, out _, cryoPod.BufferVolume);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var bloodStreamQuery = GetEntityQuery<BloodstreamComponent>();
        var metaDataQuery = GetEntityQuery<MetaDataComponent>();
        var solutionContainerManagerQuery = GetEntityQuery<SolutionContainerManagerComponent>();
        var temperatureQuery = GetEntityQuery<TemperatureComponent>();
        var query = EntityQueryEnumerator<ActiveCryoPodComponent, CryoPodComponent>();

        while (query.MoveNext(out var uid, out _, out var cryoPod))
        {
            metaDataQuery.TryGetComponent(uid, out var metaDataComponent);
            if (curTime < cryoPod.NextDoseTime + _metaDataSystem.GetPauseTime(uid, metaDataComponent))
                continue;
            cryoPod.NextDoseTime = curTime + TimeSpan.FromSeconds(cryoPod.DoseInterval);

            var patient = cryoPod.BodyContainer.ContainedEntity;
            if (patient == null)
                continue;

            if (temperatureQuery.TryGetComponent(patient, out var temperature)
                && temperature.CurrentTemperature > cryoPod.PassiveCoolingTarget)
            {
                var diff = temperature.CurrentTemperature - cryoPod.PassiveCoolingTarget;
                var coolAmount = MathF.Min(MathF.Max(diff * cryoPod.PassiveCoolingFraction, cryoPod.PassiveCoolingMinimum), diff);
                _temperatureSystem.ForceChangeTemperature(patient.Value, temperature.CurrentTemperature - coolAmount, temperature);
            }

            if (cryoPod.Running
                && bloodStreamQuery.TryGetComponent(patient, out var bloodstream)
                && solutionContainerManagerQuery.TryGetComponent(uid, out var solutionManager)
                && _solutionContainerSystem.TryGetSolution((uid, solutionManager), CryoPodComponent.BufferSolutionName, out var bufferEnt, out var bufferSolution)
                && bufferSolution.Volume > FixedPoint2.Zero)
            {
                var solutionToInject = _solutionContainerSystem.SplitSolution(bufferEnt.Value, FixedPoint2.Min(cryoPod.DoseAmount, bufferSolution.Volume));
                _bloodstreamSystem.TryAddToChemicals(patient.Value, solutionToInject, bloodstream);
                _reactiveSystem.DoEntityReaction(patient.Value, solutionToInject, ReactionMethod.Injection);
            }

            if (_uiSystem.IsUiOpen(uid, CryoPodUiKey.Key))
                UpdateUiState((uid, cryoPod));
        }
    }

    private void UpdateUiState(Entity<CryoPodComponent> entity)
    {
        var patient = entity.Comp.BodyContainer.ContainedEntity;
        NetEntity? patientNet = null;
        string? patientName = null;
        MobState? patientState = null;
        float? temperature = null;
        var bleeding = false;
        FixedPoint2? totalDamage = null;
        Dictionary<string, FixedPoint2>? damagePerGroup = null;
        Dictionary<string, FixedPoint2>? damagePerType = null;
        Dictionary<TargetBodyPart, TargetIntegrity>? body = null;

        if (patient is { } patientUid)
        {
            patientNet = GetNetEntity(patientUid);
            patientName = Name(patientUid);

            if (TryComp<MobStateComponent>(patientUid, out var mobState))
                patientState = mobState.CurrentState;

            if (TryComp<TemperatureComponent>(patientUid, out var temp))
                temperature = temp.CurrentTemperature;

            if (TryComp<DamageableComponent>(patientUid, out var damageable))
            {
                totalDamage = damageable.TotalDamage;
                damagePerGroup = new Dictionary<string, FixedPoint2>(damageable.DamagePerGroup);
                damagePerType = new Dictionary<string, FixedPoint2>(damageable.Damage.DamageDict);
            }

            if (HasComp<TargetingComponent>(patientUid))
                body = _bodySystem.GetBodyPartStatus(patientUid);

            if (TryComp<BloodstreamComponent>(patientUid, out var bloodstream))
                bleeding = bloodstream.BleedAmount > 0;
        }

        var container = _itemSlotsSystem.GetItemOrNull(entity.Owner, entity.Comp.SolutionContainerName);
        var hasBeaker = container != null;
        string? beakerName = null;
        var beakerReagents = new List<ReagentQuantity>();
        var beakerVolume = FixedPoint2.Zero;
        var beakerMaxVolume = FixedPoint2.Zero;

        if (container != null)
        {
            beakerName = Name(container.Value);
            if (_solutionContainerSystem.TryGetFitsInDispenser(container.Value, out _, out var containerSolution) && containerSolution != null)
            {
                beakerReagents.AddRange(containerSolution.Contents);
                beakerVolume = containerSolution.Volume;
                beakerMaxVolume = containerSolution.MaxVolume;
            }
        }

        var bufferReagents = new List<ReagentQuantity>();
        var bufferVolume = FixedPoint2.Zero;
        var bufferMaxVolume = FixedPoint2.Zero;
        if (_solutionContainerSystem.TryGetSolution(entity.Owner, CryoPodComponent.BufferSolutionName, out _, out var bufferSolution))
        {
            bufferReagents.AddRange(bufferSolution.Contents);
            bufferVolume = bufferSolution.Volume;
            bufferMaxVolume = bufferSolution.MaxVolume;
        }

        var state = new CryoPodBoundUserInterfaceState(
            patientNet,
            patientName,
            patientState,
            temperature,
            bleeding,
            totalDamage,
            damagePerGroup,
            damagePerType,
            body,
            entity.Comp.Running,
            hasBeaker,
            beakerName,
            beakerReagents,
            beakerVolume,
            beakerMaxVolume,
            bufferReagents,
            bufferVolume,
            bufferMaxVolume);

        _uiSystem.SetUiState(entity.Owner, CryoPodUiKey.Key, state);
    }

    private void OnTransferReagentMessage(Entity<CryoPodComponent> entity, ref CryoPodTransferReagentMessage args)
    {
        var container = _itemSlotsSystem.GetItemOrNull(entity.Owner, entity.Comp.SolutionContainerName);
        if (container is null
            || !_solutionContainerSystem.TryGetFitsInDispenser(container.Value, out var beakerEnt, out var beakerSolution)
            || !_solutionContainerSystem.TryGetSolution(entity.Owner, CryoPodComponent.BufferSolutionName, out var bufferEnt, out var bufferSolution))
            return;

        if (args.FromBuffer)
        {
            var amount = FixedPoint2.Min(bufferSolution.GetReagentQuantity(args.ReagentId), beakerSolution.AvailableVolume);
            if (amount <= FixedPoint2.Zero)
                return;

            amount = bufferSolution.RemoveReagent(args.ReagentId, amount, preserveOrder: true);
            _solutionContainerSystem.TryAddReagent(beakerEnt!.Value, args.ReagentId, amount, out _);
        }
        else
        {
            var amount = FixedPoint2.Min(beakerSolution.GetReagentQuantity(args.ReagentId), bufferSolution.AvailableVolume);
            if (amount <= FixedPoint2.Zero)
                return;

            _solutionContainerSystem.RemoveReagent(beakerEnt.Value, args.ReagentId, amount);
            bufferSolution.AddReagent(args.ReagentId, amount);
        }

        UpdateUiState(entity);
    }

    private void OnSetRunningMessage(Entity<CryoPodComponent> entity, ref CryoPodSetRunningMessage args)
    {
        entity.Comp.Running = args.Running;
        UpdateUiState(entity);
    }

    public override EntityUid? EjectBody(EntityUid uid, CryoPodComponent? cryoPodComponent)
    {
        if (!Resolve(uid, ref cryoPodComponent))
            return null;
        if (cryoPodComponent.BodyContainer.ContainedEntity is not { Valid: true } contained)
            return null;
        base.EjectBody(uid, cryoPodComponent);
        _climbSystem.ForciblySetClimbing(contained, uid);
        return contained;
    }

    #region Interaction

    private void HandleDragDropOn(Entity<CryoPodComponent> entity, ref DragDropTargetEvent args)
    {
        if (entity.Comp.BodyContainer.ContainedEntity != null)
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager, args.User, _special.GetIntelligenceMedicalActionDelay(args.User, TimeSpan.FromSeconds(entity.Comp.EntryDelay)), new CryoPodDragFinished(), entity, target: args.Dragged, used: entity)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false,
        };
        _doAfterSystem.TryStartDoAfter(doAfterArgs);
        args.Handled = true;
    }

    private void OnDragFinished(Entity<CryoPodComponent> entity, ref CryoPodDragFinished args)
    {
        if (args.Cancelled || args.Handled || args.Args.Target == null)
            return;

        if (InsertBody(entity.Owner, args.Args.Target.Value, entity.Comp))
        {
            if (!TryComp(entity.Owner, out CryoPodAirComponent? cryoPodAir))
                _adminLogger.Add(LogType.Action, LogImpact.Medium,
                    $"{ToPrettyString(args.User)} inserted {ToPrettyString(args.Args.Target.Value)} into {ToPrettyString(entity.Owner)}");

            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(args.User)} inserted {ToPrettyString(args.Args.Target.Value)} into {ToPrettyString(entity.Owner)} which contains gas: {cryoPodAir!.Air.ToPrettyString():gasMix}");
        }
        args.Handled = true;
    }

    private void OnActivateUIAttempt(Entity<CryoPodComponent> entity, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled)
        {
            return;
        }

        var containedEntity = entity.Comp.BodyContainer.ContainedEntity;
        if (containedEntity == null || containedEntity == args.User || !HasComp<ActiveCryoPodComponent>(entity))
        {
            args.Cancel();
        }
    }

    private void OnActivateUI(Entity<CryoPodComponent> entity, ref AfterActivatableUIOpenEvent args)
    {
        UpdateUiState(entity);
    }

    private void OnBeakerChanged(Entity<CryoPodComponent> entity, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == entity.Comp.SolutionContainerName && _uiSystem.IsUiOpen(entity.Owner, CryoPodUiKey.Key))
            UpdateUiState(entity);
    }

    private void OnInteractUsing(Entity<CryoPodComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled || !entity.Comp.Locked || entity.Comp.BodyContainer.ContainedEntity == null)
            return;

        args.Handled = _toolSystem.UseTool(args.Used, args.User, entity.Owner, (float) _special.GetIntelligenceMedicalActionDelay(args.User, TimeSpan.FromSeconds(entity.Comp.PryDelay)).TotalSeconds, "Prying", new CryoPodPryFinished());
    }

    private void OnExamined(Entity<CryoPodComponent> entity, ref ExaminedEvent args)
    {
        var container = _itemSlotsSystem.GetItemOrNull(entity.Owner, entity.Comp.SolutionContainerName);
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(CryoPodComponent)))
        {
            if (container != null && _solutionContainerSystem.TryGetFitsInDispenser(container.Value, out _, out var containerSolution))
            {
                args.PushMarkup(Loc.GetString("cryo-pod-examine", ("beaker", Name(container.Value))));
                if (containerSolution.Volume == 0)
                    args.PushMarkup(Loc.GetString("cryo-pod-empty-beaker"));
            }

            if (_solutionContainerSystem.TryGetSolution(entity.Owner, CryoPodComponent.BufferSolutionName, out _, out var bufferSolution))
                args.PushMarkup(Loc.GetString("cryo-pod-examine-buffer", ("current", bufferSolution.Volume), ("max", bufferSolution.MaxVolume)));
        }
    }

    private void OnPowerChanged(Entity<CryoPodComponent> entity, ref PowerChangedEvent args)
    {
        // Needed to avoid adding/removing components on a deleted entity
        if (Terminating(entity))
        {
            return;
        }

        if (args.Powered)
        {
            EnsureComp<ActiveCryoPodComponent>(entity);
        }
        else
        {
            RemComp<ActiveCryoPodComponent>(entity);
            _uiSystem.CloseUi(entity.Owner, CryoPodUiKey.Key);
        }
        UpdateAppearance(entity.Owner, entity.Comp);
    }

    #endregion

    #region Atmos handler

    private void OnCryoPodUpdateAtmosphere(Entity<CryoPodComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainer.TryGetNode(entity.Owner, entity.Comp.PortName, out PortablePipeNode? portNode))
            return;

        if (!TryComp(entity, out CryoPodAirComponent? cryoPodAir))
            return;

        _atmosphereSystem.React(cryoPodAir.Air, portNode);

        if (portNode.NodeGroup is PipeNet { NodeCount: > 1 } net)
        {
            _gasCanisterSystem.MixContainerWithPipeNet(cryoPodAir.Air, net.Air);
        }
    }

    private void OnGasAnalyzed(Entity<CryoPodComponent> entity, ref GasAnalyzerScanEvent args)
    {
        if (!TryComp(entity, out CryoPodAirComponent? cryoPodAir))
            return;

        args.GasMixtures ??= new List<(string, GasMixture?)>();
        args.GasMixtures.Add((Name(entity.Owner), cryoPodAir.Air));
        // If it's connected to a port, include the port side
        // multiply by volume fraction to make sure to send only the gas inside the analyzed pipe element, not the whole pipe system
        if (_nodeContainer.TryGetNode(entity.Owner, entity.Comp.PortName, out PipeNode? port) && port.Air.Volume != 0f)
        {
            var portAirLocal = port.Air.Clone();
            portAirLocal.Multiply(port.Volume / port.Air.Volume);
            portAirLocal.Volume = port.Volume;
            args.GasMixtures.Add((entity.Comp.PortName, portAirLocal));
        }
    }

    private void OnEjected(Entity<CryoPodComponent> cryoPod, ref EntRemovedFromContainerMessage args)
    {
        UpdateUiState(cryoPod);
    }

    #endregion
}

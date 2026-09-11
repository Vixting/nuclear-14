using Content.Server.Chat.Systems;
using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Shared._Misfits.Reactor;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Reactor;

public sealed class ReactorSystem : SharedReactorSystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly EntProtoId DestroyedReactorProto = "N14GeneratorReactorFloorDestroyed";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReactorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ReactorComponent, EntInsertedIntoContainerMessage>(OnIdCardChanged);
        SubscribeLocalEvent<ReactorComponent, EntRemovedFromContainerMessage>(OnIdCardChanged);

        Subs.BuiEvents<ReactorComponent>(ReactorUiKey.Key, subs =>
        {
            subs.Event<ReactorStartMsg>(OnStart);
            subs.Event<ReactorShutdownMsg>(OnShutdown);
            subs.Event<ReactorScramMsg>(OnScram);
            subs.Event<ReactorSetModeMsg>(OnSetMode);
            subs.Event<ReactorSetFuelingRateMsg>(OnSetFuelingRate);
            subs.Event<ReactorSetHeatingPowerMsg>(OnSetHeatingPower);
            subs.Event<ReactorSetPlasmaCurrentMsg>(OnSetPlasmaCurrent);
            subs.Event<ReactorSetDivertorRateMsg>(OnSetDivertorRate);
            subs.Event<ReactorSetCoilTargetMsg>(OnSetCoilTarget);
        });
    }

    private void OnMapInit(Entity<ReactorComponent> ent, ref MapInitEvent args)
    {
        var comp = ent.Comp;
        comp.CoilTargets = NewList(comp.CoilSegmentCount, 0f);
        comp.CoilStrength = NewList(comp.CoilSegmentCount, 0f);
        comp.CoilLocalHeat = NewList(comp.CoilSegmentCount, 0f);
        Dirty(ent);
    }

    private static List<float> NewList(int count, float value)
    {
        var list = new List<float>(count);
        for (var i = 0; i < count; i++)
            list.Add(value);
        return list;
    }

    private void OnIdCardChanged(Entity<ReactorComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        RefreshIdCard(ent);
    }

    private void OnIdCardChanged(Entity<ReactorComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        RefreshIdCard(ent);
    }

    private void RefreshIdCard(Entity<ReactorComponent> ent)
    {
        var comp = ent.Comp;
        comp.InsertedIdName = comp.IdCardSlot.Item is { Valid: true } item
            ? MetaData(item).EntityName
            : null;
        Dirty(ent);
    }

    private bool IsLoggedIn(Entity<ReactorComponent> ent, EntityUid actor)
    {
        if (ent.Comp.InsertedIdName != null)
            return true;

        _popup.PopupEntity(Loc.GetString("reactor-popup-not-logged-in"), ent, actor);
        return false;
    }

    private void OnStart(Entity<ReactorComponent> ent, ref ReactorStartMsg args)
    {
        var comp = ent.Comp;

        if (!IsLoggedIn(ent, args.Actor))
            return;

        if (comp.State != ReactorState.Offline && comp.State != ReactorState.Scrammed)
        {
            _popup.PopupEntity(Loc.GetString("reactor-popup-already-running"), ent, args.Actor);
            return;
        }

        if (comp.LockedUntil is { } lockedUntil && lockedUntil > _timing.CurTime)
        {
            _popup.PopupEntity(Loc.GetString("reactor-popup-locked-out"), ent, args.Actor);
            return;
        }

        comp.State = ReactorState.Starting;
        comp.StartupStep = ReactorStartupStep.SystemsCheck;
        comp.StepElapsed = 0f;
        comp.Alarm = null;
        comp.FuelingTarget = 0f;
        comp.HeatingTarget = 0f;
        comp.DivertorTarget = 0f;
        comp.PlasmaCurrentTarget = 0f;
        for (var i = 0; i < comp.CoilTargets.Count; i++)
            comp.CoilTargets[i] = 0f;

        Dirty(ent);
    }

    private void OnShutdown(Entity<ReactorComponent> ent, ref ReactorShutdownMsg args)
    {
        var comp = ent.Comp;

        if (!IsLoggedIn(ent, args.Actor))
            return;

        if (comp.State != ReactorState.Online)
        {
            _popup.PopupEntity(Loc.GetString("reactor-popup-not-running"), ent, args.Actor);
            return;
        }

        comp.State = ReactorState.ShuttingDown;
        comp.ShutdownStep = ReactorShutdownStep.Taper;
        comp.StepElapsed = 0f;
        Dirty(ent);
    }

    private void OnScram(Entity<ReactorComponent> ent, ref ReactorScramMsg args)
    {
        var comp = ent.Comp;

        if (!IsLoggedIn(ent, args.Actor))
            return;

        if (comp.State is not (ReactorState.Starting or ReactorState.Online or ReactorState.ShuttingDown))
            return;

        comp.FuelingRate = 0f;
        comp.FuelingTarget = 0f;
        comp.HeatingPower = 0f;
        comp.HeatingTarget = 0f;
        comp.PlasmaCurrentTarget = 0f;
        comp.PlasmaCurrent = 0f;
        comp.DivertorRate = 0f;
        comp.DivertorTarget = 0f;
        for (var i = 0; i < comp.CoilTargets.Count; i++)
        {
            comp.CoilTargets[i] = 0f;
            comp.CoilStrength[i] = 0f;
            comp.CoilLocalHeat[i] = 0f;
        }

        comp.Integrity = MathF.Max(0f, comp.Integrity - comp.ScramIntegrityCost);
        comp.State = ReactorState.Scrammed;
        comp.StartupStep = ReactorStartupStep.None;
        comp.ShutdownStep = ReactorShutdownStep.None;
        comp.LockedUntil = _timing.CurTime + comp.ScramLockout;
        comp.PowerOutput = 0f;
        comp.Alarm = "reactor-alarm-scrammed";

        if (TryComp<PowerSupplierComponent>(ent, out var supplier))
        {
            supplier.Enabled = false;
            supplier.MaxSupply = 0f;
        }

        _chat.DispatchStationAnnouncement(ent, Loc.GetString("reactor-announcement-scram"),
            Loc.GetString("reactor-announcer"), colorOverride: Color.Orange);

        Dirty(ent);

        if (comp.Integrity <= 0f)
            Disrupt(ent);
    }

    private void OnSetMode(Entity<ReactorComponent> ent, ref ReactorSetModeMsg args)
    {
        if (!IsLoggedIn(ent, args.Actor))
            return;

        if (ent.Comp.State != ReactorState.Online)
            return;

        ent.Comp.Mode = args.Mode;
        Dirty(ent);
    }

    private bool CanManuallyEdit(Entity<ReactorComponent> ent, EntityUid actor)
    {
        if (!IsLoggedIn(ent, actor))
            return false;

        if (ent.Comp.State == ReactorState.Online && ent.Comp.Mode == ReactorMode.Manual)
            return true;

        _popup.PopupEntity(Loc.GetString("reactor-popup-not-manual"), ent, actor);
        return false;
    }

    private void OnSetFuelingRate(Entity<ReactorComponent> ent, ref ReactorSetFuelingRateMsg args)
    {
        if (!CanManuallyEdit(ent, args.Actor))
            return;

        ent.Comp.FuelingTarget = Math.Clamp(args.Value, 0f, 1f);
        Dirty(ent);
    }

    private void OnSetHeatingPower(Entity<ReactorComponent> ent, ref ReactorSetHeatingPowerMsg args)
    {
        if (!CanManuallyEdit(ent, args.Actor))
            return;

        ent.Comp.HeatingTarget = Math.Clamp(args.Value, 0f, 1f);
        Dirty(ent);
    }

    private void OnSetPlasmaCurrent(Entity<ReactorComponent> ent, ref ReactorSetPlasmaCurrentMsg args)
    {
        if (!CanManuallyEdit(ent, args.Actor))
            return;

        ent.Comp.PlasmaCurrentTarget = Math.Clamp(args.Value, 0f, 1f);
        Dirty(ent);
    }

    private void OnSetDivertorRate(Entity<ReactorComponent> ent, ref ReactorSetDivertorRateMsg args)
    {
        if (!CanManuallyEdit(ent, args.Actor))
            return;

        ent.Comp.DivertorTarget = Math.Clamp(args.Value, 0f, 1f);
        Dirty(ent);
    }

    private void OnSetCoilTarget(Entity<ReactorComponent> ent, ref ReactorSetCoilTargetMsg args)
    {
        if (!CanManuallyEdit(ent, args.Actor))
            return;

        if (args.Index < 0 || args.Index >= ent.Comp.CoilTargets.Count)
            return;

        ent.Comp.CoilTargets[args.Index] = Math.Clamp(args.Value, 0f, 1f);
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ReactorComponent, PowerSupplierComponent>();
        while (query.MoveNext(out var uid, out var comp, out var supplier))
        {
            comp.UpdateAccumulator += frameTime;
            if (comp.UpdateAccumulator < comp.UpdateTimer)
                continue;

            var dt = comp.UpdateAccumulator;
            comp.UpdateAccumulator = 0f;

            Tick((uid, comp), supplier, dt);
        }
    }

    private void Tick(Entity<ReactorComponent> ent, PowerSupplierComponent supplier, float dt)
    {
        var (uid, comp) = ent;

        switch (comp.State)
        {
            case ReactorState.Offline:
            case ReactorState.Scrammed:
                supplier.Enabled = false;
                supplier.MaxSupply = 0f;
                return;
            case ReactorState.Melted:
                return;
            case ReactorState.Starting:
                ProcessStartup(ent, dt);
                break;
            case ReactorState.ShuttingDown:
                ProcessShutdown(ent, dt);
                break;
        }

        if (comp.State is ReactorState.Offline or ReactorState.Melted or ReactorState.Scrammed)
        {
            Dirty(ent);
            return;
        }

        comp.LoadFactor = GetGridDemand(uid);

        if (comp.State == ReactorState.Online && comp.Mode == ReactorMode.Automatic)
            RunAutomaticControl(ent, dt);

        RunPhysics(ent, dt);
        ApplyDamage(ent, dt);
        UpdateAlarm(ent);

        supplier.Enabled = comp.State == ReactorState.Online;
        supplier.MaxSupply = comp.PowerOutput;

        Dirty(ent);

        if (comp.Integrity <= 0f)
            Disrupt(ent);
    }

    private float GetGridDemand(EntityUid uid)
    {
        var grid = Transform(uid).GridUid;
        if (grid == null)
            return 0f;

        var total = 0f;
        var query = EntityQueryEnumerator<PowerConsumerComponent, TransformComponent>();
        while (query.MoveNext(out _, out var consumer, out var xform))
        {
            if (xform.GridUid == grid)
                total += consumer.DrawRate;
        }

        return total;
    }

    private void ProcessStartup(Entity<ReactorComponent> ent, float dt)
    {
        var comp = ent.Comp;
        comp.StepElapsed += dt;
        if (comp.StepElapsed < (float) comp.StepDuration.TotalSeconds)
            return;

        comp.StepElapsed = 0f;

        switch (comp.StartupStep)
        {
            case ReactorStartupStep.SystemsCheck:
                comp.StartupStep = ReactorStartupStep.EvacuateAsh;
                break;
            case ReactorStartupStep.EvacuateAsh:
                comp.AshLevel = 0f;
                comp.StartupStep = ReactorStartupStep.ConfinementField;
                break;
            case ReactorStartupStep.ConfinementField:
                for (var i = 0; i < comp.CoilTargets.Count; i++)
                    comp.CoilTargets[i] = 0.3f;
                comp.StartupStep = ReactorStartupStep.CurrentRamp;
                break;
            case ReactorStartupStep.CurrentRamp:
                comp.PlasmaCurrentTarget = 0.25f;
                comp.StartupStep = ReactorStartupStep.Fueling;
                break;
            case ReactorStartupStep.Fueling:
                comp.FuelingTarget = 0.15f;
                comp.DivertorTarget = 0.2f;
                comp.StartupStep = ReactorStartupStep.HeatingIgnition;
                break;
            case ReactorStartupStep.HeatingIgnition:
                comp.HeatingTarget = 0.6f;
                comp.StartupStep = ReactorStartupStep.RampToOperating;
                break;
            case ReactorStartupStep.RampToOperating:
                comp.StartupStep = ReactorStartupStep.None;
                comp.State = ReactorState.Online;
                _chat.DispatchStationAnnouncement(ent, Loc.GetString("reactor-announcement-online"),
                    Loc.GetString("reactor-announcer"), colorOverride: Color.LightGreen);
                break;
        }
    }

    private void ProcessShutdown(Entity<ReactorComponent> ent, float dt)
    {
        var comp = ent.Comp;
        comp.StepElapsed += dt;
        if (comp.StepElapsed < (float) comp.StepDuration.TotalSeconds)
            return;

        comp.StepElapsed = 0f;

        switch (comp.ShutdownStep)
        {
            case ReactorShutdownStep.Taper:
                comp.FuelingTarget = 0f;
                comp.HeatingTarget = 0f;
                comp.ShutdownStep = ReactorShutdownStep.CurrentRampDown;
                break;
            case ReactorShutdownStep.CurrentRampDown:
                comp.PlasmaCurrentTarget = 0f;
                comp.ShutdownStep = ReactorShutdownStep.RelaxCoils;
                break;
            case ReactorShutdownStep.RelaxCoils:
                for (var i = 0; i < comp.CoilTargets.Count; i++)
                    comp.CoilTargets[i] = 0f;
                comp.ShutdownStep = ReactorShutdownStep.Purge;
                break;
            case ReactorShutdownStep.Purge:
                comp.DivertorRate = 0f;
                comp.DivertorTarget = 0f;
                comp.AshLevel = 0f;
                comp.Temperature = 0f;
                comp.ShutdownStep = ReactorShutdownStep.None;
                comp.State = ReactorState.Offline;
                comp.Mode = ReactorMode.Automatic;
                break;
        }
    }

    private void RunAutomaticControl(Entity<ReactorComponent> ent, float dt)
    {
        var comp = ent.Comp;

        var demandFraction = comp.MaxOutput > 0f ? Math.Clamp(comp.LoadFactor / comp.MaxOutput, 0f, 1f) : 0f;
        var target = Math.Clamp(demandFraction + 0.15f, 0f, comp.AutomaticSafetyMargin);

        comp.FuelingTarget = target;
        comp.HeatingTarget = target;
        comp.PlasmaCurrentTarget = Math.Clamp(target + 0.2f, 0f, 1f);
        comp.DivertorTarget = Math.Clamp(comp.FuelingRate * comp.HeatingPower + 0.2f, 0f, 1f);

        var coilTarget = Math.Clamp(target + 0.25f, 0f, 1f);
        for (var i = 0; i < comp.CoilTargets.Count; i++)
            comp.CoilTargets[i] = coilTarget;
    }

    private void RunPhysics(Entity<ReactorComponent> ent, float dt)
    {
        var comp = ent.Comp;

        var rateStep = comp.RateRampRate * dt;
        comp.FuelingRate = MoveToward(comp.FuelingRate, comp.FuelingTarget, rateStep);
        comp.HeatingPower = MoveToward(comp.HeatingPower, comp.HeatingTarget, rateStep);
        comp.DivertorRate = MoveToward(comp.DivertorRate, comp.DivertorTarget, rateStep);

        comp.PlasmaCurrent = MoveToward(comp.PlasmaCurrent, comp.PlasmaCurrentTarget, comp.MaxCurrentRampRate * dt);

        for (var i = 0; i < comp.CoilStrength.Count; i++)
            comp.CoilStrength[i] = MoveToward(comp.CoilStrength[i], comp.CoilTargets[i], comp.MaxCoilRampRate * dt);

        var avgCoil = 0f;
        foreach (var coil in comp.CoilStrength)
            avgCoil += coil;
        avgCoil = comp.CoilStrength.Count > 0 ? avgCoil / comp.CoilStrength.Count : 0f;

        var densityLimit = 0.35f + comp.PlasmaCurrent * 0.9f;
        var rawDensity = comp.FuelingRate * (1f + comp.AshLevel * 0.5f);
        comp.Density = rawDensity / densityLimit;

        var targetTemp = comp.HeatingPower * 70f;
        comp.Temperature += (targetTemp - comp.Temperature) * dt * 0.25f;

        var reactionPower = 0f;
        if (comp.Temperature > comp.IgnitionTemperature && comp.Density > 0f)
        {
            reactionPower = (comp.Temperature - comp.IgnitionTemperature) * comp.Density * 0.03f;
            comp.Temperature += reactionPower * dt;
        }

        var cooling = comp.DivertorRate * 35f + 4f;
        comp.Temperature = Math.Clamp(comp.Temperature - cooling * dt * 0.2f, 0f, 200f);

        var pressure = comp.Density * comp.Temperature / 100f;
        comp.Beta = pressure / Math.Max(avgCoil, 0.05f);

        comp.AshLevel = Math.Clamp(
            comp.AshLevel + comp.AshAccumulationRate * (comp.FuelingRate * comp.HeatingPower) * dt - comp.AshClearRate * comp.DivertorRate * dt,
            0f, 1f);

        var outputFraction = Math.Clamp(reactionPower * (1f - comp.AshLevel * comp.AshOutputPenalty), 0f, 1f);
        comp.PowerOutput = outputFraction * comp.MaxOutput;

        for (var i = 0; i < comp.CoilLocalHeat.Count; i++)
        {
            var deficit = Math.Max(0f, pressure - comp.CoilStrength[i]);
            comp.CoilLocalHeat[i] = Math.Clamp(deficit, 0f, 1f);
        }
    }

    private void ApplyDamage(Entity<ReactorComponent> ent, float dt)
    {
        var comp = ent.Comp;

        if (comp.Beta > 1f)
            comp.Integrity -= comp.BetaDamageRate * (comp.Beta - 1f) * dt;

        if (comp.Density > 1f)
            comp.Integrity -= comp.DensityDamageRate * (comp.Density - 1f) * dt;

        foreach (var localHeat in comp.CoilLocalHeat)
        {
            if (localHeat > 0.5f)
                comp.Integrity -= comp.CoilImbalanceDamageRate * (localHeat - 0.5f) * dt;
        }

        comp.Integrity = Math.Clamp(comp.Integrity, 0f, 100f);
    }

    private void UpdateAlarm(Entity<ReactorComponent> ent)
    {
        var comp = ent.Comp;
        var severity = GetSeverity(comp.Integrity);
        if (severity == comp.LastAnnouncedSeverity)
            return;

        comp.LastAnnouncedSeverity = severity;

        switch (severity)
        {
            case ReactorSeverity.Warning:
                comp.Alarm = "reactor-alarm-warning";
                _chat.DispatchStationAnnouncement(ent, Loc.GetString("reactor-announcement-warning"),
                    Loc.GetString("reactor-announcer"), colorOverride: Color.Yellow);
                break;
            case ReactorSeverity.Critical:
                comp.Alarm = "reactor-alarm-critical";
                _chat.DispatchStationAnnouncement(ent, Loc.GetString("reactor-announcement-critical"),
                    Loc.GetString("reactor-announcer"), colorOverride: Color.Red);
                break;
            default:
                comp.Alarm = null;
                break;
        }
    }

    private void Disrupt(Entity<ReactorComponent> ent)
    {
        var (uid, comp) = ent;
        if (comp.State == ReactorState.Melted)
            return;

        comp.State = ReactorState.Melted;
        Dirty(ent);

        _chat.DispatchStationAnnouncement(uid, Loc.GetString("reactor-announcement-disruption"),
            Loc.GetString("reactor-announcer"), colorOverride: Color.Red);

        var coords = Transform(uid).Coordinates;
        Spawn(DestroyedReactorProto, coords);

        if (HasComp<ExplosiveComponent>(uid))
            _explosion.TriggerExplosive(uid);
        else
            QueueDel(uid);
    }

    private static float MoveToward(float current, float target, float maxDelta)
    {
        if (maxDelta <= 0f)
            return current;

        return current < target
            ? Math.Min(current + maxDelta, target)
            : Math.Max(current - maxDelta, target);
    }
}

using Content.Server.Chat.Systems;
using Content.Server.Emp;
using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Misfits.Reactor;
using Content.Shared.Radiation.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Reactor;

public sealed class ReactorSystem : SharedReactorSystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly PowerNetSystem _powerNet = default!;

    private static readonly EntProtoId DestroyedReactorProto = "N14GeneratorReactorFloorDestroyed";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReactorComponent, ComponentInit>(OnComponentInit);

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

    private void OnComponentInit(Entity<ReactorComponent> ent, ref ComponentInit args)
    {
        // Done on init rather than map init so reactors loaded from an already-initialized map still get
        // one entry per coil segment instead of empty lists.
        var comp = ent.Comp;
        ResizeList(comp.CoilTargets, comp.CoilSegmentCount);
        ResizeList(comp.CoilStrength, comp.CoilSegmentCount);
        ResizeList(comp.CoilLocalHeat, comp.CoilSegmentCount);
    }

    private static void ResizeList(List<float> list, int count)
    {
        if (list.Count > count)
            list.RemoveRange(count, list.Count - count);

        while (list.Count < count)
            list.Add(0f);
    }

    /// <summary>
    ///     Zeroes every control and plasma readout so an offline reactor doesn't keep showing its last live values.
    /// </summary>
    private static void ResetOperatingState(ReactorComponent comp, bool clearAsh)
    {
        comp.FuelingRate = 0f;
        comp.FuelingTarget = 0f;
        comp.HeatingPower = 0f;
        comp.HeatingTarget = 0f;
        comp.PlasmaCurrent = 0f;
        comp.PlasmaCurrentTarget = 0f;
        comp.DivertorRate = 0f;
        comp.DivertorTarget = 0f;

        for (var i = 0; i < comp.CoilTargets.Count; i++)
            comp.CoilTargets[i] = 0f;
        for (var i = 0; i < comp.CoilStrength.Count; i++)
            comp.CoilStrength[i] = 0f;
        for (var i = 0; i < comp.CoilLocalHeat.Count; i++)
            comp.CoilLocalHeat[i] = 0f;

        comp.Density = 0f;
        comp.Temperature = 0f;
        comp.Beta = 0f;
        comp.PowerOutput = 0f;
        comp.LoadFactor = 0f;
        comp.AutoDerated = false;
        comp.ActiveFaults.Clear();

        if (clearAsh)
            comp.AshLevel = 0f;
    }

    private static string? GetAlarm(ReactorSeverity severity)
    {
        return severity switch
        {
            ReactorSeverity.Warning => "reactor-alarm-warning",
            ReactorSeverity.Critical => "reactor-alarm-critical",
            _ => null,
        };
    }

    private void OnStart(Entity<ReactorComponent> ent, ref ReactorStartMsg args)
    {
        var comp = ent.Comp;

        if (comp.State != ReactorState.Offline && comp.State != ReactorState.Scrammed)
            return;

        if (comp.LockedUntil is { } lockedUntil && lockedUntil > _timing.CurTime)
            return;

        ResetOperatingState(comp, clearAsh: false);
        comp.State = ReactorState.Starting;
        comp.StartupStep = ReactorStartupStep.SystemsCheck;
        comp.Mode = ReactorMode.Automatic;
        comp.StepElapsed = 0f;

        // Integrity carries over between runs, so re-raise any standing alarm without re-announcing it.
        comp.LastAnnouncedSeverity = GetSeverity(comp.Integrity);
        comp.Alarm = GetAlarm(comp.LastAnnouncedSeverity);

        Dirty(ent);
    }

    private void OnShutdown(Entity<ReactorComponent> ent, ref ReactorShutdownMsg args)
    {
        var comp = ent.Comp;

        // Shutting down from Starting aborts the startup, so a stalled ignition doesn't force a SCRAM.
        if (comp.State is not (ReactorState.Online or ReactorState.Starting))
        {
            RaiseLocalEvent(ent.Owner, new ReactorCommandRejectedEvent(args.Actor, "reactor-popup-not-running"));
            return;
        }

        comp.State = ReactorState.ShuttingDown;
        comp.StartupStep = ReactorStartupStep.None;
        comp.ShutdownStep = ReactorShutdownStep.Taper;
        comp.StepElapsed = 0f;
        Dirty(ent);
    }

    private void OnScram(Entity<ReactorComponent> ent, ref ReactorScramMsg args)
    {
        var comp = ent.Comp;

        if (comp.State is not (ReactorState.Starting or ReactorState.Online or ReactorState.ShuttingDown))
            return;

        ResetOperatingState(comp, clearAsh: false);

        // A SCRAM is the emergency stop, so it must never be the thing that finishes the reactor off.
        comp.Integrity = MathF.Max(MathF.Min(comp.Integrity, 1f), comp.Integrity - comp.ScramIntegrityCost);
        comp.State = ReactorState.Scrammed;
        comp.Mode = ReactorMode.Automatic;
        comp.StartupStep = ReactorStartupStep.None;
        comp.ShutdownStep = ReactorShutdownStep.None;
        comp.LockedUntil = _timing.CurTime + comp.ScramLockout;
        comp.Alarm = "reactor-alarm-scrammed";

        if (TryComp<PowerSupplierComponent>(ent, out var supplier))
        {
            supplier.Enabled = false;
            supplier.MaxSupply = 0f;
        }

        _chat.DispatchStationAnnouncement(ent, Loc.GetString("reactor-announcement-scram"),
            Loc.GetString("reactor-announcer"), colorOverride: Color.Orange);

        TriggerScramEmp(ent);

        Dirty(ent);
    }

    private void TriggerScramEmp(Entity<ReactorComponent> ent)
    {
        var members = ReactorPowerNetworkHelper.GetNetworkMembers(EntityManager, _nodeContainer, ent.Owner);
        if (members.Count == 0)
        {
            _emp.TryEmpEffects(ent.Owner, ent.Comp.ScramEmpEnergyConsumption, ent.Comp.ScramEmpDuration);
            return;
        }

        foreach (var member in members)
            _emp.TryEmpEffects(member, ent.Comp.ScramEmpEnergyConsumption, ent.Comp.ScramEmpDuration);
    }

    private void OnSetMode(Entity<ReactorComponent> ent, ref ReactorSetModeMsg args)
    {
        if (ent.Comp.State != ReactorState.Online)
            return;

        ent.Comp.Mode = args.Mode;
        Dirty(ent);
    }

    private bool CanManuallyEdit(Entity<ReactorComponent> ent, EntityUid actor)
    {
        if (ent.Comp.State == ReactorState.Online && ent.Comp.Mode == ReactorMode.Manual)
            return true;

        RaiseLocalEvent(ent.Owner, new ReactorCommandRejectedEvent(actor, "reactor-popup-not-manual"));
        return false;
    }

    private void OnSetFuelingRate(Entity<ReactorComponent> ent, ref ReactorSetFuelingRateMsg args)
    {
        if (ent.Comp.State == ReactorState.Starting)
        {
            var value = Math.Clamp(args.Value, 0f, 1f);
            HandleStartupCommand(ent, args.Actor, ReactorStartupStep.Fueling, comp => comp.FuelingTarget = value);
            return;
        }

        if (!CanManuallyEdit(ent, args.Actor))
            return;

        ent.Comp.FuelingTarget = Math.Clamp(args.Value, 0f, 1f);
        Dirty(ent);
    }

    private void OnSetHeatingPower(Entity<ReactorComponent> ent, ref ReactorSetHeatingPowerMsg args)
    {
        var comp = ent.Comp;

        if (comp.State == ReactorState.Starting &&
            comp.StartupStep is ReactorStartupStep.HeatingIgnition or ReactorStartupStep.RampToOperating)
        {
            comp.HeatingTarget = Math.Clamp(args.Value, 0f, 1f);
            if (comp.StartupStep == ReactorStartupStep.HeatingIgnition)
            {
                comp.StepElapsed = 0f;
                comp.StartupStep = ReactorStartupStep.RampToOperating;
            }

            Dirty(ent);
            return;
        }

        if (!CanManuallyEdit(ent, args.Actor))
            return;

        ent.Comp.HeatingTarget = Math.Clamp(args.Value, 0f, 1f);
        Dirty(ent);
    }

    private void OnSetPlasmaCurrent(Entity<ReactorComponent> ent, ref ReactorSetPlasmaCurrentMsg args)
    {
        if (ent.Comp.State == ReactorState.Starting)
        {
            var value = Math.Clamp(args.Value, 0f, 1f);
            HandleStartupCommand(ent, args.Actor, ReactorStartupStep.CurrentRamp, comp => comp.PlasmaCurrentTarget = value);
            return;
        }

        if (!CanManuallyEdit(ent, args.Actor))
            return;

        ent.Comp.PlasmaCurrentTarget = Math.Clamp(args.Value, 0f, 1f);
        Dirty(ent);
    }

    private void OnSetDivertorRate(Entity<ReactorComponent> ent, ref ReactorSetDivertorRateMsg args)
    {
        if (ent.Comp.State == ReactorState.Starting)
        {
            var value = Math.Clamp(args.Value, 0f, 1f);
            HandleStartupCommand(ent, args.Actor, ReactorStartupStep.EvacuateAsh, comp =>
            {
                comp.DivertorTarget = value;
                comp.AshLevel = 0f;
            });
            return;
        }

        if (!CanManuallyEdit(ent, args.Actor))
            return;

        ent.Comp.DivertorTarget = Math.Clamp(args.Value, 0f, 1f);
        Dirty(ent);
    }

    private void OnSetCoilTarget(Entity<ReactorComponent> ent, ref ReactorSetCoilTargetMsg args)
    {
        if (ent.Comp.State == ReactorState.Starting)
        {
            var value = Math.Clamp(args.Value, 0f, 1f);
            HandleStartupCommand(ent, args.Actor, ReactorStartupStep.ConfinementField, comp =>
            {
                for (var i = 0; i < comp.CoilTargets.Count; i++)
                    comp.CoilTargets[i] = value;
            });
            return;
        }

        if (!CanManuallyEdit(ent, args.Actor))
            return;

        if (args.Index < 0 || args.Index >= ent.Comp.CoilTargets.Count)
            return;

        ent.Comp.CoilTargets[args.Index] = Math.Clamp(args.Value, 0f, 1f);
        Dirty(ent);
    }

    private void HandleStartupCommand(Entity<ReactorComponent> ent, EntityUid actor, ReactorStartupStep step, Action<ReactorComponent> apply)
    {
        var comp = ent.Comp;

        if (comp.StartupStep != step)
        {
            var expected = GetStartupStepCommand(comp.StartupStep) ?? "—";
            RaiseLocalEvent(ent.Owner, new ReactorCommandRejectedEvent(actor, "reactor-popup-wrong-startup-step",
                new Dictionary<string, string> { ["command"] = expected }));
            return;
        }

        apply(comp);
        comp.StepElapsed = 0f;
        comp.StartupStep = NextStartupStep(step);
        Dirty(ent);
    }

    private static ReactorStartupStep NextStartupStep(ReactorStartupStep step)
    {
        return step switch
        {
            ReactorStartupStep.EvacuateAsh => ReactorStartupStep.ConfinementField,
            ReactorStartupStep.ConfinementField => ReactorStartupStep.CurrentRamp,
            ReactorStartupStep.CurrentRamp => ReactorStartupStep.Fueling,
            ReactorStartupStep.Fueling => ReactorStartupStep.HeatingIgnition,
            ReactorStartupStep.HeatingIgnition => ReactorStartupStep.RampToOperating,
            _ => step,
        };
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
                SetRadiationLeak(uid, 0f);
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

        comp.LoadFactor = GetNetworkDemand(supplier);

        if (comp.State == ReactorState.Online && comp.Mode == ReactorMode.Automatic)
            RunAutomaticControl(ent, dt);

        RunPhysics(ent, dt);
        ApplyDamage(ent, dt);
        UpdateAlarm(ent);

        var leakFraction = comp.Integrity < WarningIntegrity
            ? Math.Clamp((WarningIntegrity - comp.Integrity) / WarningIntegrity, 0f, 1f)
            : 0f;
        SetRadiationLeak(uid, comp.MaxLeakRadiation * leakFraction);

        // Keep supplying through the shutdown sequence so the taper actually ramps the grid down.
        supplier.Enabled = comp.State is ReactorState.Online or ReactorState.ShuttingDown;
        supplier.MaxSupply = comp.PowerOutput;

        Dirty(ent);

        if (comp.Integrity <= 0f)
            Disrupt(ent);
    }

    private float GetNetworkDemand(PowerSupplierComponent supplier)
    {
        if (supplier.Net is not { } net)
            return 0f;

        return _powerNet.GetNetworkStatistics(net.NetworkNode).Consumption;
    }

    private void ProcessStartup(Entity<ReactorComponent> ent, float dt)
    {
        var comp = ent.Comp;

        switch (comp.StartupStep)
        {
            case ReactorStartupStep.SystemsCheck:
                comp.StepElapsed += dt;
                if (comp.StepElapsed < (float) comp.StepDuration.TotalSeconds)
                    return;

                comp.StepElapsed = 0f;
                comp.StartupStep = ReactorStartupStep.EvacuateAsh;
                break;

            case ReactorStartupStep.RampToOperating:
                if (comp.Temperature < comp.IgnitionTemperature)
                    return;

                comp.StartupStep = ReactorStartupStep.None;
                comp.State = ReactorState.Online;
                _chat.DispatchStationAnnouncement(ent, Loc.GetString("reactor-announcement-online"),
                    Loc.GetString("reactor-announcer"), colorOverride: Color.LightGreen);
                break;

            default:
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
                ResetOperatingState(comp, clearAsh: true);
                comp.ShutdownStep = ReactorShutdownStep.None;
                comp.State = ReactorState.Offline;
                comp.Mode = ReactorMode.Automatic;
                break;
        }
    }

    private void RunAutomaticControl(Entity<ReactorComponent> ent, float dt)
    {
        var comp = ent.Comp;
        var integrityDerate = comp.Integrity >= WarningIntegrity
            ? 1f
            : Math.Clamp(comp.Integrity / WarningIntegrity, comp.MinAutomaticOutput, 1f);
        comp.AutoDerated = integrityDerate < 1f;

        var demandFraction = comp.MaxOutput > 0f ? Math.Clamp(comp.LoadFactor / comp.MaxOutput, 0f, 1f) : 0f;
        var outputTarget = Math.Clamp(demandFraction + comp.AutomaticDemandHeadroom, comp.MinAutomaticOutput, integrityDerate);

        comp.PlasmaCurrentTarget = comp.AutomaticCurrentTarget;
        for (var i = 0; i < comp.CoilTargets.Count; i++)
            comp.CoilTargets[i] = comp.AutomaticCoilTarget;

        // Ash is cleared at a fixed rate per divertor percent, so match the production rate and lean on any backlog.
        var ashBalance = comp.AshClearRate > 0f ? comp.AshAccumulationRate / comp.AshClearRate : 1f;
        comp.DivertorTarget = Math.Clamp(ashBalance * comp.FuelingRate * comp.HeatingPower + comp.AshLevel * 2f, 0.1f, 1f);

        // Work back from the output we want to the reaction power, then to a density and temperature that give it.
        var ashMultiplier = Math.Max(1f - comp.AshLevel * comp.AshOutputPenalty, 0.05f);
        var reactionTarget = outputTarget * comp.FullOutputReactionPower / ashMultiplier;

        var densityTarget = comp.AutomaticSafetyMargin;
        var minReactionTemperature = comp.IgnitionTemperature + comp.AutomaticIgnitionMargin;
        var reactionTemperature = comp.IgnitionTemperature + reactionTarget / (densityTarget * ReactionCoefficient);

        // At low demand, thin the plasma rather than letting it cool towards ignition and quench.
        if (reactionTemperature < minReactionTemperature)
        {
            reactionTemperature = minReactionTemperature;
            densityTarget = Math.Clamp(reactionTarget / (comp.AutomaticIgnitionMargin * ReactionCoefficient),
                0.1f, comp.AutomaticSafetyMargin);
        }

        // Keep beta inside the safety margin for the coil strength we actually have right now.
        var betaTemperature = comp.AutomaticSafetyMargin * 100f * Math.Max(Average(comp.CoilStrength), 0.05f) / densityTarget;
        reactionTemperature = Math.Min(reactionTemperature, Math.Max(minReactionTemperature, betaTemperature));

        // The reaction sees the plasma after heating but before cooling, so offset the target to the settled readout.
        var reactionPower = (reactionTemperature - comp.IgnitionTemperature) * densityTarget * ReactionCoefficient;
        var settledTemperature = reactionTemperature + (reactionPower - GetCoolingRate(comp.DivertorRate)) * dt;

        comp.FuelingTarget = Math.Clamp(
            densityTarget * GetDensityLimit(comp.PlasmaCurrent) / (1f + comp.AshLevel * AshDensityFactor), 0f, 1f);

        comp.HeatingTarget = Math.Clamp(
            GetEquilibriumHeat(settledTemperature, comp.DivertorRate, reactionPower) +
            (settledTemperature - comp.Temperature) * comp.AutomaticTemperatureGain, 0f, 1f);
    }

    private void RunPhysics(Entity<ReactorComponent> ent, float dt)
    {
        var comp = ent.Comp;

        var rateStep = comp.RateRampRate * dt;
        comp.FuelingRate = MoveToward(comp.FuelingRate, comp.FuelingTarget, rateStep);
        comp.HeatingPower = MoveToward(comp.HeatingPower, comp.HeatingTarget, rateStep);
        comp.DivertorRate = MoveToward(comp.DivertorRate, comp.DivertorTarget, rateStep);

        comp.PlasmaCurrent = MoveToward(comp.PlasmaCurrent, comp.PlasmaCurrentTarget, comp.MaxCurrentRampRate * dt);

        if (comp.State == ReactorState.Online)
        {
            comp.FuelingRate = ApplyCosmeticNoise(comp.FuelingRate, comp.CosmeticFluctuationMagnitude, dt);
            comp.HeatingPower = ApplyCosmeticNoise(comp.HeatingPower, comp.CosmeticFluctuationMagnitude, dt);
            comp.DivertorRate = ApplyCosmeticNoise(comp.DivertorRate, comp.CosmeticFluctuationMagnitude, dt);
            comp.PlasmaCurrent = ApplyCosmeticNoise(comp.PlasmaCurrent, comp.CosmeticFluctuationMagnitude, dt);
        }

        var instabilityFactor = 1f + (1f - Math.Clamp(comp.Integrity, 0f, 100f) / 100f) * 1.5f;
        var driftMagnitude = comp.State == ReactorState.Online
            ? comp.CoilDriftRate * (0.2f + Math.Clamp(comp.Beta, 0f, 2f) * 0.4f) * instabilityFactor
            : 0f;

        var coilCorrectionRate = comp.MaxCoilRampRate *
            (comp.Mode == ReactorMode.Automatic ? comp.AutomaticCoilCorrectionMultiplier : 1f);

        for (var i = 0; i < comp.CoilStrength.Count; i++)
        {
            comp.CoilStrength[i] = MoveToward(comp.CoilStrength[i], comp.CoilTargets[i], coilCorrectionRate * dt);

            if (driftMagnitude > 0f)
                comp.CoilStrength[i] = Math.Clamp(comp.CoilStrength[i] + (_random.NextFloat() * 2f - 1f) * driftMagnitude * dt, 0f, 1f);
        }

        var avgCoil = Average(comp.CoilStrength);

        var rawDensity = comp.FuelingRate * (1f + comp.AshLevel * AshDensityFactor);
        comp.Density = rawDensity / GetDensityLimit(comp.PlasmaCurrent);

        var targetTemp = comp.HeatingPower * HeatingTemperatureScale;
        comp.Temperature += (targetTemp - comp.Temperature) * dt * HeatingResponse;

        var reactionPower = 0f;
        if (comp.Temperature > comp.IgnitionTemperature && comp.Density > 0f)
        {
            reactionPower = (comp.Temperature - comp.IgnitionTemperature) * comp.Density * ReactionCoefficient;
            comp.Temperature += reactionPower * dt;
        }

        comp.Temperature = Math.Clamp(comp.Temperature - GetCoolingRate(comp.DivertorRate) * dt, 0f, 200f);

        var pressure = comp.Density * comp.Temperature / 100f;
        comp.Beta = pressure / Math.Max(avgCoil, 0.05f);

        comp.AshLevel = Math.Clamp(
            comp.AshLevel + comp.AshAccumulationRate * (comp.FuelingRate * comp.HeatingPower) * dt - comp.AshClearRate * comp.DivertorRate * dt,
            0f, 1f);

        var outputFraction = comp.FullOutputReactionPower > 0f
            ? Math.Clamp(reactionPower / comp.FullOutputReactionPower * (1f - comp.AshLevel * comp.AshOutputPenalty), 0f, 1f)
            : 0f;
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
        comp.ActiveFaults.Clear();

        if (comp.Beta > 1f)
        {
            comp.Integrity -= comp.BetaDamageRate * (comp.Beta - 1f) * dt;
            comp.ActiveFaults.Add("beta");
        }

        if (comp.Density > 1f)
        {
            comp.Integrity -= comp.DensityDamageRate * (comp.Density - 1f) * dt;
            comp.ActiveFaults.Add("density");
        }

        foreach (var localHeat in comp.CoilLocalHeat)
        {
            if (localHeat > 0.5f)
                comp.Integrity -= comp.CoilImbalanceDamageRate * (localHeat - 0.5f) * dt;
        }

        comp.Integrity = Math.Clamp(comp.Integrity, 0f, 100f);
    }

    private void SetRadiationLeak(EntityUid uid, float intensity)
    {
        if (intensity <= 0f)
        {
            if (TryComp<RadiationSourceComponent>(uid, out var existing))
                existing.Enabled = false;
            return;
        }

        var radiation = EnsureComp<RadiationSourceComponent>(uid);
        radiation.Enabled = true;
        radiation.Intensity = intensity;
    }

    private void UpdateAlarm(Entity<ReactorComponent> ent)
    {
        var comp = ent.Comp;
        var severity = GetSeverity(comp.Integrity);
        if (severity == comp.LastAnnouncedSeverity)
            return;

        comp.LastAnnouncedSeverity = severity;

        comp.Alarm = GetAlarm(severity);

        switch (severity)
        {
            case ReactorSeverity.Warning:
                _chat.DispatchStationAnnouncement(ent, Loc.GetString("reactor-announcement-warning"),
                    Loc.GetString("reactor-announcer"), colorOverride: Color.Yellow);
                break;
            case ReactorSeverity.Critical:
                _chat.DispatchStationAnnouncement(ent, Loc.GetString("reactor-announcement-critical"),
                    Loc.GetString("reactor-announcer"), colorOverride: Color.Red);
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

    private static float Average(List<float> values)
    {
        if (values.Count == 0)
            return 0f;

        var total = 0f;
        foreach (var value in values)
            total += value;
        return total / values.Count;
    }

    private float ApplyCosmeticNoise(float value, float magnitude, float dt)
    {
        return Math.Clamp(value + (_random.NextFloat() * 2f - 1f) * magnitude * dt, 0f, 1f);
    }
}

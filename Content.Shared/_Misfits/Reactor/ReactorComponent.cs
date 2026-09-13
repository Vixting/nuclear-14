using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Reactor;

public enum ReactorMode : byte
{
    Automatic,
    Manual,
}

public enum ReactorState : byte
{
    Offline,
    Starting,
    Online,
    ShuttingDown,
    Scrammed,
    Melted,
}

public enum ReactorStartupStep : byte
{
    None,
    SystemsCheck,
    EvacuateAsh,
    ConfinementField,
    CurrentRamp,
    Fueling,
    HeatingIgnition,
    RampToOperating,
}

public enum ReactorShutdownStep : byte
{
    None,
    Taper,
    CurrentRampDown,
    RelaxCoils,
    Purge,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true, fieldDeltas: true)]
[Access(typeof(SharedReactorSystem))]
public sealed partial class ReactorComponent : Component
{
    [DataField]
    public int CoilSegmentCount = 6;

    [DataField]
    public float MaxOutput = 10000f;

    [DataField]
    public float MaxCurrentRampRate = 0.05f;

    [DataField]
    public float MaxCoilRampRate = 0.1f;

    [DataField]
    public float IgnitionTemperature = 40f;

    [DataField]
    public float BetaDamageRate = 6f;

    [DataField]
    public float DensityDamageRate = 6f;

    [DataField]
    public float CoilImbalanceDamageRate = 4f;

    [DataField]
    public float ScramIntegrityCost = 20f;

    [DataField]
    public float AshAccumulationRate = 0.06f;

    [DataField]
    public float AshClearRate = 0.10f;

    [DataField]
    public float AshOutputPenalty = 0.5f;

    [DataField]
    public float AutomaticSafetyMargin = 0.70f;

    [DataField]
    public float MinAutomaticOutput = 0.2f;

    [DataField]
    public float MaxLeakRadiation = 6f;

    [DataField]
    public float RateRampRate = 0.04f;

    [DataField]
    public TimeSpan StepDuration = TimeSpan.FromSeconds(15);

    [DataField]
    public TimeSpan ScramLockout = TimeSpan.FromMinutes(3);

    [DataField]
    public float UpdateTimer = 1f;

    public float UpdateAccumulator;

    public ReactorSeverity LastAnnouncedSeverity = ReactorSeverity.Nominal;

    [AutoNetworkedField]
    public ReactorMode Mode = ReactorMode.Automatic;

    [AutoNetworkedField]
    public ReactorState State = ReactorState.Online;

    [AutoNetworkedField]
    public ReactorStartupStep StartupStep = ReactorStartupStep.None;

    [AutoNetworkedField]
    public ReactorShutdownStep ShutdownStep = ReactorShutdownStep.None;

    [AutoNetworkedField]
    public float StepElapsed;

    [AutoNetworkedField]
    public float FuelingRate;

    [AutoNetworkedField]
    public float FuelingTarget;

    [AutoNetworkedField]
    public float HeatingPower;

    [AutoNetworkedField]
    public float HeatingTarget;

    [AutoNetworkedField]
    public float PlasmaCurrent;

    [AutoNetworkedField]
    public float PlasmaCurrentTarget;

    [AutoNetworkedField]
    public float DivertorRate;

    [AutoNetworkedField]
    public float DivertorTarget;

    [AutoNetworkedField]
    public List<float> CoilTargets = new();

    [AutoNetworkedField]
    public List<float> CoilStrength = new();

    [AutoNetworkedField]
    public List<float> CoilLocalHeat = new();

    [AutoNetworkedField]
    public float Density;

    [AutoNetworkedField]
    public float Temperature;

    [AutoNetworkedField]
    public float Beta;

    [AutoNetworkedField]
    public float AshLevel;

    [AutoNetworkedField]
    public float Integrity = 100f;

    [AutoNetworkedField]
    public float PowerOutput;

    [AutoNetworkedField]
    public float LoadFactor;

    [AutoNetworkedField]
    public TimeSpan? LockedUntil;

    [AutoNetworkedField]
    public string? Alarm;

    [AutoNetworkedField]
    public bool AutoDerated;

    [AutoNetworkedField]
    public List<string> ActiveFaults = new();
}

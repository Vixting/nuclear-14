using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Reactor;

[Serializable, NetSerializable]
public enum ReactorMonitorUiKey
{
    Key,
}

public static class ReactorMonitorConstants
{
    public const string IdCardSlotId = "reactor-monitor-id-card";
}

[Serializable, NetSerializable, DataRecord]
public partial struct ReactorMonitorEntry
{
    public string Name;
    public ReactorState State;
    public float Integrity;
    public float PowerOutput;
    public float MaxOutput;
    public string? Alarm;

    public ReactorMonitorEntry(string name, ReactorState state, float integrity, float powerOutput, float maxOutput, string? alarm)
    {
        Name = name;
        State = state;
        Integrity = integrity;
        PowerOutput = powerOutput;
        MaxOutput = maxOutput;
        Alarm = alarm;
    }
}

[Serializable, NetSerializable]
public sealed class ReactorMonitorState : BoundUserInterfaceState
{
    public readonly string? InsertedIdName;
    public readonly List<ReactorMonitorEntry> Reactors;

    public readonly int SelectedIndex;

    public readonly bool HasSelection;
    public readonly ReactorMode Mode;
    public readonly ReactorState State;
    public readonly ReactorStartupStep StartupStep;
    public readonly ReactorShutdownStep ShutdownStep;
    public readonly float StepElapsed;
    public readonly float StepDurationSeconds;
    public readonly float FuelingRate;
    public readonly float FuelingTarget;
    public readonly float HeatingPower;
    public readonly float HeatingTarget;
    public readonly float PlasmaCurrent;
    public readonly float PlasmaCurrentTarget;
    public readonly float DivertorRate;
    public readonly float DivertorTarget;
    public readonly List<float> CoilTargets;
    public readonly List<float> CoilStrength;
    public readonly List<float> CoilLocalHeat;
    public readonly float Density;
    public readonly float Temperature;
    public readonly float Beta;
    public readonly float AshLevel;
    public readonly float Integrity;
    public readonly float PowerOutput;
    public readonly float LoadFactor;
    public readonly float MaxOutput;
    public readonly string? Alarm;

    public readonly float LockedOutSeconds;

    public ReactorMonitorState(
        string? insertedIdName,
        List<ReactorMonitorEntry> reactors,
        int selectedIndex,
        bool hasSelection,
        ReactorMode mode,
        ReactorState state,
        ReactorStartupStep startupStep,
        ReactorShutdownStep shutdownStep,
        float stepElapsed,
        float stepDurationSeconds,
        float fuelingRate,
        float fuelingTarget,
        float heatingPower,
        float heatingTarget,
        float plasmaCurrent,
        float plasmaCurrentTarget,
        float divertorRate,
        float divertorTarget,
        List<float> coilTargets,
        List<float> coilStrength,
        List<float> coilLocalHeat,
        float density,
        float temperature,
        float beta,
        float ashLevel,
        float integrity,
        float powerOutput,
        float loadFactor,
        float maxOutput,
        string? alarm,
        float lockedOutSeconds = 0f)
    {
        InsertedIdName = insertedIdName;
        Reactors = reactors;
        SelectedIndex = selectedIndex;
        HasSelection = hasSelection;
        Mode = mode;
        State = state;
        StartupStep = startupStep;
        ShutdownStep = shutdownStep;
        StepElapsed = stepElapsed;
        StepDurationSeconds = stepDurationSeconds;
        FuelingRate = fuelingRate;
        FuelingTarget = fuelingTarget;
        HeatingPower = heatingPower;
        HeatingTarget = heatingTarget;
        PlasmaCurrent = plasmaCurrent;
        PlasmaCurrentTarget = plasmaCurrentTarget;
        DivertorRate = divertorRate;
        DivertorTarget = divertorTarget;
        CoilTargets = coilTargets;
        CoilStrength = coilStrength;
        CoilLocalHeat = coilLocalHeat;
        Density = density;
        Temperature = temperature;
        Beta = beta;
        AshLevel = ashLevel;
        Integrity = integrity;
        PowerOutput = powerOutput;
        LoadFactor = loadFactor;
        MaxOutput = maxOutput;
        Alarm = alarm;
        LockedOutSeconds = lockedOutSeconds;
    }
}

[Serializable, NetSerializable]
public sealed class ReactorMonitorSelectMsg(int index) : BoundUserInterfaceMessage
{
    public int Index = index;
}

[Serializable, NetSerializable]
public enum ReactorMonitorAllCommand : byte
{
    Start,
    Shutdown,
    Scram,
    SetMode,
    SetFuelingRate,
    SetHeatingPower,
    SetPlasmaCurrent,
    SetDivertorRate,
}

[Serializable, NetSerializable]
public sealed class ReactorMonitorAllMsg(ReactorMonitorAllCommand command, float value = 0f, ReactorMode mode = ReactorMode.Automatic) : BoundUserInterfaceMessage
{
    public ReactorMonitorAllCommand Command = command;
    public float Value = value;
    public ReactorMode Mode = mode;
}

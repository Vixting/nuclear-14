using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Reactor;

[Serializable, NetSerializable]
public enum ReactorMonitorUiKey
{
    Key,
}

[Serializable, NetSerializable, DataRecord]
public partial struct ReactorMonitorEntry
{
    public NetEntity Reactor;
    public string Name;
    public ReactorState State;
    public float Integrity;
    public float PowerOutput;
    public float MaxOutput;
    public string? Alarm;

    public ReactorMonitorEntry(
        NetEntity reactor,
        string name,
        ReactorState state,
        float integrity,
        float powerOutput,
        float maxOutput,
        string? alarm)
    {
        Reactor = reactor;
        Name = name;
        State = state;
        Integrity = integrity;
        PowerOutput = powerOutput;
        MaxOutput = maxOutput;
        Alarm = alarm;
    }
}

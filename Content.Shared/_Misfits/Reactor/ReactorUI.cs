using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Reactor;

[Serializable, NetSerializable]
public enum ReactorUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ReactorStartMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ReactorShutdownMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ReactorScramMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ReactorSetModeMsg(ReactorMode mode) : BoundUserInterfaceMessage
{
    public ReactorMode Mode = mode;
}

[Serializable, NetSerializable]
public sealed class ReactorSetFuelingRateMsg(float value) : BoundUserInterfaceMessage
{
    public float Value = value;
}

[Serializable, NetSerializable]
public sealed class ReactorSetHeatingPowerMsg(float value) : BoundUserInterfaceMessage
{
    public float Value = value;
}

[Serializable, NetSerializable]
public sealed class ReactorSetPlasmaCurrentMsg(float value) : BoundUserInterfaceMessage
{
    public float Value = value;
}

[Serializable, NetSerializable]
public sealed class ReactorSetDivertorRateMsg(float value) : BoundUserInterfaceMessage
{
    public float Value = value;
}

[Serializable, NetSerializable]
public sealed class ReactorSetCoilTargetMsg(int index, float value) : BoundUserInterfaceMessage
{
    public int Index = index;
    public float Value = value;
}

[Serializable, NetSerializable]
public sealed class ReactorGroupStatusRequestMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ReactorGroupStatusMsg(List<ReactorMonitorEntry> reactors) : BoundUserInterfaceMessage
{
    public List<ReactorMonitorEntry> Reactors = reactors;
}

[Serializable, NetSerializable]
public sealed class ReactorGroupSelectMsg(int index) : BoundUserInterfaceMessage
{
    public int Index = index;
}

[Serializable, NetSerializable]
public enum ReactorGroupCommand : byte
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
public sealed class ReactorGroupBroadcastMsg(ReactorGroupCommand command, float value = 0f, ReactorMode mode = ReactorMode.Automatic) : BoundUserInterfaceMessage
{
    public ReactorGroupCommand Command = command;
    public float Value = value;
    public ReactorMode Mode = mode;
}

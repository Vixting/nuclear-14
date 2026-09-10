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

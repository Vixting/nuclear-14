using Robust.Shared.Serialization;

namespace Content.Shared.Medical.Cryogenics;

[Serializable, NetSerializable]
public sealed class CryoPodSetRunningMessage : BoundUserInterfaceMessage
{
    public readonly bool Running;

    public CryoPodSetRunningMessage(bool running)
    {
        Running = running;
    }
}

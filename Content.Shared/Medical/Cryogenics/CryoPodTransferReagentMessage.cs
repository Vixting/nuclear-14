using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical.Cryogenics;

[Serializable, NetSerializable]
public sealed class CryoPodTransferReagentMessage : BoundUserInterfaceMessage
{
    public readonly ReagentId ReagentId;

    public readonly bool FromBuffer;

    public CryoPodTransferReagentMessage(ReagentId reagentId, bool fromBuffer)
    {
        ReagentId = reagentId;
        FromBuffer = fromBuffer;
    }
}

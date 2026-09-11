using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Reactor;

[RegisterComponent, Access(typeof(ReactorMonitorSystem))]
public sealed partial class ReactorMonitorComponent : Component
{
    [DataField]
    public List<ProtoId<TagPrototype>> TrackedTags = new();
}

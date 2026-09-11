using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Reactor;

[Prototype]
public sealed partial class ReactorCommandPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Usage = string.Empty;

    [DataField(required: true)]
    public string Description = string.Empty;

    [DataField]
    public int Order;
}

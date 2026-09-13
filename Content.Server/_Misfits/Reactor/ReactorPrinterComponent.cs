using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Reactor;

[RegisterComponent]
public sealed partial class ReactorPrinterComponent : Component
{
    [DataField]
    public EntProtoId PaperPrototype = "PaperReactorPrintout";
}

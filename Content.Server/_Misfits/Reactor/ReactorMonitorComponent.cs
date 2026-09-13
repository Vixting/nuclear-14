using Content.Shared._Misfits.Reactor;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Reactor;

[RegisterComponent, Access(typeof(ReactorMonitorSystem), typeof(ReactorPrinterLinkSystem))]
public sealed partial class ReactorMonitorComponent : Component
{
    [DataField]
    public List<ProtoId<TagPrototype>> TrackedTags = new();

    public static readonly string IdCardSlotId = ReactorMonitorConstants.IdCardSlotId;

    [DataField]
    public ItemSlot IdCardSlot = new()
    {
        Whitelist = new EntityWhitelist { Components = new[] { "IdCard" } },
    };

    public string? InsertedIdName;

    public EntityUid? Selected;

    public EntityUid? LinkedPrinter;
}

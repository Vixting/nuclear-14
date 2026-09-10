using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Misfits.Reactor;

public enum ReactorSeverity : byte
{
    Nominal,
    Warning,
    Critical,
}

public abstract class SharedReactorSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public const float WarningIntegrity = 66f;
    public const float CriticalIntegrity = 33f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReactorComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<ReactorComponent, ComponentRemove>(OnComponentRemove);
    }

    private void OnComponentInit(Entity<ReactorComponent> ent, ref ComponentInit args)
    {
        _itemSlots.AddItemSlot(ent, ReactorComponent.IdCardSlotId, ent.Comp.IdCardSlot);
    }

    private void OnComponentRemove(Entity<ReactorComponent> ent, ref ComponentRemove args)
    {
        _itemSlots.RemoveItemSlot(ent, ent.Comp.IdCardSlot);
    }

    public static ReactorSeverity GetSeverity(float integrity)
    {
        if (integrity <= CriticalIntegrity)
            return ReactorSeverity.Critical;

        return integrity <= WarningIntegrity ? ReactorSeverity.Warning : ReactorSeverity.Nominal;
    }
}

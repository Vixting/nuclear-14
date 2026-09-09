using Content.Shared._Misfits.Reactor;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.Reactor;

public sealed class ReactorSystem : SharedReactorSystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReactorComponent, AfterAutoHandleStateEvent>(OnHandleState);
    }

    private void OnHandleState(Entity<ReactorComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_ui.TryGetOpenUi<ReactorBui>(ent.Owner, ReactorUiKey.Key, out var bui))
            bui.Refresh();
    }
}

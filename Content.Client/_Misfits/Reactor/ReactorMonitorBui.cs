using Content.Shared._Misfits.Reactor;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.Reactor;

[UsedImplicitly]
public sealed class ReactorMonitorBui : BoundUserInterface
{
    private ReactorMonitorWindow? _window;

    public ReactorMonitorBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ReactorMonitorWindow>();
        _window.OnConnect += reactor => SendMessage(new ReactorMonitorConnectMsg(reactor));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ReactorMonitorState castState)
            _window?.UpdateState(castState);
    }
}

using Content.Shared.Containers.ItemSlots;
using Content.Shared.Medical.Cryogenics;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.Medical.Cryogenics.UI;

[UsedImplicitly]
public sealed class CryoPodBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private CryoPodWindow? _window;

    public CryoPodBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CryoPodWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;

        _window.BeakerEjectButton.OnPressed += _ =>
        {
            var slotId = EntMan.GetComponent<CryoPodComponent>(Owner).SolutionContainerName;
            SendPredictedMessage(new ItemSlotButtonPressedEvent(slotId));
        };

        _window.OnTransferReagent += (reagentId, fromBuffer) => SendMessage(new CryoPodTransferReagentMessage(reagentId, fromBuffer));
        _window.OnSetRunning += running => SendMessage(new CryoPodSetRunningMessage(running));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is CryoPodBoundUserInterfaceState cryoState)
            _window?.UpdateState(cryoState);
    }
}

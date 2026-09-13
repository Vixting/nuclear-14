using Content.Shared.DeviceLinking.Events;

namespace Content.Server._Misfits.Reactor;

public sealed class ReactorPrinterLinkSystem : EntitySystem
{
    public const string PrinterSourcePort = "ReactorPrinterSender";
    public const string PrinterSinkPort = "ReactorPrinterReceiver";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReactorMonitorComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<ReactorMonitorComponent, PortDisconnectedEvent>(OnPortDisconnected);
    }

    private void OnNewLink(Entity<ReactorMonitorComponent> ent, ref NewLinkEvent args)
    {
        if (args.SourcePort != PrinterSourcePort ||
            args.SinkPort != PrinterSinkPort ||
            !HasComp<ReactorPrinterComponent>(args.Sink))
        {
            return;
        }

        ent.Comp.LinkedPrinter = args.Sink;
    }

    private void OnPortDisconnected(Entity<ReactorMonitorComponent> ent, ref PortDisconnectedEvent args)
    {
        if (args.Port != PrinterSourcePort)
            return;

        ent.Comp.LinkedPrinter = null;
    }
}

using Content.Shared._Misfits.Reactor;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Reactor;

public sealed class ReactorMonitorSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscriber = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReactorMonitorComponent, BoundUIOpenedEvent>(OnMonitorOpened);

        SubscribeLocalEvent<ReactorComponent, BoundUIClosedEvent>(OnReactorUiClosed);
        SubscribeLocalEvent<ReactorComponent, ReactorGroupStatusRequestMsg>(OnGroupStatusRequest);
        SubscribeLocalEvent<ReactorComponent, ReactorGroupSelectMsg>(OnGroupSelect);
        SubscribeLocalEvent<ReactorComponent, ReactorGroupBroadcastMsg>(OnGroupBroadcast);
    }

    private void OnMonitorOpened(Entity<ReactorMonitorComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!ReactorMonitorUiKey.Key.Equals(args.UiKey))
            return;

        _ui.CloseUi(ent.Owner, ReactorMonitorUiKey.Key, args.Actor);

        var reactors = GetTrackedReactors(ent.Comp);
        if (reactors.Count > 0)
            ConnectTo(reactors[0], args.Actor);
    }

    private void OnGroupStatusRequest(Entity<ReactorComponent> ent, ref ReactorGroupStatusRequestMsg args)
    {
        var entries = BuildEntries(GetGroup(ent.Owner));
        _ui.ServerSendUiMessage(ent.Owner, ReactorUiKey.Key, new ReactorGroupStatusMsg(entries), args.Actor);
    }

    private void OnGroupSelect(Entity<ReactorComponent> ent, ref ReactorGroupSelectMsg args)
    {
        var group = GetGroup(ent.Owner);
        if (args.Index < 1 || args.Index > group.Count)
            return;

        var target = group[args.Index - 1];
        if (target == ent.Owner)
            return;

        ConnectTo(target, args.Actor);
        _ui.CloseUi(ent.Owner, ReactorUiKey.Key, args.Actor);
    }

    private void OnGroupBroadcast(Entity<ReactorComponent> ent, ref ReactorGroupBroadcastMsg args)
    {
        foreach (var uid in GetGroup(ent.Owner))
        {
            BoundUserInterfaceMessage? msg = args.Command switch
            {
                ReactorGroupCommand.Start => new ReactorStartMsg(),
                ReactorGroupCommand.Shutdown => new ReactorShutdownMsg(),
                ReactorGroupCommand.Scram => new ReactorScramMsg(),
                ReactorGroupCommand.SetMode => new ReactorSetModeMsg(args.Mode),
                ReactorGroupCommand.SetFuelingRate => new ReactorSetFuelingRateMsg(args.Value),
                ReactorGroupCommand.SetHeatingPower => new ReactorSetHeatingPowerMsg(args.Value),
                ReactorGroupCommand.SetPlasmaCurrent => new ReactorSetPlasmaCurrentMsg(args.Value),
                ReactorGroupCommand.SetDivertorRate => new ReactorSetDivertorRateMsg(args.Value),
                _ => null,
            };

            if (msg == null)
                continue;

            msg.UiKey = ReactorUiKey.Key;
            msg.Actor = args.Actor;

            RaiseLocalEvent(uid, msg);
        }
    }

    private void OnReactorUiClosed(Entity<ReactorComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!ReactorUiKey.Key.Equals(args.UiKey))
            return;

        if (TryComp<ActorComponent>(args.Actor, out var actorComp))
            _viewSubscriber.RemoveViewSubscriber(ent.Owner, actorComp.PlayerSession);
    }

    private void ConnectTo(EntityUid reactor, EntityUid actor)
    {
        if (!TryComp<ActorComponent>(actor, out var actorComp))
            return;

        _viewSubscriber.AddViewSubscriber(reactor, actorComp.PlayerSession);
        _ui.OpenUi(reactor, ReactorUiKey.Key, actor);
    }

    private List<EntityUid> GetTrackedReactors(ReactorMonitorComponent comp)
    {
        var reactors = new List<EntityUid>();
        if (comp.TrackedTags.Count == 0)
            return reactors;

        var query = EntityQueryEnumerator<ReactorComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            foreach (var tag in comp.TrackedTags)
            {
                if (_tag.HasTag(uid, tag))
                {
                    reactors.Add(uid);
                    break;
                }
            }
        }

        SortByName(reactors);
        return reactors;
    }

    private List<EntityUid> GetGroup(EntityUid reactor)
    {
        var group = new List<EntityUid> { reactor };

        if (!TryComp<TagComponent>(reactor, out var tags) || tags.Tags.Count == 0)
            return group;

        var query = EntityQueryEnumerator<ReactorComponent, TagComponent>();
        while (query.MoveNext(out var uid, out _, out var otherTags))
        {
            if (uid == reactor)
                continue;

            foreach (var tag in otherTags.Tags)
            {
                if (tags.Tags.Contains(tag))
                {
                    group.Add(uid);
                    break;
                }
            }
        }

        SortByName(group);
        return group;
    }

    private List<ReactorMonitorEntry> BuildEntries(List<EntityUid> reactors)
    {
        var entries = new List<ReactorMonitorEntry>();
        foreach (var uid in reactors)
        {
            if (!TryComp<ReactorComponent>(uid, out var reactor))
                continue;

            entries.Add(new ReactorMonitorEntry(
                GetNetEntity(uid),
                MetaData(uid).EntityName,
                reactor.State,
                reactor.Integrity,
                reactor.PowerOutput,
                reactor.MaxOutput,
                reactor.Alarm));
        }

        return entries;
    }

    private void SortByName(List<EntityUid> uids)
    {
        uids.Sort((a, b) => string.Compare(MetaData(a).EntityName, MetaData(b).EntityName, StringComparison.OrdinalIgnoreCase));
    }
}

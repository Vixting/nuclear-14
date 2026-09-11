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

    private const float UpdateInterval = 1f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReactorMonitorComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<ReactorMonitorComponent, ReactorMonitorConnectMsg>(OnConnect);
        SubscribeLocalEvent<ReactorComponent, BoundUIClosedEvent>(OnReactorUiClosed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<ReactorMonitorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            RefreshUi((uid, comp));
        }
    }

    private void OnOpened(Entity<ReactorMonitorComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!ReactorMonitorUiKey.Key.Equals(args.UiKey))
            return;

        RefreshUi(ent);
    }

    private void OnConnect(Entity<ReactorMonitorComponent> ent, ref ReactorMonitorConnectMsg args)
    {
        var reactor = GetEntity(args.Reactor);
        if (Deleted(reactor) || !HasComp<ReactorComponent>(reactor) || !MatchesTracked(ent.Comp, reactor))
            return;

        if (!TryComp<ActorComponent>(args.Actor, out var actorComp))
            return;

        _viewSubscriber.AddViewSubscriber(reactor, actorComp.PlayerSession);
        _ui.OpenUi(reactor, ReactorUiKey.Key, args.Actor);
        _ui.CloseUi(ent.Owner, ReactorMonitorUiKey.Key, args.Actor);
    }

    private void OnReactorUiClosed(Entity<ReactorComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!ReactorUiKey.Key.Equals(args.UiKey))
            return;

        if (TryComp<ActorComponent>(args.Actor, out var actorComp))
            _viewSubscriber.RemoveViewSubscriber(ent.Owner, actorComp.PlayerSession);
    }

    private bool MatchesTracked(ReactorMonitorComponent comp, EntityUid reactor)
    {
        if (comp.TrackedTags.Count == 0)
            return false;

        foreach (var tag in comp.TrackedTags)
        {
            if (_tag.HasTag(reactor, tag))
                return true;
        }

        return false;
    }

    private void RefreshUi(Entity<ReactorMonitorComponent> ent)
    {
        var entries = new List<ReactorMonitorEntry>();

        var query = EntityQueryEnumerator<ReactorComponent>();
        while (query.MoveNext(out var uid, out var reactor))
        {
            if (!MatchesTracked(ent.Comp, uid))
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

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        _ui.SetUiState(ent.Owner, ReactorMonitorUiKey.Key, new ReactorMonitorState(Loc.GetString(ent.Comp.MonitorTitle), entries));
    }
}

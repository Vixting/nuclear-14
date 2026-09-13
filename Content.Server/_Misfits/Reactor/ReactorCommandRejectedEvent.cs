namespace Content.Server._Misfits.Reactor;

public sealed class ReactorCommandRejectedEvent : EntityEventArgs
{
    public readonly EntityUid Actor;
    public readonly string LocId;
    public readonly Dictionary<string, string> LocArgs;

    public ReactorCommandRejectedEvent(EntityUid actor, string locId, Dictionary<string, string>? locArgs = null)
    {
        Actor = actor;
        LocId = locId;
        LocArgs = locArgs ?? new Dictionary<string, string>();
    }
}

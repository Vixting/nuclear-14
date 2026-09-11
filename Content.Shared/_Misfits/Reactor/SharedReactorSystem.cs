namespace Content.Shared._Misfits.Reactor;

public enum ReactorSeverity : byte
{
    Nominal,
    Warning,
    Critical,
}

public abstract class SharedReactorSystem : EntitySystem
{
    public const float WarningIntegrity = 66f;
    public const float CriticalIntegrity = 33f;

    public static ReactorSeverity GetSeverity(float integrity)
    {
        if (integrity <= CriticalIntegrity)
            return ReactorSeverity.Critical;

        return integrity <= WarningIntegrity ? ReactorSeverity.Warning : ReactorSeverity.Nominal;
    }
}

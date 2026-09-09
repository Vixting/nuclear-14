namespace Content.Client._Misfits.Reactor;

public static class ReactorBootSequence
{
    public readonly record struct BootLine(string Text, bool PauseAfter = false);

    public static readonly BootLine[] Lines =
    {
        new("VAULT-TEC INDUSTRIES REACTOR CONTROL SYSTEM"),
        new("DEPT. OF REACTOR ENGINEERING — CONSOLE FIRMWARE FORM VT-14-409"),
        new(""),
        new("RUNNING POWER-ON SELF TEST..."),
        new("CORE MEMORY CHECK.................. OK"),
        new("CONTAINMENT DIAGNOSTIC BUS.......... OK"),
        new("CONFINEMENT COIL ARRAY LINK......... OK"),
        new("DIVERTOR EXHAUST TELEMETRY........... OK"),
        new("PLASMA CURRENT SENSOR RING........... OK"),
        new("LOADING REACTOR CONTROL KERNEL....... DONE", PauseAfter: true),
        new(""),
        new("CONSOLE LOCKED — OPERATOR AUTHORIZATION REQUIRED"),
    };
}

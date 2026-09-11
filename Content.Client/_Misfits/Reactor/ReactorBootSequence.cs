namespace Content.Client._Misfits.Reactor;

public static class ReactorBootSequence
{
    public readonly record struct BootLine(string Text, bool PauseAfter = false);

    public static readonly BootLine[] Lines =
    {
        new("VAULT-TEC INDUSTRIES TERMINAL FIRMWARE VT-14-409"),
        new(""),
        new("RUNNING POWER ON SELF TEST..."),
        new("CORE MEORY CHECK.................. OK"),
        new("SYSTEM BUS DIAGNOSTIC............... OK"),
        new("DISPLAY ADAPTER LINK................ OK"),
        new("NETWORK INTERFACE.................... OK"),
        new("INPUT DEVICE RING.................... OK"),
        new("LOADING TERMINAL KERNEL.............. DONE", PauseAfter: true),
        new(""),
        new("CONSOLE LOCKED — OPERATOR AUTHORIZATION REQUIRED"),
    };
}

using System.Linq;
using Content.Shared._Misfits.Reactor;
using Content.Shared.Containers.ItemSlots;
using JetBrains.Annotations;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Misfits.Reactor;

[UsedImplicitly]
public sealed class ReactorBui : BoundUserInterface
{
    private static readonly SoundSpecifier[] KeystrokeSounds =
    {
        new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/ui_hacking_charsingle_01.ogg"),
        new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/ui_hacking_charsingle_02.ogg"),
        new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/ui_hacking_charsingle_03.ogg"),
        new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/ui_hacking_charsingle_04.ogg"),
        new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/ui_hacking_charsingle_05.ogg"),
        new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/ui_hacking_charsingle_06.ogg"),
    };

    private static readonly SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/confirm_blip.ogg");
    private static readonly SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/terminal_error.ogg");
    private static readonly SoundSpecifier BootSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/poweron.ogg");
    private static readonly SoundSpecifier InsertDiscSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/terminal_insert_disc.ogg");
    private static readonly SoundSpecifier LoginGrantedSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/terminal_success.ogg");
    private static readonly SoundSpecifier LoginDeniedSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/terminal_prompt_deny.ogg");
    private static readonly SoundSpecifier StartSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/terminal_on.ogg");
    private static readonly SoundSpecifier ShutdownSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/terminal_shutdown.ogg");
    private static readonly SoundSpecifier ModeSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/switch.ogg");
    private static readonly SoundSpecifier HelpSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/terminal_processing.ogg");
    private static readonly SoundSpecifier ScramArmSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Reactor/warning-buzzer.ogg");
    private static readonly SoundSpecifier ScramConfirmSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Reactor/lockdownalarm.ogg");

    private static readonly Color BootTextColor = Color.FromHex("#33FF66");
    private static readonly Color EchoColor = Color.FromHex("#33FF66");
    private static readonly Color ResponseColor = Color.FromHex("#1E9C3D");
    private static readonly Color ErrorColor = Color.FromHex("#FF5555");

    private const string CrtShaderId = "ReactorCrt";
    private const int MaxLogLines = 80;

    private enum TerminalPhase { Boot, Login, Main }

    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private AudioSystem _audio = default!;

    private ReactorWindow? _window;
    private ReactorMonitorState? _state;
    private bool _confirmScram;
    private bool _welcomed;
    private bool _authenticating;
    private int _logLineCount;

    private TerminalPhase _phase = TerminalPhase.Boot;

    private ReactorState? _lastState;
    private ReactorStartupStep? _lastStartupStep;
    private ReactorShutdownStep? _lastShutdownStep;

    private Dictionary<string, Action<string[]>> _commandHandlers = default!;

    private ReactorLineChart _outputChart = default!;
    private ReactorLineChart _fuelingChart = default!;
    private ReactorLineChart _heatingChart = default!;
    private ReactorLineChart _currentChart = default!;
    private ReactorLineChart _divertorChart = default!;
    private ReactorLineChart _coilChart = default!;
    private ReactorLineChart _betaChart = default!;
    private ReactorLineChart _densityChart = default!;

    private const float StabilityChartScale = 1.5f;

    public ReactorBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _audio = EntMan.System<AudioSystem>();

        _window = this.CreateWindow<ReactorWindow>();

        _window.IdSlotButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(ReactorMonitorConstants.IdCardSlotId));
        _window.LogoutButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(ReactorMonitorConstants.IdCardSlotId));

        _window.CommandInput.OnTextTyped += _ => PlayKeystroke();
        _window.CommandInput.OnTextEntered += args =>
        {
            ExecuteCommand(args.Text);
            _window.CommandInput.Text = string.Empty;
        };

        _commandHandlers = BuildCommandHandlers();

        SetupCharts();
        ApplyCrtShader();

        UiSystem.TryGetUiState<ReactorMonitorState>(Owner, UiKey, out var initialState);
        if (initialState?.InsertedIdName is { } operatorName)
        {
            _state = initialState;
            _welcomed = true;
            _phase = TerminalPhase.Main;
            _window.OperatorLabel.Text = Loc.GetString("reactor-operator-label", ("name", operatorName));
            SetLayerVisibility();
            Refresh();
            return;
        }

        _phase = TerminalPhase.Boot;
        SetLayerVisibility();
        _window.BootTextContainer.RemoveAllChildren();
        _audio.PlayGlobal(BootSound, Filter.Local(), false);
        PlayBootSequence(0);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ReactorMonitorState monitorState)
        {
            _state = monitorState;
            Refresh();
        }
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is not ReactorMonitorNoticeMsg notice)
            return;

        var args = notice.LocArgs.Select(kv => (kv.Key, (object) kv.Value)).ToArray();
        Log(Loc.GetString(notice.LocId, args), notice.IsError ? ErrorColor : ResponseColor);

        if (notice.IsError)
            PlayError();
        else
            PlayConfirm();
    }

    private void ApplyCrtShader()
    {
        if (_window == null)
            return;

        _window.CrtOverlay.Texture = Texture.White;

        if (_prototypes.TryIndex<ShaderPrototype>(CrtShaderId, out var proto))
            _window.CrtOverlay.ShaderOverride = proto.Instance().Duplicate();
    }

    private void SetupCharts()
    {
        if (_window == null)
            return;

        _outputChart = NewChart(_window.ChartsContainer, Loc.GetString("reactor-chart-output"));
        _betaChart = NewChart(_window.ChartsContainer, Loc.GetString("reactor-chart-beta"));
        _betaChart.Scale = StabilityChartScale;
        _densityChart = NewChart(_window.ChartsContainer, Loc.GetString("reactor-chart-density"));
        _densityChart.Scale = StabilityChartScale;
        _fuelingChart = NewChart(_window.ChartsContainer, Loc.GetString("reactor-chart-fueling"));
        _heatingChart = NewChart(_window.ChartsContainer, Loc.GetString("reactor-chart-heating"));
        _currentChart = NewChart(_window.ChartsContainer, Loc.GetString("reactor-chart-current"));
        _divertorChart = NewChart(_window.ChartsContainer, Loc.GetString("reactor-chart-divertor"));
        _coilChart = NewChart(_window.ChartsContainer, Loc.GetString("reactor-chart-coils"));
    }

    private static ReactorLineChart NewChart(BoxContainer row, string title)
    {
        var chart = new ReactorLineChart(title);
        row.AddChild(chart);
        return chart;
    }

    private void PlayBootSequence(int index)
    {
        if (_window == null || _phase != TerminalPhase.Boot)
            return;

        if (index >= ReactorBootSequence.Lines.Length)
        {
            Timer.Spawn(TimeSpan.FromSeconds(2), EnterLogin);
            return;
        }

        var line = ReactorBootSequence.Lines[index];
        AppendBootLine(line.Text);

        var delay = TimeSpan.FromMilliseconds(line.PauseAfter ? 1400 : 320);
        Timer.Spawn(delay, () => PlayBootSequence(index + 1));
    }

    private void AppendBootLine(string text)
    {
        if (_window == null)
            return;

        _window.BootTextContainer.AddChild(new Label
        {
            Text = text,
            FontColorOverride = BootTextColor,
            HorizontalAlignment = Control.HAlignment.Center,
            StyleClasses = { "PipBoyLabel" },
        });
    }

    private void EnterLogin()
    {
        _phase = TerminalPhase.Login;
        SetLayerVisibility();

        if (_window != null)
        {
            _window.LoginPromptLabel.Text = Loc.GetString("reactor-login-prompt");
            _window.LoginStatusLabel.Visible = false;
        }

        Refresh();
    }

    private void SetLayerVisibility()
    {
        if (_window == null)
            return;

        _window.BootLayer.Visible = _phase == TerminalPhase.Boot;
        _window.LoginLayer.Visible = _phase == TerminalPhase.Login;
        _window.MainLayer.Visible = _phase == TerminalPhase.Main;
    }

    private const int AuthenticationTicks = 7;

    private void BeginAuthentication()
    {
        _authenticating = true;
        if (_window != null)
            _window.IdSlotButton.Disabled = true;

        _audio.PlayGlobal(InsertDiscSound, Filter.Local(), false);
        AnimateAuthenticating(0);
    }

    private void AnimateAuthenticating(int tick)
    {
        if (_window == null || !_authenticating)
            return;

        var dots = new string('.', (tick % 3) + 1);
        _window.LoginStatusLabel.Visible = true;
        _window.LoginStatusLabel.Text = $"AUTHENTICATING{dots}";

        if (tick >= AuthenticationTicks)
        {
            ResolveAuthentication();
            return;
        }

        Timer.Spawn(TimeSpan.FromMilliseconds(200), () => AnimateAuthenticating(tick + 1));
    }

    private void ResolveAuthentication()
    {
        if (_window == null)
            return;

        _window.IdSlotButton.Disabled = false;

        if (_state?.InsertedIdName is { } name)
        {
            GrantAccess(name);
            return;
        }

        _audio.PlayGlobal(LoginDeniedSound, Filter.Local(), false);
        _window.LoginStatusLabel.Visible = true;
        _window.LoginStatusLabel.Text = Loc.GetString("reactor-login-denied");
    }

    private void GrantAccess(string name)
    {
        if (_window == null)
            return;

        _audio.PlayGlobal(LoginGrantedSound, Filter.Local(), false);
        _window.LoginStatusLabel.Visible = true;
        _window.LoginStatusLabel.Text = Loc.GetString("reactor-login-granted", ("name", name));
        _window.OperatorLabel.Text = Loc.GetString("reactor-operator-label", ("name", name));

        Timer.Spawn(TimeSpan.FromMilliseconds(1300), () =>
        {
            if (_window == null || _phase != TerminalPhase.Login)
                return;

            _phase = TerminalPhase.Main;
            SetLayerVisibility();

            if (!_welcomed)
            {
                _welcomed = true;
                Log(Loc.GetString("reactor-terminal-welcome"), ResponseColor);
            }

            Refresh();
        });
    }

    private void RevokeAccess()
    {
        if (_window == null)
            return;

        _audio.PlayGlobal(LoginDeniedSound, Filter.Local(), false);
        _confirmScram = false;
        _phase = TerminalPhase.Login;
        SetLayerVisibility();

        _window.LoginPromptLabel.Text = Loc.GetString("reactor-login-prompt");
        _window.LoginStatusLabel.Visible = true;
        _window.LoginStatusLabel.Text = Loc.GetString("reactor-login-revoked");
    }

    private Dictionary<string, Action<string[]>> BuildCommandHandlers()
    {
        return new Dictionary<string, Action<string[]>>
        {
            ["help"] = HandleHelp,
            ["start"] = _ => HandleStart(),
            ["shutdown"] = _ => HandleShutdown(),
            ["scram"] = _ => HandleScram(),
            ["mode"] = HandleMode,
            ["fuel"] = parts => HandleRate(parts, "FUEL", c => c.FuelingRate, c => c.FuelingTarget, v => new ReactorSetFuelingRateMsg(v)),
            ["heat"] = parts => HandleRate(parts, "HEAT", c => c.HeatingPower, c => c.HeatingTarget, v => new ReactorSetHeatingPowerMsg(v)),
            ["current"] = parts => HandleRate(parts, "CURRENT", c => c.PlasmaCurrent, c => c.PlasmaCurrentTarget, v => new ReactorSetPlasmaCurrentMsg(v)),
            ["divertor"] = parts => HandleRate(parts, "DIVERTOR", c => c.DivertorRate, c => c.DivertorTarget, v => new ReactorSetDivertorRateMsg(v)),
            ["coil"] = HandleCoil,
            ["reactors"] = _ => LogReactorList(),
            ["status"] = _ => LogReactorList(),
            ["select"] = HandleSelect,
            ["all"] = HandleAll,
            ["print"] = HandlePrint,
        };
    }

    private void HandlePrint(string[] parts)
    {
        if (parts.Length < 2)
        {
            PlayError();
            Log("Usage: PRINT <report> — supported reports: STATUS", ErrorColor);
            return;
        }

        ReactorPrintReport report;
        switch (parts[1].ToUpperInvariant())
        {
            case "STATUS":
                report = ReactorPrintReport.Status;
                break;
            default:
                PlayError();
                Log($"Unknown report: {parts[1].ToUpperInvariant()}", ErrorColor);
                return;
        }

        SendMessage(new ReactorMonitorPrintMsg(report));
        PlayConfirm();
        Log($"Print job sent: {report.ToString().ToUpperInvariant()}.", ResponseColor);
    }

    private void LogReactorList()
    {
        var reactors = _state?.Reactors;
        if (reactors == null || reactors.Count == 0)
        {
            Log("No linked reactors detected.", ResponseColor);
            return;
        }

        Log("LINKED REACTORS:", ResponseColor);
        for (var i = 0; i < reactors.Count; i++)
        {
            var r = reactors[i];
            var outputPct = r.MaxOutput > 0f ? (int) (r.PowerOutput / r.MaxOutput * 100) : 0;
            var marker = i + 1 == _state!.SelectedIndex ? "*" : " ";
            var line = $"{marker}{i + 1}. {r.Name} — {r.State.ToString().ToUpperInvariant()} — {(int) r.Integrity}% INTEGRITY — {outputPct}% OUTPUT";
            Log(line, r.Alarm != null ? ErrorColor : ResponseColor);
        }
    }

    private void HandleSelect(string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var index) || index < 1)
        {
            PlayError();
            Log("Usage: SELECT <n> — run REACTORS first to see numbers.", ErrorColor);
            return;
        }

        SendMessage(new ReactorMonitorSelectMsg(index));
        PlayConfirm();
        Log($"Connecting to reactor {index}...", ResponseColor);
    }

    private void HandleAll(string[] parts)
    {
        if (parts.Length < 2)
        {
            PlayError();
            Log("Usage: ALL <start|shutdown|scram|mode|fuel|heat|current|divertor> [value]", ErrorColor);
            return;
        }

        var sub = parts[1].ToLowerInvariant();
        switch (sub)
        {
            case "start":
                SendMessage(new ReactorMonitorAllMsg(ReactorMonitorAllCommand.Start));
                _audio.PlayGlobal(StartSound, Filter.Local(), false);
                Log("Startup command broadcast to all linked reactors.", ResponseColor);
                break;
            case "shutdown":
                SendMessage(new ReactorMonitorAllMsg(ReactorMonitorAllCommand.Shutdown));
                _audio.PlayGlobal(ShutdownSound, Filter.Local(), false);
                Log("Shutdown command broadcast to all linked reactors.", ResponseColor);
                break;
            case "scram":
                SendMessage(new ReactorMonitorAllMsg(ReactorMonitorAllCommand.Scram));
                _audio.PlayGlobal(ScramConfirmSound, Filter.Local(), false);
                Log("SCRAM broadcast to all linked reactors.", ErrorColor);
                break;
            case "mode":
                HandleAllMode(parts);
                break;
            case "fuel":
            case "heat":
            case "current":
            case "divertor":
                HandleAllRate(sub, parts);
                break;
            default:
                PlayError();
                Log($"Unknown ALL sub-command: {parts[1].ToUpperInvariant()}", ErrorColor);
                break;
        }
    }

    private void HandleAllMode(string[] parts)
    {
        if (parts.Length < 3)
        {
            PlayError();
            Log("Usage: ALL MODE <AUTO|MANUAL>", ErrorColor);
            return;
        }

        ReactorMode mode;
        switch (parts[2].ToUpperInvariant())
        {
            case "AUTO":
            case "AUTOMATIC":
                mode = ReactorMode.Automatic;
                break;
            case "MANUAL":
                mode = ReactorMode.Manual;
                break;
            default:
                PlayError();
                Log($"Unknown mode: {parts[2]}. Use AUTO or MANUAL.", ErrorColor);
                return;
        }

        SendMessage(new ReactorMonitorAllMsg(ReactorMonitorAllCommand.SetMode, mode: mode));
        _audio.PlayGlobal(ModeSound, Filter.Local(), false);
        Log($"Mode set to {mode.ToString().ToUpperInvariant()} on all linked reactors.", ResponseColor);
    }

    private void HandleAllRate(string sub, string[] parts)
    {
        if (parts.Length < 3 || !TryParsePercent(parts[2], out var value))
        {
            PlayError();
            Log($"Usage: ALL {sub.ToUpperInvariant()} <0-100>", ErrorColor);
            return;
        }

        var command = sub switch
        {
            "fuel" => ReactorMonitorAllCommand.SetFuelingRate,
            "heat" => ReactorMonitorAllCommand.SetHeatingPower,
            "current" => ReactorMonitorAllCommand.SetPlasmaCurrent,
            "divertor" => ReactorMonitorAllCommand.SetDivertorRate,
            _ => ReactorMonitorAllCommand.SetFuelingRate,
        };

        SendMessage(new ReactorMonitorAllMsg(command, value));
        PlayConfirm();
        Log($"{sub.ToUpperInvariant()} set to {(int) (value * 100)}% on all linked reactors.", ResponseColor);
    }

    private void ExecuteCommand(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
            return;

        Log($"> {text}", EchoColor);

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var id = parts[0].ToLowerInvariant();

        if (id != "scram" && _confirmScram)
        {
            _confirmScram = false;
            Log("SCRAM cancelled.", ResponseColor);
        }

        if (!_commandHandlers.TryGetValue(id, out var handler))
        {
            PlayError();
            Log($"Unknown command: {parts[0].ToUpperInvariant()}. Type HELP for a list of commands.", ErrorColor);
            return;
        }

        handler(parts);
    }

    private void HandleHelp(string[] parts)
    {
        if (parts.Length >= 2)
        {
            var target = parts[1].ToLowerInvariant();
            if (!_prototypes.TryIndex<ReactorCommandPrototype>(target, out var info))
            {
                PlayError();
                Log($"No such command: {parts[1].ToUpperInvariant()}", ErrorColor);
                return;
            }

            _audio.PlayGlobal(HelpSound, Filter.Local(), false);
            Log($"{info.Usage} — {info.Description}", ResponseColor);
            return;
        }

        _audio.PlayGlobal(HelpSound, Filter.Local(), false);
        Log("Available commands (HELP <command> for details):", ResponseColor);

        var commands = _prototypes.EnumeratePrototypes<ReactorCommandPrototype>().OrderBy(c => c.Order).ToList();
        var half = (commands.Count + 1) / 2;
        var row1 = string.Join("   ", commands.Take(half).Select(c => c.ID.ToUpperInvariant()));
        var row2 = string.Join("   ", commands.Skip(half).Select(c => c.ID.ToUpperInvariant()));
        Log(row1, ResponseColor);
        if (row2.Length > 0)
            Log(row2, ResponseColor);
    }

    private void HandleStart()
    {
        if (_state is { State: not (ReactorState.Offline or ReactorState.Scrammed) })
        {
            PlayError();
            Log("REACTOR IS ALREADY RUNNING.", ErrorColor);
            return;
        }

        if (_state is { LockedOutSeconds: > 0f } locked)
        {
            PlayError();
            Log($"REACTOR LOCKED OUT — {(int) Math.Ceiling(locked.LockedOutSeconds)}s REMAINING.", ErrorColor);
            return;
        }

        SendMessage(new ReactorStartMsg());
        _audio.PlayGlobal(StartSound, Filter.Local(), false);
        Log("Startup command sent.", ResponseColor);
    }

    private void HandleShutdown()
    {
        if (_state is { State: not ReactorState.Online })
        {
            PlayError();
            Log("REACTOR ISN'T ONLINE.", ErrorColor);
            return;
        }

        SendMessage(new ReactorShutdownMsg());
        _audio.PlayGlobal(ShutdownSound, Filter.Local(), false);
        Log("Shutdown command sent.", ResponseColor);
    }

    private void HandleMode(string[] parts)
    {
        if (parts.Length < 2)
        {
            if (_state is { } comp)
            {
                var currentMode = comp.Mode;
                Log($"MODE: {currentMode.ToString().ToUpperInvariant()}", ResponseColor);
            }
            return;
        }

        ReactorMode mode;
        switch (parts[1].ToUpperInvariant())
        {
            case "AUTO":
            case "AUTOMATIC":
                mode = ReactorMode.Automatic;
                break;
            case "MANUAL":
                mode = ReactorMode.Manual;
                break;
            default:
                PlayError();
                Log($"Unknown mode: {parts[1]}. Use AUTO or MANUAL.", ErrorColor);
                return;
        }

        if (_state == null || _state.State != ReactorState.Online)
        {
            PlayError();
            Log("REACTOR MUST BE ONLINE to change mode.", ErrorColor);
            return;
        }

        SendMessage(new ReactorSetModeMsg(mode));
        _audio.PlayGlobal(ModeSound, Filter.Local(), false);
        Log($"Mode set to {mode.ToString().ToUpperInvariant()}.", ResponseColor);
    }

    private bool CheckManualEditable()
    {
        if (_state == null)
            return false;

        if (_state.State == ReactorState.Starting)
            return true;

        if (_state.State != ReactorState.Online)
        {
            PlayError();
            Log("REACTOR MUST BE ONLINE to adjust this.", ErrorColor);
            return false;
        }

        if (_state.Mode != ReactorMode.Manual)
        {
            PlayError();
            Log("MANUAL MODE REQUIRED. Use MODE MANUAL first.", ErrorColor);
            return false;
        }

        return true;
    }

    private void HandleRate(string[] parts, string label, Func<ReactorMonitorState, float> current,
        Func<ReactorMonitorState, float> target, Func<float, BoundUserInterfaceMessage> makeMessage)
    {
        if (parts.Length < 2)
        {
            if (_state != null)
                Log($"{label}: {Percent(current(_state))} current / {Percent(target(_state))} target", ResponseColor);
            return;
        }

        if (!TryParsePercent(parts[1], out var normalized))
        {
            PlayError();
            Log($"Usage: {label} <0-100>", ErrorColor);
            return;
        }

        if (!CheckManualEditable())
            return;

        SendMessage(makeMessage(normalized));
        PlayConfirm();
        Log($"{label} set to {(int) (normalized * 100)}%.", ResponseColor);
    }

    private void HandleCoil(string[] parts)
    {
        var comp = _state;
        var coilCount = comp?.CoilTargets.Count ?? 0;

        if (parts.Length < 2)
        {
            if (comp == null || coilCount == 0)
                return;

            for (var i = 0; i < coilCount; i++)
                Log($"COIL {i + 1}: {Percent(comp.CoilStrength[i])} current / {Percent(comp.CoilTargets[i])} target", ResponseColor);
            return;
        }

        if (!int.TryParse(parts[1], out var oneBasedIndex) || oneBasedIndex < 1 || oneBasedIndex > coilCount)
        {
            PlayError();
            Log($"Usage: COIL <1-{Math.Max(coilCount, 1)}> [0-100]", ErrorColor);
            return;
        }

        if (parts.Length < 3)
        {
            Log($"COIL {oneBasedIndex}: {Percent(comp!.CoilStrength[oneBasedIndex - 1])} current / {Percent(comp.CoilTargets[oneBasedIndex - 1])} target", ResponseColor);
            return;
        }

        if (!TryParsePercent(parts[2], out var normalized))
        {
            PlayError();
            Log($"Usage: COIL <1-{coilCount}> <0-100>", ErrorColor);
            return;
        }

        if (!CheckManualEditable())
            return;

        SendMessage(new ReactorSetCoilTargetMsg(oneBasedIndex - 1, normalized));
        PlayConfirm();
        Log($"Coil {oneBasedIndex} target set to {(int) (normalized * 100)}%.", ResponseColor);
    }

    private void HandleScram()
    {
        if (!_confirmScram)
        {
            _confirmScram = true;
            _audio.PlayGlobal(ScramArmSound, Filter.Local(), false);
            Log("SCRAM ARMED. Type SCRAM again to confirm emergency shutdown.", ErrorColor);
            return;
        }

        _confirmScram = false;
        SendMessage(new ReactorScramMsg());
        _audio.PlayGlobal(ScramConfirmSound, Filter.Local(), false);
        Log("SCRAM CONFIRMED. Emergency shutdown executing.", ErrorColor);
    }

    private static bool TryParsePercent(string text, out float normalized)
    {
        normalized = 0f;
        if (!int.TryParse(text.Trim().TrimEnd('%'), out var percent))
            return false;

        normalized = Clamp(percent / 100f);
        return true;
    }

    private void Log(string text, Color color)
    {
        if (_window == null)
            return;

        var msg = new FormattedMessage();
        msg.PushColor(color);
        msg.AddText(text);
        msg.Pop();
        _window.CommandLog.AddMessage(msg);

        _logLineCount++;
        if (_logLineCount > MaxLogLines)
        {
            _window.CommandLog.RemoveEntry(0);
            _logLineCount--;
        }

        _window.CommandLog.ScrollToBottom();
    }

    private void PlayKeystroke() => _audio.PlayGlobal(_random.Pick(KeystrokeSounds), Filter.Local(), false);
    private void PlayConfirm() => _audio.PlayGlobal(ConfirmSound, Filter.Local(), false);
    private void PlayError() => _audio.PlayGlobal(ErrorSound, Filter.Local(), false);

    private static float Clamp(float value) => Math.Clamp(value, 0f, 1f);

    public void Refresh()
    {
        if (_window == null)
            return;

        var comp = _state;
        if (comp == null)
            return;

        switch (_phase)
        {
            case TerminalPhase.Boot:
                return;
            case TerminalPhase.Login when comp.InsertedIdName != null && !_authenticating:
                BeginAuthentication();
                return;
            case TerminalPhase.Login when comp.InsertedIdName == null:
                _authenticating = false;
                return;
            case TerminalPhase.Login:
                return;
            case TerminalPhase.Main when comp.InsertedIdName == null:
                RevokeAccess();
                return;
        }

        RefreshDashboard(comp);
        PushChartSamples(comp);
    }

    private void PushChartSamples(ReactorMonitorState comp)
    {
        _outputChart.Scale = comp.MaxOutput > 0f ? comp.MaxOutput : 1f;
        _outputChart.PushSample(comp.LoadFactor, comp.PowerOutput);
        _betaChart.PushSample(1f, comp.Beta);
        _densityChart.PushSample(1f, comp.Density);
        _fuelingChart.PushSample(comp.FuelingTarget, comp.FuelingRate);
        _heatingChart.PushSample(comp.HeatingTarget, comp.HeatingPower);
        _currentChart.PushSample(comp.PlasmaCurrentTarget, comp.PlasmaCurrent);
        _divertorChart.PushSample(comp.DivertorTarget, comp.DivertorRate);
        _coilChart.PushSample(Average(comp.CoilTargets), Average(comp.CoilStrength));
    }

    private static float Average(List<float> values)
    {
        if (values.Count == 0)
            return 0f;

        var total = 0f;
        foreach (var value in values)
            total += value;
        return total / values.Count;
    }

    private void RefreshDashboard(ReactorMonitorState comp)
    {
        var window = _window!;

        if (!comp.HasSelection)
        {
            window.StatusLabel.Text = "NO REACTOR";
            window.StatusLabel.FontColorOverride = Color.Gray;
            window.AlarmLabel.Visible = false;
            window.SequenceLabel.Visible = false;
            return;
        }

        var state = comp.State;
        window.StatusLabel.Text = state.ToString().ToUpperInvariant();
        window.StatusLabel.FontColorOverride = StatusColor(comp);

        if (comp.Alarm is { } alarm)
        {
            window.AlarmLabel.Visible = true;
            window.AlarmLabel.Text = Loc.GetString(alarm);
        }
        else
        {
            window.AlarmLabel.Visible = false;
        }

        var outputFraction = comp.MaxOutput > 0f ? comp.PowerOutput / comp.MaxOutput : 0f;
        var demandFraction = comp.MaxOutput > 0f ? comp.LoadFactor / comp.MaxOutput : 0f;
        window.OutputLabel.Text = Loc.GetString("reactor-output", ("value", (int) (outputFraction * 100)));
        window.OutputLabel.FontColorOverride = ThresholdColor(outputFraction);
        window.DemandLabel.Text = Loc.GetString("reactor-demand", ("value", (int) (demandFraction * 100)));
        window.DemandLabel.FontColorOverride = ThresholdColor(demandFraction);

        window.BetaLabel.Text = Loc.GetString("reactor-beta", ("value", (int) (comp.Beta * 100)));
        window.BetaLabel.FontColorOverride = ThresholdColor(comp.Beta);
        window.DensityLabel.Text = Loc.GetString("reactor-density", ("value", (int) (comp.Density * 100)));
        window.DensityLabel.FontColorOverride = ThresholdColor(comp.Density);
        window.AshLabel.Text = Loc.GetString("reactor-ash", ("value", (int) (comp.AshLevel * 100)));
        window.AshLabel.FontColorOverride = ThresholdColor(comp.AshLevel);
        window.IntegrityLabel.Text = Loc.GetString("reactor-integrity", ("value", (int) comp.Integrity));
        window.IntegrityLabel.FontColorOverride = SeverityColor(SharedReactorSystem.GetSeverity(comp.Integrity));

        if (comp.ActiveFaults.Count > 0)
        {
            window.FaultsLabel.Visible = true;
            window.FaultsLabel.Text = string.Join("   ", comp.ActiveFaults.Select(FormatFault));
        }
        else
        {
            window.FaultsLabel.Visible = false;
        }

        window.AutoDeratedLabel.Visible = comp.AutoDerated;
        if (comp.AutoDerated)
            window.AutoDeratedLabel.Text = Loc.GetString("reactor-auto-derated");

        window.FuelReadout.Text = $"FUEL {Percent(comp.FuelingRate)}/{Percent(comp.FuelingTarget)}";
        window.FuelReadout.FontColorOverride = ThresholdColor(comp.FuelingRate);
        window.HeatReadout.Text = $"HEAT {Percent(comp.HeatingPower)}/{Percent(comp.HeatingTarget)}";
        window.HeatReadout.FontColorOverride = ThresholdColor(comp.HeatingPower);
        window.CurrentReadout.Text = $"CURRENT {Percent(comp.PlasmaCurrent)}/{Percent(comp.PlasmaCurrentTarget)}";
        window.CurrentReadout.FontColorOverride = ThresholdColor(comp.PlasmaCurrent);
        window.DivertorReadout.Text = $"DIVERTOR {Percent(comp.DivertorRate)}/{Percent(comp.DivertorTarget)}";
        window.DivertorReadout.FontColorOverride = ThresholdColor(comp.DivertorRate);

        RefreshCoils(comp, window.CoilsContainer);
        TrackSequenceEvents(comp);

        if (comp.State == ReactorState.Starting && SharedReactorSystem.GetStartupStepCommand(comp.StartupStep) is { } awaitedCommand)
        {
            window.SequenceLabel.Visible = true;
            window.SequenceLabel.Text = Loc.GetString("reactor-sequence-awaiting",
                ("step", FormatStepName(comp.StartupStep.ToString())), ("command", awaitedCommand));
        }
        else if (comp.State is ReactorState.Starting or ReactorState.ShuttingDown)
        {
            var startupStep = comp.StartupStep;
            var shutdownStep = comp.ShutdownStep;
            var stepName = comp.State == ReactorState.Starting ? startupStep.ToString() : shutdownStep.ToString();
            var total = comp.StepDurationSeconds;
            var progress = total > 0f ? (int) (comp.StepElapsed / total * 100) : 0;
            window.SequenceLabel.Visible = true;
            window.SequenceLabel.Text = Loc.GetString("reactor-sequence-step", ("step", stepName), ("progress", progress));
        }
        else
        {
            window.SequenceLabel.Visible = false;
        }

        if (comp.State is not (ReactorState.Starting or ReactorState.Online or ReactorState.ShuttingDown))
            _confirmScram = false;
    }

    private void TrackSequenceEvents(ReactorMonitorState comp)
    {
        var startupStep = comp.StartupStep;
        var shutdownStep = comp.ShutdownStep;

        if (comp.State == ReactorState.Starting)
        {
            if (startupStep != _lastStartupStep)
            {
                if (_lastStartupStep is { } prev && prev != ReactorStartupStep.None)
                    Log($"{FormatStepName(prev.ToString())}... OK", ResponseColor);

                if (startupStep != ReactorStartupStep.None)
                {
                    var command = SharedReactorSystem.GetStartupStepCommand(startupStep);
                    Log(command != null
                        ? $"{FormatStepName(startupStep.ToString())}... ISSUE {command} TO PROCEED."
                        : $"{FormatStepName(startupStep.ToString())}...", ResponseColor);

                    var hint = SharedReactorSystem.GetStartupHint(startupStep, comp.PlasmaCurrentTarget,
                        comp.FuelingTarget, comp.CoilTargets, comp.IgnitionTemperature);
                    if (hint != null)
                        Log(hint, ResponseColor);
                }

                _lastStartupStep = startupStep;
            }
        }
        else
        {
            _lastStartupStep = null;
        }

        if (comp.State == ReactorState.ShuttingDown)
        {
            if (shutdownStep != _lastShutdownStep)
            {
                if (_lastShutdownStep is { } prev && prev != ReactorShutdownStep.None)
                    Log($"{FormatStepName(prev.ToString())}... OK", ResponseColor);
                if (shutdownStep != ReactorShutdownStep.None)
                    Log($"{FormatStepName(shutdownStep.ToString())}...", ResponseColor);
                _lastShutdownStep = shutdownStep;
            }
        }
        else
        {
            _lastShutdownStep = null;
        }

        if (_lastState is { } lastState && lastState != comp.State)
        {
            switch (comp.State)
            {
                case ReactorState.Online:
                    Log("STARTUP SEQUENCE COMPLETE. REACTOR ONLINE.", ResponseColor);
                    break;
                case ReactorState.Offline when lastState == ReactorState.ShuttingDown:
                    Log("SHUTDOWN SEQUENCE COMPLETE. REACTOR SECURED.", ResponseColor);
                    break;
                case ReactorState.Scrammed:
                    Log("REACTOR SCRAMMED. RESTART LOCKED OUT.", ErrorColor);
                    break;
                case ReactorState.Melted:
                    Log("CONTAINMENT LOST.", ErrorColor);
                    break;
            }
        }

        _lastState = comp.State;
    }

    private static string FormatStepName(string pascalCase)
    {
        var spaced = new System.Text.StringBuilder();
        foreach (var c in pascalCase)
        {
            if (char.IsUpper(c) && spaced.Length > 0)
                spaced.Append(' ');
            spaced.Append(c);
        }

        return spaced.ToString().ToUpperInvariant();
    }

    private void RefreshCoils(ReactorMonitorState comp, BoxContainer coilsContainer)
    {
        coilsContainer.DisposeAllChildren();

        var count = comp.CoilStrength.Count;
        if (count == 0)
            return;

        var half = (count + 1) / 2;
        var row1 = NewCoilRow();
        var row2 = NewCoilRow();
        coilsContainer.AddChild(row1);
        if (count > half)
            coilsContainer.AddChild(row2);

        for (var i = 0; i < count; i++)
            (i < half ? row1 : row2).AddChild(BuildCoilCell(i, comp));
    }

    private static BoxContainer NewCoilRow() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Horizontal,
        SeparationOverride = 4,
        HorizontalExpand = true,
    };

    private Control BuildCoilCell(int index, ReactorMonitorState comp)
    {
        var heat = comp.CoilLocalHeat[index];
        return new Label
        {
            Text = Loc.GetString("reactor-coil-readout",
                ("index", index + 1),
                ("current", (int) (comp.CoilStrength[index] * 100)),
                ("target", (int) (comp.CoilTargets[index] * 100))),
            HorizontalExpand = true,
            StyleClasses = { "PipBoyLabel" },
            FontColorOverride = ThresholdColor(heat),
        };
    }

    private static string FormatFault(string code)
    {
        if (code.StartsWith("coil-", StringComparison.Ordinal) &&
            int.TryParse(code.AsSpan("coil-".Length), out var index))
        {
            return Loc.GetString("reactor-fault-coil", ("index", index));
        }

        return Loc.GetString($"reactor-fault-{code}");
    }

    private static string Percent(float value) => $"{(int) (value * 100)}%";

    private static Color StatusColor(ReactorMonitorState comp)
    {
        return comp.State switch
        {
            ReactorState.Online => SeverityColor(SharedReactorSystem.GetSeverity(comp.Integrity)),
            ReactorState.Melted => Color.Red,
            ReactorState.Scrammed => Color.Orange,
            _ => Color.Gray,
        };
    }

    private static Color SeverityColor(ReactorSeverity severity)
    {
        return severity switch
        {
            ReactorSeverity.Critical => Color.Red,
            ReactorSeverity.Warning => Color.Yellow,
            _ => Color.LightGreen,
        };
    }

    private static Color ThresholdColor(float normalized)
    {
        if (normalized >= 0.8f)
            return Color.Red;
        return normalized >= 0.5f ? Color.Yellow : Color.LightGreen;
    }
}

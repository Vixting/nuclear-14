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
    private static readonly SoundSpecifier BootLineSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/keyboard1.ogg");
    private static readonly SoundSpecifier LoginGrantedSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/terminal_prompt_confirm.ogg");
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

    private readonly record struct CommandInfo(string Name, string Usage, string Description);

    private static readonly CommandInfo[] Commands =
    {
        new("HELP", "HELP [command]", "Lists all commands, or shows detail for one command."),
        new("START", "START", "Begins the reactor startup sequence."),
        new("SHUTDOWN", "SHUTDOWN", "Begins a normal shutdown sequence."),
        new("SCRAM", "SCRAM", "Emergency shutdown. Type twice to confirm."),
        new("MODE", "MODE [AUTO|MANUAL]", "Switches between automatic and manual control, or shows the current mode if given no argument."),
        new("FUEL", "FUEL [0-100]", "Sets the fueling (deuterium-tritium injection) rate, or shows the current/target reading if given no argument. Setting requires manual mode."),
        new("HEAT", "HEAT [0-100]", "Sets the auxiliary heating power, or shows the current/target reading if given no argument. Setting requires manual mode."),
        new("CURRENT", "CURRENT [0-100]", "Sets the plasma current target, or shows the current/target reading if given no argument. Setting requires manual mode."),
        new("DIVERTOR", "DIVERTOR [0-100]", "Sets the divertor exhaust rate, or shows the current/target reading if given no argument. Setting requires manual mode."),
        new("COIL", "COIL [index] [0-100]", "Sets one confinement coil segment's target strength. With no value, shows that coil's reading; with no arguments at all, shows every coil. Setting requires manual mode."),
    };

    private enum TerminalPhase { Boot, Login, Main }

    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private AudioSystem _audio = default!;

    private ReactorWindow? _window;
    private ReactorComponent? _state;
    private bool _confirmScram;
    private bool _welcomed;
    private int _logLineCount;

    private TerminalPhase _phase = TerminalPhase.Boot;
    private string _bootBuffer = string.Empty;

    private ReactorState? _lastState;
    private ReactorStartupStep? _lastStartupStep;
    private ReactorShutdownStep? _lastShutdownStep;

    private ReactorLineChart _outputChart = default!;
    private ReactorLineChart _fuelingChart = default!;
    private ReactorLineChart _heatingChart = default!;
    private ReactorLineChart _currentChart = default!;
    private ReactorLineChart _divertorChart = default!;
    private ReactorLineChart _coilChart = default!;

    public ReactorBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _audio = EntMan.System<AudioSystem>();

        _window = this.CreateWindow<ReactorWindow>();

        _window.IdSlotButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(ReactorComponent.IdCardSlotId));

        _window.CommandInput.OnTextTyped += _ => PlayKeystroke();
        _window.CommandInput.OnTextEntered += args =>
        {
            ExecuteCommand(args.Text);
            _window.CommandInput.Text = string.Empty;
        };

        SetupCharts();
        ApplyCrtShader();

        _phase = TerminalPhase.Boot;
        SetLayerVisibility();
        _audio.PlayGlobal(BootSound, Filter.Local(), false);
        PlayBootSequence(0);
    }

    private void ApplyCrtShader()
    {
        if (_window == null)
            return;

        // TextureRect.Draw() bails out before ever touching ShaderOverride if Texture is null - the
        // shader needs *something* to draw over, so give it a plain white texture to tint/replace.
        _window.CrtOverlay.Texture = Texture.White;

        if (_prototypes.TryIndex<ShaderPrototype>(CrtShaderId, out var proto))
            _window.CrtOverlay.ShaderOverride = proto.Instance().Duplicate();
    }

    private void SetupCharts()
    {
        if (_window == null)
            return;

        _outputChart = NewChart(_window.ChartsContainer, Loc.GetString("reactor-chart-output"));
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

    // --- Boot sequence ---

    private void PlayBootSequence(int index)
    {
        if (_window == null || _phase != TerminalPhase.Boot)
            return;

        if (index >= ReactorBootSequence.Lines.Length)
        {
            EnterLogin();
            return;
        }

        var line = ReactorBootSequence.Lines[index];
        AppendBootLine(line.Text);
        if (line.Text.Length > 0)
            _audio.PlayGlobal(BootLineSound, Filter.Local(), false);

        var delay = TimeSpan.FromMilliseconds(line.PauseAfter ? 700 : 160);
        Timer.Spawn(delay, () => PlayBootSequence(index + 1));
    }

    private void AppendBootLine(string text)
    {
        if (_window == null)
            return;

        _bootBuffer = _bootBuffer.Length > 0 ? $"{_bootBuffer}\n{text}" : text;

        var formatted = new FormattedMessage();
        formatted.PushColor(BootTextColor);
        formatted.AddText(_bootBuffer);
        formatted.Pop();
        _window.BootText.SetMessage(formatted);
    }

    private void EnterLogin()
    {
        _phase = TerminalPhase.Login;
        _bootBuffer = string.Empty;
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

    private void GrantAccess(string name)
    {
        if (_window == null)
            return;

        _audio.PlayGlobal(LoginGrantedSound, Filter.Local(), false);
        _window.LoginStatusLabel.Visible = true;
        _window.LoginStatusLabel.Text = Loc.GetString("reactor-login-granted", ("name", name));
        _window.OperatorLabel.Text = Loc.GetString("reactor-operator-label", ("name", name));

        Timer.Spawn(TimeSpan.FromMilliseconds(450), () =>
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

    // --- Terminal command line ---

    private void ExecuteCommand(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
            return;

        Log($"> {text}", EchoColor);

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0].ToUpperInvariant();

        if (name != "SCRAM" && _confirmScram)
        {
            _confirmScram = false;
            Log("SCRAM cancelled.", ResponseColor);
        }

        switch (name)
        {
            case "HELP":
                HandleHelp(parts);
                break;
            case "START":
                SendMessage(new ReactorStartMsg());
                _audio.PlayGlobal(StartSound, Filter.Local(), false);
                Log("Startup command sent.", ResponseColor);
                break;
            case "SHUTDOWN":
                SendMessage(new ReactorShutdownMsg());
                _audio.PlayGlobal(ShutdownSound, Filter.Local(), false);
                Log("Shutdown command sent.", ResponseColor);
                break;
            case "SCRAM":
                HandleScram();
                break;
            case "MODE":
                HandleMode(parts);
                break;
            case "FUEL":
                HandleRate(parts, "FUEL", c => c.FuelingRate, c => c.FuelingTarget, v => new ReactorSetFuelingRateMsg(v));
                break;
            case "HEAT":
                HandleRate(parts, "HEAT", c => c.HeatingPower, c => c.HeatingTarget, v => new ReactorSetHeatingPowerMsg(v));
                break;
            case "CURRENT":
                HandleRate(parts, "CURRENT", c => c.PlasmaCurrent, c => c.PlasmaCurrentTarget, v => new ReactorSetPlasmaCurrentMsg(v));
                break;
            case "DIVERTOR":
                HandleRate(parts, "DIVERTOR", c => c.DivertorRate, c => c.DivertorTarget, v => new ReactorSetDivertorRateMsg(v));
                break;
            case "COIL":
                HandleCoil(parts);
                break;
            default:
                PlayError();
                Log($"Unknown command: {name}. Type HELP for a list of commands.", ErrorColor);
                break;
        }
    }

    private void HandleHelp(string[] parts)
    {
        if (parts.Length >= 2)
        {
            var target = parts[1].ToUpperInvariant();
            var info = Commands.FirstOrDefault(c => c.Name == target);
            if (info.Name == null)
            {
                PlayError();
                Log($"No such command: {target}", ErrorColor);
                return;
            }

            _audio.PlayGlobal(HelpSound, Filter.Local(), false);
            Log($"{info.Usage} — {info.Description}", ResponseColor);
            return;
        }

        _audio.PlayGlobal(HelpSound, Filter.Local(), false);
        Log("Available commands (HELP <command> for details):", ResponseColor);

        var half = (Commands.Length + 1) / 2;
        var row1 = string.Join("   ", Commands.Take(half).Select(c => c.Name));
        var row2 = string.Join("   ", Commands.Skip(half).Select(c => c.Name));
        Log(row1, ResponseColor);
        if (row2.Length > 0)
            Log(row2, ResponseColor);
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

    /// <summary>
    /// Mirrors the server's own gate on manual-edit BUI messages (CanManuallyEdit in
    /// Content.Server/_Misfits/Reactor/ReactorSystem.cs) so the terminal can tell the operator
    /// *why* a command did nothing instead of optimistically claiming success for a message the
    /// server is about to silently reject.
    /// </summary>
    private bool CheckManualEditable()
    {
        if (_state == null)
            return false;

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

    private void HandleRate(string[] parts, string label, Func<ReactorComponent, float> current,
        Func<ReactorComponent, float> target, Func<float, BoundUserInterfaceMessage> makeMessage)
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

        _entities.TryGetComponent(Owner, out _state);
        var comp = _state;

        if (comp == null)
            return;

        switch (_phase)
        {
            case TerminalPhase.Boot:
                // State pushes that arrive mid-boot are ignored; the boot sequence drives its own pacing.
                return;
            case TerminalPhase.Login when comp.InsertedIdName != null:
                GrantAccess(comp.InsertedIdName);
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

    private void PushChartSamples(ReactorComponent comp)
    {
        _outputChart.Scale = comp.MaxOutput > 0f ? comp.MaxOutput : 1f;
        _outputChart.PushSample(comp.LoadFactor, comp.PowerOutput);
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

    private void RefreshDashboard(ReactorComponent comp)
    {
        var window = _window!;

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

        if (comp.State is ReactorState.Starting or ReactorState.ShuttingDown)
        {
            var startupStep = comp.StartupStep;
            var shutdownStep = comp.ShutdownStep;
            var stepName = comp.State == ReactorState.Starting ? startupStep.ToString() : shutdownStep.ToString();
            var total = (float) comp.StepDuration.TotalSeconds;
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

    /// <summary>
    /// Prints the reactor's startup/shutdown sequence to the terminal log as it actually happens,
    /// step by step, plus the major state transitions - so a full START/SHUTDOWN reads like a real
    /// terminal boot log rather than a single instantaneous message.
    /// </summary>
    private void TrackSequenceEvents(ReactorComponent comp)
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
                    Log($"{FormatStepName(startupStep.ToString())}...", ResponseColor);
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

    /// <summary>Turns a PascalCase enum name like "SystemsCheck" into "SYSTEMS CHECK" for console output.</summary>
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

    private void RefreshCoils(ReactorComponent comp, BoxContainer coilsContainer)
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

    private Control BuildCoilCell(int index, ReactorComponent comp)
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

    private static string Percent(float value) => $"{(int) (value * 100)}%";

    private static Color StatusColor(ReactorComponent comp)
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
        if (normalized > 1f)
            return Color.Red;
        return normalized > 0.8f ? Color.Yellow : Color.LightGreen;
    }
}

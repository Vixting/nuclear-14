using Content.Shared._Misfits.Reactor;
using Content.Shared.Containers.ItemSlots;
using JetBrains.Annotations;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Misfits.Reactor;

[UsedImplicitly]
public sealed class ReactorBui : BoundUserInterface
{
    private static readonly SoundSpecifier KeystrokeSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/keystroke_blip.ogg");
    private static readonly SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/confirm_blip.ogg");
    private static readonly SoundSpecifier BootSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/poweron.ogg");
    private static readonly SoundSpecifier BootLineSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/keyboard1.ogg");
    private static readonly SoundSpecifier LoginGrantedSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/terminal_prompt_confirm.ogg");
    private static readonly SoundSpecifier LoginDeniedSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/Terminal/terminal_prompt_deny.ogg");

    private static readonly Color BootTextColor = Color.FromHex("#33FF66");

    private const string CrtShaderId = "ReactorCrt";

    private enum TerminalPhase { Boot, Login, Main }

    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private AudioSystem _audio = default!;

    private ReactorWindow? _window;
    private ReactorComponent? _state;
    private bool _confirmScram;

    private TerminalPhase _phase = TerminalPhase.Boot;
    private string _bootBuffer = string.Empty;

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

        _window.AutoButton.OnPressed += _ => SendMessage(new ReactorSetModeMsg(ReactorMode.Automatic));
        _window.ManualButton.OnPressed += _ => SendMessage(new ReactorSetModeMsg(ReactorMode.Manual));

        WireRateInput(_window.FuelingInput, v => new ReactorSetFuelingRateMsg(v));
        WireRateInput(_window.HeatingInput, v => new ReactorSetHeatingPowerMsg(v));
        WireRateInput(_window.CurrentInput, v => new ReactorSetPlasmaCurrentMsg(v));
        WireRateInput(_window.DivertorInput, v => new ReactorSetDivertorRateMsg(v));

        _window.StartButton.OnPressed += _ => SendMessage(new ReactorStartMsg());
        _window.ShutdownButton.OnPressed += _ => SendMessage(new ReactorShutdownMsg());
        _window.ScramButton.OnPressed += _ => PressScram();

        _window.IdSlotButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(ReactorComponent.IdCardSlotId));

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

        if (_prototypes.TryIndex<ShaderPrototype>(CrtShaderId, out var proto))
            _window.CrtOverlay.ShaderOverride = proto.Instance().Duplicate();
    }

    private void SetupCharts()
    {
        if (_window == null)
            return;

        _outputChart = NewChart(Loc.GetString("reactor-chart-output"));
        _fuelingChart = NewChart(Loc.GetString("reactor-chart-fueling"));
        _heatingChart = NewChart(Loc.GetString("reactor-chart-heating"));
        _currentChart = NewChart(Loc.GetString("reactor-chart-current"));
        _divertorChart = NewChart(Loc.GetString("reactor-chart-divertor"));
        _coilChart = NewChart(Loc.GetString("reactor-chart-coils"));
    }

    private ReactorLineChart NewChart(string title)
    {
        var chart = new ReactorLineChart(_resourceCache, title);
        _window!.ChartsContainer.AddChild(chart);
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

    /// <summary>Wires a "type a 0-100 value, press Enter" terminal input to a rate-setting message, with a keystroke blip per character and a confirm blip on submit.</summary>
    private void WireRateInput(LineEdit input, Func<float, BoundUserInterfaceMessage> makeMessage)
    {
        input.OnTextTyped += _ => PlayKeystroke();
        input.OnTextEntered += args =>
        {
            if (TryParsePercent(args.Text, out var normalized))
            {
                SendMessage(makeMessage(normalized));
                PlayConfirm();
            }

            Refresh();
        };
    }

    private static bool TryParsePercent(string text, out float normalized)
    {
        normalized = 0f;
        if (!int.TryParse(text.Trim().TrimEnd('%'), out var percent))
            return false;

        normalized = Clamp(percent / 100f);
        return true;
    }

    private void PlayKeystroke() => _audio.PlayGlobal(KeystrokeSound, Filter.Local(), false);
    private void PlayConfirm() => _audio.PlayGlobal(ConfirmSound, Filter.Local(), false);

    private static float Clamp(float value) => Math.Clamp(value, 0f, 1f);

    private void PressScram()
    {
        if (!_confirmScram)
        {
            _confirmScram = true;
            _window!.ScramButton.Text = Loc.GetString("reactor-scram-confirm");
            return;
        }

        _confirmScram = false;
        SendMessage(new ReactorScramMsg());
    }

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
        window.DemandLabel.Text = Loc.GetString("reactor-demand", ("value", (int) (demandFraction * 100)));

        var manual = comp.Mode == ReactorMode.Manual;
        window.AutoButton.Disabled = !manual;
        window.ManualButton.Disabled = manual;

        var online = comp.State == ReactorState.Online;
        var canEdit = online && manual;

        SetRateInput(window.FuelingInput, comp.FuelingRate, canEdit);
        SetRateInput(window.HeatingInput, comp.HeatingPower, canEdit);
        SetRateInput(window.CurrentInput, comp.PlasmaCurrent, canEdit);
        SetRateInput(window.DivertorInput, comp.DivertorRate, canEdit);

        window.BetaLabel.Text = Loc.GetString("reactor-beta", ("value", (int) (comp.Beta * 100)));
        window.BetaLabel.FontColorOverride = ThresholdColor(comp.Beta);
        window.DensityLabel.Text = Loc.GetString("reactor-density", ("value", (int) (comp.Density * 100)));
        window.DensityLabel.FontColorOverride = ThresholdColor(comp.Density);
        window.AshLabel.Text = Loc.GetString("reactor-ash", ("value", (int) (comp.AshLevel * 100)));
        window.IntegrityLabel.Text = Loc.GetString("reactor-integrity", ("value", (int) comp.Integrity));
        window.IntegrityLabel.FontColorOverride = SeverityColor(SharedReactorSystem.GetSeverity(comp.Integrity));

        window.CoilsContainer.DisposeAllChildren();
        for (var i = 0; i < comp.CoilStrength.Count; i++)
            window.CoilsContainer.AddChild(BuildCoilRow(i, comp, canEdit));

        var canStart = comp.State is ReactorState.Offline or ReactorState.Scrammed;
        var locked = comp.LockedUntil is { } lockedUntil && lockedUntil > _timing.CurTime;
        window.StartButton.Disabled = !canStart || locked;
        window.StartButton.Text = locked
            ? Loc.GetString("reactor-start-locked", ("seconds", (int) (comp.LockedUntil!.Value - _timing.CurTime).TotalSeconds))
            : Loc.GetString("reactor-start");

        window.ShutdownButton.Disabled = comp.State != ReactorState.Online;

        var canScram = comp.State is ReactorState.Starting or ReactorState.Online or ReactorState.ShuttingDown;
        window.ScramButton.Disabled = !canScram;
        if (!canScram)
            _confirmScram = false;
        if (!_confirmScram)
            window.ScramButton.Text = Loc.GetString("reactor-scram");

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
    }

    /// <summary>Syncs a rate LineEdit to the live value, unless the player is actively typing in it.</summary>
    private static void SetRateInput(LineEdit input, float value, bool canEdit)
    {
        input.Editable = canEdit;
        if (!input.HasKeyboardFocus())
            input.Text = ((int) (value * 100)).ToString();
    }

    private Control BuildCoilRow(int index, ReactorComponent comp, bool canEdit)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, VerticalAlignment = Control.VAlignment.Center };

        row.AddChild(new Label { Text = Loc.GetString("reactor-coil", ("index", index + 1)), MinWidth = 60, StyleClasses = { "PipBoyLabel" } });
        row.AddChild(new Label { Text = Percent(comp.CoilStrength[index]), HorizontalAlignment = Control.HAlignment.Center, MinWidth = 50, StyleClasses = { "PipBoyLabel" } });

        row.AddChild(new Label { Text = "▸", Margin = new Thickness(4, 0), StyleClasses = { "PipBoyLabel" } });

        var targetInput = new LineEdit
        {
            Text = ((int) (comp.CoilTargets[index] * 100)).ToString(),
            Editable = canEdit,
            MinWidth = 60,
            StyleClasses = { "PipBoyLineEdit" },
        };
        targetInput.OnTextTyped += _ => PlayKeystroke();
        targetInput.OnTextEntered += args =>
        {
            if (TryParsePercent(args.Text, out var normalized))
            {
                SendMessage(new ReactorSetCoilTargetMsg(index, normalized));
                PlayConfirm();
            }

            Refresh();
        };
        row.AddChild(targetInput);

        var heat = comp.CoilLocalHeat[index];
        var heatLabel = new Label
        {
            Text = Loc.GetString("reactor-coil-heat", ("value", (int) (heat * 100))),
            HorizontalExpand = true,
            HorizontalAlignment = Control.HAlignment.Right,
            FontColorOverride = heat > 0.5f ? Color.OrangeRed : Color.Gray,
        };
        row.AddChild(heatLabel);

        return row;
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

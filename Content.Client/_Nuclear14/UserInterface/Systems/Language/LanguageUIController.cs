using System.Linq;
using Content.Client._Nuclear14.Language.Systems;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._Nuclear14.Language.Prototypes;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._Nuclear14.UserInterface.Systems.Language;

public sealed class LanguageUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private LanguageSystem _languageSystem = default!;
    private LanguageMenuWindow? _window;

    private MenuButton? LanguageButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.LanguageButton;

    public void OnStateEntered(GameplayState state)
    {
        _languageSystem = _entitySystemManager.GetEntitySystem<LanguageSystem>();
        _languageSystem.OnLanguagesChanged += OnLanguagesChanged;
        _languageSystem.OnLanguageLearningChanged += OnLanguageLearningChanged;

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenLanguageMenu, InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .Register<LanguageUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        if (_languageSystem != null)
        {
            _languageSystem.OnLanguagesChanged -= OnLanguagesChanged;
            _languageSystem.OnLanguageLearningChanged -= OnLanguageLearningChanged;
        }

        _window?.Close();
        _window = null;
        CommandBinds.Unregister<LanguageUIController>();
    }

    public void LoadButton()
    {
        if (LanguageButton == null)
            return;

        LanguageButton.OnPressed += LanguageButtonPressed;
    }

    public void UnloadButton()
    {
        if (LanguageButton == null)
            return;

        LanguageButton.OnPressed -= LanguageButtonPressed;
    }

    private void LanguageButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }

    public void ToggleWindow()
    {
        if (_window == null)
        {
            _window = UIManager.CreateWindow<LanguageMenuWindow>();
            _window.OnClose += () =>
            {
                _window = null;
                if (LanguageButton != null)
                    LanguageButton.Pressed = false;
            };
            _window.OnOpen += () =>
            {
                if (LanguageButton != null)
                    LanguageButton.Pressed = true;
                UpdateLanguageWindow();
            };
            _window.OnLanguageSelected += OnLanguageSelected;
        }

        if (_window.IsOpen)
        {
            _window.Close();
        }
        else
        {
            UpdateLanguageWindow();
            _window.OpenCentered();
        }
    }

    private void OnLanguagesChanged()
    {
        UpdateLanguageWindow();
    }

    private void OnLanguageLearningChanged()
    {
        UpdateLanguageWindow();
    }

    private void UpdateLanguageWindow()
    {
        if (_window == null || _player.LocalSession?.AttachedEntity is not { } entity)
            return;

        var currentLanguage = _languageSystem.GetCurrentLanguage(entity);
        var spokenLanguages = _languageSystem.GetSpokenLanguages(entity);
        var learningLanguages = _languageSystem.GetLearningLanguages(entity);
        _window.UpdateLanguages(currentLanguage, spokenLanguages, learningLanguages);
    }

    private void OnLanguageSelected(ProtoId<LanguagePrototype> language)
    {
        _languageSystem.RequestSetLanguage(language);
    }
}

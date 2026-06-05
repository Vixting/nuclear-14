using Content.Shared._Nuclear14.Language.Prototypes;
using Content.Shared._Nuclear14.Language.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Nuclear14.Language.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), ComponentProtoName("Language")]
[Access(typeof(SharedLanguageLearningSystem), typeof(SharedLanguageSystem))]
public sealed partial class LanguageComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<LanguagePrototype>> SpokenLanguages = new();

    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<LanguagePrototype>> UnderstoodLanguages = new();

    [DataField, AutoNetworkedField]
    public ProtoId<LanguagePrototype>? CurrentLanguage;

    [DataField, AutoNetworkedField]
    public ProtoId<LanguagePrototype>? DefaultLanguage;
}

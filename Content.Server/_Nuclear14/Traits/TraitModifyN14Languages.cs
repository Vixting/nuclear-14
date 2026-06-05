using Content.Server._Nuclear14.Language.Systems;
using Content.Shared._Nuclear14.Language.Prototypes;
using Content.Shared.Traits;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._Nuclear14.Traits;

/// <summary>
/// Trait function that adds/removes N14 language system languages on player spawn.
/// </summary>
[UsedImplicitly]
public sealed partial class TraitModifyN14Languages : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public List<ProtoId<LanguagePrototype>>? LanguagesSpoken { get; private set; }

    [DataField, AlwaysPushInheritance]
    public List<ProtoId<LanguagePrototype>>? LanguagesUnderstood { get; private set; }

    [DataField, AlwaysPushInheritance]
    public List<ProtoId<LanguagePrototype>>? RemoveLanguagesSpoken { get; private set; }

    [DataField, AlwaysPushInheritance]
    public List<ProtoId<LanguagePrototype>>? RemoveLanguagesUnderstood { get; private set; }

    public override void OnPlayerSpawn(EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        var language = entityManager.System<LanguageSystem>();

        if (RemoveLanguagesSpoken is not null)
            foreach (var lang in RemoveLanguagesSpoken)
                language.RemoveLanguage(uid, lang, removeSpoken: true, removeUnderstood: false);

        if (RemoveLanguagesUnderstood is not null)
            foreach (var lang in RemoveLanguagesUnderstood)
                language.RemoveLanguage(uid, lang, removeSpoken: false, removeUnderstood: true);

        if (LanguagesSpoken is not null)
            foreach (var lang in LanguagesSpoken)
                language.AddLanguage(uid, lang, addSpoken: true, addUnderstood: false);

        if (LanguagesUnderstood is not null)
            foreach (var lang in LanguagesUnderstood)
                language.AddLanguage(uid, lang, addSpoken: false, addUnderstood: true);
    }
}

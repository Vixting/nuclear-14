using Content.Shared._Nuclear14.Language.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Nuclear14.Language;

[ByRefEvent]
public record struct DetermineLanguageEvent(EntityUid Speaker, ProtoId<LanguagePrototype> Language);

[Serializable, NetSerializable]
public sealed class LanguagesSetMessage(ProtoId<LanguagePrototype> currentLanguage) : EntityEventArgs
{
    public ProtoId<LanguagePrototype> CurrentLanguage = currentLanguage;
}

[ByRefEvent]
public record struct TransformLanguageMessageEvent(
    EntityUid Speaker,
    ProtoId<LanguagePrototype> Language,
    string Message);

[ByRefEvent]
public record struct CanUnderstandLanguageEvent(
    EntityUid Listener,
    ProtoId<LanguagePrototype> Language,
    bool CanUnderstand = false);

[ByRefEvent]
public record struct DetermineEntityLanguagesEvent(
    HashSet<ProtoId<LanguagePrototype>> SpokenLanguages,
    HashSet<ProtoId<LanguagePrototype>> UnderstoodLanguages)
{
    public DetermineEntityLanguagesEvent() : this([], [])
    {
    }
}

[ByRefEvent]
public record struct ProcessSpeakerLanguageEvent(
    EntityUid Speaker,
    ProtoId<LanguagePrototype> Language,
    string ProcessedMessage);

[ByRefEvent]
public readonly record struct LanguagesUpdateEvent;

[ByRefEvent]
public readonly record struct LanguageChangeAttemptedEvent(
    EntityUid Entity,
    ProtoId<LanguagePrototype> OldLanguage,
    ProtoId<LanguagePrototype> NewLanguage,
    bool Success);

/// <summary>
/// Raised on the server when an entity speaks using the N14 language system.
/// Used by the learning system to process nearby learners.
/// </summary>
public sealed class N14EntitySpokeEvent : EntityEventArgs
{
    public readonly EntityUid Source;
    public readonly string Message;
    public readonly ProtoId<LanguagePrototype> Language;

    public N14EntitySpokeEvent(EntityUid source, string message, ProtoId<LanguagePrototype> language)
    {
        Source = source;
        Message = message;
        Language = language;
    }
}

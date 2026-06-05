using Content.Shared._Nuclear14.Language;
using Content.Shared.Chat;
using Content.Shared.Speech;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Nuclear14.Language.Prototypes;

[Prototype("n14language")]
public sealed partial class LanguagePrototype : IPrototype
{
    public const float DefaultWordComprehensionValue = 0.0f;
    public const float DefaultClearComprehensionThresholdValue = 0.6f;
    public const float DefaultPartialComprehensionThresholdValue = 0.2f;
    public const string DefaultPartialGarbleCharactersValue = "~?*#";
    public const float DefaultMinimumPartialGarbleRateValue = 0.1f;
    public const float DefaultPartialGarbleRateMultiplierValue = 0.75f;
    public const int DefaultMinimumRequiredLearnedWordsValue = 15;
    public const float DefaultMaximumOverallComprehensionValue = 0.95f;
    public const float DefaultLearnedWordComprehensionWeightValue = 0.7f;
    public const float DefaultVocabularyCompletenessWeightValue = 0.3f;
    public const float DefaultLearningRateMultiplierValue = 1.0f;

    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = default!;

    [DataField]
    public string? Description;

    [DataField]
    public bool IsVisibleLanguage;

    [DataField]
    public string? TypefaceId;

    [DataField]
    public int? TextSize;

    [DataField]
    public bool ShowLanguageName;

    [DataField]
    public bool ShowLanguageIcon = true;

    [DataField]
    public SpriteSpecifier? LanguageIcon;

    [DataField]
    public bool CanUseRadio = true;

    [DataField]
    public bool NeedsSpeech = true;

    [DataField]
    public bool NeedsLOS;

    [DataField]
    public N14ObfuscationMethod ObfuscationMethod = N14ObfuscationMethod.Default;

    [DataField]
    public bool RandomizeObfuscation;

    [DataField]
    public N14SpeechOverrideInfo SpeechOverride = new();

    [DataField]
    public LocId? FirstContactMeaning;

    [DataField]
    public int ExpectedVocabularySize = 100;

    [DataField]
    public float DefaultWordComprehension = DefaultWordComprehensionValue;

    [DataField]
    public float ClearComprehensionThreshold = DefaultClearComprehensionThresholdValue;

    [DataField]
    public float PartialComprehensionThreshold = DefaultPartialComprehensionThresholdValue;

    [DataField]
    public string PartialGarbleCharacters = DefaultPartialGarbleCharactersValue;

    [DataField]
    public float MinimumPartialGarbleRate = DefaultMinimumPartialGarbleRateValue;

    [DataField]
    public float PartialGarbleRateMultiplier = DefaultPartialGarbleRateMultiplierValue;

    [DataField]
    public int MinimumRequiredLearnedWords = DefaultMinimumRequiredLearnedWordsValue;

    [DataField]
    public float MaximumOverallComprehension = DefaultMaximumOverallComprehensionValue;

    [DataField]
    public float LearnedWordComprehensionWeight = DefaultLearnedWordComprehensionWeightValue;

    [DataField]
    public float VocabularyCompletenessWeight = DefaultVocabularyCompletenessWeightValue;

    [DataField]
    public float LearningRateMultiplier = DefaultLearningRateMultiplierValue;

    public string LocalizedName => Loc.GetString($"language-{ID}-name");
    public string LocalizedDescription => Loc.GetString($"language-{ID}-description");
    public string? DisplayedLanguageIcon => ShowLanguageIcon ? ID : null;
}

[DataDefinition]
public sealed partial class N14SpeechOverrideInfo
{
    [DataField]
    public Color? Color;

    [DataField]
    public string? FontId;

    [DataField]
    public int? FontSize;

    /// <summary>Whether this language can be used over radio. Defaults to the prototype-level CanUseRadio.</summary>
    [DataField]
    public bool AllowRadio = true;

    /// <summary>If false, entity can speak this language even while muted; accents are not applied.</summary>
    [DataField]
    public bool RequireSpeech = true;

    /// <summary>If true, messages in this language are relayed to the Empathy Chat channel.</summary>
    [DataField]
    public bool EmpathySpeech = false;

    /// <summary>If set, all messages in this language are forced to this chat type.</summary>
    [DataField]
    public InGameICChatType? ChatTypeOverride;

    [DataField]
    public List<LocId>? SpeechVerbOverrides;

    [DataField]
    public ProtoId<SpeechSoundsPrototype>? SpeechSoundsOverride;

    [DataField]
    public Dictionary<InGameICChatType, LocId> MessageWrapOverrides = new();
}

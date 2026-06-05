using Content.Server._Nuclear14.Language.Systems;
using Content.Shared._Nuclear14.Language.Systems;

namespace Content.Server.Chat.Systems;

// Registers the N14 language system dependency on ChatSystem.
public sealed partial class ChatSystem
{
    [Dependency] private readonly LanguageSystem _n14Language = default!;
}

using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server._Nuclear14.Language.Systems;
using Content.Shared._Nuclear14.Language.Prototypes;
using Content.Shared.Chat;
using Content.Shared.WhiteDream.BloodCult.BloodCultist;
using Content.Shared.WhiteDream.BloodCult.Constructs;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.WhiteDream.BloodCult;

public sealed class BloodCultChatSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BloodCultistComponent, EntitySpokeEvent>(OnCultistSpeak);
        SubscribeLocalEvent<ConstructComponent, EntitySpokeEvent>(OnConstructSpeak);
    }

    private void OnCultistSpeak(EntityUid uid, BloodCultistComponent component, EntitySpokeEvent args)
    {
        if (args.Source != uid || args.Language != component.CultLanguageId || args.IsWhisper)
            return;

        SendMessage(args.Source, args.Message, false, args.Language);
    }

    private void OnConstructSpeak(EntityUid uid, ConstructComponent component, EntitySpokeEvent args)
    {
        if (args.Source != uid || args.Language != component.CultLanguageId || args.IsWhisper)
            return;

        SendMessage(args.Source, args.Message, false, args.Language);
    }

    private void SendMessage(EntityUid source, string message, bool hideChat, ProtoId<LanguagePrototype> language)
    {
        var clients = GetClients(language);
        var playerName = Name(source);
        _protoManager.TryIndex(language, out var langProto);
        var wrappedMessage = Loc.GetString("chat-manager-send-cult-chat-wrap-message",
            ("channelName", Loc.GetString("chat-manager-cult-channel-name")),
            ("player", playerName),
            ("message", FormattedMessage.EscapeText(message)));

        _chatManager.ChatMessageToMany(ChatChannel.Telepathic,
            message,
            wrappedMessage,
            source,
            hideChat,
            true,
            clients.ToList(),
            langProto?.SpeechOverride.Color);
    }

    private IEnumerable<INetChannel> GetClients(ProtoId<LanguagePrototype> languageId)
    {
        return Filter.Empty()
            .AddWhereAttachedEntity(entity => _language.CanUnderstand(entity, languageId))
            .Recipients
            .Union(_adminManager.ActiveAdmins)
            .Select(p => p.Channel);
    }
}

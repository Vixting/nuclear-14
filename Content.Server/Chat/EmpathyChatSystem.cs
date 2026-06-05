using System.Linq;
using Robust.Shared.Utility;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Administration.Managers;
using Content.Server._Nuclear14.Language.Systems;
using Content.Shared._Nuclear14.Language.Components;
using Content.Shared._Nuclear14.Language.Prototypes;
using Content.Shared.Chat;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Chat;

public sealed partial class EmpathyChatSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LanguageComponent, EntitySpokeEvent>(OnSpeak);
    }

    private void OnSpeak(EntityUid uid, LanguageComponent component, EntitySpokeEvent args)
    {
        if (args.Source != uid || args.IsWhisper)
            return;

        if (!_prototype.TryIndex(args.Language, out var langProto) || !langProto.SpeechOverride.EmpathySpeech)
            return;

        SendEmpathyChat(args.Source, args.Message, false);
    }

    public void SendEmpathyChat(EntityUid source, string message, bool hideChat)
    {
        var clients = GetEmpathChatClients();
        var wrappedMessage = Loc.GetString("chat-manager-send-empathy-chat-wrap-message",
            ("source", source),
            ("message", FormattedMessage.EscapeText(message)));

        _chatManager.ChatMessageToMany(ChatChannel.Telepathic, message, wrappedMessage, source, hideChat, true, clients.ToList(), Color.FromHex("#be3cc5"));
    }

    private IEnumerable<INetChannel> GetEmpathChatClients()
    {
        return Filter.Empty()
            .AddWhereAttachedEntity(CanHearEmpathy)
            .Recipients
            .Union(_adminManager.ActiveAdmins)
            .Select(p => p.Channel);
    }

    public bool CanHearEmpathy(EntityUid entity)
    {
        var spokenLanguages = _language.GetSpokenLanguages(entity);
        foreach (var langId in spokenLanguages)
        {
            if (_prototype.TryIndex(langId, out var proto) && proto.SpeechOverride.EmpathySpeech)
                return true;
        }
        return false;
    }
}

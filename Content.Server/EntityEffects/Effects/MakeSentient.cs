using System.Linq;
using Content.Server.Ghost.Roles.Components;
using Content.Server._Nuclear14.Language.Systems;
using Content.Server.Speech.Components;
using Content.Shared._Nuclear14.Language.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.Mind.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Humanoid;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class MakeSentient : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-make-sentient", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entityManager = args.EntityManager;
        var uid = args.TargetEntity;

        entityManager.RemoveComponent<ReplacementAccentComponent>(uid);
        entityManager.RemoveComponent<MonkeyAccentComponent>(uid);

        // Give the entity at least the common language (English)
        var languageSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<LanguageSystem>();
        languageSystem.AddLanguage(uid, SharedLanguageSystem.CommonLanguage);

        if (entityManager.TryGetComponent<MindContainerComponent>(uid, out var mindContainer) && mindContainer.HasMind)
            return;

        if (entityManager.TryGetComponent(uid, out GhostRoleComponent? ghostRole))
            return;

        if (entityManager.HasComponent<HumanoidAppearanceComponent>(uid))
            return;

        ghostRole = entityManager.AddComponent<GhostRoleComponent>(uid);
        entityManager.EnsureComponent<GhostTakeoverAvailableComponent>(uid);

        var entityData = entityManager.GetComponent<MetaDataComponent>(uid);
        ghostRole.RoleName = entityData.EntityName;
        ghostRole.RoleDescription = Loc.GetString("ghost-role-information-cognizine-description");
    }
}

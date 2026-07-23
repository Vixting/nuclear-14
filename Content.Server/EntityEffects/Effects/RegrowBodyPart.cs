using Content.Shared.Body.Components;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class RegrowBodyPart : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-regrow-body-part");

    public override void Effect(EntityEffectBaseArgs args)
    {
        var sawmill = IoCManager.Resolve<ILogManager>().GetSawmill("regrow_body_part");
        var entityManager = args.EntityManager;
        var target = args.TargetEntity;

        if (!entityManager.TryGetComponent<BodyComponent>(target, out var body))
        {
            sawmill.Warning($"{target} has no BodyComponent - can't regrow anything.");
            return;
        }

        if (body.Prototype is not { } bodyProtoId)
        {
            sawmill.Warning($"{target}'s BodyComponent has no Prototype set - can't look up part prototypes for it.");
            return;
        }

        if (body.RootContainer.ContainedEntity is not { } rootPartId)
        {
            sawmill.Warning($"{target} has no root body part.");
            return;
        }

        var protoManager = IoCManager.Resolve<IPrototypeManager>();
        if (!protoManager.TryIndex(bodyProtoId, out var bodyProto))
        {
            sawmill.Warning($"{target}'s body prototype '{bodyProtoId}' does not exist.");
            return;
        }

        var bodySystem = entityManager.EntitySysManager.GetEntitySystem<SharedBodySystem>();
        var containerSystem = entityManager.EntitySysManager.GetEntitySystem<SharedContainerSystem>();

        var emptySlotsSeen = 0;

        foreach (var (partId, part) in bodySystem.GetBodyPartChildren(rootPartId))
        {
            foreach (var (slotId, _) in part.Children)
            {
                var containerId = SharedBodySystem.GetPartSlotContainerId(slotId);
                if (!containerSystem.TryGetContainer(partId, containerId, out var container))
                {
                    sawmill.Warning($"Slot '{slotId}' on {partId} has no container '{containerId}'.");
                    continue;
                }

                if (container.ContainedEntities.Count > 0)
                    continue; // slot is filled, nothing to regrow here

                emptySlotsSeen++;

                if (!bodyProto.Slots.TryGetValue(slotId, out var slotDef) || slotDef.Part is not { } partProtoId)
                {
                    sawmill.Warning($"Slot '{slotId}' is empty but has no matching entry (or no Part) in body prototype '{bodyProtoId}'.");
                    continue;
                }

                var coordinates = entityManager.GetComponent<TransformComponent>(target).Coordinates;
                var newPart = entityManager.SpawnEntity(partProtoId, coordinates);

                if (bodySystem.AttachPart(partId, slotId, newPart))
                {
                    sawmill.Info($"Regrew '{slotId}' ({partProtoId}) on {target}.");
                    return;
                }

                sawmill.Warning($"Spawned '{partProtoId}' for empty slot '{slotId}' on {partId}, but AttachPart failed.");
                entityManager.DeleteEntity(newPart);
            }
        }

        if (emptySlotsSeen == 0)
            sawmill.Info($"{target} has no missing body part slots - nothing to regrow.");
    }
}

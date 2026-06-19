using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Linq;

namespace Content.Server._Nuclear14.Botany;

public sealed class BotanistEyeSystem : EntitySystem
{
    [Dependency] private readonly BotanySystem _botany = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlantHolderComponent, GetVerbsEvent<ExamineVerb>>(OnPlantHolderVerb);
        SubscribeLocalEvent<SeedComponent, GetVerbsEvent<ExamineVerb>>(OnSeedVerb);
        SubscribeLocalEvent<ProduceComponent, GetVerbsEvent<ExamineVerb>>(OnProduceVerb);
    }

    private void OnPlantHolderVerb(Entity<PlantHolderComponent> entity, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || !HasComp<BotanistEyeComponent>(args.User))
            return;

        if (entity.Comp.Seed == null)
            return;

        var user = args.User;
        var target = args.Target;
        var comp = entity.Comp;

        args.Verbs.Add(new ExamineVerb
        {
            Act = () =>
            {
                _examine.SendExamineTooltip(user, target, BuildPlantAnalysis(comp), false, false);
            },
            Text = Loc.GetString("botanist-eye-verb-text"),
            Message = Loc.GetString("botanist-eye-verb-message"),
            Category = VerbCategory.Examine,
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/information.svg.192dpi.png")),
        });
    }

    private void OnSeedVerb(Entity<SeedComponent> entity, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || !HasComp<BotanistEyeComponent>(args.User))
            return;

        if (!_botany.TryGetSeed(entity.Comp, out _))
            return;

        var user = args.User;
        var target = args.Target;
        var seedComp = entity.Comp;

        args.Verbs.Add(new ExamineVerb
        {
            Act = () =>
            {
                if (!_botany.TryGetSeed(seedComp, out var seed))
                    return;
                _examine.SendExamineTooltip(user, target, BuildSeedAnalysis(seed), false, false);
            },
            Text = Loc.GetString("botanist-eye-verb-text"),
            Message = Loc.GetString("botanist-eye-verb-message"),
            Category = VerbCategory.Examine,
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/information.svg.192dpi.png")),
        });
    }

    private void OnProduceVerb(Entity<ProduceComponent> entity, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || !HasComp<BotanistEyeComponent>(args.User))
            return;

        if (!_botany.TryGetSeed(entity.Comp, out _))
            return;

        var user = args.User;
        var target = args.Target;
        var produceComp = entity.Comp;

        args.Verbs.Add(new ExamineVerb
        {
            Act = () =>
            {
                if (!_botany.TryGetSeed(produceComp, out var seed))
                    return;
                _examine.SendExamineTooltip(user, target, BuildProduceAnalysis(seed), false, false);
            },
            Text = Loc.GetString("botanist-eye-verb-text"),
            Message = Loc.GetString("botanist-eye-verb-message"),
            Category = VerbCategory.Examine,
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/information.svg.192dpi.png")),
        });
    }

    private FormattedMessage BuildPlantAnalysis(PlantHolderComponent comp)
    {
        var msg = new FormattedMessage();
        var seed = comp.Seed!;

        if (comp.Dead)
        {
            msg.AddMarkup(Loc.GetString("botanist-eye-plant-dead"));
            return msg;
        }

        msg.AddMarkup(Loc.GetString("botanist-eye-health",
            ("health", (int) comp.Health), ("endurance", (int) seed.Endurance)));
        msg.PushNewline();

        msg.AddMarkup(Loc.GetString("botanist-eye-age",
            ("age", comp.Age), ("lifespan", (int) seed.Lifespan)));
        msg.PushNewline();

        msg.AddMarkup(Loc.GetString("botanist-eye-potency",
            ("potency", MathF.Round(seed.Potency, 1).ToString("G"))));
        msg.PushNewline();

        msg.AddMarkup(Loc.GetString("botanist-eye-yield",
            ("yield", seed.Yield), ("yieldMod", comp.YieldMod)));
        msg.PushNewline();

        msg.AddMarkup(Loc.GetString("botanist-eye-weed",
            ("level", (int) comp.WeedLevel), ("tolerance", (int) seed.WeedTolerance)));
        msg.PushNewline();

        msg.AddMarkup(Loc.GetString("botanist-eye-pest",
            ("level", (int) comp.PestLevel), ("tolerance", (int) seed.PestTolerance)));

        if (comp.Toxins > 0f)
        {
            msg.PushNewline();
            msg.AddMarkup(Loc.GetString("botanist-eye-toxins",
                ("toxins", (int) comp.Toxins)));
        }

        if (comp.ImproperHeat)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-improper-heat")); }
        if (comp.ImproperLight)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-improper-light")); }
        if (comp.ImproperPressure)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-improper-pressure")); }

        if (seed.Seedless)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-trait-seedless")); }
        if (seed.Slip)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-trait-slip")); }
        if (seed.Sentient)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-trait-sentient")); }
        if (seed.Bioluminescent)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-trait-bioluminescent")); }

        foreach (var mutation in seed.Mutations)
        {
            if (mutation.Description == null || mutation.AppliesToPlant)
                continue;
            msg.PushNewline();
            msg.AddMarkup(Loc.GetString("botanist-eye-produce-mutation",
                ("desc", Loc.GetString(mutation.Description))));
        }

        AppendChemicals(msg, seed);
        return msg;
    }

    private FormattedMessage BuildSeedAnalysis(SeedData seed)
    {
        var msg = new FormattedMessage();

        msg.AddMarkup(Loc.GetString("botanist-eye-potency", ("potency", MathF.Round(seed.Potency, 1).ToString("G"))));
        msg.PushNewline();
        msg.AddMarkup(Loc.GetString("botanist-eye-yield", ("yield", seed.Yield), ("yieldMod", 1)));
        msg.PushNewline();
        msg.AddMarkup(Loc.GetString("botanist-eye-maturation", ("cycles", (int) seed.Maturation)));
        msg.PushNewline();
        msg.AddMarkup(Loc.GetString("botanist-eye-production", ("cycles", (int) seed.Production)));
        msg.PushNewline();
        msg.AddMarkup(Loc.GetString("botanist-eye-lifespan", ("cycles", (int) seed.Lifespan)));
        msg.PushNewline();
        msg.AddMarkup(Loc.GetString("botanist-eye-endurance", ("endurance", (int) seed.Endurance)));

        if (seed.Seedless)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-trait-seedless")); }
        if (!seed.Viable)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-trait-unviable")); }
        if (seed.Slip)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-trait-slip")); }
        if (seed.Sentient)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-trait-sentient")); }
        if (seed.Bioluminescent)
        { msg.PushNewline(); msg.AddMarkup(Loc.GetString("botanist-eye-trait-bioluminescent")); }

        AppendChemicals(msg, seed);
        return msg;
    }

    private FormattedMessage BuildProduceAnalysis(SeedData seed)
    {
        var msg = new FormattedMessage();
        msg.AddMarkup(Loc.GetString("botanist-eye-potency", ("potency", MathF.Round(seed.Potency, 1).ToString("G"))));
        AppendChemicals(msg, seed);
        return msg;
    }

    private void AppendChemicals(FormattedMessage msg, SeedData seed)
    {
        if (seed.Chemicals.Count == 0)
            return;

        msg.PushNewline();
        msg.AddMarkup(Loc.GetString("botanist-eye-chemicals-header"));

        foreach (var (reagentId, chem) in seed.Chemicals.OrderBy(c => c.Key))
        {
            if (!_prototype.TryIndex<ReagentPrototype>(reagentId, out var reagent))
                continue;

            var potencyPart = chem.PotencyDivisor > 0f ? seed.Potency / chem.PotencyDivisor : 0f;
            var estimated = MathF.Round(Math.Min(chem.Min + potencyPart, chem.Max), 1);

            msg.PushNewline();
            msg.AddMarkup(Loc.GetString("botanist-eye-chemical",
                ("name", reagent.LocalizedName),
                ("color", reagent.SubstanceColor.ToHexNoAlpha()),
                ("amount", estimated.ToString("G"))));
        }
    }
}

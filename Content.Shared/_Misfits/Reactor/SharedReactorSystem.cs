namespace Content.Shared._Misfits.Reactor;

public enum ReactorSeverity : byte
{
    Nominal,
    Warning,
    Critical,
}

public abstract class SharedReactorSystem : EntitySystem
{
    public const float WarningIntegrity = 66f;
    public const float CriticalIntegrity = 33f;

    public static ReactorSeverity GetSeverity(float integrity)
    {
        if (integrity <= CriticalIntegrity)
            return ReactorSeverity.Critical;

        return integrity <= WarningIntegrity ? ReactorSeverity.Warning : ReactorSeverity.Nominal;
    }

    public static string? GetStartupStepCommand(ReactorStartupStep step)
    {
        return step switch
        {
            ReactorStartupStep.EvacuateAsh => "DIVERTOR",
            ReactorStartupStep.ConfinementField => "COIL",
            ReactorStartupStep.CurrentRamp => "CURRENT",
            ReactorStartupStep.Fueling => "FUEL",
            ReactorStartupStep.HeatingIgnition => "HEAT",
            _ => null,
        };
    }

    public static string? GetStartupHint(ReactorStartupStep step, float currentTarget, float fuelTarget,
        IReadOnlyList<float> coilTargets, float ignitionTemperature)
    {
        switch (step)
        {
            case ReactorStartupStep.CurrentRamp:
                return "Higher current raises how much fuel you can safely run later (density limit = 0.35 + current × 0.9).";

            case ReactorStartupStep.Fueling:
            {
                var densityLimit = 0.35f + currentTarget * 0.9f;
                var safeFuel = Math.Clamp(densityLimit * 0.7f, 0f, 1f) * 100f;
                var maxFuel = Math.Clamp(densityLimit, 0f, 1f) * 100f;
                return $"At {(int) (currentTarget * 100)}% current, density hits 100% around {maxFuel:0}% fuel. Recommended FUEL ≤ {safeFuel:0}% for margin.";
            }

            case ReactorStartupStep.HeatingIgnition:
            {
                var minHeat = Math.Clamp(ignitionTemperature / 70f, 0f, 1f) * 100f;
                var densityLimit = 0.35f + currentTarget * 0.9f;
                var density = densityLimit > 0f ? fuelTarget / densityLimit : 0f;
                var avgCoil = Average(coilTargets);

                if (density > 0f && avgCoil > 0f)
                {
                    var maxHeat = Math.Clamp(avgCoil * 100f / density / 70f, 0f, 1f) * 100f;
                    return $"Ignition needs roughly {minHeat:0}%+ heat. At your fuel/current/coil settings, recommended ceiling is ~{maxHeat:0}% to avoid a beta fault.";
                }

                return $"Ignition needs roughly {minHeat:0}%+ heat to reach {ignitionTemperature:0} degrees.";
            }

            default:
                return null;
        }
    }

    private static float Average(IReadOnlyList<float> values)
    {
        if (values.Count == 0)
            return 0f;

        var total = 0f;
        foreach (var value in values)
            total += value;
        return total / values.Count;
    }
}

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

    public const float HeatingTemperatureScale = 70f;
    public const float HeatingResponse = 0.25f;
    public const float DivertorCoolingScale = 35f;
    public const float BaseCooling = 4f;
    public const float CoolingResponse = 0.2f;
    public const float ReactionCoefficient = 0.03f;
    public const float AshDensityFactor = 0.5f;
    public const float BaseDensityLimit = 0.35f;
    public const float CurrentDensityLimit = 0.9f;

    public const float StartupIgnitionMargin = 3f;

    public static ReactorSeverity GetSeverity(float integrity)
    {
        if (integrity <= CriticalIntegrity)
            return ReactorSeverity.Critical;

        return integrity <= WarningIntegrity ? ReactorSeverity.Warning : ReactorSeverity.Nominal;
    }

    public static float GetDensityLimit(float plasmaCurrent)
    {
        return BaseDensityLimit + plasmaCurrent * CurrentDensityLimit;
    }

    public static float GetCoolingRate(float divertorRate)
    {
        return (divertorRate * DivertorCoolingScale + BaseCooling) * CoolingResponse;
    }

    public static float GetEquilibriumHeat(float temperature, float divertorRate, float reactionPower = 0f)
    {
        return (temperature + (GetCoolingRate(divertorRate) - reactionPower) / HeatingResponse) / HeatingTemperatureScale;
    }

    public static float GetSettledTemperature(float heatingPower, float divertorRate)
    {
        return heatingPower * HeatingTemperatureScale - GetCoolingRate(divertorRate) / HeatingResponse;
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
        IReadOnlyList<float> coilTargets, float divertorTarget, float ignitionTemperature)
    {
        switch (step)
        {
            case ReactorStartupStep.ConfinementField:
                return "COIL <0-100> sets every coil segment at once during startup.";

            case ReactorStartupStep.CurrentRamp:
                return $"Higher current raises how much fuel you can safely run later (density limit = {BaseDensityLimit} + current × {CurrentDensityLimit}).";

            case ReactorStartupStep.Fueling:
            {
                var densityLimit = GetDensityLimit(currentTarget);
                var safeFuel = Math.Clamp(densityLimit * 0.7f, 0f, 1f) * 100f;
                var maxFuel = Math.Clamp(densityLimit, 0f, 1f) * 100f;
                return $"At {(int) (currentTarget * 100)}% current, density hits 100% around {maxFuel:0}% fuel. Recommended FUEL ≤ {safeFuel:0}% for margin.";
            }

            case ReactorStartupStep.HeatingIgnition:
            {
                var minHeatFraction = GetEquilibriumHeat(ignitionTemperature + StartupIgnitionMargin, divertorTarget);
                if (minHeatFraction > 1f)
                {
                    return $"Divertor at {(int) (divertorTarget * 100)}% cools the plasma faster than full heating can make up — " +
                           "it will never reach ignition. SHUTDOWN and restart with a lower DIVERTOR setting.";
                }

                var minHeat = MathF.Ceiling(Math.Clamp(minHeatFraction, 0f, 1f) * 100f);
                var density = fuelTarget / GetDensityLimit(currentTarget);
                var avgCoil = Average(coilTargets);

                if (density > 0f && avgCoil > 0f)
                {
                    var betaTemperature = avgCoil * 100f / density;
                    var reaction = Math.Max(0f, betaTemperature - ignitionTemperature) * density * ReactionCoefficient;
                    var maxHeat = MathF.Floor(Math.Clamp(GetEquilibriumHeat(betaTemperature, divertorTarget, reaction), 0f, 1f) * 100f);

                    if (maxHeat < minHeat)
                    {
                        return $"Ignition needs at least {minHeat:0}% heat, but your fuel/coil settings would hit a beta fault " +
                               $"above ~{maxHeat:0}%. Consider SHUTDOWN and restart with stronger coils or less fuel.";
                    }

                    return $"Ignition needs at least {minHeat:0}% heat with the divertor at {(int) (divertorTarget * 100)}%. " +
                           $"Keep it under ~{maxHeat:0}% to avoid a beta fault.";
                }

                return $"Ignition needs at least {minHeat:0}% heat with the divertor at {(int) (divertorTarget * 100)}%.";
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

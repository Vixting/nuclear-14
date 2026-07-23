using System;
using System.Collections.Generic;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared._Shitmed.Targeting;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical.Cryogenics;

[Serializable, NetSerializable]
public sealed class CryoPodBoundUserInterfaceState : BoundUserInterfaceState
{
    public NetEntity? Patient;
    public string? PatientName;
    public MobState? PatientState;
    public float? Temperature;
    public bool Bleeding;
    public FixedPoint2? TotalDamage;
    public Dictionary<string, FixedPoint2>? DamagePerGroup;
    public Dictionary<string, FixedPoint2>? DamagePerType;
    public Dictionary<TargetBodyPart, TargetIntegrity>? Body;

    public bool Running;

    public bool HasBeaker;
    public string? BeakerName;
    public List<ReagentQuantity> BeakerReagents;
    public FixedPoint2 BeakerVolume;
    public FixedPoint2 BeakerMaxVolume;

    public List<ReagentQuantity> BufferReagents;
    public FixedPoint2 BufferVolume;
    public FixedPoint2 BufferMaxVolume;

    public CryoPodBoundUserInterfaceState(
        NetEntity? patient,
        string? patientName,
        MobState? patientState,
        float? temperature,
        bool bleeding,
        FixedPoint2? totalDamage,
        Dictionary<string, FixedPoint2>? damagePerGroup,
        Dictionary<string, FixedPoint2>? damagePerType,
        Dictionary<TargetBodyPart, TargetIntegrity>? body,
        bool running,
        bool hasBeaker,
        string? beakerName,
        List<ReagentQuantity> beakerReagents,
        FixedPoint2 beakerVolume,
        FixedPoint2 beakerMaxVolume,
        List<ReagentQuantity> bufferReagents,
        FixedPoint2 bufferVolume,
        FixedPoint2 bufferMaxVolume)
    {
        Patient = patient;
        PatientName = patientName;
        PatientState = patientState;
        Temperature = temperature;
        Bleeding = bleeding;
        TotalDamage = totalDamage;
        DamagePerGroup = damagePerGroup;
        DamagePerType = damagePerType;
        Body = body;
        Running = running;
        HasBeaker = hasBeaker;
        BeakerName = beakerName;
        BeakerReagents = beakerReagents;
        BeakerVolume = beakerVolume;
        BeakerMaxVolume = beakerMaxVolume;
        BufferReagents = bufferReagents;
        BufferVolume = bufferVolume;
        BufferMaxVolume = bufferMaxVolume;
    }
}

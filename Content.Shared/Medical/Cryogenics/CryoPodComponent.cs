using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.Cryogenics;

[RegisterComponent, NetworkedComponent]
public sealed partial class CryoPodComponent : Component
{
    public const float HealingTemperatureThreshold = 213f;

    /// <summary>
    /// Specifies the name of the atmospherics port to draw gas from.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("port")]
    public string PortName { get; set; } = "port";

    /// <summary>
    /// Name of the item slot a beaker is inserted into to fill/drain the internal buffer tank.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("solutionContainerName")]
    public string SolutionContainerName { get; set; } = "beakerSlot";

    /// <summary>
    /// Name of the pod's single internal reagent buffer tank
    /// </summary>
    public const string BufferSolutionName = "buffer";

    /// <summary>
    /// Capacity of the internal buffer tank.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("bufferVolume")]
    public FixedPoint2 BufferVolume = FixedPoint2.New(100);

    /// <summary>
    /// How often (seconds) chemicals are dosed from the buffer into the patient, and how often
    /// passive cooling is applied.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("doseInterval")]
    public float DoseInterval = 1f;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("nextDoseTime", customTypeSerializer:typeof(TimeOffsetSerializer))]
    public TimeSpan? NextDoseTime;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("doseAmount")]
    public FixedPoint2 DoseAmount = FixedPoint2.New(1);

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("passiveCoolingTarget")]
    public float PassiveCoolingTarget = 160f;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("passiveCoolingFraction")]
    public float PassiveCoolingFraction = 0.2f;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("passiveCoolingMinimum")]
    public float PassiveCoolingMinimum = 4f;

    /// <summary>
    /// Whether the pod is actively dosing the patient from the buffer.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("running")]
    public bool Running;

    /// <summary>
    ///     Delay applied when inserting a mob in the pod.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("entryDelay")]
    public float EntryDelay = 2f;

    /// <summary>
    /// Delay applied when trying to pry open a locked pod.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("pryDelay")]
    public float PryDelay = 5f;

    /// <summary>
    /// Container for mobs inserted in the pod.
    /// </summary>
    [ViewVariables]
    public ContainerSlot BodyContainer = default!;

    /// <summary>
    /// If true, the eject verb will not work on the pod and the user must use a crowbar to pry the pod open.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("locked")]
    public bool Locked { get; set; }

    /// <summary>
    /// Causes the pod to be locked without being fixable by messing with wires.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("permaLocked")]
    public bool PermaLocked { get; set; }

    [Serializable, NetSerializable]
    public enum CryoPodVisuals : byte
    {
        ContainsEntity,
        IsOn
    }
}

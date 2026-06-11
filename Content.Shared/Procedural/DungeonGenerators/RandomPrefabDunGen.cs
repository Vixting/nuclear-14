using Content.Shared.Maps;
using Content.Shared.Tag;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

namespace Content.Shared.Procedural.DungeonGenerators;

public sealed partial class RandomPrefabDunGen : IDunGen
{
    [DataField(customTypeSerializer: typeof(PrototypeIdListSerializer<TagPrototype>))]
    public List<string> RoomWhitelist = new();

    [DataField]
    public int RoomCount = 10;

    [DataField]
    public int MaxPlacementAttempts = 50;

    [DataField]
    public int Padding = 1;

    [DataField]
    public int MinRoomDistance = 8;

    [DataField]
    public int MaxRoomDistance = 18;

    [DataField]
    public float BranchChance = 0.3f;

    [DataField]
    public float DirectionBias = 0.7f;

    [DataField]
    public float LoopChance = 0.15f;

    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<ContentTileDefinition>))]
    public string Tile = "FloorSteel";
}

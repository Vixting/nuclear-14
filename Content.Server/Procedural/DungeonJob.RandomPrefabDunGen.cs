using System.Numerics;
using System.Threading.Tasks;
using Content.Shared.Physics;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonGenerators;
using Robust.Shared.Map.Components;

namespace Content.Server.Procedural;

public sealed partial class DungeonJob
{
    private async Task<Dungeon> GenerateRandomPrefabDungeon(RandomPrefabDunGen gen, EntityUid gridUid, MapGridComponent grid, int seed)
    {
        var random = new Random(seed);
        var dungeon = new Dungeon();
        var markerQuery = _entManager.GetEntityQuery<DungeonConnectionMarkerComponent>();

        var normalPool = new List<DungeonRoomPrototype>();
        var startPool = new List<DungeonRoomPrototype>();
        var endPool = new List<DungeonRoomPrototype>();

        foreach (var proto in _prototype.EnumeratePrototypes<DungeonRoomPrototype>())
        {
            if (gen.RoomWhitelist.Count > 0)
            {
                var matched = false;
                foreach (var tag in gen.RoomWhitelist)
                {
                    if (!proto.Tags.Contains(tag))
                        continue;
                    matched = true;
                    break;
                }
                if (!matched)
                    continue;
            }

            switch (proto.Role)
            {
                case DungeonRoomRole.Start:
                    startPool.Add(proto);
                    break;
                case DungeonRoomRole.End:
                    endPool.Add(proto);
                    break;
                default:
                    normalPool.Add(proto);
                    break;
            }
        }

        normalPool.Sort((a, b) => string.Compare(a.ID, b.ID, StringComparison.Ordinal));
        startPool.Sort((a, b) => string.Compare(a.ID, b.ID, StringComparison.Ordinal));
        endPool.Sort((a, b) => string.Compare(a.ID, b.ID, StringComparison.Ordinal));

        if (startPool.Count == 0)
            startPool = normalPool;
        if (endPool.Count == 0)
            endPool = normalPool;

        if (normalPool.Count == 0)
        {
            _sawmill.Error("RandomPrefabDunGen: No rooms found matching the whitelist tags.");
            return dungeon;
        }

        var normalTotalWeight = 0f;
        foreach (var proto in normalPool)
            normalTotalWeight += proto.Weight;

        var dungeonTransform = Matrix3Helpers.CreateTransform(_position, Angle.Zero);
        var placedBounds = new List<Box2i>(gen.RoomCount);
        var placedCenters = new List<Vector2i>(gen.RoomCount);
        var markerBuffer = new HashSet<EntityUid>();

        var lastChainAngle = random.NextDouble() * Math.PI * 2;

        for (var i = 0; i < gen.RoomCount; i++)
        {
            var isStart = i == 0;
            var isEnd = i == gen.RoomCount - 1 && gen.RoomCount > 1;
            var pool = isStart ? startPool : (isEnd ? endPool : normalPool);

            var placed = false;

            for (var attempt = 0; attempt < gen.MaxPlacementAttempts; attempt++)
            {
                var room = isStart || isEnd
                    ? pool[random.Next(pool.Count)]
                    : PickWeightedRoom(normalPool, normalTotalWeight, random);

                var rotIndex = random.Next(4);
                var roomRotation = new Angle(rotIndex * Math.PI / 2);

                Vector2i rotatedSize;
                if (rotIndex == 1 || rotIndex == 3)
                    rotatedSize = new Vector2i(room.Size.Y, room.Size.X);
                else
                    rotatedSize = room.Size;

                Vector2i roomPos;
                if (placedCenters.Count == 0)
                {
                    roomPos = Vector2i.Zero;
                }
                else
                {
                    var isBranch = random.NextDouble() < gen.BranchChance;
                    var parentIndex = isBranch
                        ? random.Next(placedCenters.Count)
                        : placedCenters.Count - 1;
                    var parent = placedCenters[parentIndex];

                    double angle;
                    if (!isBranch && gen.DirectionBias > 0f)
                    {
                        var noise = (random.NextDouble() - 0.5) * Math.PI * 2 * (1.0 - gen.DirectionBias);
                        angle = lastChainAngle + noise;
                        lastChainAngle = angle;
                    }
                    else
                    {
                        angle = random.NextDouble() * Math.PI * 2;
                    }

                    var dist = random.Next(gen.MinRoomDistance, gen.MaxRoomDistance + 1);
                    roomPos = new Vector2i(
                        parent.X + (int) Math.Round(Math.Cos(angle) * dist),
                        parent.Y + (int) Math.Round(Math.Sin(angle) * dist));
                }

                var left = roomPos.X - rotatedSize.X / 2;
                var bottom = roomPos.Y - rotatedSize.Y / 2;
                var bounds = new Box2i(left, bottom, left + rotatedSize.X, bottom + rotatedSize.Y);

                var paddedBounds = new Box2i(
                    bounds.Left - gen.Padding,
                    bounds.Bottom - gen.Padding,
                    bounds.Right + gen.Padding,
                    bounds.Top + gen.Padding);

                var overlaps = false;
                foreach (var existing in placedBounds)
                {
                    if (existing.Intersects(in paddedBounds))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (overlaps)
                    continue;

                var roomTransform = Matrix3Helpers.CreateTransform((Vector2) roomPos, roomRotation);
                var dungeonMatty = Matrix3x2.Multiply(roomTransform, dungeonTransform);

                _dungeon.SpawnRoom(gridUid, grid, dungeonMatty, room);

                var roomCenter = (room.Offset + room.Size / 2f) * grid.TileSize;
                var tileOffset = -roomCenter + grid.TileSizeHalfVector;
                var roomTiles = new HashSet<Vector2i>(room.Size.X * room.Size.Y);
                var exterior = new HashSet<Vector2i>((room.Size.X + room.Size.Y) * 2 + 4);
                Box2i? mapBounds = null;
                var center = Vector2.Zero;

                for (var x = -1; x <= room.Size.X; x++)
                {
                    for (var y = -1; y <= room.Size.Y; y++)
                    {
                        if (x != -1 && y != -1 && x != room.Size.X && y != room.Size.Y)
                            continue;

                        var extPos = Vector2.Transform(
                            new Vector2(x + room.Offset.X, y + room.Offset.Y) + tileOffset,
                            dungeonMatty);
                        exterior.Add(extPos.Floored());
                    }
                }

                for (var x = 0; x < room.Size.X; x++)
                {
                    for (var y = 0; y < room.Size.Y; y++)
                    {
                        var roomTile = new Vector2i(x + room.Offset.X, y + room.Offset.Y);
                        var tilePos = Vector2.Transform((Vector2) roomTile + tileOffset, dungeonMatty);
                        var tileIndex = tilePos.Floored();
                        roomTiles.Add(tileIndex);
                        mapBounds = mapBounds?.Union(tileIndex) ?? new Box2i(tileIndex, tileIndex);
                        center += tilePos + grid.TileSizeHalfVector;
                    }
                }

                center /= roomTiles.Count;

                markerBuffer.Clear();
                var localBounds = new Box2(
                    mapBounds!.Value.Left,
                    mapBounds!.Value.Bottom,
                    mapBounds!.Value.Right + 1,
                    mapBounds!.Value.Top + 1);
                _lookup.GetLocalEntitiesIntersecting(gridUid, localBounds, markerBuffer, LookupFlags.Uncontained);

                var markerPositions = new List<Vector2i>();
                foreach (var ent in markerBuffer)
                {
                    if (!markerQuery.HasComponent(ent))
                        continue;

                    var xform = _entManager.GetComponent<TransformComponent>(ent);
                    markerPositions.Add(xform.LocalPosition.Floored());
                    _entManager.DeleteEntity(ent);
                }

                placedBounds.Add(bounds);
                placedCenters.Add(roomPos);

                var dungeonRoom = new DungeonRoom(roomTiles, center, mapBounds!.Value, exterior);

                foreach (var pos in markerPositions)
                {
                    dungeonRoom.Entrances.Add(pos);
                    dungeon.Entrances.Add(pos);
                }

                dungeon.Rooms.Add(dungeonRoom);
                placed = true;

                await SuspendIfOutOfTime();
                if (!ValidateResume())
                    return dungeon;

                break;
            }

            if (!placed)
                _sawmill.Warning($"RandomPrefabDunGen: Could not place room {i} after {gen.MaxPlacementAttempts} attempts.");
        }

        foreach (var room in dungeon.Rooms)
        {
            dungeon.RoomTiles.UnionWith(room.Tiles);
            dungeon.RoomExteriorTiles.UnionWith(room.Exterior);
        }

        foreach (var room in dungeon.Rooms)
        {
            if (room.Entrances.Count == 0)
                SetDungeonEntrance(dungeon, room, random);
        }

        if (gen.LoopChance > 0f)
            AddLoopEntrances(dungeon, placedCenters, random, gen);

        return dungeon;
    }

    private DungeonRoomPrototype PickWeightedRoom(List<DungeonRoomPrototype> pool, float totalWeight, Random random)
    {
        var pick = (float) (random.NextDouble() * totalWeight);
        var cumulative = 0f;
        foreach (var proto in pool)
        {
            cumulative += proto.Weight;
            if (pick <= cumulative)
                return proto;
        }
        return pool[pool.Count - 1];
    }

    private void AddLoopEntrances(Dungeon dungeon, List<Vector2i> centers, Random random, RandomPrefabDunGen gen)
    {
        var loopRange = gen.MaxRoomDistance * 2;
        var maxDistSq = loopRange * loopRange;

        for (var i = 0; i < dungeon.Rooms.Count; i++)
        {
            if (random.NextDouble() > gen.LoopChance)
                continue;

            for (var j = 0; j < dungeon.Rooms.Count; j++)
            {
                if (j == i || j == i - 1 || j == i + 1)
                    continue;

                var dx = centers[j].X - centers[i].X;
                var dy = centers[j].Y - centers[i].Y;
                if (dx * dx + dy * dy > maxDistSq)
                    continue;

                AddDirectionalEntrance(dungeon, dungeon.Rooms[i], centers[j]);
                AddDirectionalEntrance(dungeon, dungeon.Rooms[j], centers[i]);
                break;
            }
        }
    }

    private void AddDirectionalEntrance(Dungeon dungeon, DungeonRoom room, Vector2i targetCenter)
    {
        var diff = new Vector2(targetCenter.X - room.Center.X, targetCenter.Y - room.Center.Y);
        var dir = diff.ToWorldAngle().GetCardinalDir();

        Vector2i entrancePos;
        switch (dir)
        {
            case Direction.East:
                entrancePos = new Vector2i(room.Bounds.Right + 1, room.Bounds.Bottom + room.Bounds.Height / 2);
                break;
            case Direction.North:
                entrancePos = new Vector2i(room.Bounds.Left + room.Bounds.Width / 2, room.Bounds.Top + 1);
                break;
            case Direction.West:
                entrancePos = new Vector2i(room.Bounds.Left - 1, room.Bounds.Bottom + room.Bounds.Height / 2);
                break;
            case Direction.South:
                entrancePos = new Vector2i(room.Bounds.Left + room.Bounds.Width / 2, room.Bounds.Bottom - 1);
                break;
            default:
                return;
        }

        if (dungeon.Entrances.Contains(entrancePos))
            return;

        room.Entrances.Add(entrancePos);
        dungeon.Entrances.Add(entrancePos);
    }
}

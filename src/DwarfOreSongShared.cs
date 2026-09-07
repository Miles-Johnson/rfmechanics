using System;
using System.Collections.Generic;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics;

// Only coarse impressions cross the network. Never send ore coordinates or a block list.
[ProtoContract]
public sealed class OreSongRequest
{
    [ProtoMember(1)] public int Id;
    [ProtoMember(2)] public int X;
    [ProtoMember(3)] public int Y;
    [ProtoMember(4)] public int Z;
    [ProtoMember(5)] public bool Cancel;
}

[ProtoContract]
public sealed class OreSongReply
{
    [ProtoMember(1)] public int Id;
    // settling, answer, cancelled, busy, invalid, disabled
    [ProtoMember(2)] public string State = "";
    [ProtoMember(3)] public bool Incomplete;
    [ProtoMember(4)] public int ListenMs;
    [ProtoMember(5)] public int RestMs;
    [ProtoMember(6)] public OreSongVoice[] Voices = Array.Empty<OreSongVoice>();
    [ProtoMember(7)] public int MaxVoices = 3;
}

[ProtoContract]
public sealed class OreSongVoice
{
    [ProtoMember(1)] public string Material = "unknown";
    [ProtoMember(2)] public string Asset = "unknown";
    [ProtoMember(3)] public int Bearing; // 12 world-aligned sectors, never exact azimuth
    [ProtoMember(4)] public int Elevation; // -1, 0, 1
    [ProtoMember(5)] public int DistanceBand; // 0: enveloping, 1: near, 2: far, 3: distant
    [ProtoMember(6)] public float Fullness;
    [ProtoMember(7)] public float Grade;
}

internal readonly record struct OreSongMaterial(int Id, string Name, string Asset, float Grade);

internal static class OreSongRules
{
    internal const string Channel = "rfmechanics:oresong-v2";
    internal const double Reach = 2;
    internal const double MoveToleranceSq = 0.04; // 20cm; tolerate resting-position corrections
    internal const int TickMs = 50;
    internal const int MaxSearchMs = 12000;
    internal const int MaxReplyVoices = 12;

    internal static bool IsSeated(EntityPlayer entity, bool server)
    {
        var controls = server ? entity.ServerControls : entity.Controls;
        return entity.Alive && controls.FloorSitting && !controls.TriesToMove
            && !controls.Jump && !controls.LeftMouseDown && !controls.RightMouseDown
            && !entity.Swimming && entity.MountedOn == null
            && entity.RightHandItemSlot.Empty;
    }

    internal static bool InReach(EntityPlayer entity, BlockPos wall, bool server)
    {
        var pos = entity.Pos; // Server entities already expose authoritative Pos in 1.22.
        if (pos.Dimension != wall.dimension) return false;
        // Distance to the closest point on the selected block, from the eyes.
        double x = pos.X, y = pos.Y + entity.LocalEyePos.Y, z = pos.Z;
        double dx = x - GameMath.Clamp(x, wall.X, wall.X + 1.0);
        double dy = y - GameMath.Clamp(y, wall.Y, wall.Y + 1.0);
        double dz = z - GameMath.Clamp(z, wall.Z, wall.Z + 1.0);
        return dx * dx + dy * dy + dz * dz <= Reach * Reach;
    }

    internal static bool IsStone(Block block) => block != null && block.Id != 0
        && (block.BlockMaterial == EnumBlockMaterial.Stone || block.BlockMaterial == EnumBlockMaterial.Ore);

    internal static string ResolveMaterial(string type)
    {
        int split = type.IndexOf('_');
        if (split < 0) return type;
        string inclusion = type.Substring(split + 1);
        if (inclusion == "peridot") return "olivine";
        // Joint ores describe the inclusion, including unfamiliar modded minerals.
        return inclusion;
    }

    internal static string AssetFor(string material) => material switch
    {
        "nativecopper" or "malachite" => "nativecopper",
        "hematite" or "limonite" or "magnetite" => "iron",
        "anthracite" or "bituminouscoal" or "lignite" => "coal",
        "nativegold" or "nativesilver" or "galena" or "cassiterite" or "sphalerite"
            or "bismuthinite" or "chromite" or "ilmenite" or "quartz" or "diamond"
            or "emerald" or "olivine" => material,
        _ => "unknown"
    };

    internal static float GradeFor(Block block) => block.Variant["grade"] switch
    {
        "poor" => 0f, "medium" => 0.4f, "rich" => 0.75f, "bountiful" => 1f,
        _ => block.Variant["potential"] switch { "low" => 0f, "medium" => 0.5f, "high" => 1f, _ => 0.5f }
    };

    internal static Dictionary<int, OreSongMaterial> BuildLookup(IWorldAccessor world)
    {
        var result = new Dictionary<int, OreSongMaterial>();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Block block in world.Blocks)
        {
            if (block?.BlockMaterial != EnumBlockMaterial.Ore) continue;
            string type = block.Variant["type"];
            if (string.IsNullOrEmpty(type)) continue;
            string name = ResolveMaterial(type);
            if (!ids.TryGetValue(name, out int id)) ids[name] = id = ids.Count;
            result[block.Id] = new OreSongMaterial(id, name, AssetFor(name), GradeFor(block));
        }
        return result;
    }
}

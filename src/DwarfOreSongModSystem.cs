using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace rfmechanics
{
    public class OreSongEntry
    {
        public string AssetPath;
        public float GradeGain;
    }

    /// <summary>
    /// Client-only lookup table for the Dwarf ore-song mechanic: maps every registered
    /// Ore-material block id to (asset path, grade-derived gain). Built once at
    /// StartClientSide by iterating capi.World.Blocks -- Variant["type"]/["grade"]/["potential"]
    /// reads are already O(1) per-block, so this just memoizes an already-cheap read (see
    /// notes/diagnostics/ore-song-discovery.md Q1).
    ///
    /// Client-only (ShouldLoad), mirroring GoblinDarkvisionModSystem's own pattern (Q3) --
    /// exactly one instance ever exists, so unlike RFMechanicsModSystem.Api this has no
    /// last-writer-wins race. RfDwarfOreSongBehavior resolves this instance via
    /// world.Api.ModLoader.GetModSystem, never a static field.
    /// </summary>
    public class DwarfOreSongModSystem : ModSystem
    {
        public Dictionary<int, OreSongEntry> Lookup { get; } = new Dictionary<int, OreSongEntry>();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            BuildLookup(api);
        }

        private void BuildLookup(ICoreClientAPI api)
        {
            var loggedUnmapped = new HashSet<string>();

            foreach (Block block in api.World.Blocks)
            {
                if (block == null || block.BlockMaterial != EnumBlockMaterial.Ore)
                    continue;

                // Read Variant["type"] directly off Block, never cast to BlockOre -- gems are
                // plain Block and a cast throws/returns null, silently dropping every
                // diamond/emerald/olivine (discovery report Q1).
                string type = block.Variant["type"];
                if (string.IsNullOrEmpty(type))
                    continue;

                string material = ResolveJointMaterial(type);
                string assetPath = MaterialToAsset(material);
                if (assetPath == null)
                {
                    assetPath = "oresong-nativecopper";
                    if (loggedUnmapped.Add(material))
                    {
                        api.Logger.Debug("[rfmechanics] DwarfOreSong: unmapped ore type '{0}' (from '{1}') -- routed to neutral default (oresong-nativecopper).", material, type);
                    }
                }

                Lookup[block.Id] = new OreSongEntry
                {
                    AssetPath = assetPath,
                    GradeGain = ComputeGradeGain(block)
                };
            }

            api.Logger.Notification("[rfmechanics] DwarfOreSong lookup built: {0} ore/gem blocks mapped.", Lookup.Count);
        }

        /// <summary>
        /// Joint type values (e.g. "galena_nativesilver") need a single substance picked out of
        /// two. Verified against the live install's actual ore-graded.json/ore-gem.json rather
        /// than assumed: the only real joint values that exist are galena_nativesilver,
        /// quartz_nativegold, quartz_nativesilver (host-mineral + precious-metal pairs -- the
        /// game's own ItemOre.cs:29-31/168 always resolves these to the metal half via
        /// Split('_')[1] / prefix-strip, e.g. for smashing ore into nuggets) and
        /// olivine_peridot (NOT a two-substance pairing -- "peridot" is just the gem-quality
        /// name for olivine, not a separate material, and has no entry of its own in the
        /// material map). Preferring the second segment when it resolves, falling back to the
        /// first otherwise, satisfies all four real cases without hardcoding them individually.
        /// </summary>
        private static string ResolveJointMaterial(string type)
        {
            int underscoreIdx = type.IndexOf('_');
            if (underscoreIdx < 0)
                return type;

            string first = type.Substring(0, underscoreIdx);
            string second = type.Substring(underscoreIdx + 1);

            return MaterialToAsset(second) != null ? second : first;
        }

        private static string MaterialToAsset(string material)
        {
            switch (material)
            {
                case "galena": return "oresong-galena";
                case "lignite":
                case "bituminouscoal":
                case "anthracite": return "oresong-coal";
                case "nativegold": return "oresong-nativegold";
                case "nativesilver": return "oresong-nativesilver";
                case "nativecopper":
                case "malachite": return "oresong-nativecopper";
                case "sphalerite":
                case "bismuthinite": return "oresong-sphalerite";
                case "cassiterite": return "oresong-cassiterite";
                case "chromite":
                case "ilmenite": return "oresong-chromite";
                case "limonite":
                case "hematite":
                case "magnetite": return "oresong-iron";
                case "quartz":
                case "diamond":
                case "emerald":
                case "olivine": return "oresong-quartzgem";
                default: return null;
            }
        }

        private static float ComputeGradeGain(Block block)
        {
            switch (block.Variant["grade"])
            {
                case "poor": return 0.55f;
                case "medium": return 0.75f;
                case "rich": return 0.9f;
                case "bountiful": return 1.0f;
            }

            switch (block.Variant["potential"])
            {
                case "low": return 0.6f;
                case "medium": return 0.8f;
                case "high": return 1.0f;
            }

            return 0.75f;
        }
    }
}

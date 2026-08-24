using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace rfmechanics
{
    internal enum ScentCategory { Unknown, Herbivore, Predator, Omnivore }

    internal static class OrcSmellClassifier
    {
        internal static bool IsSmellableFauna(Entity entity)
        {
            // EntityBehaviorHarvestable alone also matches drifters/shivers (harvestable for
            // loot, no creatureDiet) -- both halves required to exclude them.
            if (entity.GetBehavior<EntityBehaviorHarvestable>() == null) return false;
            return entity.Properties.Attributes?["creatureDiet"]?.Exists ?? false;
        }

        // Keyed by entity type code (includes variant, e.g. bear-adult-panda vs.
        // bear-adult-brown are separate AssetLocations under one bear-adult.json) --
        // Properties/Attributes/Tags are shared across every entity of the same type, so this
        // only needs resolving once per type, and variants sharing one file's tags: line still
        // get independent entries.
        private static readonly Dictionary<AssetLocation, ScentCategory> categoryCache = new();

        // Force-list/tag checks happen before diet so a dangerous but untagged/omnivore mob
        // (config SmellForcePredatorCodes) or a tagged-but-herbivorous one (e.g. vanilla panda,
        // tagged via bear-adult.json) resolve to Predator -- the orc nose reads behavioral
        // threat, not stomach contents, so tag/override wins over diet on conflict by design.
        internal static ScentCategory Classify(Entity entity, RFMechanicsConfig cfg)
        {
            AssetLocation code = entity.Code;
            if (categoryCache.TryGetValue(code, out ScentCategory cached)) return cached;

            ScentCategory category;
            if (Array.IndexOf(cfg.SmellForcePredatorCodes, code.ToString()) >= 0)
            {
                category = ScentCategory.Predator;
            }
            else if (cfg.SmellUseEntityTags && HasAnyTag(entity, cfg.SmellPredatorTags))
            {
                category = ScentCategory.Predator;
            }
            else
            {
                category = ClassifyDiet(entity);
            }

            categoryCache[code] = category;
            return category;
        }

        // Entity.HasTags is obsolete as of 1.22 ("avoid using in favor of matching directly
        // against Tags") and was an ALL-match (Tags.ContainsAll) besides -- Overlaps is the
        // native ANY-match this needs, so it's used directly instead of looping HasTags calls.
        private static bool HasAnyTag(Entity entity, string[] tags)
        {
            entity.Api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagSetFast tagSet, tags);
            return entity.Tags.Overlaps(in tagSet);
        }

        // Vintage Story has no predator/herbivore flag; CreatureDiet.FoodCategories (what the
        // animal itself eats) is the only client-readable proxy for it.
        internal static ScentCategory ClassifyDiet(Entity entity)
        {
            ScentCategory category = ScentCategory.Unknown;
            CreatureDiet? diet = entity.Properties.Attributes?["creatureDiet"]?.AsObject<CreatureDiet>();
            if (diet?.FoodCategories != null)
            {
                bool eatsProtein = false;
                bool eatsPlant = false;
                foreach (EnumFoodCategory foodCat in diet.FoodCategories)
                {
                    if (foodCat == EnumFoodCategory.Protein) eatsProtein = true;
                    else if (foodCat == EnumFoodCategory.Fruit || foodCat == EnumFoodCategory.Vegetable || foodCat == EnumFoodCategory.Grain) eatsPlant = true;
                }
                category = (eatsProtein, eatsPlant) switch
                {
                    (true, true) => ScentCategory.Omnivore,
                    (true, false) => ScentCategory.Predator,
                    (false, true) => ScentCategory.Herbivore,
                    _ => ScentCategory.Unknown,
                };
            }

            return category;
        }
    }
}

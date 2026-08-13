using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Postfix on CollectibleObject.GetNutritionProperties -- grants game:rot a minimal
    /// FoodNutritionProperties for goblins only (rf-goblin-positive trait), so vanilla's
    /// tryEatBegin/tryEatStep/tryEatStop (which gate solely on this returning non-null) let
    /// them eat it. Never overrides an existing non-null result, so ordering against
    /// dietsetup's own postfix on this same method is irrelevant.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetNutritionProperties))]
    public static class GoblinRotEdiblePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemStack itemstack, Entity forEntity, ref FoodNutritionProperties? __result)
        {
            if (__result != null) return;

            var code = itemstack?.Collectible?.Code;
            if (code == null || code.Domain != "game" || code.Path != "rot") return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableGoblinRotEdible) return;

            if (forEntity is not EntityPlayer player) return;

            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) return;

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) return;

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) return;

            if (!charSys.HasTrait(iplayer, cfg.GoblinTraitCode)) return;

            __result = new FoodNutritionProperties
            {
                FoodCategory = EnumFoodCategory.NoNutrition,
                Satiety = cfg.GoblinRotEdibleSatiety,
                Health = 0f
            };
        }
    }
}

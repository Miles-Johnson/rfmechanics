using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Block behavior letting a goblin spend a spit charge to apply repair via empty-hand
    /// interact, on the same reparable blocktypes vanilla's Reparable behavior covers. Declared
    /// after "Reparable" in each blocktype's behaviors array -- Reparable returns PassThrough on
    /// empty-hand, so this is free to claim it, and in practice the two never compete since this
    /// only acts on an empty hotbar slot while Reparable only acts when holding a repairGain item.
    /// Mirrors BlockBehaviorReparable.OnBlockInteractStart's repair-application shape (same
    /// claims check, repairState increment formula, sound gate, no explicit MarkDirty) rather
    /// than reimplementing it. Deliberate deviation: vanilla consumes glue from the held
    /// ItemSlot; there is no ItemSlot here, so a spit charge on WatchedAttributes is decremented instead.
    /// </summary>
    public class RfGoblinSpitRepairBehavior : BlockBehavior
    {
        private const string SpitChargesKey = "rfmechanics:spitCharges";

        public RfGoblinSpitRepairBehavior(Block block) : base(block)
        {
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableGoblinSpitCharges)
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            // Only act on empty hand -- never interfere with real glue or any other held item.
            if (!byPlayer.InventoryManager.ActiveHotbarSlot.Empty)
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            EntityPlayer player = byPlayer.Entity;

            // Load-bearing: HasTrait returns true for a null class.
            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass))
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null || !charSys.HasTrait(byPlayer, cfg.GoblinTraitCode))
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            int charges = player.WatchedAttributes.GetInt(SpitChargesKey, 0);
            if (charges <= 0)
            {
                if (byPlayer is IServerPlayer noChargesPlr)
                {
                    noChargesPlr.SendMessage(GlobalConstants.GeneralChatGroup, "No spit left -- eat rot to refill.", EnumChatType.Notification);
                }
                handling = EnumHandling.PassThrough;
                return false;
            }

            var bec = block.GetBEBehavior<BEBehaviorShapeFromAttributes>(blockSel.Position);
            if (bec == null)
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            string message;
            if (bec.repairState < 1f && bec.reparability > 1)
            {
                double repairQuantity = cfg.SpitRepairGain;
                if (repairQuantity < 0.001)
                {
                    // Mirrors vanilla's "gluehardened" rejection; reachable only if SpitRepairGain is tuned near zero.
                    message = "Your spit has hardened -- no repair applied.";
                }
                else
                {
                    bec.repairState += (float)(repairQuantity * 5 / (bec.reparability - 1));

                    int remaining = charges - 1;
                    player.WatchedAttributes.SetInt(SpitChargesKey, remaining);

                    message = $"Spit thins -- {remaining} left.";

                    if (world.Side == EnumAppSide.Client)
                    {
                        var sound = AssetLocation.Create("sounds/player/gluerepair");
                        world.PlaySoundAt(sound, blockSel.Position, 0, byPlayer, true, 8);
                    }
                }
            }
            else
            {
                message = "Nothing more to repair here.";
            }

            if (byPlayer is IServerPlayer splr)
            {
                splr.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }

            handling = EnumHandling.Handled;
            return true;
        }
    }
}

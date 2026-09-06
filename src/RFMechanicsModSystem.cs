using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace rfmechanics
{
    public class RFMechanicsModSystem : ModSystem
    {
        private const string HarmonyId = "rfmechanics";
        private Harmony? harmony;
        private static RFMechanicsConfig? config;
        private static ICoreAPI? staticApi;

        public static RFMechanicsConfig? Config => config;
        public static ICoreAPI? Api => staticApi;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            staticApi = api;

            LoadConfig(api);

            api.Logger.Notification("[{0}] Build {1} ({2}{3})", Mod.Info.ModID, Mod.Info.Version,
                GitInfo.Sha, GitInfo.Dirty ? "-dirty" : "");

            if (!api.ModLoader.IsModEnabled("raceframework"))
            {
                api.Logger.Notification("[{0}] raceframework not detected — race-trait mechanics ({1}, {2}, {3}, {4}) will never trigger; running in Mods-solo mode.",
                    Mod.Info.ModID, config.DwarfTraitCode, config.ElfTraitCode, config.OrcTraitCode, config.GoblinTraitCode);
            }

            api.Logger.Notification("[rfmechanics] Config loaded. DwarfTraitCode={0}, EnableMiningCurve={1}, EnableOreCurve={2}, OreThreshold={3}, OreCeiling={4}, ClimbSpeedFactor={5}, ClimbSaturationPerSecond={6}, EnableClimbSpeed={7}, EnableClimbSaturation={8}, ElfTraitCode={9}, EnableBranchyLeavesPassthrough={10}, EnableTreeProximitySpeed={11}, TreeProximityRadius={12}, TreeProximityMaxBonus={13}, EnableTreeClimbing={14}, EnableFallDamageReduction={15}, FallDamageReductionFactor={16}, GoblinTraitCode={17}, EnableGoblinDarkvision={18}, GoblinDarkvisionStrength={19}, EnableGoblinFallDamageReduction={20}, GoblinFallDamageReductionFactor={21}",
                config.DwarfTraitCode, config.EnableMiningCurve, config.EnableOreCurve, config.OreThreshold, config.OreCeiling, config.ClimbSpeedFactor, config.ClimbSaturationPerSecond, config.EnableClimbSpeed, config.EnableClimbSaturation, config.ElfTraitCode, config.EnableBranchyLeavesPassthrough, config.EnableTreeProximitySpeed, config.TreeProximityRadius, config.TreeProximityMaxBonus, config.EnableTreeClimbing, config.EnableFallDamageReduction, config.FallDamageReductionFactor, config.GoblinTraitCode, config.EnableGoblinDarkvision, config.GoblinDarkvisionStrength, config.EnableGoblinFallDamageReduction, config.GoblinFallDamageReductionFactor);

            api.RegisterEntityBehaviorClass("rftreeproximity", typeof(RFTreeProximityBehavior));
            api.RegisterEntityBehaviorClass("rfelfidentity", typeof(PlayerRaceBehavior));
            api.RegisterEntityBehaviorClass("rfelfstepheight", typeof(ElfStepHeightBehavior));
            api.RegisterEntityBehaviorClass("rfelfzoom", typeof(RFElfZoomBehavior));
            api.RegisterEntityBehaviorClass("rfthew", typeof(ThewBehavior));
            api.RegisterEntityBehaviorClass("rfband", typeof(BandBehavior));
            api.RegisterEntityBehaviorClass("rfburn", typeof(BurnBehavior));
            api.RegisterEntityBehaviorClass("rffrenzy", typeof(FrenzyBehavior));
            api.RegisterEntityBehaviorClass("rfgoblintunnel", typeof(RFGoblinTunnelBehavior));
            api.RegisterEntityBehaviorClass("rfgoblinrotaura", typeof(GoblinRotAuraBehavior));
            api.RegisterCropBehavior("RfGoblinCropStunt", typeof(GoblinCropStuntBehavior));
            // GoblinDigModifierBehavior re-homed to src/BugRace/ (future bug race), disabled -- see its class header.
            // api.RegisterBlockBehaviorClass("GoblinDigModifier", typeof(rfmechanics.BugRace.GoblinDigModifierBehavior));

            // Start(ICoreAPI) runs once per side (client + server), each on its own
            // RFMechanicsModSystem instance -- in singleplayer both sides share one process, and
            // Harmony patches the shared CLR MethodBase, so an unguarded PatchAll() from each
            // side double-patches every prefix/postfix in this assembly. Confirmed in-game:
            // ChunkScarBreakPatch double-counted a single block break before this guard existed.
            // HasAnyPatches is a process-wide check, not per-Harmony-instance, so it's the right guard here.
            harmony = new Harmony(HarmonyId);
            try
            {
                if (!Harmony.HasAnyPatches(HarmonyId))
                {
                    harmony.PatchAll(Assembly.GetExecutingAssembly());
                    api.Logger.Notification("[rfmechanics] Harmony patches applied successfully.");
                }
                else
                {
                    api.Logger.Notification("[rfmechanics] Harmony patches already applied by another side's ModSystem instance, skipping.");
                }
            }
            catch (Exception ex)
            {
                api.Logger.Error("[rfmechanics] Harmony patch failed: {0}", ex);
            }
        }

        private static long lastStepHeightToggleSentMs;
        private static long lastGoblinSpitSentMs;

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            RegisterElfStepHeightHotkey(api);
            RegisterFliesLagCommand(api);
        }

        /// <summary>Called from RaceAbilityHotkeyModSystem's dispatch table once it has already
        /// confirmed the presser is cached as Goblin -- no race check here, that decision belongs
        /// to the dispatcher alone. Repairing spends a charge, so (unlike the idempotent
        /// step-height toggle) an undebounced key-repeat burst would visibly overspend charges.</summary>
        internal bool TryTriggerGoblinSpit(ICoreClientAPI api)
        {
            if (Config == null) return false;

            long now = api.World.ElapsedMilliseconds;
            if (now - lastGoblinSpitSentMs < 200) return true;
            lastGoblinSpitSentMs = now;

            api.SendChatMessage("/rfgoblinspit repair");
            return true;
        }

        /// <summary>Client-side fly tuning; distinct command name keeps server diagnostics reachable.</summary>
        private void RegisterFliesLagCommand(ICoreClientAPI api)
        {
            CommandArgumentParsers parsers = api.ChatCommands.Parsers;

            api.ChatCommands.Create("rfflieslag")
                .WithDescription("Live-tune world-space flies. Client-side only, not persisted.")
                .BeginSubCommand("sizeaura")
                    .WithDescription("Aura fly size in blocks (0.01-0.25, default 0.035).")
                    .WithArgs(parsers.OptionalFloat("blocks"))
                    .HandleWith(args => TuneFloat(args, "GoblinRotFliesSize", () => Config?.GoblinRotFliesSize, v => Config!.GoblinRotFliesSize = v))
                .EndSubCommand()
                .BeginSubCommand("speed")
                    .WithDescription("Independent fly drift in blocks/second (0-2, default 0.12).")
                    .WithArgs(parsers.OptionalFloat("speed"))
                    .HandleWith(args => TuneFloat(args, "GoblinRotFliesSpeed", () => Config?.GoblinRotFliesSpeed, v => Config!.GoblinRotFliesSpeed = v))
                .EndSubCommand()
                .BeginSubCommand("speedspit")
                    .WithDescription("Charge fly cruise speed (0.1-5 blocks/second, default 2.2), independent of aura flies.")
                    .WithArgs(parsers.OptionalFloat("speed"))
                    .HandleWith(args => TuneFloat(args, "GoblinSpitFliesSpeed", () => Config?.GoblinSpitFliesSpeed, v => Config!.GoblinSpitFliesSpeed = v))
                .EndSubCommand()
                .BeginSubCommand("trail")
                    .WithDescription("Retired following control; flies now stay in world space.")
                    .WithArgs(parsers.OptionalFloat("blocks"))
                    .HandleWith(args => TextCommandResult.Success("Aura flies stay in world space. Use .rfflieslag life to tune their lifetime. Charge flies use independent steering."))
                .EndSubCommand()
                .BeginSubCommand("aura")
                    .WithDescription("Retired following control; flies now stay in world space.")
                    .WithArgs(parsers.OptionalFloat("seconds"))
                    .HandleWith(args => TextCommandResult.Success("Aura flies stay in world space. Use .rfflieslag life to tune their lifetime. Charge flies use independent steering."))
                .EndSubCommand()
                .BeginSubCommand("life")
                    .WithDescription("Mean lifetime in seconds (0.5-5, default 2), with staggered fade in/out.")
                    .WithArgs(parsers.OptionalFloat("seconds"))
                    .HandleWith(args => TuneFloat(args, "GoblinRotFliesLifeSeconds", () => Config?.GoblinRotFliesLifeSeconds, v => Config!.GoblinRotFliesLifeSeconds = v))
                .EndSubCommand()
                .BeginSubCommand("opacitymin")
                    .WithDescription("Fly opacity at the smallest active aura (0-1, default 0.2).")
                    .WithArgs(parsers.OptionalFloat("opacity"))
                    .HandleWith(args => TuneFloat(args, "GoblinRotFliesOpacityMin", () => Config?.GoblinRotFliesOpacityMin, v => Config!.GoblinRotFliesOpacityMin = v))
                .EndSubCommand()
                .BeginSubCommand("opacitymax")
                    .WithDescription("Fly opacity at the largest aura (0-1, default 0.65).")
                    .WithArgs(parsers.OptionalFloat("opacity"))
                    .HandleWith(args => TuneFloat(args, "GoblinRotFliesOpacityMax", () => Config?.GoblinRotFliesOpacityMax, v => Config!.GoblinRotFliesOpacityMax = v))
                .EndSubCommand()
                .BeginSubCommand("size")
                    .WithDescription("Original winged charge-sprite width in blocks (0.01-0.25, default 0.06).")
                    .WithArgs(parsers.OptionalFloat("blocks"))
                    .HandleWith(args => TuneFloat(args, "GoblinSpitFliesSize", () => Config?.GoblinSpitFliesSize, v => Config!.GoblinSpitFliesSize = v))
                .EndSubCommand()
                .BeginSubCommand("radius")
                    .WithDescription("Get/set GoblinSpitFliesRadius (spit fly cloud horizontal radius, blocks).")
                    .WithArgs(parsers.OptionalFloat("blocks"))
                    .HandleWith(args => TuneFloat(args, "GoblinSpitFliesRadius", () => Config?.GoblinSpitFliesRadius, v => Config!.GoblinSpitFliesRadius = v))
                .EndSubCommand()
                .BeginSubCommand("vext")
                    .WithDescription("Get/set GoblinSpitFliesVerticalExtent (spit fly cloud vertical half-extent, blocks).")
                    .WithArgs(parsers.OptionalFloat("blocks"))
                    .HandleWith(args => TuneFloat(args, "GoblinSpitFliesVerticalExtent", () => Config?.GoblinSpitFliesVerticalExtent, v => Config!.GoblinSpitFliesVerticalExtent = v))
                .EndSubCommand();
        }

        /// <summary>Shared get/set body for /rfflieslag's tuning subcommands -- one implementation
        /// instead of four near-identical HandleWith blocks.</summary>
        private static TextCommandResult TuneFloat(TextCommandCallingArgs args, string fieldName, Func<double?> get, Action<double> set)
        {
            double? current = get();
            if (current == null)
                return TextCommandResult.Success("Config not loaded.");

            if (args.Parsers[0].IsMissing)
                return TextCommandResult.Success(string.Format("{0}={1:F3}", fieldName, current.Value));

            double value = (float)args[0];
            if (!double.IsFinite(value) || value < 0) return TextCommandResult.Error("Use a finite non-negative number.");
            (double min, double max) = fieldName switch
            {
                "GoblinRotFliesSize" or "GoblinSpitFliesSize" => (0.01, 0.25),
                "GoblinRotFliesSpeed" => (0, 2),
                "GoblinSpitFliesSpeed" => (0.1, 5),
                "GoblinRotFliesLifeSeconds" => (0.5, 5),
                "GoblinRotFliesOpacityMin" or "GoblinRotFliesOpacityMax" => (0, 1),
                "GoblinRotFliesMaxTrailBlocks" => (0, 3),
                "GoblinSpitFliesRadius" => (0.4, 2),
                "GoblinSpitFliesVerticalExtent" => (0.1, 1),
                _ => (0, 5)
            };
            if (value < min - 0.000001 || value > max + 0.000001) return TextCommandResult.Error($"Use a value between {min} and {max}.");
            value = Math.Clamp(value, min, max);
            set(value);
            return TextCommandResult.Success(string.Format("{0} set to {1:F3} (this session only, not saved to rfmechanics.json)", fieldName, value));
        }

        /// <summary>200ms client-side debounce so a held key doesn't spam the server with
        /// repeated toggle commands -- RegisterHotKey's handler fires on key-repeat, not just the
        /// initial press.</summary>
        private void RegisterElfStepHeightHotkey(ICoreClientAPI api)
        {
            api.Input.RegisterHotKey("rfelfstepheighttoggle", "Toggle Elf Step Height Boost", GlKeys.H, HotkeyType.CharacterControls, ctrlPressed: true);
            api.Input.SetHotKeyHandler("rfelfstepheighttoggle", _ =>
            {
                long now = api.World.ElapsedMilliseconds;
                if (now - lastStepHeightToggleSentMs < 200) return true;
                lastStepHeightToggleSentMs = now;

                api.SendChatMessage("/rfelfstepheight toggle");
                return true;
            });
        }

        public override void Dispose()
        {
            if (harmony != null)
            {
                harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }

            // Static zoom state has no per-entity despawn hook that fires on client disconnect
            // (EnumDespawnReason.Disconnect means "last player left the server", not this).
            RFElfZoomBehavior.ResetStaticState();
            GoblinRotAuraRegistry.ClearAll();

            base.Dispose();
        }

        /// <summary>Missing file or successful parse are stored back (this drops stale/removed
        /// keys and adds new ones). Malformed JSON falls back to defaults in memory only,
        /// without touching the file, so the user's broken JSON is left in place to fix.</summary>
        private static void LoadConfig(ICoreAPI api)
        {
            RFMechanicsConfig? loaded;
            bool malformed = false;
            try
            {
                loaded = api.LoadModConfig<RFMechanicsConfig>("rfmechanics.json");
            }
            catch (Exception ex)
            {
                api.Logger.Error("[rfmechanics] Failed to parse rfmechanics.json, using defaults without overwriting the file: {0}", ex);
                loaded = null;
                malformed = true;
            }

            config = loaded ?? new RFMechanicsConfig();
            if (config.MigrateGoblinAura())
                api.Logger.Notification("[rfmechanics] Migrated goblin visuals revision 3: independent persistent winged spit-charge flies; aura recovery retained.");
            if (config.MigrateSmellVisuals())
                api.Logger.Notification("[rfmechanics] Migrated smell visuals to revision 2: walking/full focus, acquisition 4.5-13.5s, stronger body-size contrast, yellow-green fallback.");

            if (!malformed)
            {
                api.StoreModConfig(config, "rfmechanics.json");
            }
        }

        // ── Curve helpers (shared by patch and command) ──

        /// <summary>
        /// Compute the depth fraction for a given Y coordinate, clamped to [0, 1].
        /// Used by both the mining speed curve and the ore yield curve.
        /// Formula: clamp((SeaLevel - y) / SeaLevel, 0, 1).
        /// </summary>
        public static double ComputeDepthFrac(int y, int seaLevel)
        {
            if (seaLevel <= 0) return 0.0;
            return GameMath.Clamp((double)(seaLevel - y) / seaLevel, 0.0, 1.0);
        }

        /// <summary>
        /// Compute the depth/altitude bonus for a given Y coordinate.
        /// Formula: depthFrac = (SeaLevel - y) / SeaLevel, altFrac = (y - SeaLevel) / SeaLevel,
        /// bonus = depthFrac * MiningDepthWeight + altFrac * MiningAltitudeWeight, capped at MiningBonusCap.
        /// </summary>
        public static double ComputeBonus(int y, int seaLevel)
        {
            if (seaLevel <= 0) return 0.0;
            double depthFrac = ComputeDepthFrac(y, seaLevel);
            double altFrac = GameMath.Clamp((double)(y - seaLevel) / seaLevel, 0.0, 1.0);
            double bonus = depthFrac * config.MiningDepthWeight + altFrac * config.MiningAltitudeWeight;
            return Math.Min(bonus, config.MiningBonusCap);
        }

        /// <summary>
        /// Compute the ore yield bonus for a given Y coordinate.
        /// Depth-only; no altitude component.
        /// Formula: if depthFrac <= OreThreshold → 0; else
        ///          OreCeiling * (depthFrac - OreThreshold) / (1 - OreThreshold).
        /// </summary>
        public static double ComputeOreBonus(int y, int seaLevel)
        {
            if (seaLevel <= 0) return 0.0;
            double threshold = config.OreThreshold;
            if (threshold >= 1.0) return 0.0; // divide-by-zero guard
            double depthFrac = ComputeDepthFrac(y, seaLevel);
            if (depthFrac <= threshold) return 0.0;
            return config.OreCeiling * (depthFrac - threshold) / (1.0 - threshold);
        }

        /// <summary>
        /// Living harvest yield multiplier (E3.1 stub, curve is E4.2). Convex ease-in --
        /// poor + (full - poor) * (attunement/100)^2 -- so early attunement stays meaningfully
        /// poor instead of ramping proportionally with a linear curve. Not called from anywhere
        /// yet; Phase 4 wires this once the harvest tool (shears vs. knife, D3) is decided.
        /// </summary>
        public static double ComputeHarvestYieldMultiplier(float attunement)
        {
            double t = GameMath.Clamp(attunement, 0f, 100f) / 100.0;
            return config.ElfHarvestYieldPoor + (config.ElfHarvestYieldFull - config.ElfHarvestYieldPoor) * t * t;
        }

        // ── Command registration ──

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            RegisterDwarfDepthCommand(api);
            RegisterStatsFixCommand(api);
            RegisterPhase0Commands(api);
            RegisterThewCommand(api);
            RegisterRotAuraDiagCommand(api);
            RegisterRotAuraDebugCommand(api);
            RegisterElfStepHeightToggleCommand(api);
            RegisterGoblinSpitCommand(api);
            RegisterChunkScarCommand(api);
            RegisterFliesDiagCommand(api);
        }

        /// <summary>Server-side counterpart to the client hotkey (RegisterElfStepHeightHotkey) --
        /// flips a per-player WatchedAttributes bool, which ElfStepHeightBehavior reads directly.
        /// Chat command rather than a network channel: this codebase has no existing packet
        /// infrastructure, and every other per-player toggle here already goes through
        /// ChatCommands.</summary>
        private void RegisterElfStepHeightToggleCommand(ICoreServerAPI api)
        {
            api.ChatCommands.Create("rfelfstepheight")
                .WithDescription("Toggle the Elf step-height boost for the calling player (also bound to a client hotkey, default Ctrl+H).")
                .RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("toggle")
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        bool defaultOn = Config?.ElfStepHeightDefaultEnabled ?? true;
                        bool current = player.Entity.WatchedAttributes.GetBool("rf-elf-stepheight-enabled", defaultOn);
                        bool next = !current;
                        player.Entity.WatchedAttributes.SetBool("rf-elf-stepheight-enabled", next);

                        return TextCommandResult.Success(string.Format("Elf step height boost {0}.", next ? "enabled" : "disabled"));
                    })
                .EndSubCommand();
        }

        /// <summary>Server-side counterpart to the goblin spit ability, dispatched client-side by
        /// RaceAbilityHotkeyModSystem via RFMechanicsModSystem.TryTriggerGoblinSpit -- ported from
        /// the old RfGoblinSpitRepairBehavior block-behavior, which ran via vanilla's own
        /// click-interact dispatch (client-predicted + server-authoritative automatically). A bare
        /// hotkey has no such dispatch, so this goes through a chat command instead, same shape as
        /// the elf step-height toggle. Repairs whatever block the player is currently looking at
        /// (CurrentBlockSelection) -- present on the base IPlayer interface, so it's populated
        /// server-side too, and already carries the same reach cap vanilla block selection always
        /// has. Re-checks race fresh (not the client's cache) since this is the trust boundary for
        /// a client-triggered command.</summary>
        private void RegisterGoblinSpitCommand(ICoreServerAPI api)
        {
            api.ChatCommands.Create("rfgoblinspit")
                .WithDescription("Goblin spit repair for the calling player (also bound to a client hotkey, default C).")
                .RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("repair")
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        var cfg = Config;
                        if (cfg == null || !cfg.EnableGoblinSpitCharges)
                            return TextCommandResult.Success("Goblin spit charges are disabled.");

                        if (!RaceTraits.HasTrait(player, cfg.GoblinTraitCode))
                            return TextCommandResult.Success("Not a goblin.");

                        BlockSelection blockSel = player.CurrentBlockSelection;
                        if (blockSel == null)
                            return TextCommandResult.Success("Nothing in reach to repair.");

                        IWorldAccessor world = api.World;
                        if (!world.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
                            return TextCommandResult.Success("You don't have access to build here.");

                        EntityPlayer entityPlayer = player.Entity;
                        const string SpitChargesKey = "rfmechanics:spitCharges";
                        int charges = entityPlayer.WatchedAttributes.GetInt(SpitChargesKey, 0);
                        if (charges <= 0)
                            return TextCommandResult.Success("No spit left -- eat rot to refill.");

                        Block block = world.BlockAccessor.GetBlock(blockSel.Position);
                        var bec = block?.GetBEBehavior<BEBehaviorShapeFromAttributes>(blockSel.Position);
                        if (bec == null)
                            return TextCommandResult.Success("Nothing to repair here.");

                        if (bec.repairState >= 1f || bec.reparability <= 1)
                            return TextCommandResult.Success("Nothing more to repair here.");

                        double repairQuantity = cfg.SpitRepairGain;
                        if (repairQuantity < 0.001)
                            return TextCommandResult.Success("Your spit has hardened -- no repair applied.");

                        bec.repairState += (float)(repairQuantity * 5 / (bec.reparability - 1));

                        // Vanilla's own BlockBehaviorReparable never calls MarkDirty either -- it
                        // gets away with that because OnBlockInteractStart runs on both sides via
                        // the click-interact dispatch, so the client's own local copy of
                        // repairState is set directly by its own predicted execution. This command
                        // only ever runs server-side, so without an explicit MarkDirty the client's
                        // BE never re-syncs and the tooltip stays frozen at its last known value.
                        bec.Blockentity.MarkDirty();

                        int remaining = charges - 1;
                        entityPlayer.WatchedAttributes.SetInt(SpitChargesKey, remaining);

                        // Server-triggered PlaySoundAt broadcasts to nearby clients on its own --
                        // no client-side branch needed here, unlike the old dual-invocation block behavior.
                        world.PlaySoundAt(AssetLocation.Create("sounds/player/gluerepair"), blockSel.Position, 0, player, true, 8);

                        return TextCommandResult.Success(string.Format("Spit thins -- {0} left.", remaining));
                    })
                .EndSubCommand();
        }

        /// <summary>Chunk scar tracker commands (RFMechanicsConfig.
        /// ChunkScarTrackingEnabled, ChunkScarTracker, ChunkScarBreakPatch) -- archived passive
        /// data collector, see ChunkScarTracker.cs's header. "here"/"around"/
        /// "bench"/"rate" only read; "selftest" writes then immediately removes its own
        /// dedicated key, net no persisted change. "reset" is the only subcommand that leaves
        /// persisted state mutated, so it alone is bumped to root privilege, overriding the
        /// parent's chat-level default -- same shape as /rfphase0's per-command root gate but
        /// scoped to just one subcommand here since the rest are effectively read-only.</summary>
        private void RegisterChunkScarCommand(ICoreServerAPI api)
        {
            api.ChatCommands.Create("rfscar")
                .WithDescription("Diagnostic-only chunk scar tracker (no gameplay effect) -- IMapChunk moddata persistence and read-cost checks.")
                .RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("here")
                    .WithDescription("Print scar and leaf counts (raw, decayed, hours since last write) for the calling player's current map chunk.")
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        var cfg = Config;
                        if (cfg == null)
                            return TextCommandResult.Success("Config not loaded.");

                        var pos = player.Entity.Pos.AsBlockPos;
                        int cx = ChunkScarTracker.ToChunkCoord(pos.X);
                        int cz = ChunkScarTracker.ToChunkCoord(pos.Z);
                        double nowHours = player.Entity.World.Calendar.TotalHours;
                        var ba = api.World.BlockAccessor;

                        string FormatOne(string label, string key)
                        {
                            var status = ChunkScarTracker.TryGetRaw(ba, cx, cz, key, out ChunkScarData? data);
                            if (status == ChunkScarCellStatus.Unloaded)
                                return string.Format("{0}: map chunk not resident", label);
                            if (status == ChunkScarCellStatus.AbsentKey)
                                return string.Format("{0}: never recorded", label);

                            int decayed = ChunkScarTracker.ComputeDecayed(data!, nowHours, cfg.ChunkScarDecayHoursPerPoint);
                            double sinceHours = Math.Max(0.0, nowHours - data!.LastWriteHours);
                            return string.Format("{0}: raw={1} decayed={2} hoursSinceLastWrite={3:F2}", label, data.Count, decayed, sinceHours);
                        }

                        string msg = string.Format("mapChunk=({0},{1})\n{2}\n{3}",
                            cx, cz, FormatOne("scar", ChunkScarTracker.ScarKey), FormatOne("leaf", ChunkScarTracker.LeafScarKey));
                        return TextCommandResult.Success(msg);
                    })
                .EndSubCommand()
                .BeginSubCommand("around")
                    .WithDescription("Print a grid of decayed scar values for the calling player's current map chunk and its neighbours (radius from ChunkScarNeighborSampleRadius).")
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        var cfg = Config;
                        if (cfg == null)
                            return TextCommandResult.Success("Config not loaded.");

                        var pos = player.Entity.Pos.AsBlockPos;
                        int cx = ChunkScarTracker.ToChunkCoord(pos.X);
                        int cz = ChunkScarTracker.ToChunkCoord(pos.Z);
                        double nowHours = player.Entity.World.Calendar.TotalHours;
                        int radius = cfg.ChunkScarNeighborSampleRadius;
                        var ba = api.World.BlockAccessor;

                        ChunkScarSample[,] grid = ChunkScarTracker.SampleGrid(ba, cx, cz, radius, nowHours, cfg.ChunkScarDecayHoursPerPoint, ChunkScarTracker.ScarKey);

                        var sb = new System.Text.StringBuilder();
                        sb.AppendFormat("Decayed scar grid, mapChunk=({0},{1}) radius={2} (U=unloaded, .=never recorded):\n", cx, cz, radius);
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            var rowParts = new string[radius * 2 + 1];
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                ChunkScarSample sample = grid[dx + radius, dz + radius];
                                rowParts[dx + radius] = sample.Status switch
                                {
                                    ChunkScarCellStatus.Unloaded => "U",
                                    ChunkScarCellStatus.AbsentKey => ".",
                                    _ => sample.DecayedCount.ToString()
                                };
                            }
                            sb.AppendLine(string.Join(" ", rowParts));
                        }

                        return TextCommandResult.Success(sb.ToString());
                    })
                .EndSubCommand()
                .BeginSubCommand("bench")
                    .WithDescription("Run the neighbour-grid read 1000 times and report total/mean cost, plus how many sampled cells were unloaded (a cheap read that would make the timing look artificially fast).")
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        var cfg = Config;
                        if (cfg == null)
                            return TextCommandResult.Success("Config not loaded.");

                        var pos = player.Entity.Pos.AsBlockPos;
                        int cx = ChunkScarTracker.ToChunkCoord(pos.X);
                        int cz = ChunkScarTracker.ToChunkCoord(pos.Z);
                        double nowHours = player.Entity.World.Calendar.TotalHours;
                        int radius = cfg.ChunkScarNeighborSampleRadius;
                        var ba = api.World.BlockAccessor;

                        const int iterations = 1000;

                        // Warmup excluded from the timed loop -- first-touch cost, not steady-state read cost.
                        ChunkScarTracker.SampleGrid(ba, cx, cz, radius, nowHours, cfg.ChunkScarDecayHoursPerPoint, ChunkScarTracker.ScarKey);

                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 0; i < iterations; i++)
                        {
                            ChunkScarTracker.SampleGrid(ba, cx, cz, radius, nowHours, cfg.ChunkScarDecayHoursPerPoint, ChunkScarTracker.ScarKey);
                        }
                        sw.Stop();

                        double totalMs = sw.Elapsed.TotalMilliseconds;
                        double meanMicrosPerRead = sw.Elapsed.TotalMicroseconds / iterations;

                        // Counted once, untimed, after the loop -- chunk residency doesn't change
                        // within a millisecond-scale benchmark, and counting per-iteration would
                        // pollute the very number being measured.
                        ChunkScarSample[,] sampleGrid = ChunkScarTracker.SampleGrid(ba, cx, cz, radius, nowHours, cfg.ChunkScarDecayHoursPerPoint, ChunkScarTracker.ScarKey);
                        int unloadedCells = 0;
                        int totalCells = 0;
                        foreach (ChunkScarSample sample in sampleGrid)
                        {
                            totalCells++;
                            if (sample.Status == ChunkScarCellStatus.Unloaded) unloadedCells++;
                        }

                        int gridSide = radius * 2 + 1;
                        return TextCommandResult.Success(string.Format(
                            "{0}x{0} neighbour read x{1}: totalMs={2:F3} meanMicrosPerRead={3:F3} unloadedCells={4}/{5} (unloaded reads are cheap -- a fast mean with many unloaded cells is not a real perf number)",
                            gridSide, iterations, totalMs, meanMicrosPerRead, unloadedCells, totalCells));
                    })
                .EndSubCommand()
                .BeginSubCommand("reset")
                    .WithDescription("Clear the scar and leaf counters for the calling player's current map chunk.")
                    .RequiresPrivilege(Privilege.root)
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        var pos = player.Entity.Pos.AsBlockPos;
                        int cx = ChunkScarTracker.ToChunkCoord(pos.X);
                        int cz = ChunkScarTracker.ToChunkCoord(pos.Z);
                        var ba = api.World.BlockAccessor;

                        ChunkScarTracker.Reset(ba, cx, cz, ChunkScarTracker.ScarKey);
                        ChunkScarTracker.Reset(ba, cx, cz, ChunkScarTracker.LeafScarKey);

                        return TextCommandResult.Success(string.Format("Cleared scar and leaf counters for mapChunk=({0},{1}).", cx, cz));
                    })
                .EndSubCommand()
                .BeginSubCommand("rate")
                    .WithDescription("Print raw count, first/most-recent break hour, and elapsed hours between them, separately for scar and leaf, for the calling player's current map chunk.")
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        var pos = player.Entity.Pos.AsBlockPos;
                        int cx = ChunkScarTracker.ToChunkCoord(pos.X);
                        int cz = ChunkScarTracker.ToChunkCoord(pos.Z);
                        var ba = api.World.BlockAccessor;

                        string FormatRate(string label, string key)
                        {
                            var status = ChunkScarTracker.TryGetRaw(ba, cx, cz, key, out ChunkScarData? data);
                            if (status == ChunkScarCellStatus.Unloaded)
                                return string.Format("{0}: map chunk not resident", label);
                            if (status == ChunkScarCellStatus.AbsentKey)
                                return string.Format("{0}: never recorded", label);

                            double elapsed = Math.Max(0.0, data!.LastWriteHours - data.FirstWriteHours);
                            return string.Format("{0}: count={1} firstBreakHour={2:F2} lastBreakHour={3:F2} elapsedHours={4:F2}",
                                label, data.Count, data.FirstWriteHours, data.LastWriteHours, elapsed);
                        }

                        string msg = string.Format("mapChunk=({0},{1})\n{2}\n{3}",
                            cx, cz, FormatRate("scar", ChunkScarTracker.ScarKey), FormatRate("leaf", ChunkScarTracker.LeafScarKey));
                        return TextCommandResult.Success(msg);
                    })
                .EndSubCommand()
                .BeginSubCommand("selftest")
                    .WithDescription("Round-trip a sentinel through SetModdata/GetModdata on the current map chunk, and report this mod's own prefix/postfix count on Block.OnBlockBroken (expected 1/1 -- more means the PatchAll double-patch bug is back).")
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        var pos = player.Entity.Pos.AsBlockPos;
                        bool roundTripOk = ChunkScarTracker.SelfTest(api.World, pos, out int byteLength);

                        ChunkScarBreakPatch.CountOwnPatches(HarmonyId, out int prefixCount, out int postfixCount);
                        string patchWarning = (prefixCount != 1 || postfixCount != 1)
                            ? " WARNING: expected 1 prefix/1 postfix -- double-patch bug may be back, every scar count this session is suspect."
                            : "";

                        return TextCommandResult.Success(string.Format(
                            "selftest: roundTrip={0} payloadBytes={1} | OnBlockBroken patches owned by rfmechanics: prefixes={2} postfixes={3}{4}",
                            roundTripOk ? "PASS" : "FAIL", byteLength, prefixCount, postfixCount, patchWarning));
                    })
                .EndSubCommand();
        }

        /// <summary>Read the server aura state and calendar-day recovery for the caller.</summary>
        private void RegisterRotAuraDiagCommand(ICoreServerAPI api)
        {
            api.ChatCommands.Create("rfrotdiag")
                .WithDescription("Show literal-rot aura radius, recovery and fly targets for the calling player.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(args => Config == null ? TextCommandResult.Error("Config not loaded.")
                    : TextCommandResult.Success(GoblinAuraCommands.Describe(args.Caller.Player, Config)));
        }

        /// <summary>Report aura and charge targets; the renderer may cull flies near the camera.</summary>
        private void RegisterFliesDiagCommand(ICoreServerAPI api)
        {
            api.ChatCommands.Create("rfflies")
                .WithDescription("Show literal-rot aura radius, recovery and fly targets for the calling player.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(args => Config == null ? TextCommandResult.Error("Config not loaded.")
                    : TextCommandResult.Success(GoblinAuraCommands.Describe(args.Caller.Player, Config)));
        }

        /// <summary>Register radius, off, age, registry and legacy world-timescale test commands.</summary>
        private void RegisterRotAuraDebugCommand(ICoreServerAPI api)
        {
            GoblinAuraCommands.Register(api);
        }

        /// <summary>Registered server-side only: EntityBehaviorHunger and entity.Attributes
        /// (rf-climbseconds/rf-climbflush) are server-authoritative, not synced to the client, so
        /// reading them from a client-side registration would silently return null/default.</summary>
        private void RegisterDwarfDepthCommand(ICoreAPI api)
        {
            api.ChatCommands.Create("dwarfdepth")
                .WithDescription("Print depth/altitude curve debug info for the calling player")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(args =>
                {
                    IPlayer player = args.Caller.Player;
                    if (player == null)
                        return TextCommandResult.Success("No player context.");

                    int y = (int)player.Entity.Pos.Y;
                    int seaLevel = api.World.SeaLevel;

                    bool hasClass = player.Entity.WatchedAttributes.GetString("characterClass") != null;
                    bool hasTrait = RaceTraits.HasTrait(player, config.DwarfTraitCode);

                    double depthFrac = GameMath.Clamp((double)(seaLevel - y) / seaLevel, 0.0, 1.0);
                    double altFrac = GameMath.Clamp((double)(y - seaLevel) / seaLevel, 0.0, 1.0);
                    double bonus = ComputeBonus(y, seaLevel);
                    double multiplier = 1.0 + bonus;

                    string msg = string.Format(
                        "Y={0} SeaLevel={1} depthFrac={2:F4} altFrac={3:F4} bonus={4:F4} multiplier={5:F4} hasClass={6} traitDetected={7} traitCode={8}",
                        y, seaLevel, depthFrac, altFrac, bonus, multiplier, hasClass, hasTrait, config.DwarfTraitCode);

                    return TextCommandResult.Success(msg);
                });

            api.ChatCommands.Create("rfdiag")
                .WithDescription("Dump raw traits and blended stats for the calling player (diagnostic)")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(args =>
                {
                    IPlayer player = args.Caller.Player;
                    if (player == null)
                        return TextCommandResult.Success("No player context.");

                    var wa = player.Entity.WatchedAttributes;
                    string[] extraTraits = wa.GetStringArray("extraTraits", null);
                    string extraTraitsStr = extraTraits == null ? "(null)" : string.Join(",", extraTraits);

                    float walkspeed = player.Entity.Stats.GetBlended("walkspeed");
                    float hungerrate = player.Entity.Stats.GetBlended("hungerrate");

                    var charSys = api.ModLoader.GetModSystem<CharacterSystem>();
                    bool hasClass = !string.IsNullOrEmpty(wa.GetString("characterClass"));
                    bool hasPositive = hasClass && charSys != null && charSys.HasTrait(player, "rf-dwarf-positive");
                    bool hasRfNegative = hasClass && charSys != null && charSys.HasTrait(player, "rf-dwarf-negative");
                    bool hasLrNegative = hasClass && charSys != null && charSys.HasTrait(player, "dwarf-negative");
                    bool hasElfPositive = hasClass && charSys != null && charSys.HasTrait(player, config.ElfTraitCode);

                    float bankedClimbSeconds = player.Entity.Attributes.GetFloat("rf-climbseconds");
                    float flushTimer = player.Entity.Attributes.GetFloat("rf-climbflush");

                    var hungerBhv = player.Entity.GetBehavior<EntityBehaviorHunger>();
                    string saturationStr = hungerBhv == null ? "(no hunger behavior)" : string.Format("{0:F1}/{1:F1}", hungerBhv.Saturation, hungerBhv.MaxSaturation);

                    string msg = string.Format(
                        "extraTraits=[{0}] walkspeed={1:F4} hungerrate={2:F4} rf-dwarf-positive={3} rf-dwarf-negative={4} dwarf-negative={5} {6}={7} bankedClimbSeconds={8:F2} flushTimer={9:F2} saturation={10}",
                        extraTraitsStr, walkspeed, hungerrate, hasPositive, hasRfNegative, hasLrNegative, config.ElfTraitCode, hasElfPositive, bankedClimbSeconds, flushTimer, saturationStr);

                    return TextCommandResult.Success(msg + "\n" + FormatStatBreakdown(player, "miningSpeedMul") + "\n" + FormatStatBreakdown(player, "forageDropRate") + "\n" + FormatStatBreakdown(player, "wildCropDropRate") + "\n" + FormatStatBreakdown(player, "hungerrate") + "\n" + FormatStatBreakdown(player, "walkspeed"));
                });
        }

        /// <summary>LANDMINE: a bare "&gt;" in chat-command output desyncs the client's rich-text
        /// tag parser, silently failing to display the whole message even though it's written to
        /// client-chat.log correctly -- avoid any bare "&lt;"/"&gt;" here (e.g. no literal "-&gt;").</summary>
        private static string FormatStatBreakdown(IPlayer player, string category)
        {
            try
            {
                var floatStats = player.Entity.Stats[category];
                var parts = new System.Collections.Generic.List<string>();
                foreach (var kv in floatStats.ValuesByKey)
                    parts.Add(string.Format("{0}={1:F3}", kv.Key, kv.Value.Value));

                return string.Format("{0}: {1}, blended={2:F3}", category, string.Join(" ", parts), player.Entity.Stats.GetBlended(category));
            }
            catch (Exception ex)
            {
                return string.Format("{0}: (unavailable: {1})", category, ex.Message);
            }
        }

        /// <summary>Workaround for a race/model-swap bug external to rfmechanics: whatever
        /// performs a live model swap (e.g. PlayerModelLib) updates characterClass/extraTraits
        /// but never re-invokes CharacterSystem.applyTraitAttributes, leaving old trait-sourced
        /// Stats entries stuck at the previous race's values. setCharacterClass(...,
        /// initializeGear: false) re-runs that recompute without touching gear.</summary>
        private void RegisterStatsFixCommand(ICoreServerAPI api)
        {
            api.ChatCommands.Create("rfstatsfix")
                .WithDescription("Force a full trait/stat recompute for the calling player (fixes stale walkspeed/hungerrate left over from a race/model swap)")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(args =>
                {
                    IPlayer player = args.Caller.Player;
                    if (player == null)
                        return TextCommandResult.Success("No player context.");

                    string classCode = player.Entity.WatchedAttributes.GetString("characterClass");
                    if (string.IsNullOrEmpty(classCode))
                        return TextCommandResult.Success("No characterClass set; nothing to refresh.");

                    var charSys = api.ModLoader.GetModSystem<CharacterSystem>();
                    if (charSys == null)
                        return TextCommandResult.Success("CharacterSystem not found.");

                    charSys.setCharacterClass(player.Entity, classCode, false);
                    return TextCommandResult.Success("Recomputed trait/stat attributes for class '" + classCode + "'.");
                });
        }

        /// <summary>Orc Thew diagnostics: current value plus a named per-condition readout so
        /// gain/decay tuning can be debugged without guessing which condition is failing.
        /// Root-privileged since "set" force-sets Thew for testing. Message formatting avoids
        /// bare '&lt;'/'&gt;' -- see FormatStatBreakdown's chat-rendering landmine.</summary>
        private void RegisterThewCommand(ICoreServerAPI api)
        {
            CommandArgumentParsers parsers = api.ChatCommands.Parsers;

            api.ChatCommands.Create("rfthew")
                .WithDescription("Orc Thew diagnostics: current value, named gain/decay condition readout, and a root-only override.")
                .RequiresPrivilege(Privilege.root)
                .BeginSubCommand("dump")
                    .WithDescription("Dump Thew value and named gain/decay condition readout for the calling player.")
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        var cfg = Config;
                        if (cfg == null)
                            return TextCommandResult.Success("Config not loaded.");

                        Entity entity = player.Entity;
                        var thewBhv = entity.GetBehavior<ThewBehavior>();
                        float thew = thewBhv != null ? thewBhv.Thew : entity.Attributes.GetFloat("rf-orc-thew", 0f);

                        string charClass = entity.WatchedAttributes.GetString("characterClass");
                        bool isOrc = RaceTraits.HasTrait(player, cfg.OrcTraitCode);

                        var hunger = entity.GetBehavior<EntityBehaviorHunger>();
                        if (hunger == null || hunger.MaxSaturation <= 0f)
                            return TextCommandResult.Success(string.Format("thew={0:F4} orc={1} (no hunger behavior)", thew, isOrc));

                        float satFrac = hunger.Saturation / hunger.MaxSaturation;
                        bool proteinGated = ThewBehavior.IsProteinGated(hunger, cfg);

                        EnumFoodCategory lastFoodCat = (EnumFoodCategory)entity.Attributes.GetInt(ThewBehavior.LastFoodCategoryKey, (int)EnumFoodCategory.NoNutrition);
                        bool foodTypeBlocksGain = cfg.EnableThewFoodTypeGate && ThewBehavior.IsNonProteinPlantCategory(lastFoodCat);

                        string zone = ThewBehavior.SatietyZoneName(hunger, satFrac, cfg);
                        bool gaining = isOrc && zone == "Gain" && proteinGated && !foodTypeBlocksGain;

                        string[] extraTraits = entity.WatchedAttributes.GetStringArray("extraTraits", null);
                        string extraTraitsStr = extraTraits == null ? "(null)" : string.Join(",", extraTraits);

                        string bandStr = "(no band behavior)";
                        var bandBhv = entity.GetBehavior<BandBehavior>();
                        if (bandBhv != null)
                        {
                            BandBehavior.Band band = bandBhv.CurrentBand;
                            float actualSize = entity.WatchedAttributes.GetFloat("entitySize", 1f);
                            float targetSize = BandBehavior.ComputeTargetSize(thew, cfg);
                            bandStr = string.Format(
                                "band={0} entitySize={1:F3} thewTargetSize={2:F3} sizeRatePerSec={3:F4} hungerrateMult={4:F2} walkspeedDelta={5:F2} seekRangeDelta={6:F2} maxHpExtra={7:F1} thewGainMult={8:F2}",
                                band, actualSize, targetSize, cfg.SizeChangeRatePerSecond,
                                BandBehavior.Pick(cfg.HungerRateMult, band), BandBehavior.Pick(cfg.WalkSpeedDelta, band),
                                BandBehavior.Pick(cfg.AnimalSeekingRangeDelta, band), BandBehavior.Pick(cfg.MaxHpExtraPoints, band),
                                BandBehavior.Pick(cfg.ThewGainBandMult, band));
                            if (band == BandBehavior.Band.Bulky)
                            {
                                bandStr += string.Format(" +meleeDamage={0:F2} armorWalkSpeedAffDelta={1:F2}",
                                    cfg.BulkyMeleeDamageBonus, cfg.BulkyArmorWalkSpeedAffectednessDelta);
                            }
                        }

                        string burnStr = "(no burn behavior)";
                        var burnBhv = entity.GetBehavior<BurnBehavior>();
                        if (burnBhv != null)
                        {
                            var healthBhv = entity.GetBehavior<EntityBehaviorHealth>();
                            string healthStr = healthBhv == null ? "?" : string.Format("{0:F1}/{1:F1}", healthBhv.Health, healthBhv.MaxHealth);
                            burnStr = string.Format(
                                "burnActive={0} health={1} activationGap={2:F2} maxHealPerSec={3:F2} curveExp={4:F1} thewPerHp={5:F3} debtIncurredThisBurn={6:F4}",
                                burnBhv.Burning, healthStr, cfg.BurnActivationHealthFracGap, cfg.BurnMaxHealPerSecond, cfg.BurnCurveExponent, cfg.BurnThewPerHp, burnBhv.DebtIncurredThisBurn);
                        }

                        string frenzyStr = "(no frenzy behavior)";
                        var frenzyBhv = entity.GetBehavior<FrenzyBehavior>();
                        if (frenzyBhv != null)
                        {
                            float frenzyCurveMult = FrenzyBehavior.ComputeCurveMult(satFrac, cfg);
                            bool frenzyStalled = thewBhv != null && thewBhv.Thew <= 0f && (thewBhv.BurnDebt + thewBhv.FrenzyDebt) > 0f;
                            frenzyStr = string.Format(
                                "frenzyCurveMult={0:F3} stalled={1} walkspeedBonus={2:F3} jumpBonus={3:F3} debtGate={4:F2} debtPerGameHour={5:F3}",
                                frenzyCurveMult, frenzyStalled, (float)cfg.FrenzyMaxSpeedBonus * frenzyCurveMult, (float)cfg.FrenzyMaxJumpBonus * frenzyCurveMult, cfg.FrenzyDebtSatietyThreshold, cfg.FrenzyDebtPerGameHour);
                        }

                        string debtStr = thewBhv != null
                            ? string.Format("burnDebt={0:F4} frenzyDebt={1:F4} totalDebt={2:F4} drainPerHour={3:F3}",
                                thewBhv.BurnDebt, thewBhv.FrenzyDebt, thewBhv.BurnDebt + thewBhv.FrenzyDebt, cfg.DebtDrainPerHour)
                            : "(no thew behavior)";

                        string resistStr = "(not orc)";
                        if (isOrc)
                        {
                            var healthBhv = entity.GetBehavior<EntityBehaviorHealth>();
                            if (healthBhv == null)
                            {
                                resistStr = "resist=? (no health behavior)";
                            }
                            else if (!cfg.EnableOrcWildAnimalResist)
                            {
                                resistStr = "resist=0.00 (disabled in config)";
                            }
                            else
                            {
                                float resist = OrcWildAnimalResistPatch.ComputeResist(healthBhv, cfg);
                                if (resist <= 0f)
                                {
                                    resistStr = string.Format("resist=0.00 (below activation gap {0:F2})", cfg.OrcWildResistActivationHealthFracGap);
                                }
                                else if (cfg.OrcWildResistRequiresNoArmor && entity is EntityPlayer entityPlayer && OrcWildAnimalResistPatch.IsWearingArmor(entityPlayer))
                                {
                                    resistStr = string.Format("resist=0.00 (wearing armor, would be {0:F3})", resist);
                                }
                                else
                                {
                                    resistStr = string.Format("resist={0:F3}", resist);
                                }
                            }
                        }

                        string msg = string.Format(
                            "thew={0:F4} orc={1} charClass={2} extraTraits=[{3}] satFrac={4:F3} zone={5} (gainGate {6:F2} lowSatietyThreshold {7:F2}) protein={8:F1} dairy={9:F1} proteinGated={10} (threshold {11:F1}, Protein OR Dairy) lastFoodCategory={12} foodTypeBlocksGain={13} gaining={14} {15} {16} {17} {18} {19}",
                            thew, isOrc, charClass ?? "(null)", extraTraitsStr, satFrac, zone, cfg.ThewGainSatietyGate, cfg.ThewDecayLowSatietyThreshold, hunger.ProteinLevel, hunger.DairyLevel, proteinGated, cfg.ProteinGateLevel, lastFoodCat, foodTypeBlocksGain, gaining, debtStr, bandStr, burnStr, frenzyStr, resistStr);

                        return TextCommandResult.Success(msg);
                    })
                .EndSubCommand()
                .BeginSubCommand("set")
                    .WithDescription("Force-set Thew on the calling player (testing only).")
                    .WithArgs(parsers.Float("value"))
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        var thewBhv = player.Entity.GetBehavior<ThewBehavior>();
                        if (thewBhv == null)
                            return TextCommandResult.Success("ThewBehavior not attached to this entity (relog after a fresh deploy?).");

                        thewBhv.Thew = (float)args[0];
                        return TextCommandResult.Success(string.Format("Thew set to {0:F4}", thewBhv.Thew));
                    })
                .EndSubCommand()
                .BeginSubCommand("setband")
                    .WithDescription("Force-set the calling player's Band directly (testing only) -- bypasses hysteresis, applies stats only. entitySize is unaffected: it tracks Thew continuously, not band.")
                    .WithArgs(parsers.Word("band", new[] { "lean", "standard", "bulky" }))
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        var bandBhv = player.Entity.GetBehavior<BandBehavior>();
                        if (bandBhv == null)
                            return TextCommandResult.Success("BandBehavior not attached to this entity (relog after a fresh deploy?).");

                        string raw = (string)args[0];
                        string arg = raw?.ToLowerInvariant() ?? "";
                        BandBehavior.Band? band = arg switch
                        {
                            "lean" => BandBehavior.Band.Lean,
                            "standard" => BandBehavior.Band.Standard,
                            "bulky" => BandBehavior.Band.Bulky,
                            _ => (BandBehavior.Band?)null
                        };
                        if (band == null)
                            return TextCommandResult.Success(string.Format("Unrecognized band '{0}' (raw arg: '{1}') -- use lean/standard/bulky.", arg, raw ?? "(null)"));

                        bandBhv.ForceBand(band.Value);
                        return TextCommandResult.Success(string.Format("Band forced to {0} (parsed from '{1}').", band.Value, raw));
                    })
                .EndSubCommand();
        }

        // LOAD-BEARING: originally written for the PHASE0-DIAG diagnostics below, but
        // BandBehavior now depends on these too for real band-size writes -- not safe to delete
        // alongside a PHASE0-DIAG cleanup pass. Kept `internal` so BandBehavior.cs can call them directly.

        // Looked up via Entity.GetBehavior(string) + reflection so rfmechanics does not need a compile-time reference to PlayerModelLib.dll.
        internal const string PmlSkinBehaviorPropertyName = "skinnableplayercustommodel";

        internal static EntityBehavior? GetPmlSkinBehavior(Entity entity)
        {
            return entity.GetBehavior(PmlSkinBehaviorPropertyName);
        }

        internal static string DescribePmlCurrentSize(Entity entity)
        {
            EntityBehavior? behavior = GetPmlSkinBehavior(entity);
            if (behavior == null)
                return "(behavior absent)";

            PropertyInfo? prop = behavior.GetType().GetProperty("CurrentSize");
            object? value = prop?.GetValue(behavior);
            return value is float f ? f.ToString("F3") : "(CurrentSize unavailable)";
        }

        // Mirrors CustomModelsSystem.HandleChangePlayerModelSizePacket exactly: SetFloat("entitySize", ...) followed by an explicit UpdateEntityProperties() call, never touching PML's client packet path.
        internal static bool TryUpdatePmlEntityProperties(Entity entity, out string message)
        {
            EntityBehavior? behavior = GetPmlSkinBehavior(entity);
            if (behavior == null)
            {
                message = "PlayerSkinBehavior absent (is PlayerModelLib installed and its behaviors.json patch applied?).";
                return false;
            }

            MethodInfo? method = behavior.GetType().GetMethod("UpdateEntityProperties");
            if (method == null)
            {
                message = "PlayerSkinBehavior found but UpdateEntityProperties not found via reflection (PML version mismatch?).";
                return false;
            }

            method.Invoke(behavior, null);
            message = "UpdateEntityProperties invoked.";
            return true;
        }

        // PHASE0-DIAG -- remove before release. No gameplay behavior: read/write of
        // diagnostic-only state (rf-p0-marker) plus a direct mirror of PlayerModelLib's own
        // entitySize write pattern.

        private const string P0MarkerKey = "rf-p0-marker";

        private void RegisterPhase0Commands(ICoreServerAPI api)
        {
            CommandArgumentParsers parsers = api.ChatCommands.Parsers;

            api.ChatCommands.Create("rfphase0")
                .WithDescription("PHASE0-DIAG — orc phase 0 verification diagnostics. Remove before release.")
                .RequiresPrivilege(Privilege.root)
                .BeginSubCommand("marker")
                    .WithDescription("Get/set entity.Attributes[\"rf-p0-marker\"] on the calling player.")
                    .WithArgs(parsers.Word("action", new[] { "set", "get" }), parsers.OptionalFloat("value"))
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        string action = (string)args[0];
                        if (action == "set")
                        {
                            float value = (float)args[1];
                            player.Entity.Attributes.SetFloat(P0MarkerKey, value);
                            return TextCommandResult.Success(string.Format("rf-p0-marker set to {0:F3}", value));
                        }

                        float current = player.Entity.Attributes.GetFloat(P0MarkerKey, 0f);
                        return TextCommandResult.Success(string.Format("rf-p0-marker = {0:F3}", current));
                    })
                .EndSubCommand()
                .BeginSubCommand("entitysize")
                    .WithDescription("Set WatchedAttributes[\"entitySize\"] on the calling player, mirroring PML's own server-side write.")
                    .WithArgs(parsers.Float("value"))
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        float value = (float)args[0];
                        player.Entity.WatchedAttributes.SetFloat("entitySize", value);
                        TryUpdatePmlEntityProperties(player.Entity, out string message);
                        return TextCommandResult.Success(string.Format("entitySize set to {0:F3}. {1}", value, message));
                    })
                .EndSubCommand()
                .BeginSubCommand("dump")
                    .WithDescription("Dump phase0 diagnostic state for the calling player.")
                    .HandleWith(args =>
                    {
                        IPlayer player = args.Caller.Player;
                        if (player == null)
                            return TextCommandResult.Success("No player context.");

                        Entity entity = player.Entity;
                        float marker = entity.Attributes.GetFloat(P0MarkerKey, 0f);
                        float entitySize = entity.WatchedAttributes.GetFloat("entitySize", 0f);
                        string pmlCurrentSize = DescribePmlCurrentSize(entity);
                        Vec2f collisionBox = entity.Properties.CollisionBoxSize;
                        float clientSize = entity.Properties.Client.Size;
                        float eyeHeight = (float)entity.Properties.EyeHeight;
                        float localEyePosY = (float)entity.LocalEyePos.Y;

                        var hunger = entity.GetBehavior<EntityBehaviorHunger>();
                        string hungerStr = hunger == null
                            ? "(no hunger behavior)"
                            : string.Format(
                                "sat={0:F1}/{1:F1} fruit={2:F1} veg={3:F1} protein={4:F1} grain={5:F1} dairy={6:F1}",
                                hunger.Saturation, hunger.MaxSaturation, hunger.FruitLevel, hunger.VegetableLevel,
                                hunger.ProteinLevel, hunger.GrainLevel, hunger.DairyLevel);

                        string msg = string.Format(
                            "marker={0:F3} entitySize(attr)={1:F3} pmlCurrentSize={2} collisionBox=({3:F3},{4:F3}) clientSize={5:F3} eyeHeight={6:F3} localEyePosY={7:F3} {8}",
                            marker, entitySize, pmlCurrentSize, collisionBox.X, collisionBox.Y, clientSize, eyeHeight, localEyePosY, hungerStr);

                        return TextCommandResult.Success(msg);
                    })
                .EndSubCommand();
        }
    }
}

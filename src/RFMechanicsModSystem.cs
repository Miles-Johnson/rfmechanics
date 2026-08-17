using System;
using System.Reflection;
using HarmonyLib;
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
            ValidateAttunementConfig(api, config);

            api.Logger.Notification("[rfmechanics] Config loaded. DwarfTraitCode={0}, EnableMiningCurve={1}, EnableOreCurve={2}, OreThreshold={3}, OreCeiling={4}, ClimbSpeedFactor={5}, ClimbSaturationPerSecond={6}, EnableClimbSpeed={7}, EnableClimbSaturation={8}, ElfTraitCode={9}, EnableBranchyLeavesPassthrough={10}, EnableTreeProximitySpeed={11}, TreeProximityRadius={12}, TreeProximityMaxBonus={13}, EnableTreeClimbing={14}, EnableFallDamageReduction={15}, FallDamageReductionFactor={16}, GoblinTraitCode={17}, EnableGoblinDarkvision={18}, GoblinDarkvisionStrength={19}, EnableGoblinFallDamageReduction={20}, GoblinFallDamageReductionFactor={21}",
                config.DwarfTraitCode, config.EnableMiningCurve, config.EnableOreCurve, config.OreThreshold, config.OreCeiling, config.ClimbSpeedFactor, config.ClimbSaturationPerSecond, config.EnableClimbSpeed, config.EnableClimbSaturation, config.ElfTraitCode, config.EnableBranchyLeavesPassthrough, config.EnableTreeProximitySpeed, config.TreeProximityRadius, config.TreeProximityMaxBonus, config.EnableTreeClimbing, config.EnableFallDamageReduction, config.FallDamageReductionFactor, config.GoblinTraitCode, config.EnableGoblinDarkvision, config.GoblinDarkvisionStrength, config.EnableGoblinFallDamageReduction, config.GoblinFallDamageReductionFactor);

            api.RegisterEntityBehaviorClass("rftreeproximity", typeof(RFTreeProximityBehavior));
            api.RegisterEntityBehaviorClass("rfelfattunement", typeof(ElfAttunementBehavior));
            api.RegisterEntityBehaviorClass("rfthew", typeof(ThewBehavior));
            api.RegisterEntityBehaviorClass("rfband", typeof(BandBehavior));
            api.RegisterEntityBehaviorClass("rfburn", typeof(BurnBehavior));
            api.RegisterEntityBehaviorClass("rffrenzy", typeof(FrenzyBehavior));
            api.RegisterEntityBehaviorClass("rfgoblintunnel", typeof(RFGoblinTunnelBehavior));
            api.RegisterEntityBehaviorClass("rfgoblinrotaura", typeof(GoblinRotAuraBehavior));
            api.RegisterCropBehavior("RfGoblinCropStunt", typeof(GoblinCropStuntBehavior));
            api.RegisterBlockBehaviorClass("RfGoblinSpitRepair", typeof(RfGoblinSpitRepairBehavior));
            api.RegisterBlockBehaviorClass("RfDwarfOreSong", typeof(RfDwarfOreSongBehavior));

            // GoblinDigModifierBehavior re-homed to src/BugRace/ (future bug race), disabled -- see its class header.
            // api.RegisterBlockBehaviorClass("GoblinDigModifier", typeof(rfmechanics.BugRace.GoblinDigModifierBehavior));

            // Apply Harmony patches
            harmony = new Harmony(HarmonyId);
            try
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                api.Logger.Notification("[rfmechanics] Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                api.Logger.Error("[rfmechanics] Harmony patch failed: {0}", ex);
            }
        }

        public override void Dispose()
        {
            if (harmony != null)
            {
                harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
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

            if (!malformed)
            {
                api.StoreModConfig(config, "rfmechanics.json");
            }
        }

        /// <summary>Guards the invariant AttunementThresholdHysteresis's own doc comment states
        /// but can't enforce on its own: it must exceed the largest possible single-tick
        /// attunement delta, or threshold-crossing events chatter. Warning only, not a hard
        /// failure -- retuning those rate/interval fields is expected, so this surfaces a bad retune immediately instead of as unexplained event spam later.</summary>
        private static void ValidateAttunementConfig(ICoreAPI api, RFMechanicsConfig cfg)
        {
            double maxRate = Math.Max(cfg.AttunementDecayRate, cfg.AttunementGainRate);
            double maxTickDelta = maxRate * cfg.AttunementTickInterval;

            if (cfg.AttunementThresholdHysteresis <= maxTickDelta)
            {
                api.Logger.Warning(
                    "[rfmechanics] ElfAttunement: AttunementThresholdHysteresis ({0}) does not exceed the worst-case single-tick delta ({1:F3} = max(DecayRate={2}, GainRate={3}) * TickInterval={4}) -- threshold-crossing events can chatter near a threshold. Raise AttunementThresholdHysteresis above {1:F3}.",
                    cfg.AttunementThresholdHysteresis, maxTickDelta, cfg.AttunementDecayRate, cfg.AttunementGainRate, cfg.AttunementTickInterval);
            }

            // Phase 1b: an inverted/empty band silently under-scans (or never scans) instead of
            // throwing, so this would otherwise surface as "check 2 never reads Forest" with
            // no obvious cause.
            if (cfg.AttunementCensusSurfaceBandBelow < 0 || cfg.AttunementCensusSurfaceBandAbove < 0)
            {
                api.Logger.Warning(
                    "[rfmechanics] ElfAttunement: AttunementCensusSurfaceBandBelow ({0}) and AttunementCensusSurfaceBandAbove ({1}) must both be >= 0 -- a negative band inverts or shrinks the census scan range.",
                    cfg.AttunementCensusSurfaceBandBelow, cfg.AttunementCensusSurfaceBandAbove);
            }

            // A threshold <= 0 makes every censused column read as forest unconditionally --
            // technically well-defined, but almost certainly not what a retune intended.
            if (cfg.AttunementCensusLogCountThreshold <= 0)
            {
                api.Logger.Warning(
                    "[rfmechanics] ElfAttunement: AttunementCensusLogCountThreshold ({0}) is <= 0 -- every censused column will read as forest-present unconditionally.",
                    cfg.AttunementCensusLogCountThreshold);
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

            // Server-only: ElfAttunementBehavior's tick never runs client-side, so only the server needs the resolved whitelist.
            if (config != null) ElfAttunementBlockWhitelist.Resolve(api, config);

            // Logging only. E3.4's leaf-standing gate (LeafStandingActive) is not maintained
            // here -- it's set inline inside ElfAttunementBehavior.EvaluateThresholds, the same
            // crossing detection that raises this event, so BranchyLeavesPassthroughPatch's
            // per-substep read never depends on subscriber registration order at mod start.
            ElfAttunementBehavior.ThresholdCrossed += (entity, threshold, active, value) =>
            {
                api.Logger.Notification("[rfmechanics] ElfAttunement threshold {0} {1} for entity {2} (value={3:F2})",
                    threshold, active ? "ENTERED" : "LEFT", entity.EntityId, value);
            };

            RegisterAttunementDiagCommand(api);
            RegisterAttunementSetCommand(api);
        }

        /// <summary>Elf attunement diagnostics: the float, the resolved context, which of the
        /// three GetAttunementContext checks individually passed/failed, which threshold
        /// bands are active, and E3.4's leaf-standing gate (LeafStandingActive plus the
        /// configured threshold it's compared against). Server-side only: the live value lives
        /// in behavior memory plus WatchedAttributes, both only meaningful against the real
        /// server entity.</summary>
        private void RegisterAttunementDiagCommand(ICoreServerAPI api)
        {
            api.ChatCommands.Create("rfattune")
                .WithDescription("Dump Elf attunement diagnostics for the calling player: float value, resolved context, per-check breakdown, active thresholds, leaf-standing gate.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(args =>
                {
                    IPlayer player = args.Caller.Player;
                    if (player == null)
                        return TextCommandResult.Success("No player context.");

                    var cfg = Config;
                    if (cfg == null)
                        return TextCommandResult.Success("Config not loaded.");

                    Entity entity = player.Entity;
                    var behavior = entity.GetBehavior<ElfAttunementBehavior>();
                    if (behavior == null)
                        return TextCommandResult.Success("ElfAttunementBehavior not attached to this entity (relog after a fresh deploy?).");

                    // Prefers the behavior's own per-tick cache over a fresh GetDiagnostics call, but only when
                    // IsElfCached confirms the tick has actually run -- otherwise LastDiagnostics sits at its
                    // Unevaluated default, and reporting that as real would misleadingly show "context=None, every check false".
                    string diagSource;
                    AttunementDiagnostics diag;
                    if (cfg.EnableElfAttunement && behavior.IsElfCached)
                    {
                        diag = behavior.LastDiagnostics;
                        diagSource = "cached (last tick)";
                    }
                    else
                    {
                        diag = ElfAttunementContext.GetDiagnostics(entity, behavior.ForestCache, out var updatedCache);
                        behavior.ForestCache = updatedCache; // keeps the cache warm even when called off the tick path
                        diagSource = cfg.EnableElfAttunement ? "live (not cached yet -- not currently an elf)" : "live (EnableElfAttunement=false, tick not running)";
                    }

                    bool[] active = behavior.ActiveThresholdsSnapshot;
                    var thresholdParts = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < cfg.AttunementThresholds.Length; i++)
                    {
                        bool isActive = i < active.Length && active[i];
                        thresholdParts.Add(string.Format("{0}={1}", cfg.AttunementThresholds[i], isActive ? "on" : "off"));
                    }

                    long cacheAgeMs = diag.ForestCensus.LastCheckedTimeMs < 0
                        ? -1
                        : entity.World.ElapsedMilliseconds - diag.ForestCensus.LastCheckedTimeMs;

                    string msg = string.Format(
                        "attunement={0:F2} isElf={1} context={2} ({3}) checks[forestNaturalGround={4} forestPresence={5}] " +
                        "census[logCount={6} threshold={7} cacheAgeMs={8} fromCache={9} cachedGen={10} currentGen={11}] thresholds=[{12}] " +
                        "leafStanding[active={13} threshold={14}]",
                        behavior.LiveAttunement, behavior.IsElfCached, diag.Context, diagSource,
                        diag.ForestNaturalGround, diag.ForestPresence,
                        diag.ForestCensus.LogCount, cfg.AttunementCensusLogCountThreshold, cacheAgeMs, diag.ForestCensus.FromCache,
                        diag.ForestCensus.CachedGeneration, diag.ForestCensus.CurrentGeneration,
                        string.Join(" ", thresholdParts),
                        behavior.LeafStandingActive, cfg.LeafStandingAttunementThreshold);

                    return TextCommandResult.Success(msg);
                });
        }

        /// <summary>Force-sets Elf attunement on the calling player (testing only) -- writes
        /// both the in-memory live value and the flushed WatchedAttributes value together via
        /// ElfAttunementBehavior.DebugSetAttunement, and evaluates thresholds immediately so
        /// LeafStandingActive reflects the forced value without waiting for the next slow tick.
        /// Root-privileged like rfthew's "set" subcommand -- this bypasses real gain/decay
        /// entirely, at rates where reaching threshold 25 naturally takes hours.</summary>
        private void RegisterAttunementSetCommand(ICoreServerAPI api)
        {
            CommandArgumentParsers parsers = api.ChatCommands.Parsers;

            api.ChatCommands.Create("rfattuneset")
                .WithDescription("Force-set Elf attunement on the calling player (testing only).")
                .RequiresPrivilege(Privilege.root)
                .WithArgs(parsers.Float("value"))
                .HandleWith(args =>
                {
                    IPlayer player = args.Caller.Player;
                    if (player == null)
                        return TextCommandResult.Success("No player context.");

                    var behavior = player.Entity.GetBehavior<ElfAttunementBehavior>();
                    if (behavior == null)
                        return TextCommandResult.Success("ElfAttunementBehavior not attached to this entity (relog after a fresh deploy?).");

                    float value = behavior.DebugSetAttunement((float)args[0]);
                    return TextCommandResult.Success(string.Format("Attunement set to {0:F2}", value));
                });
        }

        /// <summary>Rot aura diagnostics: raw dietsetup rot-intake, elapsed hours since last
        /// dietsetup write, the live decayed value, and the resulting radius/intensity -- confirms the intake-&gt;shape mapping without eating rotten food and waiting to see it change.</summary>
        private void RegisterRotAuraDiagCommand(ICoreServerAPI api)
        {
            api.ChatCommands.Create("rfrotdiag")
                .WithDescription("Dump goblin rot aura diagnostics (rot-intake, decay, resulting radius/intensity) for the calling player")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(args =>
                {
                    IPlayer player = args.Caller.Player;
                    if (player == null)
                        return TextCommandResult.Success("No player context.");

                    var cfg = Config;
                    if (cfg == null)
                        return TextCommandResult.Success("Config not loaded.");

                    Entity entity = player.Entity;
                    var wa = entity.WatchedAttributes;
                    double nowHours = entity.World.Calendar.TotalHours;
                    double lastHours = wa.GetDouble("dietsetup:rotIntakeUpdatedHours", nowHours);
                    double raw = wa.GetDouble("dietsetup:rotIntake", 0.0);
                    double elapsedHours = Math.Max(0.0, nowHours - lastHours);

                    float t = GoblinRotAuraBehavior.ReadLiveRotIntake(entity, cfg);
                    (int radius, float intensity) = GoblinRotAuraBehavior.ComputeShape(cfg, GameMath.Clamp(t, 0f, 1f));

                    string msg = string.Format(
                        "rawRotIntake={0:F4} elapsedHoursSinceWrite={1:F2} liveDecayedIntake={2:F4} -> radius={3} intensity={4:F4} (RadiusMin={5} RadiusMax={6} halfLifeHours={7:F1})",
                        raw, elapsedHours, t, radius, intensity, cfg.GoblinRotAuraRadiusMin, cfg.GoblinRotAuraRadiusMax, cfg.GoblinRotAuraIntakeHalfLifeHours);

                    return TextCommandResult.Success(msg);
                });
        }

        /// <summary>Testing tools for the rot aura. Neither subcommand touches rot-aura game
        /// logic: "registry" reads GoblinRotAuraRegistry's live state; "timescale" wraps vanilla's
        /// IGameCalendar.CalendarSpeedMul so the calendar-hour-driven crop-growth and rot-intake-
        /// decay checks (otherwise real-time-slow) can be observed quickly. Spoilage acceleration
        /// itself is NOT calendar-gated (runs on the real-seconds sweep throttle), so it doesn't need this dial.</summary>
        private void RegisterRotAuraDebugCommand(ICoreServerAPI api)
        {
            CommandArgumentParsers parsers = api.ChatCommands.Parsers;

            api.ChatCommands.Create("rfrotaura")
                .WithDescription("Rot aura testing tools: live registry dump, and a calendar-speed dial so crop-growth/rot-intake-decay tests don't need real-time waiting.")
                .RequiresPrivilege(Privilege.root)
                .BeginSubCommand("registry")
                    .WithDescription("Dump every currently registered AuraSource (entityId, position, radius/intensity, age).")
                    .HandleWith(args =>
                    {
                        if (GoblinRotAuraRegistry.AllSources.Count == 0)
                            return TextCommandResult.Success("No active AuraSource entries.");

                        long nowMs = api.World.ElapsedMilliseconds;
                        var lines = new System.Collections.Generic.List<string>();
                        foreach (var kv in GoblinRotAuraRegistry.AllSources)
                        {
                            AuraSource src = kv.Value;
                            lines.Add(string.Format(
                                "entityId={0} pos=({1},{2},{3}) radius={4} vExtent={5} intensity={6:F4} ageMs={7}",
                                kv.Key, src.Pos.X, src.Pos.Y, src.Pos.Z, src.Radius, src.VerticalHalfExtent, src.Intensity, nowMs - src.UpdatedMs));
                        }
                        return TextCommandResult.Success(string.Join("\n", lines));
                    })
                .EndSubCommand()
                .BeginSubCommand("timescale")
                    .WithDescription("Get/set world.Calendar.CalendarSpeedMul (vanilla, default 0.5). Higher = faster in-game days = faster crop-growth-check and rot-intake-decay testing. Remember to set it back afterward -- this affects the whole server, not just testing.")
                    .WithArgs(parsers.OptionalFloat("mul"))
                    .HandleWith(args =>
                    {
                        if (args.Parsers[0].IsMissing)
                            return TextCommandResult.Success(string.Format("CalendarSpeedMul={0:F2} (vanilla default 0.5)", api.World.Calendar.CalendarSpeedMul));

                        float mul = (float)args[0];
                        api.World.Calendar.CalendarSpeedMul = mul;
                        return TextCommandResult.Success(string.Format("CalendarSpeedMul set to {0:F2}. Remember to set it back to 0.5 (vanilla default) when done testing.", mul));
                    })
                .EndSubCommand();
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

                    string charClass = player.Entity.WatchedAttributes.GetString("characterClass");
                    bool hasClass = charClass != null;

                    bool hasTrait = false;
                    if (hasClass)
                    {
                        var charSys = api.ModLoader.GetModSystem<CharacterSystem>();
                        if (charSys != null)
                            hasTrait = charSys.HasTrait(player, config.DwarfTraitCode);
                    }

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
                    bool hasPositive = charSys != null && charSys.HasTrait(player, "rf-dwarf-positive");
                    bool hasRfNegative = charSys != null && charSys.HasTrait(player, "rf-dwarf-negative");
                    bool hasLrNegative = charSys != null && charSys.HasTrait(player, "dwarf-negative");
                    bool hasElfPositive = charSys != null && charSys.HasTrait(player, config.ElfTraitCode);

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
                        var charSys = api.ModLoader.GetModSystem<CharacterSystem>();
                        bool isOrc = !string.IsNullOrEmpty(charClass) && charSys != null && charSys.HasTrait(player, cfg.OrcTraitCode);

                        var hunger = entity.GetBehavior<EntityBehaviorHunger>();
                        if (hunger == null || hunger.MaxSaturation <= 0f)
                            return TextCommandResult.Success(string.Format("thew={0:F4} orc={1} (no hunger behavior)", thew, isOrc));

                        float satFrac = hunger.Saturation / hunger.MaxSaturation;
                        float rampMult = ThewBehavior.RampMultiplier(satFrac, cfg);
                        bool proteinGated = ThewBehavior.IsProteinGated(hunger, cfg);

                        EnumFoodCategory lastFoodCat = (EnumFoodCategory)entity.Attributes.GetInt(ThewBehavior.LastFoodCategoryKey, (int)EnumFoodCategory.NoNutrition);
                        bool foodTypeBlocksGain = cfg.EnableThewFoodTypeGate && ThewBehavior.IsNonProteinPlantCategory(lastFoodCat);

                        bool gaining = isOrc && proteinGated && rampMult > 0f && !foodTypeBlocksGain;
                        string decayTier = "(none)";
                        if (isOrc && !gaining)
                        {
                            decayTier = satFrac < (float)cfg.ThewRampFloor
                                ? ThewBehavior.DecayTierName(hunger, satFrac, cfg)
                                : "SatedNonProtein";
                        }
                        bool decaying = decayTier != "(none)";

                        string[] extraTraits = entity.WatchedAttributes.GetStringArray("extraTraits", null);
                        string extraTraitsStr = extraTraits == null ? "(null)" : string.Join(",", extraTraits);

                        string bandStr = "(no band behavior)";
                        var bandBhv = entity.GetBehavior<BandBehavior>();
                        if (bandBhv != null)
                        {
                            BandBehavior.Band band = bandBhv.CurrentBand;
                            float actualSize = entity.WatchedAttributes.GetFloat("entitySize", 1f);
                            float targetSize = (float)BandBehavior.Pick(cfg.BandSizes, band);
                            bandStr = string.Format(
                                "band={0} midLerp={1} entitySize={2:F3} targetSize={3:F3} hungerrateMult={4:F2} walkspeedDelta={5:F2} seekRangeDelta={6:F2} maxHpExtra={7:F1} thewGainMult={8:F2}",
                                band, bandBhv.MidLerp, actualSize, targetSize,
                                BandBehavior.Pick(cfg.HungerRateMult, band), BandBehavior.Pick(cfg.WalkSpeedDelta, band),
                                BandBehavior.Pick(cfg.AnimalSeekingRangeDelta, band), BandBehavior.Pick(cfg.MaxHpExtraPoints, band),
                                BandBehavior.Pick(cfg.ThewGainBandMult, band));
                            if (band == BandBehavior.Band.Bulky)
                            {
                                bandStr += string.Format(" +meleeDamage={0:F2} +bulkyHoldDecay={1:F3}/h armorWalkSpeedAffDelta={2:F2}",
                                    cfg.BulkyMeleeDamageBonus, cfg.BulkyHoldDecayPerHour, cfg.BulkyArmorWalkSpeedAffectednessDelta);
                            }
                        }

                        string burnStr = "(no burn behavior)";
                        var burnBhv = entity.GetBehavior<BurnBehavior>();
                        if (burnBhv != null)
                        {
                            var healthBhv = entity.GetBehavior<EntityBehaviorHealth>();
                            string healthStr = healthBhv == null ? "?" : string.Format("{0:F1}/{1:F1}", healthBhv.Health, healthBhv.MaxHealth);
                            float usableThew = Math.Max(0f, thew - (float)cfg.BurnThewFloor);
                            double barsRemaining = cfg.BurnThewPerHp > 0 ? usableThew / (cfg.BurnThewPerHp * BurnBehavior.ReferenceBarHp) : 0.0;
                            burnStr = string.Format(
                                "burnActive={0} health={1} activationGap={2:F2} maxHealPerSec={3:F2} curveExp={4:F1} thewPerHp={5:F3} thewSpentThisBurn={6:F4} barsRemaining={7:F2}",
                                burnBhv.Burning, healthStr, cfg.BurnActivationHealthFracGap, cfg.BurnMaxHealPerSecond, cfg.BurnCurveExponent, cfg.BurnThewPerHp, burnBhv.ThewSpentThisBurn, barsRemaining);
                        }

                        string frenzyStr = "(no frenzy behavior)";
                        var frenzyBhv = entity.GetBehavior<FrenzyBehavior>();
                        if (frenzyBhv != null)
                        {
                            frenzyStr = string.Format(
                                "frenzyActive={0} curveExp={1:F1} maxSpeedBonus={2:F2} maxDamageBonus={3:F2} maxThewPerSec={4:F3} thewSpentThisFrenzy={5:F4}",
                                frenzyBhv.Frenzied, cfg.FrenzyCurveExponent, cfg.FrenzyMaxSpeedBonus, cfg.FrenzyMaxDamageBonus, cfg.FrenzyMaxThewPerSecond, frenzyBhv.ThewSpentThisFrenzy);
                        }

                        string msg = string.Format(
                            "thew={0:F4} orc={1} charClass={2} extraTraits=[{3}] satFrac={4:F3} rampMult={5:F3} (floor {6:F2} ceiling {7:F2}) protein={8:F1} dairy={9:F1} proteinGated={10} (threshold {11:F1}, Protein OR Dairy) lastFoodCategory={12} foodTypeBlocksGain={13} gaining={14} decaying={15} decayTier={16} shieldActive={17} {18} {19} {20}",
                            thew, isOrc, charClass ?? "(null)", extraTraitsStr, satFrac, rampMult, cfg.ThewRampFloor, cfg.ThewRampCeiling, hunger.ProteinLevel, hunger.DairyLevel, proteinGated, cfg.ProteinGateLevel, lastFoodCat, foodTypeBlocksGain, gaining, decaying, decayTier, cfg.StarvationShieldWhileThew && thew > 0f, bandStr, burnStr, frenzyStr);

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
                    .WithDescription("Force-set the calling player's Band directly (testing only) -- bypasses hysteresis, applies stats and starts the entitySize lerp.")
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

                        var hunger = entity.GetBehavior<EntityBehaviorHunger>();
                        string hungerStr = hunger == null
                            ? "(no hunger behavior)"
                            : string.Format(
                                "sat={0:F1}/{1:F1} fruit={2:F1} veg={3:F1} protein={4:F1} grain={5:F1} dairy={6:F1}",
                                hunger.Saturation, hunger.MaxSaturation, hunger.FruitLevel, hunger.VegetableLevel,
                                hunger.ProteinLevel, hunger.GrainLevel, hunger.DairyLevel);

                        string msg = string.Format(
                            "marker={0:F3} entitySize(attr)={1:F3} pmlCurrentSize={2} collisionBox=({3:F3},{4:F3}) clientSize={5:F3} {6}",
                            marker, entitySize, pmlCurrentSize, collisionBox.X, collisionBox.Y, clientSize, hungerStr);

                        return TextCommandResult.Success(msg);
                    })
                .EndSubCommand();
        }
    }
}
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    /// <summary>
    /// Client-only visual cue for ThewBehavior.StateAttributeKey ("rf-orc-state"): a short
    /// particle burst repeated on an interval that scales with state (never the burst size).
    /// State 1 (gaining) renders local-player-only; states 2/3 (debt) render for every nearby
    /// player. Per-tick reposition-and-spawn -- no attach-to-entity option exists in the particle
    /// API (see Entity.OnGameTick's IsOnFire branch, the same pattern OrcSmellModSystem uses).
    /// Reads the raw WatchedAttributes key directly, with no trait check of its own -- the byte
    /// is only ever nonzero for an orc (ThewBehavior clears it on race-swap-away), so any
    /// EntityPlayer carrying it is a valid render target.
    /// </summary>
    public class OrcPuffModSystem : ModSystem
    {
        private ICoreClientAPI? capi;
        private SimpleParticleProperties? puffParticle;
        private static bool loggedException = false;
        private bool disabled = false;

        private readonly Dictionary<long, float> accumByEntity = new Dictionary<long, float>();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            BuildParticleTemplate();
            capi.Event.RegisterGameTickListener(OnGameTick, 100);
        }

        private void BuildParticleTemplate()
        {
            puffParticle = new SimpleParticleProperties
            {
                ParticleModel = EnumParticleModel.Quad,
                Color = ColorUtil.ToRgba(160, 235, 235, 235),
                GravityEffect = 0.1f,
                LifeLength = 0.6f,
                MinSize = 0.15f,
                MaxSize = 0.25f,
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = new Vec3d(),
                AddPos = new Vec3d(0.3, 0.3, 0.3),
                MinVelocity = new Vec3f(-0.15f, 0.1f, -0.15f),
                AddVelocity = new Vec3f(0.3f, 0.25f, 0.3f),
            };
            puffParticle.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -1f);
        }

        private void OnGameTick(float dt)
        {
            if (disabled || capi == null || puffParticle == null) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnablePuff) return;

            try
            {
                EntityPlayer? self = capi.World.Player?.Entity;
                if (self == null) return;

                float range = (float)cfg.PuffRenderRange;
                Entity[] nearby = capi.World.GetEntitiesAround(self.Pos.XYZ, range, range, e => e is EntityPlayer && e.Alive);

                foreach (Entity e in nearby)
                {
                    int state = e.WatchedAttributes.GetInt(ThewBehavior.StateAttributeKey, 0);
                    if (state <= 0)
                    {
                        accumByEntity.Remove(e.EntityId);
                        continue;
                    }

                    bool isSelf = e.EntityId == self.EntityId;
                    if (state == 1 && !isSelf) continue;

                    double dx = e.Pos.X - self.Pos.X;
                    double dy = e.Pos.Y - self.Pos.Y;
                    double dz = e.Pos.Z - self.Pos.Z;
                    double distSq = dx * dx + dy * dy + dz * dz;
                    if (distSq > (double)range * range) continue;

                    double interval = state switch
                    {
                        1 => cfg.PuffIntervalGaining,
                        2 => cfg.PuffIntervalLightDebt,
                        _ => cfg.PuffIntervalHeavyDebt
                    };
                    if (interval <= 0.0) continue;

                    float accum = accumByEntity.TryGetValue(e.EntityId, out float existing) ? existing : 0f;
                    accum += dt;
                    if (accum >= interval)
                    {
                        EmitBurst(cfg, e);
                        accum -= (float)interval;
                    }
                    accumByEntity[e.EntityId] = accum;
                }
            }
            catch (Exception ex)
            {
                disabled = true;
                if (!loggedException)
                {
                    loggedException = true;
                    capi.Logger?.Warning("[rfmechanics] Exception in OrcPuffModSystem, disabling: {0}", ex);
                }
            }
        }

        private void EmitBurst(RFMechanicsConfig cfg, Entity e)
        {
            double eyeY = e is EntityPlayer ep ? ep.LocalEyePos.Y * 0.7 : 1.0;

            puffParticle!.MinPos.Set(e.Pos.X - 0.15, e.Pos.Y + eyeY, e.Pos.Z - 0.15);
            puffParticle.MinQuantity = (float)cfg.PuffParticleCount;

            capi!.World.SpawnParticles(puffParticle);
        }
    }
}

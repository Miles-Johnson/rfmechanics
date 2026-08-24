using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    /// <summary>
    /// Client-side ModSystem owning the spit fly renderer's lifecycle (Phase G4 Step 4).
    /// </summary>
    public class GoblinSpitFliesModSystem : ModSystem
    {
        private GoblinSpitFliesRenderer? renderer;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            renderer = new GoblinSpitFliesRenderer(this, api);
        }

        public override void Dispose()
        {
            renderer?.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// Exact-count spit fly renderer: one instance per spit charge, 0-6, no floor or scaling.
    /// Shape follows RiftRenderer (uploaded mesh, per-instance matrix, camera-facing by zeroing
    /// the model matrix's rotation columns after multiplying in the camera matrix) but not its
    /// shader -- that one samples the primary framebuffer for a screen-space distortion effect
    /// this doesn't need. rfspitflies.vsh/.fsh are a plain textured-quad shader instead.
    /// Registered at AfterBlit to match GoblinDarkvisionModSystem's convention for this mod's
    /// client-only renderers.
    /// Each fly is a crossed pair of quads (2026-08-22 tuning pass), not one -- a single quad
    /// billboard, however well it faces the camera, still reads as a flat cutout because the
    /// silhouette itself never changes with viewing angle; two quads at 90 degrees give it a
    /// real cross-section, same technique vanilla uses for foliage sprites.
    /// </summary>
    internal class GoblinSpitFliesRenderer : IRenderer
    {
        public double RenderOrder => 0.05;
        public int RenderRange => 100;

        private readonly ModSystem ownerMod;
        private readonly ICoreClientAPI capi;
        private readonly MeshRef meshref;
        private readonly Matrixf matrixf = new();
        private IShaderProgram? prog;
        private int flyTexId = -1;
        private static bool loggedException = false;

        private class SpitFlyInstance
        {
            public Vec3d LocalOffset = new();
            public Vec3d TargetOffset = new();
            public double NextRetargetMs;
            public float FadeT;
            public bool Removing;
        }

        private readonly Dictionary<long, List<SpitFlyInstance>> instancesByGoblin = new();

        public GoblinSpitFliesRenderer(ModSystem ownerMod, ICoreClientAPI capi)
        {
            this.ownerMod = ownerMod;
            this.capi = capi;

            MeshData mesh = BuildCrossedQuadMesh();
            meshref = capi.Render.UploadMesh(mesh);

            capi.Event.ReloadShader += LoadShader;
            LoadShader();

            capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "rfspitflies");
        }

        /// <summary>Two quads sharing the same local origin and size: quad 1 in the local XY
        /// plane (Z=0, QuadMeshUtil.GetQuad()'s own layout), quad 2 the same shape rotated 90
        /// degrees about the local Y axis (X,Y,Z) -> (Z,Y,-X), landing in the local YZ plane
        /// (X=0). Same vertex/uv layout the existing rfspitflies.vsh already expects (location 0
        /// = position, location 1 = uv), so the shader needs no change.</summary>
        private static MeshData BuildCrossedQuadMesh()
        {
            MeshData m = new MeshData();

            float[] xyz =
            {
                -1, -1, 0,   1, -1, 0,   1, 1, 0,   -1, 1, 0,
                0, -1, 1,    0, -1, -1,  0, 1, -1,   0, 1, 1,
            };
            m.SetXyz(xyz);

            float[] uv =
            {
                0, 0,  1, 0,  1, 1,  0, 1,
                0, 0,  1, 0,  1, 1,  0, 1,
            };
            m.SetUv(uv);

            m.SetVerticesCount(8);
            m.SetIndices(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 });
            m.SetIndicesCount(12);
            return m;
        }

        private bool LoadShader()
        {
            prog = capi.Shader.NewShaderProgram();
            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
            prog.AssetDomain = ownerMod.Mod.Info.ModID;
            capi.Shader.RegisterFileShaderProgram("rfspitflies", prog);
            return prog.Compile();
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (prog == null) return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableGoblinSpitFlies) return;

                if (flyTexId < 0)
                {
                    flyTexId = capi.Render.GetOrLoadTexture(new AssetLocation("rfmechanics", "entity/rotflies/fly.png"));
                }

                List<EntityPlayer> goblins = GoblinRotFliesShared.GetNearbyGoblins(capi, cfg);
                var seenGoblinIds = new HashSet<long>();
                double nowMs = capi.World.ElapsedMilliseconds;
                Random rand = capi.World.Rand;
                var plrPos = capi.World.Player.Entity.Pos;
                var toDraw = new List<(double x, double y, double z, float fadeT)>();

                float radius = (float)cfg.GoblinSpitFliesRadius;
                float vExtent = (float)cfg.GoblinSpitFliesVerticalExtent;

                foreach (EntityPlayer goblin in goblins)
                {
                    seenGoblinIds.Add(goblin.EntityId);

                    // Polled every frame (not event-driven) so charges present at login/relog show immediately.
                    int charges = GameMath.Clamp(goblin.WatchedAttributes.GetInt("rfmechanics:spitCharges", 0), 0, cfg.SpitChargeCap);

                    if (!instancesByGoblin.TryGetValue(goblin.EntityId, out List<SpitFlyInstance>? instances))
                    {
                        instances = new List<SpitFlyInstance>();
                        instancesByGoblin[goblin.EntityId] = instances;
                    }

                    while (instances.Count < charges)
                    {
                        // Spawn scaled outside the envelope so the lag+fade reads as "arriving", not "appearing".
                        instances.Add(new SpitFlyInstance
                        {
                            NextRetargetMs = nowMs,
                            LocalOffset = SampleUniformInCylinderVolume(rand, radius * 1.5, vExtent * 1.5)
                        });
                    }

                    for (int i = 0; i < instances.Count; i++)
                    {
                        instances[i].Removing = i >= charges;
                    }

                    // Body midpoint, not chest: vertical envelope is symmetric around this point,
                    // and its height above the feet is set equal to the half-extent itself, so a
                    // ~1.8-block-tall goblin's envelope spans roughly floor to head.
                    Vec3d bodyMidPos = goblin.Pos.XYZ;
                    bodyMidPos.Y += vExtent;

                    for (int i = instances.Count - 1; i >= 0; i--)
                    {
                        SpitFlyInstance inst = instances[i];

                        if (nowMs >= inst.NextRetargetMs)
                        {
                            inst.TargetOffset = SampleUniformInCylinderVolume(rand, radius, vExtent);
                            inst.NextRetargetMs = nowMs + cfg.GoblinSpitFliesRetargetSeconds * 1000.0;
                        }

                        double lagFrac = cfg.GoblinSpitFliesLagSeconds <= 0 ? 1.0 : GameMath.Clamp(deltaTime / cfg.GoblinSpitFliesLagSeconds, 0.0, 1.0);
                        inst.LocalOffset += (inst.TargetOffset - inst.LocalOffset) * lagFrac;

                        float fadeStep = cfg.GoblinSpitFliesFadeSeconds <= 0 ? 1f : deltaTime / (float)cfg.GoblinSpitFliesFadeSeconds;
                        inst.FadeT = inst.Removing
                            ? Math.Max(0f, inst.FadeT - fadeStep)
                            : Math.Min(1f, inst.FadeT + fadeStep);

                        if (inst.Removing && inst.FadeT <= 0f)
                        {
                            instances.RemoveAt(i);
                            continue;
                        }

                        toDraw.Add((bodyMidPos.X + inst.LocalOffset.X, bodyMidPos.Y + inst.LocalOffset.Y, bodyMidPos.Z + inst.LocalOffset.Z, inst.FadeT));
                    }
                }

                // Drop tracking for goblins that left the scan range so their instance lists don't leak.
                if (instancesByGoblin.Count > seenGoblinIds.Count)
                {
                    var stale = new List<long>();
                    foreach (long id in instancesByGoblin.Keys)
                    {
                        if (!seenGoblinIds.Contains(id)) stale.Add(id);
                    }
                    foreach (long id in stale) instancesByGoblin.Remove(id);
                }

                if (toDraw.Count == 0) return;

                capi.Render.GlToggleBlend(true);
                prog.Use();
                prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
                prog.BindTexture2D("tex2d", flyTexId, 0);
                prog.Uniform("rgbaTint", new Vec4f(1f, 1f, 1f, 1f));

                float size = (float)cfg.GoblinSpitFliesSize;
                foreach (var (x, y, z, fadeT) in toDraw)
                {
                    RenderFly(x, y, z, plrPos.X, plrPos.Y, plrPos.Z, size, fadeT);
                }

                prog.Stop();
                capi.Render.GlToggleBlend(false);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    capi.Logger?.Warning("[rfmechanics] Exception in GoblinSpitFliesRenderer: {0}", ex);
                }
            }
        }

        private void RenderFly(double x, double y, double z, double camX, double camY, double camZ, float size, float fadeT)
        {
            if (fadeT <= 0f || prog == null) return;

            prog.Uniform("opacity", fadeT);

            matrixf.Identity();
            matrixf.Translate((float)(x - camX), (float)(y - camY), (float)(z - camZ));
            matrixf.ReverseMul(capi.Render.CameraMatrixOriginf);

            // Full spherical billboard: discard the camera-relative rotation entirely (all three
            // columns reset to identity), keep only the translation. RiftRenderer itself only
            // resets columns 0/2 (Values[0,1,2] and [8,9,10]) and leaves column 1 (up,
            // Values[4,5,6]) inherited from the camera matrix -- that reads as a full billboard
            // only because Vintage Story's camera never rolls in normal play, not because it
            // actually is one. Resetting column 1 too removes that assumption.
            matrixf.Values[0] = 1f;
            matrixf.Values[1] = 0f;
            matrixf.Values[2] = 0f;
            matrixf.Values[4] = 0f;
            matrixf.Values[5] = 1f;
            matrixf.Values[6] = 0f;
            matrixf.Values[8] = 0f;
            matrixf.Values[9] = 0f;
            matrixf.Values[10] = 1f;

            matrixf.Scale(size / 2f, size / 2f, size / 2f);

            prog.UniformMatrix("modelViewMatrix", matrixf.Values);
            capi.Render.RenderMesh(meshref);
        }

        /// <summary>Uniform sample through the cylinder volume (sqrt(rand) for horizontal-disk
        /// area uniformity, plain uniform on the vertical axis) -- through the volume, not on a
        /// shell, per the 2026-08-22 tuning pass.</summary>
        private static Vec3d SampleUniformInCylinderVolume(Random rand, double radius, double vExtent)
        {
            double angle = rand.NextDouble() * 2.0 * Math.PI;
            double horizDist = radius * Math.Sqrt(rand.NextDouble());
            double y = (rand.NextDouble() * 2.0 - 1.0) * vExtent;
            return new Vec3d(Math.Cos(angle) * horizDist, y, Math.Sin(angle) * horizDist);
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
            capi.Event.ReloadShader -= LoadShader;
            meshref?.Dispose();
            prog?.Dispose();
        }
    }
}

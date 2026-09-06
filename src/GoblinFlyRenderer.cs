using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics;

/// <summary>Short-lived voxel flies born in world space; never translated with the player.</summary>
internal sealed class GoblinFlyRenderer : IRenderer
{
    public double RenderOrder => 0.39;
    public int RenderRange => 48;
    private readonly ICoreClientAPI capi;
    private readonly MeshRef mesh;
    private readonly int whiteTexture;
    private readonly float[] model = Mat4f.Create();
    private readonly Random random = new();
    private readonly Dictionary<long, Cloud> clouds = new();
    private Vec3d? previousCamera;
    private bool failed;

    private sealed class Fly
    {
        public int Slot;
        public double Phase, Age, Lifetime, SizeScale;
        public Vec3d Spawn = new(), Velocity = new();
        public Vec3d? PreviousPosition;
        public float Alpha;
    }

    private sealed class Cloud
    {
        public Vec3d Body = new();
        public readonly List<Fly> Flies = new();
    }

    private readonly record struct DrawFly(Vec3d Position, double Size, float Alpha, int Dimension);

    internal GoblinFlyRenderer(ICoreClientAPI api)
    {
        capi = api;
        // Own untextured cube, centered at zero. No More Bugs assets or runtime dependency.
        mesh = capi.Render.UploadMesh(CubeMeshUtil.GetCube(0.5f, 0.5f, 0.5f, new Vec3f(-0.5f, -0.5f, -0.5f)));
        whiteTexture = capi.Render.LoadTextureFromRgba(new[] { -1 }, 1, 1, false, 0);
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "rfgoblinflies");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (failed) return;
        try
        {
            var cfg = RFMechanicsModSystem.Config;
            EntityPlayer? self = capi.World.Player?.Entity;
            if (cfg == null || self == null || capi.IsGamePaused || !float.IsFinite(deltaTime) || deltaTime <= 0) return;
            float[] view = capi.Render.CameraMatrixOriginf;
            Vec3d origin = self.CameraPos, camera = GoblinFlyGeometry.Camera(origin, view);
            if (deltaTime > 0.25 || previousCamera != null && camera.SquareDistanceTo(previousCamera) > 64)
            {
                clouds.Clear();
                previousCamera = camera;
            }
            double dt = Math.Min(deltaTime, 0.1);
            Vec3d oldCamera = previousCamera ?? camera;
            previousCamera = camera;
            var goblins = GoblinRotFliesShared.GetNearbyGoblins(capi, cfg);
            goblins.Sort((a, b) => a.Pos.XYZ.SquareDistanceTo(camera).CompareTo(b.Pos.XYZ.SquareDistanceTo(camera)));
            var seen = new HashSet<long>();
            var draw = new List<DrawFly>();
            int budget = Math.Clamp(cfg.GoblinRotFliesRenderCap, 16, 512);
            foreach (EntityPlayer goblin in goblins)
            {
                if (!goblin.Alive || goblin.Pos.Dimension != self.Pos.Dimension) continue;
                GoblinAuraShape shape = GoblinRotAuraState.ReadVisual(goblin);
                // Only ambient flies belong to the aura; charge flies have their own renderer.
                if (!shape.Active) continue;
                seen.Add(goblin.EntityId);
                Vec3d body = goblin.Pos.XYZ;
                double height = Math.Clamp(goblin.CollisionBox?.Y2 ?? 0.9f, 0.3, 3);
                Vec3d target = body.AddCopy(0, height * 0.45, 0);
                if (!clouds.TryGetValue(goblin.EntityId, out Cloud? cloud) || cloud.Body.SquareDistanceTo(body) > 64)
                {
                    cloud = new Cloud { Body = body };
                    clouds[goblin.EntityId] = cloud;
                }
                cloud.Body = body;
                int auraCount = GoblinFlyGeometry.AuraCount(shape, cfg);
                EnsureFlies(cloud, auraCount, target, height, shape, cfg);
                for (int i = cloud.Flies.Count - 1; i >= 0; i--)
                {
                    Fly fly = cloud.Flies[i];
                    bool removing = fly.Slot >= auraCount;
                    const double fadeSeconds = 0.5;
                    fly.Alpha = Math.Clamp(fly.Alpha + (float)(dt / fadeSeconds) * (removing ? -1 : 1), 0, 1);
                    if (removing && fly.Alpha <= 0) { cloud.Flies.RemoveAt(i); continue; }
                    bool waitingForBirth = fly.Age < 0;
                    fly.Age += dt;
                    if (fly.Age < 0) continue;
                    if (waitingForBirth || fly.Age >= fly.Lifetime)
                    {
                        if (removing) { cloud.Flies.RemoveAt(i); continue; }
                        SpawnFly(fly, target, height, shape, cfg);
                    }
                    double size = Safe(cfg.GoblinRotFliesSize, 0.035, 0.01, 0.25) * fly.SizeScale;
                    // Only the birth position uses the player's location. Existing flies
                    // drift independently until transparent, then respawn around the player.
                    Vec3d position = fly.Spawn + fly.Velocity * fly.Age;
                    position.Y += (Math.Sin(fly.Age * 1.4 + fly.Phase) - Math.Sin(fly.Phase)) * 0.025;
                    Vec3d before = fly.PreviousPosition ?? position;
                    fly.PreviousPosition = position;
                    double clearance = Safe(cfg.GoblinRotFliesCameraClearance, 0.8, 0.5, 3) + size;
                    if (GoblinFlyGeometry.CrossesCamera(before - oldCamera, position - camera, clearance)) continue;
                    // Leave the near central view open as well as the camera's immediate surroundings.
                    Vec3d relative = position - origin;
                    double vx = view[0]*relative.X + view[4]*relative.Y + view[8]*relative.Z + view[12];
                    double vy = view[1]*relative.X + view[5]*relative.Y + view[9]*relative.Z + view[13];
                    double vz = view[2]*relative.X + view[6]*relative.Y + view[10]*relative.Z + view[14];
                    if (vz < 0 && vz > -2.5 && vx*vx + vy*vy < vz*vz * 0.16) continue;
                    Vec3d local = position - body;
                    var box = goblin.CollisionBox;
                    if (box != null && local.X >= box.X1 - size && local.X <= box.X2 + size
                        && local.Y >= box.Y1 - size && local.Y <= box.Y2 + size
                        && local.Z >= box.Z1 - size && local.Z <= box.Z2 + size) continue;
                    var pos = new BlockPos((int)Math.Floor(position.X), (int)Math.Floor(position.Y),
                        (int)Math.Floor(position.Z), goblin.Pos.Dimension);
                    var block = capi.World.BlockAccessor.GetBlock(pos);
                    if (block.Id != 0 && block.GetCollisionBoxes(capi.World.BlockAccessor, pos)?.Length > 0) continue;
                    double distance = position.DistanceTo(camera);
                    double lifetimeFade = Math.Min(0.5, fly.Lifetime * 0.3);
                    float envelope = GoblinFlyGeometry.SmoothFade(fly.Age / lifetimeFade)
                        * GoblinFlyGeometry.SmoothFade((fly.Lifetime - fly.Age) / lifetimeFade);
                    float alpha = fly.Alpha * envelope * GoblinFlyGeometry.Opacity(shape, cfg)
                        * (float)Math.Clamp((32 - distance) / 8, 0, 1)
                        * (float)Math.Clamp((distance - clearance) / 0.35, 0, 1);
                    if (draw.Count < budget && alpha > 0.01f) draw.Add(new(position, size, alpha, goblin.Pos.Dimension));
                }
            }
            var stale = new List<long>();
            foreach (long id in clouds.Keys) if (!seen.Contains(id)) stale.Add(id);
            foreach (long id in stale) clouds.Remove(id);
            Render(draw, origin, camera);
        }
        catch (Exception ex)
        {
            failed = true;
            clouds.Clear();
            capi.Logger.Error("[rfmechanics] Goblin fly renderer disabled: {0}", ex);
        }
    }

    private static double Safe(double value, double fallback, double min, double max) => GoblinAuraMath.FiniteClamp(value, fallback, min, max);

    private void EnsureFlies(Cloud cloud, int count, Vec3d target, double height,
        GoblinAuraShape shape, RFMechanicsConfig cfg)
    {
        var existing = new HashSet<int>();
        foreach (Fly fly in cloud.Flies) existing.Add(fly.Slot);
        for (int i = 0; i < count; i++)
        {
            if (existing.Contains(i)) continue;
            var fly = new Fly { Slot = i };
            SpawnFly(fly, target, height, shape, cfg);
            // Stagger initial births; there must be no cloud-wide pulse or reset.
            fly.Age = -random.NextDouble() * fly.Lifetime;
            cloud.Flies.Add(fly);
        }
    }

    private void SpawnFly(Fly fly, Vec3d target, double height, GoblinAuraShape shape, RFMechanicsConfig cfg)
    {
        double radius = shape.Radius;
        // A quarter identify the source; a quarter suggest the edge; the rest fill the area.
        double radial = fly.Slot % 4 == 0 ? Math.Min(0.65, radius * 0.8) * (0.8 + random.NextDouble() * 0.2)
            : radius * (fly.Slot % 4 == 1 ? 0.78 + random.NextDouble() * 0.17 : 0.2 + random.NextDouble() * 0.55);
        double angle = random.NextDouble() * Math.PI * 2;
        double vertical = Math.Min(shape.VerticalHalfExtent * 0.65, height * 0.7 + shape.Radius * 0.025);
        fly.Spawn = target.AddCopy(Math.Cos(angle) * radial, (random.NextDouble() * 2 - 1) * vertical, Math.Sin(angle) * radial);
        double direction = random.NextDouble() * Math.PI * 2;
        double speed = Safe(cfg.GoblinRotFliesSpeed, 0.12, 0, 2);
        fly.Velocity = new Vec3d(Math.Cos(direction) * speed, (random.NextDouble() - 0.5) * speed * 0.3, Math.Sin(direction) * speed);
        fly.Phase = random.NextDouble() * Math.PI * 2;
        fly.SizeScale = 0.85 + random.NextDouble() * 0.3;
        fly.Lifetime = Safe(cfg.GoblinRotFliesLifeSeconds, 2, 0.5, 5) * (0.75 + random.NextDouble() * 0.5);
        fly.Age = 0;
        fly.PreviousPosition = null;
    }

    private void Render(List<DrawFly> flies, Vec3d origin, Vec3d camera)
    {
        if (flies.Count == 0) return;
        flies.Sort((a, b) => b.Position.SquareDistanceTo(camera).CompareTo(a.Position.SquareDistanceTo(camera)));
        var render = capi.Render;
        IShaderProgram previous = render.CurrentActiveShader;
        previous?.Stop();
        IStandardShaderProgram? shader = null;
        render.GlToggleBlend(true);
        render.GLEnableDepthTest();
        render.GLDepthMask(false);
        try
        {
            shader = render.PreparedStandardShader((int)camera.X, (int)camera.Y, (int)camera.Z);
            shader.Tex2D = whiteTexture;
            shader.ViewMatrix = render.CameraMatrixOriginf;
            shader.ProjectionMatrix = render.CurrentProjectionMatrix;
            shader.DontWarpVertices = 1;
            shader.NormalShaded = 1;
            shader.AlphaTest = 0.01f;
            shader.Oit = false;
            foreach (DrawFly fly in flies)
            {
                Vec3d p = fly.Position - origin;
                Mat4f.Identity(model);
                Mat4f.Translate(model, model, (float)p.X, (float)p.Y, (float)p.Z);
                Mat4f.Scale(model, model, (float)fly.Size, (float)(fly.Size * 0.7), (float)fly.Size);
                shader.ModelMatrix = model;
                shader.RgbaTint = new Vec4f(0.11f, 0.13f, 0.055f, fly.Alpha);
                shader.RgbaLightIn = capi.World.BlockAccessor.GetLightRGBs(new BlockPos(
                    (int)Math.Floor(fly.Position.X), (int)Math.Floor(fly.Position.Y), (int)Math.Floor(fly.Position.Z), fly.Dimension));
                render.RenderMesh(mesh);
            }
        }
        finally
        {
            shader?.Stop();
            render.GLDepthMask(true);
            render.GlToggleBlend(false);
            previous?.Use();
        }
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        clouds.Clear();
        mesh.Dispose();
        capi.Render.GLDeleteTexture(whiteTexture);
    }
}

using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics;

public class GoblinSpitFliesModSystem : ModSystem
{
    private GoblinSpitFliesRenderer? renderer;
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api) => renderer = new GoblinSpitFliesRenderer(api);
    public override void Dispose() { renderer?.Dispose(); base.Dispose(); }
}

/// <summary>One persistent original winged sprite per charge, independent of aura recovery.</summary>
internal sealed class GoblinSpitFliesRenderer : IRenderer
{
    public double RenderOrder => 0.40;
    public int RenderRange => 48;
    private readonly ICoreClientAPI capi;
    private readonly MeshRef mesh;
    private readonly Matrixf matrix = new();
    private readonly Random random = new();
    private readonly Dictionary<long, Swarm> swarms = new();
    private IShaderProgram? shader;
    private int texture = -1;
    private Vec3d? previousCamera;
    private bool failed;

    private sealed class Fly
    {
        public Vec3d Position = new(), Velocity = new(), Target = new();
        public double Retarget, Clock, Phase, Speed;
    }

    private sealed class Swarm
    {
        public Vec3d LastHome = new();
        public int Dimension;
        public readonly List<Fly> Flies = new();
    }

    internal GoblinSpitFliesRenderer(ICoreClientAPI api)
    {
        capi = api;
        // Original crossed-quad model and original fly.png, including its tiny wings.
        var data = new MeshData();
        data.SetXyz(new float[] { -1,-1,0, 1,-1,0, 1,1,0, -1,1,0, 0,-1,1, 0,-1,-1, 0,1,-1, 0,1,1 });
        data.SetUv(new float[] { 0,0, 1,0, 1,1, 0,1, 0,0, 1,0, 1,1, 0,1 });
        data.SetVerticesCount(8);
        data.SetIndices(new[] { 0,1,2, 0,2,3, 4,5,6, 4,6,7 });
        data.SetIndicesCount(12);
        mesh = api.Render.UploadMesh(data);
        capi.Event.ReloadShader += LoadShader;
        LoadShader();
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "rfspitflies");
    }

    private bool LoadShader()
    {
        shader?.Dispose();
        shader = capi.Shader.NewShaderProgram();
        shader.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
        shader.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
        shader.AssetDomain = "rfmechanics";
        capi.Shader.RegisterFileShaderProgram("rfspitflies", shader);
        return shader.Compile();
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (failed || shader == null) return;
        try
        {
            var cfg = RFMechanicsModSystem.Config;
            var self = capi.World.Player?.Entity;
            if (cfg == null || self == null || capi.IsGamePaused || !float.IsFinite(deltaTime) || deltaTime <= 0) return;
            if (!cfg.EnableGoblinSpitFlies) { swarms.Clear(); return; }
            Vec3d origin = self.CameraPos;
            Vec3d camera = GoblinFlyGeometry.Camera(origin, capi.Render.CameraMatrixOriginf);
            Vec3d oldCamera = previousCamera ?? camera;
            previousCamera = camera;
            double dt = Math.Min(deltaTime, 0.1);
            double size = Safe(cfg.GoblinSpitFliesSize, 0.06, 0.01, 0.25);
            var seen = new HashSet<long>();
            var draw = new List<Vec3d>();
            foreach (EntityPlayer goblin in GoblinRotFliesShared.GetNearbyGoblins(capi, cfg, chargesOnly: true))
            {
                seen.Add(goblin.EntityId);
                double height = Math.Clamp(goblin.CollisionBox?.Y2 ?? 0.9, 0.3, 3);
                Vec3d home = goblin.Pos.XYZ.AddCopy(0, height * 0.6, 0);
                double radius = Safe(cfg.GoblinSpitFliesRadius, 1.1, 0.4, 2);
                double vertical = Math.Min(height * 0.4, Safe(cfg.GoblinSpitFliesVerticalExtent, 0.45, 0.1, 1));
                if (!swarms.TryGetValue(goblin.EntityId, out Swarm? swarm)
                    || swarm.Dimension != goblin.Pos.Dimension || home.SquareDistanceTo(swarm.LastHome) > 64)
                {
                    swarm = new Swarm { Dimension = goblin.Pos.Dimension };
                    swarms[goblin.EntityId] = swarm;
                }
                swarm.LastHome = home;
                int count = Math.Clamp(goblin.WatchedAttributes.GetInt("rfmechanics:spitCharges"), 0, 32);
                // No lifetime, opacity animation, aura-level scaling or population thinning.
                if (swarm.Flies.Count > count) swarm.Flies.RemoveRange(count, swarm.Flies.Count - count);
                while (swarm.Flies.Count < count)
                {
                    var fly = new Fly { Phase = random.NextDouble() * Math.PI * 2 };
                    fly.Position = ChooseTarget(home, radius, vertical, goblin, size);
                    swarm.Flies.Add(fly);
                }
                foreach (Fly fly in swarm.Flies)
                {
                    Vec3d before = fly.Position;
                    Move(fly, swarm, goblin, home, radius, vertical, size, dt, cfg);
                    double clearance = Safe(cfg.GoblinRotFliesCameraClearance, 0.8, 0.5, 3) + size;
                    // Geometry/camera occlusion only; never fade a charge to signal aura strength.
                    if (GoblinFlyGeometry.CrossesCamera(before - oldCamera, fly.Position - camera, clearance)) continue;
                    if (!Passable(fly.Position, goblin, size)) continue;
                    draw.Add(fly.Position);
                }
            }
            var stale = new List<long>();
            foreach (long id in swarms.Keys) if (!seen.Contains(id)) stale.Add(id);
            foreach (long id in stale) swarms.Remove(id);
            Render(draw, origin, camera, (float)size);
        }
        catch (Exception ex)
        {
            failed = true;
            capi.Logger.Error("[rfmechanics] Spit-charge fly renderer disabled: {0}", ex);
        }
    }

    private void Move(Fly fly, Swarm swarm, EntityPlayer goblin, Vec3d home, double radius,
        double vertical, double size, double dt, RFMechanicsConfig cfg)
    {
        fly.Clock += dt;
        fly.Retarget -= dt;
        double distance = fly.Position.DistanceTo(home);
        if (fly.Retarget <= 0 || fly.Position.SquareDistanceTo(fly.Target) < 0.025 || distance > radius * 1.5)
        {
            fly.Target = ChooseTarget(home, radius, vertical, goblin, size);
            fly.Speed = Safe(cfg.GoblinSpitFliesSpeed, 2.2, 0.1, 5) * (0.65 + random.NextDouble() * 0.7);
            fly.Retarget = Safe(cfg.GoblinSpitFliesRetargetSeconds, 0.6, 0.1, 5) * (0.75 + random.NextDouble() * 0.5);
        }
        Vec3d direction = fly.Target - fly.Position;
        double length = direction.Length();
        if (length > 0.001) direction *= 1 / length;
        // Independent steering, mild lateral weaving and separation keep a readable swarm.
        double weave = Math.Sin(fly.Clock * 2.0 + fly.Phase) * 0.22;
        direction += new Vec3d(-direction.Z * weave, Math.Sin(fly.Clock * 1.6 + fly.Phase) * 0.12, direction.X * weave);
        foreach (Fly other in swarm.Flies)
        {
            if (ReferenceEquals(fly, other)) continue;
            Vec3d away = fly.Position - other.Position;
            double separation = away.Length();
            if (separation > 0.001 && separation < 0.22) direction += away * ((0.22 - separation) / (0.22 * separation));
        }
        length = direction.Length();
        if (length > 0.001) direction *= 1 / length;
        // Catch up under locomotion by steering faster, never translating with the model.
        double speed = Math.Min(6, fly.Speed + Math.Max(0, distance - radius * 1.5) * 2);
        fly.Velocity += (direction * speed - fly.Velocity) * (1 - Math.Exp(-dt * 7));
        // Small steps prevent tunnelling through a wall during catch-up or a slow frame.
        int steps = Math.Max(1, (int)Math.Ceiling(fly.Velocity.Length() * dt / 0.04));
        for (int step = 0; step < steps; step++)
        {
            Vec3d next = fly.Position + fly.Velocity * (dt / steps);
            if (Passable(next, goblin, size)) { fly.Position = next; continue; }
            fly.Velocity *= -0.5;
            fly.Retarget = 0;
            break;
        }
    }

    private Vec3d ChooseTarget(Vec3d home, double radius, double vertical, EntityPlayer goblin, double size)
    {
        Vec3d result = home;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            double angle = random.NextDouble() * Math.PI * 2;
            double reach = radius * (0.6 + random.NextDouble() * 0.4);
            result = home.AddCopy(Math.Cos(angle) * reach, (random.NextDouble() * 2 - 1) * vertical, Math.Sin(angle) * reach);
            if (Passable(result, goblin, size)) return result;
        }
        return result;
    }

    private bool Passable(Vec3d point, EntityPlayer goblin, double size)
    {
        Vec3d local = point - goblin.Pos.XYZ;
        var box = goblin.CollisionBox;
        if (box != null && local.X >= box.X1 - size && local.X <= box.X2 + size
            && local.Y >= box.Y1 - size && local.Y <= box.Y2 + size
            && local.Z >= box.Z1 - size && local.Z <= box.Z2 + size) return false;
        var pos = new BlockPos((int)Math.Floor(point.X), (int)Math.Floor(point.Y), (int)Math.Floor(point.Z), goblin.Pos.Dimension);
        var block = capi.World.BlockAccessor.GetBlock(pos);
        return block.Id == 0 || block.GetCollisionBoxes(capi.World.BlockAccessor, pos)?.Length is not > 0;
    }

    private void Render(List<Vec3d> flies, Vec3d origin, Vec3d camera, float size)
    {
        if (flies.Count == 0 || shader == null) return;
        texture = texture < 0 ? capi.Render.GetOrLoadTexture(new AssetLocation("rfmechanics", "entity/rotflies/fly.png")) : texture;
        flies.Sort((a, b) => b.SquareDistanceTo(camera).CompareTo(a.SquareDistanceTo(camera)));
        var render = capi.Render;
        var previous = render.CurrentActiveShader;
        previous?.Stop();
        render.GLEnableDepthTest();
        render.GLDepthMask(false);
        render.GlToggleBlend(true);
        try
        {
            shader.Use();
            shader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
            shader.BindTexture2D("tex2d", texture, 0);
            shader.Uniform("rgbaTint", new Vec4f(1, 1, 1, 1));
            shader.Uniform("opacity", 1f);
            foreach (Vec3d point in flies)
            {
                Vec3d p = point - origin;
                matrix.Identity().Translate((float)p.X, (float)p.Y, (float)p.Z);
                matrix.ReverseMul(render.CameraMatrixOriginf);
                // Original spherical billboard orientation, preserving the crossed silhouette.
                matrix.Values[0] = matrix.Values[5] = matrix.Values[10] = 1;
                matrix.Values[1] = matrix.Values[2] = matrix.Values[4] = 0;
                matrix.Values[6] = matrix.Values[8] = matrix.Values[9] = 0;
                matrix.Scale(size / 2, size / 2, size / 2);
                shader.UniformMatrix("modelViewMatrix", matrix.Values);
                render.RenderMesh(mesh);
            }
        }
        finally
        {
            shader.Stop();
            render.GLDepthMask(true);
            render.GlToggleBlend(false);
            previous?.Use();
        }
    }

    private static double Safe(double value, double fallback, double min, double max) => GoblinAuraMath.FiniteClamp(value, fallback, min, max);

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        capi.Event.ReloadShader -= LoadShader;
        swarms.Clear();
        mesh.Dispose();
        shader?.Dispose();
    }
}

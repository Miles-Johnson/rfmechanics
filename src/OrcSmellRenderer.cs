using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace rfmechanics;

// Owns particles until death. Vanilla's spawn-only API cannot enforce a moving body exclusion.
internal sealed class OrcSmellRenderer : IRenderer
{
    internal const int Capacity = 1200;
    private sealed class Wisp
    {
        internal Vec3d Position = new();
        internal double Age, Life, Speed, Phase, Angle;
        internal float Size, Alpha;
        internal int R, G, B;
        internal long SourceId;
    }
    private readonly ICoreClientAPI capi;
    private readonly List<Wisp> wisps = new();
    private readonly MeshData mesh = new(Capacity * 4, Capacity * 6, false, true, true, false);
    private MeshRef? meshRef;
    private IShaderProgram? shader;
    private Vec3d? previousBody, previousCamera;
    private Cuboidf? previousBox;
    private float release = 1;
    private bool failed;
    private int trailGeneration;
    public double RenderOrder => 0.06;
    public int RenderRange => 256;
    internal int Count => wisps.Count;

    internal OrcSmellRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        capi.Event.ReloadShader += LoadShader;
        LoadShader();
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "rforcsmellwisps");
    }

    private bool LoadShader()
    {
        shader?.Dispose();
        shader = capi.Shader.NewShaderProgram();
        shader.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
        shader.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
        shader.AssetDomain = "rfmechanics";
        capi.Shader.RegisterFileShaderProgram("rforcsmell", shader);
        bool ok = shader.Compile();
        failed = !ok;
        return ok;
    }

    internal void Add(long sourceId, int sourceCount, Vec3d position, double angle, double speed, double life, float size, float alpha, int[] rgb)
    {
        if (failed || !capi.Settings.Bool["renderParticles"] || wisps.Count >= Capacity) return;
        int limit = Math.Clamp(Capacity * capi.Settings.Int["particleLevel"] / 100, 0, Capacity);
        int sourceLimit = limit / Math.Max(1, sourceCount), sourceAlive = 0;
        foreach (Wisp w in wisps) if (w.SourceId == sourceId) sourceAlive++;
        if (wisps.Count >= limit || sourceAlive >= sourceLimit) return;
        wisps.Add(new Wisp { Position = position, Angle = angle, Speed = speed, Life = life,
            Size = size, Alpha = alpha, R = rgb[0], G = rgb[1], B = rgb[2], SourceId = sourceId,
            Phase = capi.World.Rand.NextDouble()*Math.PI*2 });
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (failed) return;
        try
        {
            var cfg = RFMechanicsModSystem.Config;
            var self = capi.World.Player?.Entity;
            if (cfg == null || !cfg.SmellEnabled || self == null || !self.Alive || !capi.Settings.Bool["renderParticles"])
            {
                wisps.Clear(); previousBody = previousCamera = null; previousBox = null;
                return;
            }
            if (trailGeneration != OrcSmellShared.TrailGeneration)
            {
                // Walking immediately discards information acquired at the full stationary range.
                wisps.Clear();
                trailGeneration = OrcSmellShared.TrailGeneration;
            }
            if (capi.IsGamePaused) return;
            double dt = Math.Max(0, deltaTime);
            // A long rendering stall or teleport invalidates the old geometry. Don't replay a cloud.
            Vec3d body = self.Pos.XYZ, renderOrigin = self.CameraPos;
            float[] view = capi.Render.CameraMatrixOriginf;
            // CameraPos is the interpolated feet/reference position, not the optical camera.
            // Invert the rigid view transform, including eye height and third-person offset.
            Vec3d camera = renderOrigin.AddCopy(
                -(view[0]*view[12] + view[1]*view[13] + view[2]*view[14]),
                -(view[4]*view[12] + view[5]*view[13] + view[6]*view[14]),
                -(view[8]*view[12] + view[9]*view[13] + view[10]*view[14]));
            if (dt > 0.25 || (previousBody != null && body.SquareDistanceTo(previousBody) > 4)) wisps.Clear();
            Vec3d oldBody = previousBody ?? body, oldCamera = previousCamera ?? camera;
            Cuboidf box = self.CollisionBox;
            // Union of previous/current local bounds also covers crouch and size transitions.
            Cuboidf sweptBox = previousBox == null ? box : new Cuboidf(
                Math.Min(box.X1, previousBox.X1), Math.Min(box.Y1, previousBox.Y1), Math.Min(box.Z1, previousBox.Z1),
                Math.Max(box.X2, previousBox.X2), Math.Max(box.Y2, previousBox.Y2), Math.Max(box.Z2, previousBox.Z2));
            Vec3d eye = body.AddCopy(self.LocalEyePos.X, self.LocalEyePos.Y, self.LocalEyePos.Z);
            release = OrcSmellShared.FocusActive ? 1 : Math.Max(0, release - (float)dt / 0.2f);
            if (release <= 0) wisps.Clear();
            for (int i = wisps.Count - 1; i >= 0; i--)
            {
                Wisp w = wisps[i];
                w.Age += dt;
                Vec3d toward = eye - w.Position;
                double distance = toward.Length();
                double ease = Math.Clamp((distance - cfg.SmellArrivalRadius) / 2, 0.35, 1);
                double sway = cfg.SmellSwayAmplitude * Math.Clamp((distance - cfg.SmellArrivalRadius) / 2, 0, 1);
                double phase = w.Phase + w.Age * 1.8;
                double step = w.Speed * ease * dt / Math.Max(0.01, distance);
                Vec3d next = w.Position.AddCopy(toward.X * step - Math.Sin(w.Angle)*Math.Cos(phase)*sway*dt,
                    toward.Y * step + Math.Sin(phase)*sway*dt,
                    toward.Z * step + Math.Cos(w.Angle)*Math.Cos(phase)*sway*dt);
                // Circumscribed quad radius plus margin: even its corners remain outside the body.
                double margin = w.Size * 0.7072 + 0.12;
                bool blocked = OrcSmellGeometry.CrossesBox(w.Position - oldBody, next - body, sweptBox, margin)
                    || OrcSmellGeometry.CrossesSphere(w.Position - oldCamera, next - camera, cfg.SmellArrivalRadius + margin);
                if (w.Age >= w.Life || blocked)
                {
                    wisps.RemoveAt(i);
                    continue;
                }
                w.Position = next;
            }
            previousBody = body; previousCamera = camera; previousBox = box.Clone();
            if (wisps.Count == 0 || shader == null) return;
            // Standard alpha compositing, far to near; positions stay in world space when looking around.
            wisps.Sort((a, b) => b.Position.SquareDistanceTo(camera).CompareTo(a.Position.SquareDistanceTo(camera)));
            mesh.Clear();
            foreach (Wisp w in wisps)
            {
                double margin = w.Size * 0.7072 + 0.12;
                double clearance = Math.Min(OrcSmellGeometry.DistanceToBox(w.Position - body, box) - margin,
                    w.Position.DistanceTo(camera) - cfg.SmellArrivalRadius - margin);
                float alpha = w.Alpha * release * OrcSmellGeometry.Smooth((float)(w.Age / 0.3))
                    * OrcSmellGeometry.Smooth((float)((w.Life - w.Age) / 0.8))
                    * OrcSmellGeometry.Smooth((float)(clearance / 0.65));
                if (alpha < 0.003) continue;
                Vec3d p = w.Position - renderOrigin;
                float x = (float)(view[0]*p.X + view[4]*p.Y + view[8]*p.Z + view[12]);
                float y = (float)(view[1]*p.X + view[5]*p.Y + view[9]*p.Z + view[13]);
                float z = (float)(view[2]*p.X + view[6]*p.Y + view[10]*p.Z + view[14]);
                float half = w.Size / 2;
                int color = OrcSmellVisuals.MeshColor(w.R, w.G, w.B, alpha), start = mesh.VerticesCount;
                mesh.AddVertex(x-half, y-half, z, 0, 0, color);
                mesh.AddVertex(x+half, y-half, z, 1, 0, color);
                mesh.AddVertex(x+half, y+half, z, 1, 1, color);
                mesh.AddVertex(x-half, y+half, z, 0, 1, color);
                mesh.AddIndex(start); mesh.AddIndex(start+1); mesh.AddIndex(start+2);
                mesh.AddIndex(start); mesh.AddIndex(start+2); mesh.AddIndex(start+3);
            }
            if (mesh.VerticesCount == 0) return;
            if (meshRef == null)
            {
                int vertices = mesh.VerticesCount, indices = mesh.IndicesCount;
                mesh.VerticesCount = Capacity*4; mesh.IndicesCount = Capacity*6;
                meshRef = capi.Render.UploadMesh(mesh);
                mesh.VerticesCount = vertices; mesh.IndicesCount = indices;
            }
            capi.Render.UpdateMesh(meshRef, mesh);
            IShaderProgram previousShader = capi.Render.CurrentActiveShader;
            previousShader?.Stop();
            capi.Render.GlToggleBlend(true);
            capi.Render.GLEnableDepthTest();
            capi.Render.GLDepthMask(false);
            try
            {
                shader.Use();
                shader.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
                capi.Render.RenderMesh(meshRef);
            }
            finally
            {
                shader.Stop();
                capi.Render.GLDepthMask(true);
                capi.Render.GlToggleBlend(false);
                previousShader?.Use();
            }
        }
        catch (Exception ex)
        {
            failed = true; wisps.Clear();
            capi.Logger.Error("[rfmechanics] Orc smell renderer disabled: {0}", ex);
        }
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
        capi.Event.ReloadShader -= LoadShader;
        wisps.Clear(); meshRef?.Dispose(); shader?.Dispose();
    }
}

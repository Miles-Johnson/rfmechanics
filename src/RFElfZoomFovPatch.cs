using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.Client.NoObf;

namespace rfmechanics
{
    /// <summary>
    /// Read-modify-write FOV transpiler on ClientMain.Set3DProjection(float zfar, float fov),
    /// mirroring Spyglass 0.6.1's own Set3DProjection patch exactly (same anchor -- arg index 2
    /// is "fov" on this instance method -- same instruction sequence: Ldarg_2 / call / Starg_S)
    /// rather than inventing a different insertion point. Harmony chains transpilers targeting
    /// the same method sequentially, and two pure multiplications of the same parameter compose
    /// correctly regardless of application order (fov * spyglassMult * ourMult), so matching
    /// Spyglass's proven, pattern-independent splice keeps composition with it safe.
    /// </summary>
    [HarmonyPatch(typeof(ClientMain), nameof(ClientMain.Set3DProjection), new[] { typeof(float), typeof(float) })]
    public static class RFElfZoomFovPatch
    {
        public static float AdjustFov(float fov)
        {
            return fov * RFElfZoomBehavior.CurrentFovMult;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call, typeof(RFElfZoomFovPatch).GetMethod(nameof(AdjustFov), BindingFlags.Static | BindingFlags.Public)),
                new CodeInstruction(OpCodes.Starg_S, (object)2)
            };
            list.AddRange(instructions);
            return list;
        }
    }
}

using Monolith.Core;
using UnityEngine;

namespace Monolith.Hud
{
    /// <summary>
    /// The cabin collapses the HUD into its own displays when the pilot gets up (<see cref="GameWorld.Cabin"/>'s HudCollapse): the line graphics shrink toward the bottom
    /// centre of the screen, where the dash is, and fade. Both HUD passes ask this for the matrix to draw with and the fade to apply; with no cabin it changes nothing.
    /// </summary>
    public static class HudCollapse
    {
        public static readonly int DimId = Shader.PropertyToID("_MN_Dim");

        /// <summary>0 (the HUD as it is) .. 1 (gone).</summary>
        public static float Amount => Mathf.Clamp01(GameWorld.Current?.Cabin?.HudCollapse ?? 0f);

        /// <summary>The pass's clip matrix for a screen of w x h pixels, with the collapse applied.</summary>
        public static Matrix4x4 Matrix(float w, float h, float collapse)
        {
            var ortho = Matrix4x4.Ortho(0f, w, h, 0f, -1f, 1f);
            if (collapse > 0.001f)
            {
                float sc = Mathf.Lerp(1f, 0.30f, collapse);
                var pivot = new Vector3(w * 0.5f, h * 0.96f, 0f);
                ortho *= Matrix4x4.Translate(pivot) * Matrix4x4.Scale(new Vector3(sc, sc, 1f)) * Matrix4x4.Translate(-pivot);
            }
            return GL.GetGPUProjectionMatrix(ortho, true);
        }

        /// <summary>The fade for a collapse: the lines are half gone a third of the way in and gone by two thirds.</summary>
        public static float Dim(float collapse) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(collapse * 1.5f));
    }
}

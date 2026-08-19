using UnityEngine;

namespace TacticalSoccer.Editor
{
    /// <summary>
    /// Raw Color32-buffer drawing primitives: filled rects, rect outlines,
    /// ring outlines. Used to paint the pitch markings directly into a
    /// Texture2D's pixel buffer instead of building them out of meshes (see
    /// TestEnvironmentGenerator.DrawPenaltyAreas for why — briefly, so a line
    /// marking can never z-fight with the grass or catch a route raycast).
    ///
    /// Pulled out of TestEnvironmentGenerator because none of this knows
    /// anything about football: it is generic pixel-buffer math that any
    /// scene-generation code could reuse, and it does not belong in a
    /// 4000+-line file whose actual job is building the test scene.
    /// </summary>
    public static class TextureDrawing
    {
        public static void FillRect(Color32[] pixels, int texWidth, int texHeight,
            int x0, int y0, int x1, int y1, Color32 color)
        {
            x0 = Mathf.Clamp(x0, 0, texWidth);
            x1 = Mathf.Clamp(x1, 0, texWidth);
            y0 = Mathf.Clamp(y0, 0, texHeight);
            y1 = Mathf.Clamp(y1, 0, texHeight);

            for (int y = y0; y < y1; y++)
            {
                int rowStart = y * texWidth;
                for (int x = x0; x < x1; x++)
                {
                    pixels[rowStart + x] = color;
                }
            }
        }

        public static void DrawRectOutline(Color32[] pixels, int texWidth, int texHeight,
            int x0, int y0, int x1, int y1, int thickness, Color32 color)
        {
            FillRect(pixels, texWidth, texHeight, x0, y0, x1, y0 + thickness, color);
            FillRect(pixels, texWidth, texHeight, x0, y1 - thickness, x1, y1, color);
            FillRect(pixels, texWidth, texHeight, x0, y0, x0 + thickness, y1, color);
            FillRect(pixels, texWidth, texHeight, x1 - thickness, y0, x1, y1, color);
        }

        public static void DrawCircleOutline(Color32[] pixels, int texWidth, int texHeight,
            int centerX, int centerY, int radius, int thickness, Color32 color)
        {
            float halfThickness = thickness * 0.5f;
            float innerRadius = radius - halfThickness;
            float outerRadius = radius + halfThickness;
            float innerSqr = innerRadius * innerRadius;
            float outerSqr = outerRadius * outerRadius;

            int minX = Mathf.Max(0, centerX - radius - thickness);
            int maxX = Mathf.Min(texWidth, centerX + radius + thickness);
            int minY = Mathf.Max(0, centerY - radius - thickness);
            int maxY = Mathf.Min(texHeight, centerY + radius + thickness);

            for (int y = minY; y < maxY; y++)
            {
                int dy = y - centerY;
                int rowStart = y * texWidth;

                for (int x = minX; x < maxX; x++)
                {
                    int dx = x - centerX;
                    float distanceSqr = (dx * dx) + (dy * dy);

                    if (distanceSqr >= innerSqr && distanceSqr <= outerSqr)
                    {
                        pixels[rowStart + x] = color;
                    }
                }
            }
        }
    }
}

using UnityEngine;

namespace TacticalSoccer.Editor
{
    // Funciones básicas para dibujar formas directamente sobre un buffer de píxeles.
    public static class TextureDrawing
    {
        // Rellena un rectángulo de color en el buffer de píxeles.
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

        // Rellena un círculo de color en el buffer de píxeles.
        public static void FillCircle(Color32[] pixels, int texWidth, int texHeight,
            int centerX, int centerY, int radius, Color32 color)
        {
            float radiusSqr = radius * radius;

            int minX = Mathf.Max(0, centerX - radius);
            int maxX = Mathf.Min(texWidth, centerX + radius);
            int minY = Mathf.Max(0, centerY - radius);
            int maxY = Mathf.Min(texHeight, centerY + radius);

            for (int y = minY; y < maxY; y++)
            {
                int dy = y - centerY;
                int rowStart = y * texWidth;

                for (int x = minX; x < maxX; x++)
                {
                    int dx = x - centerX;

                    if ((dx * dx) + (dy * dy) <= radiusSqr)
                    {
                        pixels[rowStart + x] = color;
                    }
                }
            }
        }

        // Dibuja el contorno de un rectángulo con el grosor indicado.
        public static void DrawRectOutline(Color32[] pixels, int texWidth, int texHeight,
            int x0, int y0, int x1, int y1, int thickness, Color32 color)
        {
            FillRect(pixels, texWidth, texHeight, x0, y0, x1, y0 + thickness, color);
            FillRect(pixels, texWidth, texHeight, x0, y1 - thickness, x1, y1, color);
            FillRect(pixels, texWidth, texHeight, x0, y0, x0 + thickness, y1, color);
            FillRect(pixels, texWidth, texHeight, x1 - thickness, y0, x1, y1, color);
        }

        // Dibuja el contorno de un círculo (un anillo) con el grosor indicado.
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

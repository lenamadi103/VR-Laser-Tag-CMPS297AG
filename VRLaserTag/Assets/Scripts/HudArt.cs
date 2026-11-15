using System.Collections.Generic;
using UnityEngine;

// Procedural art for the player HUD: panel frames, bar segments and icons.
// Generated once when first used (a few small textures, no assets to import
// or keep in sync). Every sprite is white with alpha, so a UI Image can tint
// it to any colour.
public static class HudArt
{
    // Panel frame: a rectangle with cut (chamfered) corners and a soft glow
    // outside the outline. 9-sliced, so one texture fits any panel size.
    private const int FrameSize = 64;
    private const int FrameMargin = 8;   // transparent room for the glow
    private const int FrameChamfer = 12; // size of the cut corners
    private const int FrameBorder = FrameMargin + FrameChamfer + 4;

    private const int IconSize = 64;
    private const int Samples = 4; // 4 x 4 supersampling = smooth edges

    private delegate float Painter(float x, float y);
    private delegate bool Shape(float u, float v);

    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
    private static Material overlayMaterial;
    private static bool overlayLoaded;

    // UI material drawn on top of the arena (Resources/HudOverlay.shader).
    // Null if the shader is missing: the HUD then uses the default UI
    // material and still works, but close walls can cover it.
    public static Material OverlayMaterial
    {
        get
        {
            if (!overlayLoaded)
            {
                overlayLoaded = true;
                Shader shader = Resources.Load<Shader>("HudOverlay");

                if (shader != null && shader.isSupported)
                    overlayMaterial = new Material(shader) { name = "HudOverlay (runtime)" };
                else
                    Debug.LogWarning("[HudArt] HudOverlay shader not found or not supported; using the default UI material.");
            }

            return overlayMaterial;
        }
    }

    // Panel background (tint it dark and translucent).
    public static Sprite FrameFill => Frame("FrameFill", false);

    // Panel outline with outer glow (same shape as FrameFill).
    public static Sprite FrameLine => Frame("FrameLine", true);

    // Fixed-width end tabs with a stretchable slim connection. The two
    // existing bar Images render the complete shape: no extra UI draw objects.
    public static Sprite ConnectedFrameFill => ConnectedFrame(false);
    public static Sprite ConnectedFrameLine => ConnectedFrame(true);

    private static Sprite ConnectedFrame(bool outline)
    {
        return Create(outline ? "ConnectedFrameLine" : "ConnectedFrameFill", 640, 128,
            new Vector4(220f, 0f, 220f, 0f), (x, y) =>
            {
                float left = ChamferedRect(x - 110f, y - 64f, 102f, 56f, 14f);
                float right = ChamferedRect(x - 530f, y - 64f, 102f, 56f, 14f);
                float bridge = Mathf.Max(Mathf.Abs(x - 320f) - 210f, Mathf.Abs(y - 64f) - 6f);
                float d = Mathf.Min(left, Mathf.Min(right, bridge));
                if (!outline) return d <= 0f ? 1f : 0f;
                float line = d <= 0f && d >= -2.5f ? 1f : 0f;
                float glow = d > 0f ? .45f * Mathf.Exp(-d / 2.5f) : 0f;
                return Mathf.Max(line, glow);
            });
    }

    private static float ChamferedRect(float x, float y, float halfX, float halfY, float chamfer)
    {
        float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
        return Mathf.Max(Mathf.Max(ax - halfX, ay - halfY),
            (ax + ay - (halfX + halfY - chamfer)) * .70710678f);
    }

    // Rounded bar segment (health / ammo).
    public static Sprite Pill => Create("Pill", 16, new Vector4(6f, 6f, 6f, 6f),
        (x, y) => RoundedRect(x - 8f, y - 8f, 7f, 7f, 4f) <= 0f ? 1f : 0f);

    // Heart outline with a pulse line (health).
    public static Sprite Heart => Icon("Heart", (u, v) =>
    {
        bool outer = InsideHeart(u * 1.25f, v * 1.25f + 0.15f);
        bool inner = InsideHeart(u * 1.6f, v * 1.6f + 0.2f);
        return (outer && !inner) || NearPolyline(u, v, PulseLine, 0.07f);
    });

    // Stopwatch (match time).
    public static Sprite Clock => Icon("Clock", (u, v) =>
    {
        float r = Mathf.Sqrt(u * u + v * v);
        bool ring = r >= 0.66f && r <= 0.84f;
        bool knob = Mathf.Abs(u) < 0.13f && v > 0.82f && v < 1f;
        bool hands = NearSegment(u, v, 0f, 0f, 0f, 0.5f, 0.07f) ||
                     NearSegment(u, v, 0f, 0f, 0.36f, 0f, 0.07f);
        return ring || knob || hands;
    });

    // Trophy (rank).
    public static Sprite Trophy => Icon("Trophy", (u, v) =>
    {
        float au = Mathf.Abs(u);
        bool rim = au < 0.62f && v > 0.66f && v < 0.8f;
        bool bowl = v >= -0.05f && v <= 0.72f &&
                    (u * u) / (0.55f * 0.55f) + ((v - 0.72f) * (v - 0.72f)) / (0.78f * 0.78f) <= 1f;
        float hd = Mathf.Sqrt((au - 0.55f) * (au - 0.55f) + (v - 0.42f) * (v - 0.42f));
        bool handle = au > 0.55f && hd >= 0.13f && hd <= 0.26f;
        bool stem = au < 0.09f && v >= -0.48f && v < 0f;
        bool foot = au < 0.42f && v >= -0.78f && v <= -0.52f;
        return rim || bowl || handle || stem || foot;
    });

    // Target (score).
    public static Sprite Target => Icon("Target", (u, v) =>
    {
        float r = Mathf.Sqrt(u * u + v * v);
        bool ring = r >= 0.48f && r <= 0.64f;
        bool dot = r <= 0.14f;
        bool vertical = Mathf.Abs(u) < 0.055f && Mathf.Abs(v) > 0.3f && Mathf.Abs(v) < 0.95f;
        bool horizontal = Mathf.Abs(v) < 0.055f && Mathf.Abs(u) > 0.3f && Mathf.Abs(u) < 0.95f;
        return ring || dot || vertical || horizontal;
    });

    // Four bullets (ammo).
    public static Sprite Bullets => Icon("Bullets", (u, v) =>
    {
        for (int i = 0; i < 4; i++)
        {
            float dx = u - (-0.6f + 0.4f * i);
            bool body = Mathf.Abs(dx) < 0.13f && v >= -0.75f && v <= 0.2f;
            bool tip = v > 0.2f && (dx * dx) / (0.13f * 0.13f) + ((v - 0.2f) * (v - 0.2f)) / (0.5f * 0.5f) <= 1f;
            bool rim = Mathf.Abs(dx) < 0.16f && v >= -0.8f && v <= -0.66f;
            if (body || tip || rim)
                return true;
        }

        return false;
    });

    // Classic hit marker: four short diagonal strokes around the centre.
    public static Sprite HitMarker => Icon("HitMarker", (u, v) =>
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float offDiagonal = Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) * 0.70710678f;
        return r > 0.38f && r < 0.92f && offDiagonal < 0.08f;
    });

    // ------------------------------------------------------------------

    private static readonly Vector2[] PulseLine =
    {
        new Vector2(-0.55f, 0.05f), new Vector2(-0.25f, 0.05f), new Vector2(-0.1f, 0.38f),
        new Vector2(0.08f, -0.32f), new Vector2(0.22f, 0.18f), new Vector2(0.32f, 0.05f),
        new Vector2(0.55f, 0.05f),
    };

    private static Sprite Frame(string name, bool outline)
    {
        float b = FrameBorder;
        float half = FrameSize * 0.5f - FrameMargin;

        return Create(name, FrameSize, new Vector4(b, b, b, b), (x, y) =>
        {
            float d = ChamferedSquare(x - FrameSize * 0.5f, y - FrameSize * 0.5f, half, FrameChamfer);

            if (!outline)
                return d <= 0f ? 1f : 0f;

            const float thickness = 2.5f;
            float line = d <= 0f && d >= -thickness ? 1f : 0f;
            float glow = d > 0f ? 0.45f * Mathf.Exp(-d / 2.5f) : 0f;
            return Mathf.Max(line, glow);
        });
    }

    private static Sprite Icon(string name, Shape shape)
    {
        return Create(name, IconSize, Vector4.zero, (x, y) =>
            shape(x / IconSize * 2f - 1f, y / IconSize * 2f - 1f) ? 1f : 0f);
    }

    private static Sprite Create(string name, int size, Vector4 border, Painter painter)
    {
        return Create(name, size, size, border, painter);
    }

    private static Sprite Create(string name, int width, int height, Vector4 border, Painter painter)
    {
        if (Cache.TryGetValue(name, out Sprite cached) && cached != null)
            return cached;

        Color32[] pixels = new Color32[width * height];

        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                float sum = 0f;

                for (int sy = 0; sy < Samples; sy++)
                {
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        float x = px + (sx + 0.5f) / Samples;
                        float y = py + (sy + 0.5f) / Samples;
                        sum += Mathf.Clamp01(painter(x, y));
                    }
                }

                byte alpha = (byte)Mathf.RoundToInt(sum / (Samples * Samples) * 255f);
                pixels[py * width + px] = new Color32(255, 255, 255, alpha);
            }
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Hud_" + name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true); // upload and free the CPU copy

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, border);
        sprite.name = "Hud_" + name;

        Cache[name] = sprite;
        return sprite;
    }

    // Signed distance (pixels) to a square with cut corners, centred at 0.
    private static float ChamferedSquare(float x, float y, float half, float chamfer)
    {
        float ax = Mathf.Abs(x);
        float ay = Mathf.Abs(y);
        float box = Mathf.Max(ax - half, ay - half);
        float cut = (ax + ay - (2f * half - chamfer)) * 0.70710678f;
        return Mathf.Max(box, cut);
    }

    // Signed distance to a rectangle with rounded corners, centred at 0.
    private static float RoundedRect(float x, float y, float halfX, float halfY, float radius)
    {
        float qx = Mathf.Abs(x) - halfX + radius;
        float qy = Mathf.Abs(y) - halfY + radius;
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    // Heart curve: (x^2 + y^2 - 1)^3 - x^2 y^3 <= 0.
    private static bool InsideHeart(float x, float y)
    {
        float a = x * x + y * y - 1f;
        return a * a * a - x * x * y * y * y <= 0f;
    }

    private static bool NearSegment(float u, float v, float ax, float ay, float bx, float by, float halfWidth)
    {
        float dx = bx - ax;
        float dy = by - ay;
        float t = Mathf.Clamp01(((u - ax) * dx + (v - ay) * dy) / (dx * dx + dy * dy));
        float ex = u - (ax + t * dx);
        float ey = v - (ay + t * dy);
        return ex * ex + ey * ey <= halfWidth * halfWidth;
    }

    private static bool NearPolyline(float u, float v, Vector2[] points, float halfWidth)
    {
        for (int i = 0; i + 1 < points.Length; i++)
        {
            if (NearSegment(u, v, points[i].x, points[i].y, points[i + 1].x, points[i + 1].y, halfWidth))
                return true;
        }

        return false;
    }
}

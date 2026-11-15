using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 程序生成的简易状态图标（避免依赖外部资源）。
/// </summary>
public static class StatusIconFactory
{
    private static readonly Dictionary<StatusType, Sprite> cache = new Dictionary<StatusType, Sprite>();

    public static Sprite GetIcon(StatusType type)
    {
        if (cache.TryGetValue(type, out var s) && s != null)
            return s;
        s = type switch
        {
            StatusType.Trip => CreateBananaIcon(),
            StatusType.Poison => CreatePoisonIcon(),
            StatusType.Burning => CreateFireIcon(),
            StatusType.Hidden => CreateHiddenIcon(),
            StatusType.Leader => CreateLeaderIcon(),
            StatusType.Stun => SpriteFactory.CreateBorderedSprite(new Color(0.7f, 0.55f, 0.9f), new Color(0.3f, 0.2f, 0.45f), 32, 2),
            StatusType.Blind => SpriteFactory.CreateBorderedSprite(new Color(0.15f, 0.15f, 0.18f), new Color(0.6f, 0.6f, 0.65f), 32, 2),
            _ => SpriteFactory.CreateColorSprite(Color.white, 32)
        };
        cache[type] = s;
        return s;
    }

    private static Sprite CreateFireIcon()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, clear);

        // 火焰：下层橙红、上层黄
        for (int y = 4; y < 28; y++)
        {
            float t = (y - 4) / 24f;
            int half = Mathf.RoundToInt(2 + t * 10 * (1f - t) * 4f);
            for (int x = 16 - half; x <= 16 + half; x++)
            {
                if (x < 0 || x >= size) continue;
                var c = t > 0.55f
                    ? new Color(1f, 0.85f, 0.2f)
                    : new Color(1f, 0.35f + t * 0.3f, 0.05f);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Sprite CreatePoisonIcon()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, clear);

        var green = new Color(0.25f, 0.85f, 0.35f);
        var dark = new Color(0.1f, 0.45f, 0.15f);
        // 圆瓶
        for (int y = 6; y < 24; y++)
            for (int x = 10; x < 22; x++)
            {
                float cx = 15.5f, cy = 14.5f;
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d < 7.5f) tex.SetPixel(x, y, green);
                else if (d < 8.5f) tex.SetPixel(x, y, dark);
            }
        // 瓶口
        for (int y = 22; y < 28; y++)
            for (int x = 13; x < 19; x++)
                tex.SetPixel(x, y, dark);
        // 骷髅点
        tex.SetPixel(13, 15, Color.black);
        tex.SetPixel(18, 15, Color.black);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Sprite CreateBananaIcon()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, clear);

        var yellow = new Color(1f, 0.88f, 0.2f);
        var edge = new Color(0.75f, 0.55f, 0.05f);
        // 弯香蕉近似：沿弧画粗线
        for (int i = 0; i < 40; i++)
        {
            float t = i / 39f;
            float ang = Mathf.Lerp(-0.6f, 1.1f, t);
            float r = 10f;
            int cx = Mathf.RoundToInt(18 + Mathf.Cos(ang) * r);
            int cy = Mathf.RoundToInt(10 + Mathf.Sin(ang) * r);
            for (int oy = -2; oy <= 2; oy++)
                for (int ox = -2; ox <= 2; ox++)
                {
                    int x = cx + ox, y = cy + oy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    tex.SetPixel(x, y, ox * ox + oy * oy <= 4 ? yellow : edge);
                }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Sprite CreateHiddenIcon()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, clear);

        var purple = new Color(0.45f, 0.35f, 0.7f, 0.9f);
        for (int y = 8; y < 24; y++)
            for (int x = 8; x < 24; x++)
            {
                int dx = x - 16, dy = y - 16;
                if (dx * dx + dy * dy <= 64)
                    tex.SetPixel(x, y, purple);
            }
        tex.SetPixel(12, 16, Color.white);
        tex.SetPixel(20, 16, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Sprite CreateLeaderIcon()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, clear);

        var gold = new Color(1f, 0.84f, 0.2f);
        var dark = new Color(0.55f, 0.38f, 0.05f);
        // 简易王冠：底带 + 三尖
        for (int y = 10; y <= 14; y++)
            for (int x = 6; x <= 25; x++)
                tex.SetPixel(x, y, gold);
        for (int tip = 0; tip < 3; tip++)
        {
            int cx = 10 + tip * 6;
            for (int y = 15; y <= 24; y++)
            {
                int half = Mathf.Max(0, 24 - y);
                for (int x = cx - half; x <= cx + half; x++)
                {
                    if (x < 0 || x >= size) continue;
                    tex.SetPixel(x, y, y == 24 ? dark : gold);
                }
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}

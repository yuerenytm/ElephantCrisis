using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 角色立绘/棋子图：优先读 Resources/Roles/ 下同名 Sprite，没有则用程序生成简易剪影。
/// 放入 PNG（导入为 Sprite）到 Assets/Resources/Roles/Elephant.png 等即可自动替换。
/// </summary>
public static class RolePortrait
{
    private static readonly Dictionary<RoleType, Sprite> cache = new Dictionary<RoleType, Sprite>();
    private static readonly Dictionary<RoleType, bool> fromArt = new Dictionary<RoleType, bool>();

    public static Sprite GetSprite(RoleType role)
    {
        if (cache.TryGetValue(role, out var cached) && cached != null)
            return cached;

        string file = role.ToString(); // Elephant / Human / Monkey / Cat
        var art = ClientPerfResourceProbe.Load<Sprite>($"Roles/{file}");
        if (art != null)
        {
            cache[role] = art;
            fromArt[role] = true;
            return art;
        }

        // 也尝试不带扩展名的 Texture2D → Sprite
        var tex = ClientPerfResourceProbe.Load<Texture2D>($"Roles/{file}");
        if (tex != null)
        {
            art = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), Mathf.Max(tex.width, tex.height));
            cache[role] = art;
            fromArt[role] = true;
            return art;
        }

        art = CreateFallbackIcon(role);
        cache[role] = art;
        fromArt[role] = false;
        return art;
    }

    /// <summary>若来自美术资源，地图上可不再叠文字。</summary>
    public static bool IsArtSprite(RoleType role)
    {
        GetSprite(role);
        return fromArt.TryGetValue(role, out bool art) && art;
    }

    private static Sprite CreateFallbackIcon(RoleType role)
    {
        Color fill = RoleInfo.GetColor(role);
        Color border = new Color(0.08f, 0.08f, 0.08f);
        const int size = 48;

        switch (role)
        {
            case RoleType.Elephant:
                return SpriteFactory.CreateEllipseSprite(fill, border, size, 0.42f, 0.32f);
            case RoleType.Human:
                return SpriteFactory.CreateHumanoidSprite(fill, border, size);
            case RoleType.Monkey:
                return SpriteFactory.CreateCircleSprite(fill, border, size, 0.38f);
            case RoleType.Cat:
                return SpriteFactory.CreateCatSprite(fill, border, size);
            default:
                return SpriteFactory.CreateBorderedSprite(fill, border, size, 3);
        }
    }
}

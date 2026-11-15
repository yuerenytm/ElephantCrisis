using UnityEngine;

/// <summary>
/// 表现层的 Unity 实现：逻辑层唯一需要的画面出口都落在这里。
/// 只有本文件（和 App 的装配代码）允许认识 MapVisual / GameUI / 输入控制器。
/// </summary>
public sealed class UnityPresentation : IPresentation
{
    // 由 App 注入的流程控制委托：表现层不认识启动代码，只负责转发
    public System.Action StartHotseatAction;
    public System.Action<RoleType> StartAiBattleAction;
    public System.Action<RoleType> StartAdminAction;
    public System.Action RestartAction;
    public System.Action ReturnToMenuAction;

    public void ApplyFog(UnitActor viewer)
    {
        MapVisual.Instance?.ApplyFog(viewer);
        AtmosphereVisual.Instance?.Refresh();
    }

    public void RefreshTiles()
    {
        MapVisual.Instance?.RefreshAllTiles(GridManager.Instance);
    }

    public void RefreshHints()
    {
        PlayerInputController.Instance?.RefreshHints();
    }

    public void RequestHud()
    {
        GameUI.Instance?.RequestRefresh();
    }

    public void BeginItemTargeting(int itemIndex, ItemKind kind)
    {
        var input = PlayerInputController.Instance;
        if (input == null)
            return;

        switch (kind)
        {
            case ItemKind.BananaPeel: input.StartBananaMode(itemIndex); break;
            case ItemKind.Mine: input.StartMineMode(itemIndex); break;
            case ItemKind.Flashbang: input.StartFlashbangMode(itemIndex); break;
            case ItemKind.GasolineBottle: input.StartMolotovMode(itemIndex); break;
            case ItemKind.Flamethrower: input.StartFlameMode(itemIndex); break;
            case ItemKind.Boomerang: input.StartBoomerangMode(itemIndex); break;
            case ItemKind.Motorcycle: input.StartMotorcycleRamMode(itemIndex); break;
            case ItemKind.GrappleHook: input.StartHookMode(itemIndex); break;
            case ItemKind.Bomb:
            case ItemKind.MegaBomb:
            case ItemKind.TimedBomb: input.StartBombMode(itemIndex); break;
            default: input.StartShootMode(itemIndex); break;
        }
    }

    public void BeginShootWithAmmo(int weaponIndex, ItemKind ammo)
    {
        PlayerInputController.Instance?.StartShootModeWithAmmo(weaponIndex, ammo);
    }

    public void StartMatch(GameMode mode, RoleType human)
    {
        switch (mode)
        {
            case GameMode.Hotseat: StartHotseatAction?.Invoke(); break;
            case GameMode.AdminMode: StartAdminAction?.Invoke(human); break;
            default: StartAiBattleAction?.Invoke(human); break;
        }
    }

    public void RestartMatch()
    {
        RestartAction?.Invoke();
    }

    public void ReturnToMenu()
    {
        ReturnToMenuAction?.Invoke();
    }

    public void LogConsole(string message)
    {
        Debug.Log(message);
    }

    public void HideMenu()
    {
        MainMenuUI.Instance?.Hide();
    }

    // 视觉常量：原在 HazardManager 里，随创建代码一起搬过来
    private static readonly Color HazardRed = new Color(0.92f, 0.22f, 0.18f, 1f);
    private static readonly Color HazardRedBorder = new Color(0.45f, 0.08f, 0.06f, 1f);
    private static readonly Color FlameEdge = new Color(0.95f, 0.15f, 0.05f, 0.55f);
    private static readonly Color FlameGlow = new Color(1f, 0.75f, 0.2f, 0.35f);

    public void StyleLoot(GameObject go, Color fill, Color border, Vector2Int cell, bool emphasize)
    {
        var grid = GridManager.Instance;
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.CreateBorderedSprite(fill, border, 16, 2);
        sr.sortingOrder = grid.GetSortOrder(cell, emphasize ? 35 : 30);
        go.transform.position = grid.CellToWorld(cell) + new Vector3(0.32f, 0.04f, -0.32f);
        go.transform.localScale = Vector3.one * (emphasize ? 0.34f : 0.28f);
        if (go.GetComponent<CameraBillboard>() == null)
            go.AddComponent<CameraBillboard>();
    }

    public GameObject BuildFlameVisual(Vector2Int cell, Transform parent)
    {
        var grid = GridManager.Instance;
        var go = new GameObject("Flame");
        go.transform.SetParent(parent, false);
        Vector3 pos = grid != null ? grid.CellToWorld(cell) : Vector3.zero;
        go.transform.position = pos + new Vector3(0f, 0.05f, 0f);

        var glow = new GameObject("Glow");
        glow.transform.SetParent(go.transform, false);
        glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        glow.transform.localScale = Vector3.one * 0.95f;
        var gsr = glow.AddComponent<SpriteRenderer>();
        gsr.sprite = SpriteFactory.CreateCircleSprite(FlameGlow, FlameEdge, 32, 0.48f);
        gsr.sortingOrder = grid != null ? grid.GetSortOrder(cell, 20) : 20;

        var body = new GameObject("Body");
        body.transform.SetParent(go.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        body.transform.localScale = Vector3.one * 0.72f;
        var bsr = body.AddComponent<SpriteRenderer>();
        bsr.sprite = SpriteFactory.CreateBorderedSprite(
            new Color(1f, 0.62f, 0.12f, 0.95f), FlameEdge, 24, 3);
        bsr.sortingOrder = grid != null ? grid.GetSortOrder(cell, 21) : 21;

        var tongue = new GameObject("Tongue");
        tongue.transform.SetParent(go.transform, false);
        tongue.transform.localPosition = new Vector3(0f, 0.28f, 0f);
        tongue.transform.localScale = new Vector3(0.35f, 0.55f, 0.35f);
        var tsr = tongue.AddComponent<SpriteRenderer>();
        tsr.sprite = SpriteFactory.CreateBorderedSprite(
            new Color(1f, 0.85f, 0.25f, 0.95f),
            new Color(1f, 0.35f, 0.05f, 0.9f),
            16, 2);
        tsr.sortingOrder = grid != null ? grid.GetSortOrder(cell, 36) : 36;
        tongue.AddComponent<CameraBillboard>();

        go.AddComponent<FlameFlicker>();
        return go;
    }

    public GameObject BuildRedMarker(Vector2Int cell, Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var grid = GridManager.Instance;
        go.transform.position = grid.CellToWorld(cell) + new Vector3(-0.32f, 0.04f, 0.32f);
        go.transform.localScale = Vector3.one * 0.28f;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.CreateBorderedSprite(HazardRed, HazardRedBorder, 16, 2);
        sr.sortingOrder = grid.GetSortOrder(cell, 38);
        go.AddComponent<CameraBillboard>();
        return go;
    }

    public void BuildBombLabel(GameObject marker, int actions)
    {
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(marker.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        labelGo.transform.localScale = Vector3.one * 2.2f;
        var tm = labelGo.AddComponent<TextMesh>();
        tm.text = actions.ToString();
        tm.characterSize = 0.12f;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        var mr = labelGo.GetComponent<MeshRenderer>();
        if (mr != null)
            mr.sortingOrder = 6;
    }

}

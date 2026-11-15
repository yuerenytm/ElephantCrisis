using UnityEngine;

/// <summary>
/// 表现层的 Unity 实现：逻辑层唯一需要的画面出口都落在这里。
/// 只有本文件（和 App 的装配代码）允许认识 MapVisual / GameUI / 输入控制器。
/// </summary>
public sealed class UnityPresentation : IPresentation
{
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

    public void HideMenu()
    {
        MainMenuUI.Instance?.Hide();
    }
}

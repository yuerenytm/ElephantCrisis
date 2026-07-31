using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class PlayerInputController : MonoBehaviour
{
    public static PlayerInputController Instance;

    private Camera cam;

    private void Awake()
    {
        Instance = this;
        cam = Camera.main;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        // 回车结束回合（瞄准中不触发）
        if (Keyboard.current != null &&
            (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
        {
            if (MatchConfig.IsHumanTurn())
                TryEndTurnFromInput();
        }

        if (Mouse.current == null)
            return;

        // AI 回合：仅允许悬停查看，不处理点击/取消瞄准
        bool humanTurn = MatchConfig.IsHumanTurn();

        if (humanTurn && Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (IsCancellablePhase())
            {
                CancelTargetMode();
                GameUI.Instance?.Refresh();
                return;
            }
        }

        UpdateUnitHover();
        if (humanTurn)
            UpdateBombHoverPreview();

        if (!humanTurn)
            return;

        if (TurnManager.Instance == null || TurnManager.Instance.Phase == TurnPhase.GameOver)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;
        if (!Mouse.current.leftButton.wasPressedThisFrame)
            return;

        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return;

        if (!TryGetCellUnderMouse(out Vector2Int cell))
            return;

        HandleCellClick(cell);
    }

    private void TryEndTurnFromInput()
    {
        var turn = TurnManager.Instance;
        if (turn == null || turn.Phase == TurnPhase.GameOver)
            return;
        if (!MatchConfig.IsHumanTurn())
            return;
        if (IsCancellablePhase())
            return;
        if (turn.CurrentUnit == null || turn.CurrentUnit.IsDead)
            return;
        if (turn.Phase != TurnPhase.WaitingAction && turn.Phase != TurnPhase.MandatoryDiscard)
            return;
        turn.RequestEndTurn();
        GameUI.Instance?.Refresh();
    }

    private void UpdateBombHoverPreview()
    {
        var turn = TurnManager.Instance;
        if (turn == null || turn.Phase != TurnPhase.SelectingBombTarget)
            return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            MapVisual.Instance?.UpdateBombHoverPreview(null);
            return;
        }
        if (TryGetCellUnderMouse(out Vector2Int cell))
            MapVisual.Instance?.UpdateBombHoverPreview(cell);
        else
            MapVisual.Instance?.UpdateBombHoverPreview(null);
    }

    private bool IsCancellablePhase()
    {
        var phase = TurnManager.Instance?.Phase;
        return phase == TurnPhase.SelectingBombTarget
            || phase == TurnPhase.SelectingBananaTarget
            || phase == TurnPhase.SelectingMineTarget
            || phase == TurnPhase.SelectingDiscardTarget
            || phase == TurnPhase.SelectingShootTarget
            || phase == TurnPhase.SelectingReinforce
            || phase == TurnPhase.SelectingSkillReinforce
            || phase == TurnPhase.SelectingMonkeyStealTarget
            || phase == TurnPhase.SelectingMonkeyMarkItem
            || phase == TurnPhase.SelectingAmmo
            || phase == TurnPhase.SelectingTimedBombDelay
            || phase == TurnPhase.SelectingFlameDirection
            || phase == TurnPhase.SelectingPickup;
    }

    private void UpdateUnitHover()
    {
        if (GameUI.Instance == null)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            GameUI.Instance.ClearHover();
            return;
        }

        if (cam == null)
            cam = Camera.main;
        if (cam == null || GridManager.Instance == null)
        {
            GameUI.Instance.ClearHover();
            return;
        }

        if (!TryGetCellUnderMouse(out Vector2Int cell))
        {
            GameUI.Instance.ClearHover();
            return;
        }

        var occ = GridManager.Instance.GetOccupant(cell);
        var unit = occ != null ? occ.GetComponent<UnitActor>() : null;
        var fogViewer = VisibilityService.GetFogViewer();
        if (unit != null && !unit.IsDead && VisibilityService.CanSeeUnit(fogViewer, unit))
        {
            GameUI.Instance.SetHoverUnit(unit);
            return;
        }

        if (fogViewer != null && !VisibilityService.CanSeeCell(fogViewer, cell))
        {
            GameUI.Instance.ClearHover();
            return;
        }

        var viewer = TurnManager.Instance?.CurrentUnit;
        var tile = GridManager.Instance.GetTileType(cell);
        string info = TerrainInfo.GetDescription(tile);
        var loot = GroundItemManager.Instance?.DescribeLoot(cell);
        var hazard = HazardManager.Instance?.DescribeHazards(cell, viewer);
        if (loot != null)
            info += "\n" + loot;
        if (hazard != null)
            info += "\n" + hazard;
        GameUI.Instance.SetHoverCell(cell, info);
    }

    private bool TryGetCellUnderMouse(out Vector2Int cell)
    {
        cell = default;
        Vector2 screen = Mouse.current.position.ReadValue();
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, Mathf.Abs(cam.transform.position.z)));
        world.z = 0f;
        cell = GridManager.Instance.WorldToCell(world);
        return GridManager.Instance.IsValidCell(cell);
    }

    private void HandleCellClick(Vector2Int cell)
    {
        var turn = TurnManager.Instance;
        var unit = turn.CurrentUnit;
        if (unit == null || unit.IsDead)
            return;

        switch (turn.Phase)
        {
            case TurnPhase.SelectingDiscardTarget:
                if (!ActionService.TryDiscard(unit, turn.PendingItemIndex, cell))
                    turn.LogFor(unit, "弃置失败（需落在自己半径1内）");
                RefreshHints();
                return;

            case TurnPhase.SelectingBombTarget:
                if (!ActionService.TryThrowBomb(unit, cell))
                    turn.LogFor(unit, "炸弹投放失败（超距或无效）");
                RefreshHints();
                return;

            case TurnPhase.SelectingBananaTarget:
                if (!ActionService.TryThrowBanana(unit, cell))
                    turn.LogFor(unit, "香蕉皮投掷失败（需在攻击距离内）");
                RefreshHints();
                return;

            case TurnPhase.SelectingMineTarget:
                if (!ActionService.TryPlaceMine(unit, cell))
                    turn.LogFor(unit, "地雷放置失败（需在攻击距离内）");
                RefreshHints();
                return;

            case TurnPhase.SelectingFlameDirection:
                if (!ActionService.TryFlamethrower(unit, cell))
                    turn.LogFor(unit, "请点击自身相邻的四向之一以确定喷射方向");
                RefreshHints();
                return;

            case TurnPhase.SelectingShootTarget:
            {
                var occ = GridManager.Instance.GetOccupant(cell);
                var target = occ != null ? occ.GetComponent<UnitActor>() : null;
                if (target == null || target == unit || target.IsDead)
                {
                    turn.LogFor(unit, "请点击射程内的敌方角色");
                    return;
                }
                if (!ActionService.TryShoot(unit, turn.PendingItemIndex, target))
                    turn.LogFor(unit, "射击失败（超距或缺弹药）");
                RefreshHints();
                return;
            }

            case TurnPhase.SelectingReinforce:
            case TurnPhase.SelectingSkillReinforce:
            case TurnPhase.SelectingMonkeyMarkItem:
            case TurnPhase.SelectingAmmo:
            case TurnPhase.SelectingTimedBombDelay:
                turn.LogFor(unit, "请在右侧面板完成选择（右键取消）");
                return;

            case TurnPhase.SelectingMonkeyStealTarget:
            {
                var occ = GridManager.Instance.GetOccupant(cell);
                var target = occ != null ? occ.GetComponent<UnitActor>() : null;
                if (target == null || target == unit || target.IsDead)
                {
                    turn.LogFor(unit, "请点击半径内的其他角色");
                    return;
                }
                if (!SkillService.TryMonkeySelectTarget(unit, target))
                    turn.LogFor(unit, "无法抢夺该目标");
                RefreshHints();
                return;
            }

            case TurnPhase.SelectingPickup:
                turn.LogFor(unit, "请在右侧列表选择要拾取的物品（右键取消）");
                return;

            case TurnPhase.MandatoryDiscard:
                turn.LogFor(unit, "背包超重：请先在右侧背包点「弃置」，将负重降至容量以内");
                return;
        }

        var occupant = GridManager.Instance.GetOccupant(cell);
        if (occupant != null)
        {
            var target = occupant.GetComponent<UnitActor>();
            if (target != null && target != unit && !target.IsDead)
            {
                if (!ActionService.TryAttack(unit, target))
                    turn.LogFor(unit, "无法攻击（已攻击过、超出范围或目标无效）");
                RefreshHints();
                return;
            }
        }

        if (!turn.HasMoved)
        {
            if (!ActionService.TryMove(unit, cell))
                turn.LogFor(unit, "无法移动（已移动过、超距、能见度外或格子被占）");
            RefreshHints();
        }
    }

    public void StartBombMode(int itemIndex = -1)
    {
        var turn = TurnManager.Instance;
        var unit = turn?.CurrentUnit;
        if (unit == null || unit.IsDying)
            return;
        if (itemIndex < 0)
            itemIndex = unit.Inventory.Items.FindIndex(i => ItemInfo.IsThrowableBomb(i.Kind));
        if (itemIndex < 0 || itemIndex >= unit.Inventory.Count)
            return;
        var kind = unit.Inventory.Items[itemIndex].Kind;
        if (!ItemInfo.IsThrowableBomb(kind))
            return;

        turn.EnterBombTargeting(itemIndex);
        MapVisual.Instance?.ShowBombHints(unit, 5, ItemInfo.GetBombBlastRadius(kind));
        turn.LogFor(unit, $"选择【{ItemInfo.GetDisplayName(kind)}】落点（射程5，悬停预览爆炸范围，右键取消）");
    }

    public void StartBananaMode(int itemIndex)
    {
        var turn = TurnManager.Instance;
        var unit = turn?.CurrentUnit;
        if (unit == null || unit.IsDying)
            return;
        turn.EnterBananaTargeting(itemIndex);
        MapVisual.Instance?.ShowBananaHints(unit);
        turn.LogFor(unit, $"选择香蕉皮落点（攻击距离 {unit.AttackRange}，投掷后仅你可见，右键取消）");
    }

    public void StartMineMode(int itemIndex)
    {
        var turn = TurnManager.Instance;
        var unit = turn?.CurrentUnit;
        if (unit == null || unit.IsDying)
            return;
        turn.EnterMineTargeting(itemIndex);
        MapVisual.Instance?.ShowBananaHints(unit);
        turn.LogFor(unit, $"选择地雷落点（攻击距离 {unit.AttackRange}，放置后全员可见，右键取消）");
    }

    public void StartFlameMode(int itemIndex)
    {
        var turn = TurnManager.Instance;
        var unit = turn?.CurrentUnit;
        if (unit == null || unit.IsDying)
            return;
        turn.EnterFlameDirection(itemIndex);
        MapVisual.Instance?.ShowFlameHints(unit);
        turn.LogFor(unit, "点击相邻四向之一确定火焰喷射方向（右键取消）");
    }

    public void StartShootMode(int weaponIndex)
    {
        var turn = TurnManager.Instance;
        var unit = turn?.CurrentUnit;
        if (unit == null || weaponIndex < 0 || weaponIndex >= unit.Inventory.Count)
            return;

        var weapon = unit.Inventory.Items[weaponIndex].Kind;
        if (!ItemInfo.IsRangedWeapon(weapon))
            return;

        int need = ItemInfo.GetArrowCost(weapon);
        var ammos = unit.Inventory.GetAvailableAmmoTypes(need);
        if (ammos.Count == 0)
        {
            turn.LogFor(unit, "弹药不足");
            return;
        }
        if (ammos.Count == 1)
        {
            StartShootModeWithAmmo(weaponIndex, ammos[0]);
            return;
        }

        turn.EnterAmmoChoice(weaponIndex);
        turn.LogFor(unit, "选择弹药：弓箭 / 毒箭 / 火箭（右侧，右键取消）");
        GameUI.Instance?.RequestRefresh();
    }

    public void StartShootModeWithAmmo(int weaponIndex, ItemKind ammo)
    {
        var turn = TurnManager.Instance;
        var unit = turn?.CurrentUnit;
        if (unit == null || weaponIndex < 0 || weaponIndex >= unit.Inventory.Count)
            return;
        var weapon = unit.Inventory.Items[weaponIndex].Kind;
        if (!ItemInfo.IsRangedWeapon(weapon) || !ItemInfo.IsStackableAmmo(ammo))
            return;

        int range = unit.GetShootRange(weapon);
        turn.EnterShootTargeting(weaponIndex, ammo);
        MapVisual.Instance?.ShowShootHints(unit, range);
        turn.LogFor(unit, $"选择射击目标（【{ItemInfo.GetDisplayName(weapon)}】+【{ItemInfo.GetDisplayName(ammo)}】射程{range}，右键取消）");
        GameUI.Instance?.RequestRefresh();
    }

    public void StartDiscardMode(int itemIndex)
    {
        var turn = TurnManager.Instance;
        var unit = turn?.CurrentUnit;
        if (unit == null || unit.IsDead)
            return;
        if (itemIndex < 0 || itemIndex >= unit.Inventory.Count)
            return;

        turn.EnterDiscardTargeting(itemIndex);
        MapVisual.Instance?.ShowDiscardHints(unit);
        var kind = unit.Inventory.Items[itemIndex].Kind;
        turn.LogFor(unit, $"选择弃置【{ItemInfo.GetDisplayName(kind)}】的落点（右键取消）");
    }

    public void CancelTargetMode()
    {
        TurnManager.Instance?.CancelTargeting();
        RefreshHints();
    }

    public void RefreshHints()
    {
        var turn = TurnManager.Instance;
        if (turn == null || turn.CurrentUnit == null || turn.Phase == TurnPhase.GameOver)
        {
            MapVisual.Instance?.ClearHints();
            return;
        }

        // AI 回合不画操作提示，避免干扰观看
        if (!MatchConfig.IsHumanControlled(turn.CurrentUnit))
        {
            MapVisual.Instance?.ClearHints();
            return;
        }

        var unit = turn.CurrentUnit;
        switch (turn.Phase)
        {
            case TurnPhase.SelectingBombTarget:
            {
                var kind = turn.PendingItemIndex >= 0 && turn.PendingItemIndex < unit.Inventory.Count
                    ? unit.Inventory.Items[turn.PendingItemIndex].Kind
                    : ItemKind.Bomb;
                MapVisual.Instance?.ShowBombHints(unit, 5, ItemInfo.GetBombBlastRadius(kind));
                return;
            }
            case TurnPhase.SelectingBananaTarget:
                MapVisual.Instance?.ShowBananaHints(unit);
                return;
            case TurnPhase.SelectingMineTarget:
                MapVisual.Instance?.ShowBananaHints(unit);
                return;
            case TurnPhase.SelectingFlameDirection:
                MapVisual.Instance?.ShowFlameHints(unit);
                return;
            case TurnPhase.SelectingDiscardTarget:
                MapVisual.Instance?.ShowDiscardHints(unit);
                return;
            case TurnPhase.SelectingShootTarget:
            {
                if (turn.PendingItemIndex >= 0 && turn.PendingItemIndex < unit.Inventory.Count)
                {
                    var w = unit.Inventory.Items[turn.PendingItemIndex].Kind;
                    MapVisual.Instance?.ShowShootHints(unit, unit.GetShootRange(w));
                }
                return;
            }
            case TurnPhase.SelectingReinforce:
            case TurnPhase.SelectingAmmo:
            case TurnPhase.SelectingTimedBombDelay:
                MapVisual.Instance?.ClearHints();
                return;
            case TurnPhase.SelectingPickup:
                MapVisual.Instance?.ShowPickupHints(unit);
                return;
            case TurnPhase.MandatoryDiscard:
                MapVisual.Instance?.ClearHints();
                return;
        }

        if (!turn.HasMoved)
            MapVisual.Instance?.ShowMoveHints(unit);
        else if (!turn.HasMeleeAttacked)
            MapVisual.Instance?.ShowAttackHints(unit);
        else
            MapVisual.Instance?.ClearHints();
    }
}

using UnityEngine;

/// <summary>主动技能入口（象威慑为被动，在 UnitActor 属性中结算）。</summary>
public static class SkillService
{
    public static bool CanUseActiveSkill(UnitActor unit, out string reason)
    {
        reason = null;
        if (unit == null || unit.IsDead)
        {
            reason = "无法使用技能";
            return false;
        }
        if (unit.IsDying)
        {
            reason = "濒死时不可发动技能";
            return false;
        }
        if (SkillInfo.IsPassive(unit.Role))
        {
            reason = "威慑为被动技能";
            return false;
        }
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentUnit != unit)
        {
            reason = "非自己的行动";
            return false;
        }
        if (TurnManager.Instance.Phase != TurnPhase.WaitingAction)
        {
            reason = "当前无法使用技能";
            return false;
        }
        if (unit.SkillCooldownLeft > 0)
        {
            reason = $"技能冷却中（剩{unit.SkillCooldownLeft}回合）";
            return false;
        }
        return true;
    }

    public static bool TryBeginHumanReinforce(UnitActor unit)
    {
        if (!CanUseActiveSkill(unit, out string reason))
        {
            TurnManager.Instance?.LogFor(unit, reason);
            return false;
        }
        if (unit.Role != RoleType.Human)
            return false;

        TurnManager.Instance.EnterSkillReinforce();
        TurnManager.Instance.LogFor(unit, "技能强化：选择 攻击 / 防御 / 移动");
        return true;
    }

    public static bool TryConfirmHumanReinforce(UnitActor unit, StatBoost boost)
    {
        if (unit == null || unit.Role != RoleType.Human || unit.IsDying)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingSkillReinforce)
            return false;
        if (unit.SkillCooldownLeft > 0)
            return false;
        if (boost != StatBoost.Attack && boost != StatBoost.Defense && boost != StatBoost.Move)
            return false;

        string detail = unit.ApplySkillReinforce(boost);
        int cd = SkillInfo.GetHumanCooldownRounds(unit.SkillLevel);
        unit.SetSkillCooldown(cd);
        TurnManager.Instance.CancelTargeting();
        string shield = unit.SkillLevel >= GameRulesConfig.SkillLevelMax ? "；获得一层技能护盾" : "";
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 发动【强化】：{detail}（冷却{cd}回合）{shield}");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryBeginMonkeySteal(UnitActor unit)
    {
        if (!CanUseActiveSkill(unit, out string reason))
        {
            TurnManager.Instance?.LogFor(unit, reason);
            return false;
        }
        if (unit.Role != RoleType.Monkey)
            return false;

        TurnManager.Instance.EnterMonkeyStealTarget();
        TurnManager.Instance.LogFor(unit, "抢夺：选择半径内一名角色，再选择要夺取的非玩偶物品");
        return true;
    }

    public static bool TryMonkeySelectTarget(UnitActor monkey, UnitActor target)
    {
        if (monkey == null || target == null || monkey.Role != RoleType.Monkey)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingMonkeyStealTarget)
            return false;
        if (target.IsDead || target == monkey)
            return false;

        int radius = SkillInfo.GetMonkeyRadius(monkey.SkillLevel);
        if (GridManager.Instance.GetManhattanDistance(monkey.Cell, target.Cell) > radius)
        {
            TurnManager.Instance.LogFor(monkey, "目标超出抢夺半径");
            return false;
        }

        bool hasLoot = false;
        foreach (var it in target.Inventory.Items)
        {
            if (!ItemInfo.IsDoll(it.Kind))
            {
                hasLoot = true;
                break;
            }
        }
        if (!hasLoot)
        {
            TurnManager.Instance.LogFor(monkey, "目标没有可抢的非玩偶物品");
            return false;
        }

        TurnManager.Instance.PendingSkillTargetUnit = target;
        TurnManager.Instance.EnterMonkeyMarkItem(); // 复用「选物品」相位
        TurnManager.Instance.LogFor(monkey,
            $"查看 {RoleInfo.GetDisplayName(target.Role)} 的物品，点选一件夺取");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryMonkeyTakeItem(UnitActor monkey, int itemIndex)
    {
        var target = TurnManager.Instance.PendingSkillTargetUnit;
        if (monkey == null || target == null || monkey.Role != RoleType.Monkey)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingMonkeyMarkItem)
            return false;
        if (itemIndex < 0 || itemIndex >= target.Inventory.Count)
            return false;

        var item = target.Inventory.Items[itemIndex];
        if (ItemInfo.IsDoll(item.Kind))
        {
            TurnManager.Instance.LogFor(monkey, "不可抢夺玩偶");
            return false;
        }

        int radius = SkillInfo.GetMonkeyRadius(monkey.SkillLevel);
        if (GridManager.Instance.GetManhattanDistance(monkey.Cell, target.Cell) > radius)
            return false;

        target.Inventory.RemoveAt(itemIndex);
        monkey.Inventory.Add(item);
        monkey.RefreshBagCapacity();
        target.RefreshBagCapacity();
        int cd = SkillInfo.GetMonkeyCooldownRounds(monkey.SkillLevel);
        monkey.SetSkillCooldown(cd);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(monkey.Role)} 抢夺获得 {RoleInfo.GetDisplayName(target.Role)} 的【{ItemInfo.GetDisplayName(item.Kind)}】（冷却{cd}回合）");
        GameManager.Instance?.CheckWinConditions();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryCatStealth(UnitActor unit)
    {
        if (!CanUseActiveSkill(unit, out string reason))
        {
            TurnManager.Instance?.LogFor(unit, reason);
            return false;
        }
        if (unit.Role != RoleType.Cat)
            return false;

        unit.ApplyHiddenStatus();
        int cd = SkillInfo.GetCatCooldownRounds(unit.SkillLevel);
        unit.SetSkillCooldown(cd);
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 发动【隐匿】（冷却{cd}回合）");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }
}

/// <summary>隐匿：邻接外不可见/不可选为目标；攻击或被攻击后解除；获得着火时解除；撞入隐匿格则弹回来向邻格（不解除隐匿）。</summary>
public static class StealthService
{
    public static void CheckAll() { }

    public static void CheckBreakFor(UnitActor unit) { }

    public static bool CanTargetDespiteHidden(UnitActor attacker, UnitActor defender)
    {
        if (defender == null || !defender.HasStatus(StatusType.Hidden))
            return true;
        if (attacker == null)
            return false;
        if (ItemInfo.CanRevealHidden(attacker))
            return true;
        return GridManager.Instance.GetManhattanDistance(attacker.Cell, defender.Cell) <= GameRulesConfig.HiddenRevealRange;
    }

    public static UnitActor GetOccupantUnit(Vector2Int cell)
    {
        var go = GridManager.Instance?.GetOccupant(cell);
        if (go == null)
            return null;
        var u = go.GetComponent<UnitActor>();
        if (u == null || u.IsDead)
            return null;
        return u;
    }

    /// <summary>对移动者可见的占格者会阻挡移动预判与落入。</summary>
    public static bool BlocksMovementFor(UnitActor mover, Vector2Int cell)
    {
        var occ = GetOccupantUnit(cell);
        if (occ == null || occ == mover)
            return false;
        return VisibilityService.CanSeeUnit(mover, occ);
    }

    /// <summary>
    /// 落入对移动者不可见的占格者所在格时，弹回「来向」邻格。
    /// 主轴优先；等距对角线则在两正交来向邻格中随机。
    /// </summary>
    public static Vector2Int GetBumpLandCell(Vector2Int from, Vector2Int occupied)
    {
        var grid = GridManager.Instance;
        int sx = from.x == occupied.x ? 0 : (from.x > occupied.x ? 1 : -1);
        int sy = from.y == occupied.y ? 0 : (from.y > occupied.y ? 1 : -1);

        var preferred = new System.Collections.Generic.List<Vector2Int>(2);
        if (sx != 0 && sy != 0)
        {
            int adx = Mathf.Abs(from.x - occupied.x);
            int ady = Mathf.Abs(from.y - occupied.y);
            var alongX = new Vector2Int(occupied.x + sx, occupied.y);
            var alongY = new Vector2Int(occupied.x, occupied.y + sy);
            if (adx == ady)
            {
                preferred.Add(alongX);
                preferred.Add(alongY);
            }
            else if (ady > adx)
                preferred.Add(alongY);
            else
                preferred.Add(alongX);
        }
        else if (sx != 0)
            preferred.Add(new Vector2Int(occupied.x + sx, occupied.y));
        else if (sy != 0)
            preferred.Add(new Vector2Int(occupied.x, occupied.y + sy));

        // 等距时打乱顺序再按可落点筛选
        if (preferred.Count == 2 && Random.value < 0.5f)
            (preferred[0], preferred[1]) = (preferred[1], preferred[0]);

        for (int i = 0; i < preferred.Count; i++)
        {
            if (IsValidBumpLand(grid, preferred[i], from, occupied))
                return preferred[i];
        }

        // 后备：其它来向邻格 / 任意空邻格
        var fallback = new System.Collections.Generic.List<Vector2Int>(4);
        if (sx != 0) fallback.Add(new Vector2Int(occupied.x + sx, occupied.y));
        if (sy != 0) fallback.Add(new Vector2Int(occupied.x, occupied.y + sy));
        fallback.Add(occupied + Vector2Int.up);
        fallback.Add(occupied + Vector2Int.down);
        fallback.Add(occupied + Vector2Int.left);
        fallback.Add(occupied + Vector2Int.right);
        for (int i = 0; i < fallback.Count; i++)
        {
            if (IsValidBumpLand(grid, fallback[i], from, occupied))
                return fallback[i];
        }

        return from;
    }

    private static bool IsValidBumpLand(GridManager grid, Vector2Int cell, Vector2Int from, Vector2Int occupied)
    {
        if (grid == null || !grid.IsValidCell(cell))
            return false;
        if (cell == occupied)
            return false;
        if (cell == from)
            return true;
        if (!grid.IsCellOccupied(cell))
            return true;
        return false;
    }
}

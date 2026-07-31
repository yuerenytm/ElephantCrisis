using System.Collections.Generic;
using UnityEngine;

public class UnitActor : MonoBehaviour
{
    public RoleType Role { get; private set; }
    public Vector2Int Cell { get; private set; }
    public Inventory Inventory { get; private set; }

    public int BaseMove { get; private set; }
    public int BaseAtk { get; private set; }
    public int BaseDef { get; private set; }
    public int MaxHp { get; private set; }
    public int Hp { get; private set; }
    public float BagCapacity { get; private set; }

    public int PermMove { get; private set; }
    public int PermAtk { get; private set; }
    public int PermDef { get; private set; }

    /// <summary>肾上腺素临时加成（按回合计数）。</summary>
    public int TempMove { get; private set; }
    public int TempAtk { get; private set; }
    public int AdrenalineRoundsLeft { get; private set; }

    public int FlamethrowerCooldown { get; set; }
    /// <summary>摩托车冲击冷却（完整回合倒数）。</summary>
    public int MotorcycleCooldown { get; set; }

    /// <summary>弩已蓄力，可在后续行动射击。</summary>
    public bool CrossbowCharged { get; private set; }
    /// <summary>本行动已对弩蓄力（同行动不可再射）。</summary>
    public bool CrossbowChargedThisAction { get; private set; }

    /// <summary>主动技能冷却（完整回合倒数）。象威慑无冷却。</summary>
    public int SkillCooldownLeft { get; private set; }

    /// <summary>人 Lv3 技能护盾：抵消 1 次物伤（非装备）。</summary>
    public int SkillShieldCharges { get; private set; }

    /// <summary>技能升级卡累计等级加成（每 3 张 +1，上限计入 SkillLevel）。</summary>
    public int SkillUpgradeBonus { get; private set; }

    /// <summary>本局是否已发动过领袖宣言。</summary>
    public bool HasUsedLeaderDeclaration { get; set; }

    private readonly List<StatusEffect> statuses = new List<StatusEffect>();
    public IReadOnlyList<StatusEffect> Statuses => statuses;

    public bool IsDying { get; private set; }
    public bool IsDead { get; private set; }
    public int DyingRoundsLeft { get; private set; }

    /// <summary>角色默认近战射程（不计入远程武器叠加）。</summary>
    public int BaseAttackRange => 1;

    /// <summary>基础能见度半径（虚拟时钟时段；夜视镜黑夜按 8）。</summary>
    public int BaseVisibility
    {
        get
        {
            int round = TurnManager.Instance != null ? TurnManager.Instance.RoundNumber : 1;
            int v = GameClock.GetBaseVisibility(round);
            if (GameClock.GetPeriod(round) == GameClock.Period.Night
                && FindEquippedIndex(ItemKind.NightVision) >= 0)
                v = GameClock.NightVisionVisibility;
            return v;
        }
    }

    /// <summary>当前能见度（曼哈顿半径）。致盲=0；濒死/中毒=1；再叠天气。</summary>
    public int CurrentVisibility
    {
        get
        {
            if (IsDead) return 0;
            if (HasStatus(StatusType.Blind)) return 0;
            if (IsDying) return 1;
            if (HasStatus(StatusType.Poison)) return 1;
            return Mathf.Max(0, WeatherService.GetVisibilityAfterWeather(BaseVisibility));
        }
    }

    /// <summary>近战射程 = 近战武器距离（无则默认 1）+ 外部增益（高地等）。</summary>
    public int AttackRange => GetMeleeWeaponRange() + GetRangeBonus();

    /// <summary>仅高地等外部增益，不含角色默认射程；供弓弩等叠加。</summary>
    public int GetRangeBonus()
    {
        var grid = GridManager.Instance;
        if (grid == null || !grid.IsValidCell(Cell))
            return 0;
        return TerrainInfo.GetRangeBonus(grid.GetTileType(Cell));
    }

    public int GetMeleeWeaponRange()
    {
        if (Inventory == null) return BaseAttackRange;
        foreach (var item in Inventory.Items)
        {
            if (!item.Equipped || !ItemInfo.IsMeleeWeapon(item.Kind))
                continue;
            return ItemInfo.GetMeleeWeaponRange(item.Kind);
        }
        return BaseAttackRange;
    }

    public int GetMeleeWeaponAtkBonus()
    {
        if (Inventory == null) return 0;
        foreach (var item in Inventory.Items)
        {
            if (!item.Equipped || !ItemInfo.IsMeleeWeapon(item.Kind))
                continue;
            return ItemInfo.GetMeleeWeaponAtkBonus(item.Kind);
        }
        return 0;
    }

    /// <summary>普攻物伤用的攻击力（含近战武器加成）。</summary>
    public int MeleeAtk => CurrentAtk + GetMeleeWeaponAtkBonus();

    public TileType CurrentTile
    {
        get
        {
            var grid = GridManager.Instance;
            if (grid == null || !grid.IsValidCell(Cell))
                return TileType.Normal;
            return grid.GetTileType(Cell);
        }
    }

    /// <summary>射击射程 = 武器射程贡献 + 外部增益（不含默认射程 1）。</summary>
    public int GetShootRange(ItemKind weapon)
    {
        return ItemInfo.GetWeaponRangeBonus(weapon) + GetRangeBonus();
    }

    public int Atk => BaseAtk + PermAtk + TempAtk + GetDollAtkBonus() + GetStatusAtkMod() + GetTerrainAtkMod();
    public int Def => BaseDef + PermDef + GetDollDefBonus() + (Inventory?.GetArmorDefenseBonus() ?? 0)
        + GetStatusDefMod() + GetTerrainDefMod() + WeatherService.GetDefMod();

    public int CurrentMove
    {
        get
        {
            if (IsDead) return 0;
            if (IsDying) return 1;
            int m = BaseMove + PermMove + TempMove + GetDollMoveBonus() + GetStatusMoveMod()
                + GetTerrainMoveMod() + GetVehicleMoveBonus();
            m -= GetDeterrencePenalty();
            return Mathf.Max(1, m);
        }
    }

    public int GetVehicleMoveBonus()
    {
        if (Inventory == null)
            return 0;
        int best = 0;
        foreach (var item in Inventory.Items)
        {
            if (!item.Equipped)
                continue;
            best = Mathf.Max(best, ItemInfo.GetVehicleMoveBonus(item.Kind));
        }
        return best;
    }

    public int CurrentAtk => IsDead ? 0 : Atk;
    public int CurrentDef => IsDead ? 0 : Def;

    public float CurrentBagCapacity => BagCapacity + GetDollBagBonus();

    /// <summary>基础 1 + 本命玩偶 +1 + 升级卡加成；上限 3。</summary>
    public int SkillLevel
    {
        get
        {
            int lv = 1 + SkillUpgradeBonus;
            if (Inventory != null && Inventory.Contains(RoleInfo.GetOwnDoll(Role)))
                lv++;
            return Mathf.Clamp(lv, 1, 3);
        }
    }

    public int GetEquippedWeaponAtkBonus()
    {
        if (Inventory == null) return 0;
        foreach (var item in Inventory.Items)
        {
            if (!item.Equipped || !ItemInfo.IsRangedWeapon(item.Kind))
                continue;
            return ItemInfo.GetWeaponAtkBonus(item.Kind);
        }
        return 0;
    }

    public int FindEquippedIndex(ItemKind kind)
    {
        if (Inventory == null) return -1;
        for (int i = 0; i < Inventory.Count; i++)
        {
            if (Inventory.Items[i].Kind == kind && Inventory.Items[i].Equipped)
                return i;
        }
        return -1;
    }

    public void ResetCrossbowActionFlags()
    {
        CrossbowChargedThisAction = false;
    }

    public bool TryChargeCrossbow()
    {
        if (IsDead || IsDying)
            return false;
        if (FindEquippedIndex(ItemKind.Crossbow) < 0)
            return false;
        if (CrossbowCharged)
            return false;
        CrossbowCharged = true;
        CrossbowChargedThisAction = true;
        return true;
    }

    public bool CanFireCrossbow()
        => CrossbowCharged && !CrossbowChargedThisAction && FindEquippedIndex(ItemKind.Crossbow) >= 0;

    public void ConsumeCrossbowCharge()
    {
        CrossbowCharged = false;
    }

    public int GetDollAtkBonus()
    {
        if (Inventory == null) return 0;
        int m = 0;
        if (Inventory.Contains(ItemKind.DollHuman)) m += 3;
        if (Inventory.Contains(ItemKind.DollMonkey)) m += 3;
        return m;
    }

    public int GetDollDefBonus()
    {
        if (Inventory == null) return 0;
        return Inventory.Contains(ItemKind.DollElephant) ? 3 : 0;
    }

    public int GetDollMoveBonus()
    {
        if (Inventory == null) return 0;
        int m = 0;
        if (Inventory.Contains(ItemKind.DollMonkey)) m += 1;
        if (Inventory.Contains(ItemKind.DollCat)) m += 2;
        return m;
    }

    public int GetDollBagBonus()
    {
        if (Inventory == null) return 0;
        return Inventory.Contains(ItemKind.DollHuman) ? 3 : 0;
    }

    public void RefreshBagCapacity()
    {
        Inventory?.SetCapacity(CurrentBagCapacity);
    }

    /// <summary>其他存活非濒死象的威慑光环。</summary>
    public int GetDeterrencePenalty()
    {
        var units = TurnManager.Instance?.Units;
        if (units == null) return 0;
        var grid = GridManager.Instance;
        if (grid == null) return 0;

        int penalty = 0;
        foreach (var other in units)
        {
            if (other == null || other == this || other.IsDead || other.IsDying)
                continue;
            if (other.Role != RoleType.Elephant)
                continue;
            int dist = grid.GetManhattanDistance(Cell, other.Cell);
            int lv = other.SkillLevel;
            int inner = SkillInfo.GetDeterrenceInnerRadius(lv);
            int outer = SkillInfo.GetDeterrenceOuterRadius(lv);
            if (inner > 0 && dist <= inner)
                penalty += 2;
            else if (dist <= outer)
                penalty += 1;
        }
        return penalty;
    }

    private int GetStatusAtkMod()
    {
        int m = 0;
        foreach (var s in statuses)
        {
            if (s.Type == StatusType.Poison) m -= 3;
            if (s.Type == StatusType.Leader) m += 9;
        }
        return m;
    }

    private int GetStatusDefMod()
    {
        int m = 0;
        foreach (var s in statuses)
        {
            if (s.Type == StatusType.Trip) m -= 3;
            if (s.Type == StatusType.Poison) m -= 3;
            if (s.Type == StatusType.Leader) m += 9;
        }
        return m;
    }

    private int GetStatusMoveMod()
    {
        int m = 0;
        foreach (var s in statuses)
        {
            if (s.Type == StatusType.Trip) m -= 1;
            if (s.Type == StatusType.Poison) m -= 1;
            if (s.Type == StatusType.Leader) m += 3;
        }
        return m;
    }

    private int GetTerrainMoveMod() => TerrainInfo.GetMoveMod(CurrentTile);
    private int GetTerrainAtkMod() => TerrainInfo.GetAtkMod(CurrentTile);
    private int GetTerrainDefMod() => TerrainInfo.GetDefMod(CurrentTile);

    /// <summary>进入格子时触发的地形效果（丛林隐匿等）。</summary>
    public void ApplyTerrainEnterEffects()
    {
        if (IsDead || IsDying)
            return;
        if (CurrentTile == TileType.Jungle)
        {
            ApplyHiddenStatus();
            TurnManager.Instance?.LogFor(this,
                $"{RoleInfo.GetDisplayName(Role)} 进入丛林，获得【隐匿】");
        }
    }

    /// <summary>行动开始：冰地 20% 跌倒。</summary>
    public void TickIceTerrainOnTurnStart()
    {
        if (IsDead || IsDying)
            return;
        if (CurrentTile != TileType.Ice)
            return;
        if (Random.value >= 0.2f)
            return;
        ApplyStatus(StatusType.Trip, 3, this);
        TurnManager.Instance?.Log(
            $"{RoleInfo.GetDisplayName(Role)} 在冰地上滑倒，获得【跌倒】3回合");
    }

    public bool HasStatus(StatusType type)
    {
        foreach (var s in statuses)
            if (s.Type == type) return true;
        return false;
    }

    public void ApplyStatus(StatusType type, int rounds, UnitActor source = null)
    {
        if (IsDead || IsDying || rounds <= 0)
            return;
        if (type == StatusType.Burning && WeatherService.BlocksBurning)
            return;

        StatusEffect next = source != null
            ? new StatusEffect(type, rounds, source.Role)
            : new StatusEffect(type, rounds);

        for (int i = 0; i < statuses.Count; i++)
        {
            if (statuses[i].Type != type)
                continue;
            int keep = Mathf.Max(statuses[i].RoundsLeft, rounds);
            if (source != null)
                statuses[i] = new StatusEffect(type, keep, source.Role);
            else if (statuses[i].HasSource)
                statuses[i] = new StatusEffect(type, keep, statuses[i].SourceRole);
            else
                statuses[i] = new StatusEffect(type, keep);
            RefreshVisual();
            return;
        }
        statuses.Add(next);
        RefreshVisual();
    }

    public void ClearStatus(StatusType type)
    {
        for (int i = statuses.Count - 1; i >= 0; i--)
        {
            if (statuses[i].Type != type)
                continue;
            statuses.RemoveAt(i);
        }
        RefreshVisual();
    }

    /// <summary>猫隐匿：直到攻击解除，不按回合倒数。</summary>
    public void ApplyHiddenStatus()
    {
        if (IsDead || IsDying)
            return;
        ClearStatus(StatusType.Hidden);
        statuses.Add(new StatusEffect(StatusType.Hidden, 99));
        RefreshVisual();
    }

    public void SetSkillCooldown(int rounds)
    {
        SkillCooldownLeft = Mathf.Max(0, rounds);
    }

    public void TickSkillCooldownOnFullRound()
    {
        if (SkillCooldownLeft > 0)
            SkillCooldownLeft--;
    }

    /// <summary>消耗 3 张技能升级卡，永久技能等级 +1（受上限 3 约束）。</summary>
    public bool TryConsumeSkillUpgradeCards(out string reason)
    {
        reason = null;
        if (IsDead || IsDying)
        {
            reason = "无法使用技能升级卡";
            return false;
        }
        if (Inventory == null || Inventory.CountOf(ItemKind.SkillUpgrade) < 3)
        {
            reason = "需集齐 3 张技能升级卡";
            return false;
        }
        if (SkillLevel >= 3)
        {
            reason = "技能等级已达上限";
            return false;
        }

        for (int i = 0; i < 3; i++)
        {
            if (!Inventory.Remove(ItemKind.SkillUpgrade))
            {
                reason = "技能升级卡不足";
                return false;
            }
            DeckManager.Instance?.AddToDiscard(ItemKind.SkillUpgrade);
        }

        SkillUpgradeBonus++;
        RefreshBagCapacity();
        RefreshVisual();
        return true;
    }

    /// <summary>人·强化效果。返回战报用短描述。</summary>
    public string ApplySkillReinforce(StatBoost boost)
    {
        string detail;
        switch (boost)
        {
            case StatBoost.Attack:
                PermAtk += 1;
                detail = "攻击+1";
                break;
            case StatBoost.Defense:
                PermDef += 1;
                detail = "防御+1";
                break;
            case StatBoost.Move:
                PermMove += 1;
                detail = "移动+1";
                break;
            case StatBoost.Draw:
                DeckManager.Instance?.DrawFor(this);
                detail = "摸1张牌";
                break;
            default:
                detail = "无效果";
                break;
        }
        if (SkillLevel >= 3)
            SkillShieldCharges = Mathf.Max(SkillShieldCharges, 1);
        RefreshVisual();
        return detail;
    }

    public void ClearAllStatuses()
    {
        statuses.Clear();
    }

    private SpriteRenderer bodyRenderer;
    private SpriteRenderer outlineRenderer;
    private TextMesh label;
    private bool worldVisible = true;

    public void Setup(RoleType role, Vector2Int startCell)
    {
        Role = role;
        RoleInfo.GetBaseStats(role, out int move, out int hp, out int atk, out int def, out int bag);
        BaseMove = move;
        BaseAtk = atk;
        BaseDef = def;
        MaxHp = hp;
        Hp = hp;
        BagCapacity = bag;
        PermMove = 0;
        PermAtk = 0;
        PermDef = 0;
        TempMove = 0;
        TempAtk = 0;
        AdrenalineRoundsLeft = 0;
        FlamethrowerCooldown = 0;
        MotorcycleCooldown = 0;
        CrossbowCharged = false;
        CrossbowChargedThisAction = false;
        SkillCooldownLeft = 0;
        SkillShieldCharges = 0;
        SkillUpgradeBonus = 0;
        HasUsedLeaderDeclaration = false;
        statuses.Clear();
        Inventory = new Inventory(bag);
        RefreshBagCapacity();

        IsDying = false;
        IsDead = false;
        DyingRoundsLeft = 0;

        EnsureVisuals();
        PlaceAt(startCell, true);
        RefreshVisual();
    }

    private void EnsureVisuals()
    {
        outlineRenderer = CreateChildSprite("Outline", 0);
        bodyRenderer = CreateChildSprite("Body", 1);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(transform, false);
        // 角色名叠在棋子中央
        labelGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        label = labelGo.AddComponent<TextMesh>();
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.characterSize = 0.22f;
        label.fontSize = 64;
        label.color = Color.white;
        label.fontStyle = FontStyle.Bold;
        var labelRenderer = labelGo.GetComponent<MeshRenderer>();
        if (labelRenderer != null)
            labelRenderer.sortingOrder = 10;
    }

    private SpriteRenderer CreateChildSprite(string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        return sr;
    }

    public void PlaceAt(Vector2Int cell, bool register)
    {
        var grid = GridManager.Instance;
        if (register && grid.IsValidCell(Cell) && grid.GetOccupant(Cell) == gameObject)
            grid.ClearCell(Cell);

        Cell = cell;
        transform.position = grid.CellToWorld(cell);

        if (register)
            grid.SetCellOccupied(cell, gameObject);
    }

    /// <summary>尝试移动。若撞上不可见的隐匿占格者，会弹回来向邻格并破隐；LastMoveBumped 供日志。</summary>
    public bool LastMoveBumped { get; private set; }
    public Vector2Int LastMoveIntendedCell { get; private set; }
    public UnitActor LastMoveBumpedUnit { get; private set; }

    public bool TryMoveTo(Vector2Int target)
    {
        LastMoveBumped = false;
        LastMoveIntendedCell = target;
        LastMoveBumpedUnit = null;

        if (IsDead)
            return false;

        var grid = GridManager.Instance;
        if (!grid.IsValidCell(target))
            return false;
        if (StealthService.BlocksMovementFor(this, target))
            return false;
        if (grid.GetManhattanDistance(Cell, target) > CurrentMove)
            return false;
        if (!VisibilityService.CanMoveTo(this, target))
            return false;

        Vector2Int from = Cell;
        var invisibleOcc = StealthService.GetOccupantUnit(target);
        if (invisibleOcc != null && invisibleOcc != this
            && !VisibilityService.CanSeeUnit(this, invisibleOcc))
        {
            // 不解除对方隐匿：弹至邻格后仅邻接者可见，避免把位置广播给全场
            Vector2Int land = StealthService.GetBumpLandCell(from, target);
            PlaceAt(land, true);
            ApplyTerrainEnterEffects();
            LastMoveBumped = true;
            LastMoveBumpedUnit = invisibleOcc;
            LastMoveIntendedCell = target;
            return true;
        }

        if (grid.IsCellOccupied(target))
            return false;

        PlaceAt(target, true);
        ApplyTerrainEnterEffects();
        return true;
    }

    public void Heal(int amount)
    {
        if (IsDead || amount <= 0)
            return;

        bool wasDying = IsDying;
        Hp = Mathf.Min(MaxHp, Hp + amount);
        if (wasDying && Hp >= 1)
        {
            IsDying = false;
            DyingRoundsLeft = 0;
            // 恢复基础值 + 永久强化；玩偶/甲若已掉落则不生效
            RefreshVisual();
        }
        else
            RefreshVisual();
    }

    /// <summary>
    /// amount 为伤害基础值。
    /// 物伤：护盾 → 减防 → 耗甲耐久。
    /// 法伤：护盾可吸收；不减防、不耗甲（法抗未实装）。
    /// 真伤：不减防、不耗甲、护盾不可吸收，直接扣血。
    /// </summary>
    public int TakeDamage(int amount, bool magicDamage = false, bool trueDamage = false)
    {
        if (IsDead || amount < 0)
            return 0;

        // 能量护盾 / 技能护盾：仅吸收物伤与法伤，不挡真伤
        if (!trueDamage && !IsDying)
        {
            if (SkillShieldCharges > 0)
            {
                SkillShieldCharges--;
                TurnManager.Instance?.Log(
                    $"{RoleInfo.GetDisplayName(Role)} 的技能护盾吸收了伤害");
                RefreshVisual();
                return 0;
            }

            if (Inventory != null && Inventory.TryConsumeShieldCharge(out bool shieldBroken))
            {
                if (shieldBroken)
                    DeckManager.Instance?.AddToDiscard(ItemKind.EnergyShield);
                TurnManager.Instance?.Log(
                    $"{RoleInfo.GetDisplayName(Role)} 的能量护盾吸收了伤害" +
                    (shieldBroken ? "（该护盾耗尽）" : ""));
                RefreshVisual();
                return 0;
            }
        }

        int applied = (magicDamage || trueDamage) ? amount : Mathf.Max(0, amount - CurrentDef);

        // 木甲/铁甲：仅物伤耗耐久
        if (!magicDamage && !trueDamage && !IsDying && Inventory != null &&
            Inventory.TryWearArmor(out bool armorBroken, out var brokenArmor) && armorBroken)
        {
            DeckManager.Instance?.AddToDiscard(brokenArmor);
            TurnManager.Instance?.Log($"{RoleInfo.GetDisplayName(Role)} 的【{ItemInfo.GetDisplayName(brokenArmor)}】损坏");
        }

        if (applied <= 0)
            return 0;

        if (IsDying)
        {
            Die();
            return applied;
        }

        Hp -= applied;
        if (Hp < 1)
            EnterDying();
        RefreshVisual();
        return applied;
    }

    public void TickStatusOnFullRound()
    {
        if (AdrenalineRoundsLeft > 0)
        {
            AdrenalineRoundsLeft--;
            if (AdrenalineRoundsLeft <= 0)
            {
                TempMove = 0;
                TempAtk = 0;
                TurnManager.Instance?.LogFor(this, $"{RoleInfo.GetDisplayName(Role)} 的肾上腺素效果结束");
            }
        }

        if (FlamethrowerCooldown > 0)
            FlamethrowerCooldown--;
        if (MotorcycleCooldown > 0)
            MotorcycleCooldown--;

        for (int i = statuses.Count - 1; i >= 0; i--)
        {
            var s = statuses[i];
            if (s.HasSource)
                continue; // 有施加者：在施加者行动开始时倒数
            if (s.Type == StatusType.Hidden || s.Type == StatusType.Burning)
                continue;
            s.RoundsLeft--;
            if (s.RoundsLeft <= 0)
            {
                TurnManager.Instance?.LogFor(this,
                    $"{RoleInfo.GetDisplayName(Role)} 的【{StatusInfo.GetDisplayName(s.Type)}】结束");
                statuses.RemoveAt(i);
            }
            else
                statuses[i] = s;
        }
        RefreshVisual();
    }

    /// <summary>施加者行动开始：倒数由其施加的状态。</summary>
    public void TickStatusesFromApplier(RoleType applier)
    {
        for (int i = statuses.Count - 1; i >= 0; i--)
        {
            var s = statuses[i];
            if (!s.HasSource || s.SourceRole != applier)
                continue;
            if (s.Type == StatusType.Burning || s.Type == StatusType.Leader)
                continue; // 着火/领袖在受害者行动开始 DoT 时自行倒数
            s.RoundsLeft--;
            if (s.RoundsLeft <= 0)
            {
                TurnManager.Instance?.LogFor(this,
                    $"{RoleInfo.GetDisplayName(Role)} 的【{StatusInfo.GetDisplayName(s.Type)}】结束");
                statuses.RemoveAt(i);
            }
            else
                statuses[i] = s;
        }
        RefreshVisual();
    }

    /// <summary>
    /// 行动开始着火：先造成法伤，再按持续倒数（优先于熔岩等其它行动开始结算）。
    /// </summary>
    public void TickBurningOnTurnStart()
    {
        if (!HasStatus(StatusType.Burning) || IsDead)
            return;
        int dealt = TakeDamage(10, magicDamage: true);
        TurnManager.Instance?.Log(
            $"{RoleInfo.GetDisplayName(Role)} 处于着火，受到 {dealt} 点法伤");

        for (int i = statuses.Count - 1; i >= 0; i--)
        {
            if (statuses[i].Type != StatusType.Burning)
                continue;
            var s = statuses[i];
            s.RoundsLeft--;
            if (s.RoundsLeft <= 0)
            {
                TurnManager.Instance?.LogFor(this,
                    $"{RoleInfo.GetDisplayName(Role)} 的【着火】结束");
                statuses.RemoveAt(i);
            }
            else
                statuses[i] = s;
            break;
        }
        RefreshVisual();
    }

    /// <summary>
    /// 行动开始领袖：着火之后结算；造成 6 真伤并倒数持续。
    /// </summary>
    public void TickLeaderOnTurnStart()
    {
        if (!HasStatus(StatusType.Leader) || IsDead)
            return;
        int dealt = TakeDamage(6, trueDamage: true);
        TurnManager.Instance?.Log(
            $"{RoleInfo.GetDisplayName(Role)} 处于领袖，受到 {dealt} 点真伤");

        for (int i = statuses.Count - 1; i >= 0; i--)
        {
            if (statuses[i].Type != StatusType.Leader)
                continue;
            var s = statuses[i];
            s.RoundsLeft--;
            if (s.RoundsLeft <= 0)
            {
                TurnManager.Instance?.LogFor(this,
                    $"{RoleInfo.GetDisplayName(Role)} 的【领袖】结束");
                statuses.RemoveAt(i);
            }
            else
                statuses[i] = s;
            break;
        }
        RefreshVisual();
    }

    public bool TryApplyAdrenaline()
    {
        if (IsDead || IsDying)
            return false;
        if (Hp * 10 >= MaxHp * 3)
            return false;

        TempMove = 2;
        TempAtk = 3;
        AdrenalineRoundsLeft = 3;
        RefreshVisual();
        return true;
    }

    public void EnterDying()
    {
        if (IsDead || IsDying)
            return;

        IsDying = true;
        DyingRoundsLeft = 3; // 含当回合起算，每过完 1 回合 -1
        Hp = 0;
        // 其余属性与已有状态保留；仅强制移速口径见 CurrentMove
        DropAllItems();
        RefreshVisual();
    }

    // 每回合推进时调用（四人都行动完）
    public void TickDyingOnFullRound()
    {
        if (!IsDying || IsDead)
            return;

        DyingRoundsLeft--;
        if (DyingRoundsLeft <= 0)
            Die();
        else
            RefreshVisual();
    }

    public void Die()
    {
        if (IsDead)
            return;

        IsDead = true;
        IsDying = false;
        Hp = 0;
        DropAllItems();

        var grid = GridManager.Instance;
        if (grid != null && grid.GetOccupant(Cell) == gameObject)
            grid.ClearCell(Cell);

        RefreshVisual();
        gameObject.SetActive(false);
    }

    public void DropAllItems()
    {
        if (Inventory == null || Inventory.Count == 0)
            return;
        var dropped = Inventory.TakeAll();
        GroundItemManager.Instance?.DropItems(Cell, dropped);
    }

    public void ApplyPermanentBoost(StatBoost boost, int value)
    {
        if (IsDead || IsDying || value == 0)
            return;

        switch (boost)
        {
            case StatBoost.Attack: PermAtk += value; break;
            case StatBoost.Defense: PermDef += value; break;
            case StatBoost.Move: PermMove += value; break;
        }
        RefreshVisual();
    }

    public void SetSelected(bool selected)
    {
        if (outlineRenderer == null)
            return;
        outlineRenderer.enabled = selected;
        outlineRenderer.color = selected ? Color.white : Color.clear;
        outlineRenderer.transform.localScale = Vector3.one * 1.25f;
    }

    /// <summary>迷雾：非视野内单位隐藏渲染（逻辑仍在场上）。</summary>
    public void SetWorldVisible(bool visible)
    {
        worldVisible = visible;
        ApplyWorldVisible();
    }

    private void ApplyWorldVisible()
    {
        if (bodyRenderer != null)
            bodyRenderer.enabled = worldVisible;
        if (outlineRenderer != null && !worldVisible)
            outlineRenderer.enabled = false;
        if (label != null)
        {
            bool showLabel = worldVisible && !RolePortrait.IsArtSprite(Role);
            label.gameObject.SetActive(showLabel);
        }
    }

    public void RefreshVisual()
    {
        if (bodyRenderer == null)
            return;

        var portrait = RolePortrait.GetSprite(Role);
        bodyRenderer.sprite = portrait;
        bodyRenderer.color = IsDying ? new Color(1f, 0.55f, 0.55f, 1f) : Color.white;
        bodyRenderer.transform.localScale = Vector3.one * 0.85f;

        outlineRenderer.sprite = SpriteFactory.CreateColorSprite(new Color(1f, 1f, 1f, 0.85f));
        outlineRenderer.transform.localScale = Vector3.one * 1.15f;
        if (!outlineRenderer.enabled)
            outlineRenderer.color = Color.clear;

        // 有美术图时可不叠字；占位剪影时在棋子上显示 象/人/猴/猫（不含 HP）
        bool useArt = RolePortrait.IsArtSprite(Role);
        if (useArt)
            label.text = "";
        else
        {
            label.text = RoleInfo.GetDisplayName(Role);
            label.color = IsDying ? new Color(1f, 0.85f, 0.85f) : Color.white;
        }
        ApplyWorldVisible();
    }

    public string GetStatusText()
    {
        if (IsDead) return "已死亡";
        if (IsDying) return $"濒死(剩余{DyingRoundsLeft}回合) 能见度1";
        return $"移{CurrentMove} 血{Hp}/{MaxHp} 攻{CurrentAtk} 防{CurrentDef} 视{CurrentVisibility} 包{Inventory.UsedWeight:0.##}/{CurrentBagCapacity:0.##} 偶{Inventory.CountDolls()} 技{SkillInfo.GetSkillName(Role)}Lv{SkillLevel}";
    }

    public string GetHoverStatusText()
    {
        if (IsDead)
            return $"{RoleInfo.GetDisplayName(Role)}  ·  已死亡";
        if (IsDying)
            return $"{RoleInfo.GetDisplayName(Role)}  ·  濒死（剩余{DyingRoundsLeft}回合）  能见度1  移{CurrentMove}";

        return $"{RoleInfo.GetDisplayName(Role)}  ·  " +
               $"移 {CurrentMove}   血 {Hp}/{MaxHp}   攻 {CurrentAtk}   防 {CurrentDef}   视 {CurrentVisibility}   " +
               $"包 {Inventory.UsedWeight:0.##}/{CurrentBagCapacity:0.##}   玩偶 {Inventory.CountDolls()}   " +
               $"{SkillInfo.GetSkillName(Role)} Lv{SkillLevel}" +
               (SkillCooldownLeft > 0 ? $" CD{SkillCooldownLeft}" : "") +
               (SkillShieldCharges > 0 ? " 技能护盾" : "") +
               (AdrenalineRoundsLeft > 0 ? $"   肾上腺素{AdrenalineRoundsLeft}回合" : "") +
               StatusSuffix();
    }

    private string StatusSuffix()
    {
        if (statuses.Count == 0)
            return "";
        var parts = new List<string>();
        foreach (var s in statuses)
            parts.Add($"{StatusInfo.GetDisplayName(s.Type)}{s.RoundsLeft}");
        return "   " + string.Join(" ", parts);
    }
}

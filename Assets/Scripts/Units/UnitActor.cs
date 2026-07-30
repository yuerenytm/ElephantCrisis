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

    /// <summary>肾上腺素临时加成（完整回合计数）。</summary>
    public int TempMove { get; private set; }
    public int TempAtk { get; private set; }
    public int AdrenalineRoundsLeft { get; private set; }

    public int FlamethrowerCooldown { get; set; }

    private readonly List<StatusEffect> statuses = new List<StatusEffect>();
    public IReadOnlyList<StatusEffect> Statuses => statuses;

    public bool IsDying { get; private set; }
    public bool IsDead { get; private set; }
    public int DyingRoundsLeft { get; private set; }

    /// <summary>角色默认近战射程（不计入远程武器叠加）。</summary>
    public int BaseAttackRange => 1;

    /// <summary>近战射程 = 默认射程 + 外部增益（高地等）。</summary>
    public int AttackRange => BaseAttackRange + GetRangeBonus();

    /// <summary>仅高地等外部增益，不含角色默认射程；供弓弩等叠加。</summary>
    public int GetRangeBonus()
    {
        var grid = GridManager.Instance;
        if (grid == null || !grid.IsValidCell(Cell))
            return 0;
        if (grid.GetTileType(Cell) == TileType.Highland)
            return 1;
        return 0;
    }

    /// <summary>射击射程 = 武器射程贡献 + 外部增益（不含默认射程 1）。</summary>
    public int GetShootRange(ItemKind weapon)
    {
        return ItemInfo.GetWeaponRangeBonus(weapon) + GetRangeBonus();
    }

    public int Atk => BaseAtk + PermAtk + TempAtk + GetStatusAtkMod();
    public int Def => BaseDef + PermDef + (Inventory?.GetArmorDefenseBonus() ?? 0) + GetStatusDefMod();

    public int CurrentMove
    {
        get
        {
            if (IsDead) return 0;
            if (IsDying) return 1;
            return Mathf.Max(1, BaseMove + PermMove + TempMove + GetStatusMoveMod());
        }
    }

    public int CurrentAtk => IsDying || IsDead ? 0 : Atk;
    public int CurrentDef => IsDying || IsDead ? 0 : Def;

    private int GetStatusAtkMod()
    {
        int m = 0;
        foreach (var s in statuses)
            if (s.Type == StatusType.Poison) m -= 3;
        return m;
    }

    private int GetStatusDefMod()
    {
        int m = 0;
        foreach (var s in statuses)
        {
            if (s.Type == StatusType.Trip) m -= 3;
            if (s.Type == StatusType.Poison) m -= 3;
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
        }
        return m;
    }

    public bool HasStatus(StatusType type)
    {
        foreach (var s in statuses)
            if (s.Type == type) return true;
        return false;
    }

    public void ApplyStatus(StatusType type, int rounds)
    {
        if (IsDead || IsDying || rounds <= 0)
            return;
        for (int i = 0; i < statuses.Count; i++)
        {
            if (statuses[i].Type != type)
                continue;
            // 不可叠加：刷新持续时间
            statuses[i] = new StatusEffect(type, Mathf.Max(statuses[i].RoundsLeft, rounds));
            RefreshVisual();
            return;
        }
        statuses.Add(new StatusEffect(type, rounds));
        RefreshVisual();
    }

    public void ClearAllStatuses()
    {
        statuses.Clear();
    }

    private SpriteRenderer bodyRenderer;
    private SpriteRenderer outlineRenderer;
    private TextMesh label;

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
        statuses.Clear();
        Inventory = new Inventory(bag);
        Inventory.Add(RoleInfo.GetOwnDoll(role));

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

    public bool TryMoveTo(Vector2Int target)
    {
        if (IsDead)
            return false;

        var grid = GridManager.Instance;
        if (!grid.IsValidCell(target) || grid.IsCellOccupied(target))
            return false;
        if (grid.GetManhattanDistance(Cell, target) > CurrentMove)
            return false;

        PlaceAt(target, true);
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
    /// amount 为伤害基础值。物伤先经能量护盾，再减防；最终扣血为 0 时不算「受到攻击」。
    /// 真伤不耗甲、不触发护盾。
    /// </summary>
    public int TakeDamage(int amount, bool trueDamage = false)
    {
        if (IsDead || amount < 0)
            return 0;

        if (!trueDamage && !IsDying)
        {
            if (Inventory != null && Inventory.TryConsumeShieldCharge(out bool shieldBroken))
            {
                if (shieldBroken)
                    DeckManager.Instance?.AddToDiscard(ItemKind.EnergyShield);
                // 护盾挡伤是战斗公开结果
                TurnManager.Instance?.Log(
                    $"{RoleInfo.GetDisplayName(Role)} 的能量护盾吸收了物伤" +
                    (shieldBroken ? "（该护盾耗尽）" : ""));
                RefreshVisual();
                return 0;
            }
        }

        int applied = trueDamage ? amount : Mathf.Max(0, amount - CurrentDef);
        if (applied <= 0)
            return 0;

        if (!trueDamage && !IsDying && Inventory != null &&
            Inventory.TryWearArmor(out bool armorBroken, out var brokenArmor) && armorBroken)
        {
            DeckManager.Instance?.AddToDiscard(brokenArmor);
            TurnManager.Instance?.Log($"{RoleInfo.GetDisplayName(Role)} 的【{ItemInfo.GetDisplayName(brokenArmor)}】损坏");
        }

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

        for (int i = statuses.Count - 1; i >= 0; i--)
        {
            var s = statuses[i];
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

    /// <summary>行动回合开始的着火结算。</summary>
    public void TickBurningOnTurnStart()
    {
        if (!HasStatus(StatusType.Burning) || IsDead)
            return;
        int dealt = TakeDamage(15, trueDamage: false);
        TurnManager.Instance?.Log(
            $"{RoleInfo.GetDisplayName(Role)} 处于着火，受到 {dealt} 点物伤");
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
        DyingRoundsLeft = 3; // 含当完整回合起算，每过完一轮 -1
        Hp = 0;
        TempMove = 0;
        TempAtk = 0;
        AdrenalineRoundsLeft = 0;
        ClearAllStatuses();
        DropAllItems();
        RefreshVisual();
    }

    // 完整回合推进时调用（四人都行动完）
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
        {
            label.text = "";
            label.gameObject.SetActive(false);
        }
        else
        {
            label.gameObject.SetActive(true);
            label.text = RoleInfo.GetDisplayName(Role);
            label.color = IsDying ? new Color(1f, 0.85f, 0.85f) : Color.white;
        }
    }

    public string GetStatusText()
    {
        if (IsDead) return "已死亡";
        if (IsDying) return $"濒死(剩余{DyingRoundsLeft}完整回合) 能见度1";
        return $"移{CurrentMove} 血{Hp}/{MaxHp} 攻{CurrentAtk} 防{CurrentDef} 包{Inventory.UsedWeight:0.##}/{BagCapacity} 偶{Inventory.CountDolls()}";
    }

    public string GetHoverStatusText()
    {
        if (IsDead)
            return $"{RoleInfo.GetDisplayName(Role)}  ·  已死亡";
        if (IsDying)
            return $"{RoleInfo.GetDisplayName(Role)}  ·  濒死（剩余{DyingRoundsLeft}完整回合）  能见度1  移{CurrentMove}";

        return $"{RoleInfo.GetDisplayName(Role)}  ·  " +
               $"移 {CurrentMove}   血 {Hp}/{MaxHp}   攻 {CurrentAtk}   防 {CurrentDef}   " +
               $"包 {Inventory.UsedWeight:0.##}/{BagCapacity}   玩偶 {Inventory.CountDolls()}" +
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

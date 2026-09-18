using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 逻辑对局 JSONL 日志（与 Tools/sim events.jsonl / meta.json schema 对齐）。
/// 仅 LogicSim 模式写入。
/// </summary>
public sealed class LogicMatchLogger
{
    public static LogicMatchLogger Active { get; private set; }
    public static bool IsRecording => Active != null;
    /// <summary>是否在写逐事件 events.jsonl（balance 模式为 false，仅记结果）。</summary>
    public static bool IsRecordingEvents => Active != null && Active.RecordEvents;

    private readonly List<string> lines = new List<string>(512);
    private int t;
    private readonly Dictionary<string, string> strategies;
    private readonly Dictionary<string, int> deathRounds = new Dictionary<string, int>();
    private readonly int seed;
    private readonly string collect;
    private string winner;
    private string reason = "unknown";
    private int fullRounds;
    private bool finished;

    /// <summary>是否写逐事件 events.jsonl（balance 模式为 false）。</summary>
    public bool RecordEvents => collect != "balance";

    public LogicMatchLogger(int seed, Dictionary<string, string> strategies, string collect = "both")
    {
        this.seed = seed;
        this.strategies = strategies ?? new Dictionary<string, string>();
        this.collect = collect ?? "both";
        Active = this;
        EmitRaw("match_start", Obj(
            ("seed", seed.ToString(CultureInfo.InvariantCulture)),
            ("strategies", Dict(this.strategies))));
    }

    public static void ClearActive()
    {
        if (Active != null)
            Active = null;
    }

    public void Emit(string type, params (string key, string jsonValue)[] fields)
    {
        EmitRaw(type, Obj(fields));
    }

    public void EmitTurnStart(UnitActor unit)
    {
        if (unit == null) return;
        var tm = TurnManager.Instance;
        Emit("turn_start",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("round", I(tm != null ? tm.RoundNumber : 1)),
            ("hour", I(GameClock.GetHour(tm != null ? tm.RoundNumber : 1))));
    }

    public void EmitTurnEnd(UnitActor unit, bool skipped)
    {
        Emit("turn_end",
            ("actor", unit != null ? Q(LogicSimNaming.Role(unit.Role)) : "null"),
            ("skipped", skipped ? "true" : "false"));
    }

    public void EmitStunSkip(UnitActor unit)
    {
        Emit("stun_skip", ("actor", Q(LogicSimNaming.Role(unit.Role))));
    }

    public void EmitDraw(UnitActor unit, ItemKind item)
    {
        Emit("draw",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("item", Q(LogicSimNaming.Item(item))));
    }

    public void EmitMove(UnitActor unit, Vector2Int from, Vector2Int to)
    {
        Emit("move",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("from_cell", Cell(from)),
            ("to_cell", Cell(to)));
    }

    public void EmitDamage(string actor, string target, string kind, int raw, int dealt, string via = null)
    {
        var fields = new List<(string, string)>
        {
            ("actor", Q(actor ?? "")),
            ("target", Q(target ?? "")),
            ("kind", Q(kind ?? "physical")),
            ("raw", I(raw)),
            ("dealt", I(dealt))
        };
        if (!string.IsNullOrEmpty(via))
            fields.Add(("via", Q(via)));
        EmitRaw("damage", Obj(fields.ToArray()));
    }

    public void EmitHeal(UnitActor unit, ItemKind item, int healed, int hpAfter)
    {
        Emit("heal",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("item", Q(LogicSimNaming.Item(item))),
            ("healed", I(healed)),
            ("hp_after", I(hpAfter)));
    }

    public void EmitBomb(UnitActor unit, Vector2Int cell)
    {
        Emit("bomb",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("cell", Cell(cell)));
    }

    public void EmitEquip(UnitActor unit, ItemKind item)
    {
        Emit("equip",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("item", Q(LogicSimNaming.Item(item))));
    }

    public void EmitPickup(UnitActor unit, int count)
    {
        Emit("pickup",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("count", I(count)));
    }

    public void EmitDiscard(UnitActor unit, ItemKind item)
    {
        Emit("discard",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("item", Q(LogicSimNaming.Item(item))));
    }

    public void EmitDrop(UnitActor unit, int count)
    {
        Emit("drop",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("count", I(count)));
    }

    public void EmitOverweight(UnitActor unit)
    {
        Emit("overweight",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("weight", F(unit.Inventory.UsedWeight)),
            ("cap", F(unit.Inventory.Capacity)));
    }

    public void EmitStatus(UnitActor unit, StatusType status, string op)
    {
        Emit("status",
            ("actor", Q(LogicSimNaming.Role(unit.Role))),
            ("status", Q(LogicSimNaming.Status(status))),
            ("op", Q(op)));
    }

    public void EmitDying(UnitActor unit)
    {
        Emit("dying", ("actor", Q(LogicSimNaming.Role(unit.Role))));
    }

    public void EmitDeath(UnitActor unit)
    {
        string role = LogicSimNaming.Role(unit.Role);
        int full = TurnManager.Instance != null
            ? Mathf.Max(0, TurnManager.Instance.RoundNumber - 1)
            : 0;
        deathRounds[role] = full;
        Emit("death", ("actor", Q(role)));
    }

    public void EmitClock(int hour, int fullRound)
    {
        Emit("clock", ("hour", I(hour)), ("full_round", I(fullRound)));
    }

    public void EmitWeather()
    {
        int round = TurnManager.Instance != null ? TurnManager.Instance.RoundNumber : 1;
        int lockLeft = Mathf.Max(0, WeatherService.NextChangeRound - round);
        Emit("weather",
            ("weather", Q(LogicSimNaming.Weather(WeatherService.Current))),
            ("lock", I(lockLeft)));
    }

    public void EmitLavaShrink(int inset)
    {
        Emit("lava_shrink", ("inset", I(inset)));
    }

    public void EmitSnapshot()
    {
        var tm = TurnManager.Instance;
        var grid = GridManager.Instance;
        int round = tm != null ? Mathf.Max(0, tm.RoundNumber - 1) : 0;
        int hour = GameClock.GetHour(tm != null ? tm.RoundNumber : 1);
        int lava = grid != null ? grid.LavaInset : 0;
        int deck = DeckManager.Instance != null ? DeckManager.Instance.DrawCount : 0;

        var unitsJson = new StringBuilder();
        unitsJson.Append('[');
        bool first = true;
        IEnumerable<UnitActor> units = GameManager.Instance != null
            ? (IEnumerable<UnitActor>)GameManager.Instance.Units
            : tm?.Units;
        if (units != null)
        {
            foreach (var u in units)
            {
                if (u == null) continue;
                if (!first) unitsJson.Append(',');
                first = false;
                unitsJson.Append(BuildUnitJson(u, grid));
            }
        }
        unitsJson.Append(']');

        EmitRaw("snapshot", Obj(
            ("round", I(round)),
            ("weather", Q(LogicSimNaming.Weather(WeatherService.Current))),
            ("hour", I(hour)),
            ("lava_inset", I(lava)),
            ("deck_draw", I(deck)),
            ("units", unitsJson.ToString())));
    }

    public void FinishMatch(string winnerRole, string reason, int fullRoundsCompleted)
    {
        if (finished)
            return;
        finished = true;
        winner = winnerRole;
        this.reason = reason ?? "unknown";
        fullRounds = fullRoundsCompleted;
        Emit("match_end",
            ("winner", winnerRole != null ? Q(winnerRole) : "null"),
            ("reason", Q(this.reason)),
            ("full_rounds", I(fullRounds)));
    }

    public void Save(string matchDir)
    {
        Directory.CreateDirectory(matchDir);
        if (RecordEvents)
        {
            var eventsPath = Path.Combine(matchDir, "events.jsonl");
            File.WriteAllLines(eventsPath, lines, new UTF8Encoding(false));
        }

        var meta = new StringBuilder();
        meta.Append("{\n");
        meta.Append("  \"seed\": ").Append(seed).Append(",\n");
        meta.Append("  \"strategies\": ").Append(Dict(strategies)).Append(",\n");
        meta.Append("  \"winner\": ").Append(winner != null ? Q(winner) : "null").Append(",\n");
        meta.Append("  \"reason\": ").Append(Q(reason)).Append(",\n");
        meta.Append("  \"full_rounds\": ").Append(fullRounds).Append(",\n");
        meta.Append("  \"events\": ").Append(RecordEvents ? lines.Count : 0).Append(",\n");
        meta.Append("  \"collect\": ").Append(Q(collect)).Append(",\n");
        meta.Append("  \"per_role\": ").Append(PerRoleJson()).Append(",\n");
        meta.Append("  \"source\": \"game_logic_sim\"\n");
        meta.Append("}\n");
        File.WriteAllText(Path.Combine(matchDir, "meta.json"), meta.ToString(), new UTF8Encoding(false));

        if (Active == this)
            Active = null;
    }

    private string PerRoleJson()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var kv in strategies)
        {
            if (!first) sb.Append(',');
            first = false;
            string role = kv.Key;
            bool survived = !deathRounds.ContainsKey(role);
            string deathRound = survived ? "null" : I(deathRounds[role]);
            sb.Append(Q(role)).Append(":{")
                .Append("\"death_round\":").Append(deathRound)
                .Append(",\"survived\":").Append(B(survived))
                .Append('}');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private void EmitRaw(string type, string bodyFieldsObject)
    {
        if (!RecordEvents)
            return;
        t++;
        // bodyFieldsObject is {...} without outer type/t
        var sb = new StringBuilder(bodyFieldsObject.Length + 48);
        sb.Append("{\"t\":").Append(t).Append(",\"type\":").Append(Q(type));
        if (bodyFieldsObject.Length > 2)
        {
            // strip { }
            sb.Append(',').Append(bodyFieldsObject, 1, bodyFieldsObject.Length - 2);
        }
        sb.Append('}');
        lines.Add(sb.ToString());
    }

    private static string BuildUnitJson(UnitActor u, GridManager grid)
    {
        var bag = new StringBuilder();
        bag.Append('[');
        // bag_items：物品详情（kind/charges/equipped），供准状态基线（诅咒刃次数、甲耐久、弹药支数等）
        var bagItems = new StringBuilder();
        bagItems.Append('[');
        if (u.Inventory != null)
        {
            bool first = true;
            foreach (var it in u.Inventory.Items)
            {
                if (!first) bag.Append(',');
                first = false;
                bag.Append(Q(LogicSimNaming.Item(it.Kind)));
            }
            bool bf = true;
            foreach (var it in u.Inventory.Items)
            {
                if (!bf) bagItems.Append(',');
                bf = false;
                bagItems.Append('{')
                    .Append("\"kind\":").Append(Q(LogicSimNaming.Item(it.Kind))).Append(',')
                    .Append("\"charges\":").Append(I(it.Charges)).Append(',')
                    .Append("\"equipped\":").Append(B(it.Equipped))
                    .Append('}');
            }
        }
        bag.Append(']');
        bagItems.Append(']');

        var statuses = new StringBuilder();
        statuses.Append('[');
        bool sf = true;
        if (u.IsDying)
        {
            statuses.Append(Q("dying"));
            sf = false;
        }
        if (u.IsDead)
        {
            if (!sf) statuses.Append(',');
            statuses.Append(Q("dead"));
            sf = false;
        }
        foreach (StatusType st in System.Enum.GetValues(typeof(StatusType)))
        {
            if (!u.HasStatus(st)) continue;
            if (!sf) statuses.Append(',');
            sf = false;
            statuses.Append(Q(LogicSimNaming.Status(st)));
        }
        statuses.Append(']');

        TileType tile = grid != null ? grid.GetTileType(u.Cell) : TileType.Normal;
        var sb = new StringBuilder(256);
        sb.Append('{');
        sb.Append("\"role\":").Append(Q(LogicSimNaming.Role(u.Role))).Append(',');
        sb.Append("\"cell\":").Append(Cell(u.Cell)).Append(',');
        sb.Append("\"hp\":").Append(u.Hp).Append(',');
        sb.Append("\"max_hp\":").Append(u.MaxHp).Append(',');
        sb.Append("\"atk\":").Append(u.CurrentAtk).Append(',');
        sb.Append("\"def\":").Append(u.CurrentDef).Append(',');
        sb.Append("\"magic_resist\":").Append(u.MagicResist).Append(',');
        sb.Append("\"move\":").Append(u.CurrentMove).Append(',');
        sb.Append("\"vis\":").Append(u.CurrentVisibility).Append(',');
        sb.Append("\"bag\":").Append(bag).Append(',');
        sb.Append("\"bag_items\":").Append(bagItems).Append(',');
        sb.Append("\"weight\":").Append(F(u.Inventory != null ? u.Inventory.UsedWeight : 0f)).Append(',');
        sb.Append("\"cap\":").Append(F(u.Inventory != null ? u.Inventory.Capacity : 0f)).Append(',');
        sb.Append("\"statuses\":").Append(statuses).Append(',');
        sb.Append("\"skill_level\":").Append(u.SkillLevel).Append(',');
        sb.Append("\"tile\":").Append(Q(LogicSimNaming.Tile(tile)));
        // 准状态：计数器/布尔（非具名状态，但影响结算，供 logic_qa 基线）
        sb.Append(",\"crossbow_charged\":").Append(B(u.CrossbowCharged));
        sb.Append(",\"crossbow_charged_this_action\":").Append(B(u.CrossbowChargedThisAction));
        sb.Append(",\"motorcycle_active\":").Append(u.MotorcycleActiveRounds);
        sb.Append(",\"pending_extra_actions\":").Append(u.PendingExtraActions);
        sb.Append(",\"skill_cooldown\":").Append(u.SkillCooldownLeft);
        sb.Append(",\"skill_shield\":").Append(u.SkillShieldCharges);
        sb.Append(",\"amulet_shield\":").Append(u.AmuletShieldCharges);
        sb.Append(",\"amulet_buff\":").Append(B(u.AmuletBuffActive));
        sb.Append(",\"adrenaline_rounds\":").Append(u.AdrenalineRoundsLeft);
        sb.Append(",\"hidden_from_jungle\":").Append(B(u.HiddenFromJungle));
        sb.Append(",\"dying_rounds\":").Append(u.DyingActionsLeft);
        sb.Append(",\"leader_declared\":").Append(B(u.HasUsedLeaderDeclaration));
        sb.Append(",\"flamethrower_cooldown\":").Append(u.FlamethrowerCooldown);
        if (u.IsDead)
            sb.Append(",\"dead\":true");
        sb.Append('}');
        return sb.ToString();
    }

    private static string Obj(params (string key, string jsonValue)[] fields)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Q(fields[i].key)).Append(':').Append(fields[i].jsonValue);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string Dict(Dictionary<string, string> d)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;
        if (d != null)
        {
            foreach (var kv in d)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Q(kv.Key)).Append(':').Append(Q(kv.Value));
            }
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string Q(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string B(bool v) => v ? "true" : "false";
    private static string Cell(Vector2Int c) => "[" + c.x + "," + c.y + "]";
}

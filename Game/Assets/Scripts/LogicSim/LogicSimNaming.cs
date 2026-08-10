using System.Text;

/// <summary>LogicSim 日志字段命名：与 Tools/sim events.jsonl schema 对齐（snake_case）。</summary>
public static class LogicSimNaming
{
    public static string Role(RoleType role) => role.ToString().ToLowerInvariant();

    public static string Item(ItemKind kind) => ToSnake(kind.ToString());

    public static string Status(StatusType type) => type.ToString().ToLowerInvariant();

    public static string Weather(WeatherType w) => w.ToString().ToLowerInvariant();

    public static string Tile(TileType t)
    {
        switch (t)
        {
            case TileType.Normal: return "normal";
            case TileType.Sand: return "sand";
            case TileType.Swamp: return "swamp";
            case TileType.Ice: return "ice";
            case TileType.Jungle: return "jungle";
            case TileType.Highland: return "highland";
            case TileType.Lava: return "lava";
            default: return ToSnake(t.ToString());
        }
    }

    public static string ToSnake(string pascal)
    {
        if (string.IsNullOrEmpty(pascal))
            return "";
        var sb = new StringBuilder(pascal.Length + 8);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

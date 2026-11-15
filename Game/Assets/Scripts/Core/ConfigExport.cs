#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把"只存在于代码里"的元数据（卡牌 / 地形 / 状态的显示名、描述、槽位、用法）导出成 JSON。
///
/// 为什么需要它：这些文本写在 C# 的 switch 里，外部脚本只能靠正则"反推"，又脆又难懂。
/// 规矩是**谁拥有数据，谁负责导出**——代码拥有元数据，就让代码自己吐出来。
///
/// 命令行（策划表构建的第一步）：
/// <code>
/// Unity.exe -batchmode -nographics -projectPath Game ^
///   -executeMethod ConfigExport.Run -configOut Design/meta/meta.json -logFile export.log
/// </code>
/// 省略 -configOut 时默认写到仓库根的 Design/meta/meta.json。
/// </summary>
public static class ConfigExport
{
    /// <summary>JSON 结构版本；字段增删时 +1，下游据此判断能否读取。</summary>
    private const int SchemaVersion = 1;

    [Serializable]
    public class CardRow
    {
        public string id;
        public string name;
        public string desc;
        public string slot;
        public string use;
    }

    [Serializable]
    public class TileRow
    {
        public string id;
        public string name;
        public string effect;
    }

    [Serializable]
    public class StatusRow
    {
        public string id;
        public string name;
        public string tip;
    }

    [Serializable]
    public class Table
    {
        public int schema;
        public string source;
        public CardRow[] cards;
        public TileRow[] tiles;
        public StatusRow[] statuses;
    }

    /// <summary>命令行入口：导出后立即退出编辑器。</summary>
    public static void Run()
    {
        string relative = LogicSimRunner.GetStringArg("-configOut", "Design/meta/meta.json");
        string full = Path.GetFullPath(relative);
        Export(full);
        EditorApplication.Exit(0);
    }

    /// <summary>导出到指定绝对路径（也供编辑器菜单/测试直接调用）。</summary>
    public static void Export(string fullPath)
    {
        var table = Build();
        string dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, JsonUtility.ToJson(table, true), new UTF8Encoding(false));
        Debug.Log($"[ConfigExport] schema={table.schema} 卡牌 {table.cards.Length} / " +
                  $"地形 {table.tiles.Length} / 状态 {table.statuses.Length} → {fullPath}");
    }

    private static Table Build()
    {
        var cards = new List<CardRow>();
        foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
        {
            var slot = ItemInfo.GetEquipSlot(kind);
            cards.Add(new CardRow
            {
                // 与 game_rules.yaml 的 deck / items 键保持同一套命名
                id = GameRulesConfig.ItemKindToYamlKey(kind),
                name = ItemInfo.GetDisplayName(kind),
                // 描述里的数值在导出时已按当前配置代入，改数值后需重新导出
                desc = ItemInfo.GetShortDesc(kind),
                slot = slot == ItemInfo.EquipSlot.None ? "" : slot.ToString(),
                use = ItemInfo.GetUseKind(kind).ToString()
            });
        }

        var tiles = new List<TileRow>();
        foreach (TileType type in Enum.GetValues(typeof(TileType)))
        {
            tiles.Add(new TileRow
            {
                id = GameRulesConfig.ToSnakeCase(type.ToString()),
                name = TerrainInfo.GetDisplayName(type),
                effect = TerrainInfo.GetShortEffect(type)
            });
        }

        var statuses = new List<StatusRow>();
        foreach (StatusType type in Enum.GetValues(typeof(StatusType)))
        {
            statuses.Add(new StatusRow
            {
                id = GameRulesConfig.ToSnakeCase(type.ToString()),
                name = StatusInfo.GetDisplayName(type),
                tip = StatusInfo.GetShortTip(type)
            });
        }

        return new Table
        {
            schema = SchemaVersion,
            source = "game_code_export",
            cards = cards.ToArray(),
            tiles = tiles.ToArray(),
            statuses = statuses.ToArray()
        };
    }
}
#endif

using UnityEngine;

/// <summary>
/// LogicSim / AI 对战可选 RL 推理桥：优先 TryActOnce 走已绑定策略，否则回落启发式。
/// 命令行 -ai rl 时 AiController 使用本桥（需场景中存在 RlAgent 或后续 ONNX）。
/// </summary>
public static class RlPolicyBridge
{
    public static bool UseRlForLogicSim
    {
        get
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-ai", System.StringComparison.OrdinalIgnoreCase)
                    && string.Equals(args[i + 1], "rl", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 尝试用 RL 微操作；若无可用 Agent，返回 false 由调用方回落启发式。
    /// </summary>
    public static bool TryActOnce(UnitActor unit)
    {
        if (unit == null || !UseRlForLogicSim)
            return false;

        if (RlMatchController.Instance != null
            && RlMatchController.Instance.IsRlControlled(unit))
        {
            // 训练会话内由 MatchController 驱动；LogicSim 泵不应双驱动
            return false;
        }

        // 无在线 Agent 时：用启发式动作空间采样非法掩码下的贪心近似（占位）
        // 完整 ONNX 推理请在 BehaviorParameters.InferenceOnly 下用 RlMatchController.Eval。
        return SimpleHeuristicAi.TryActOnce(unit);
    }
}

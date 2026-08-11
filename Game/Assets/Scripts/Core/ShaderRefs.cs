using UnityEngine;

/// <summary>在 Inspector 中拖入 URP Shader；运行时所有程序化模型/地形共用。</summary>
[CreateAssetMenu(menuName = "ElephantCrisis/Shader Refs")]
public class ShaderRefs : ScriptableObject
{
    [Header("用于程序化角色模型")]
    public Shader PlayerModelShader;

    [Header("用于高地台体（MapVisual）")]
    public Shader HighlandShader;

    private static ShaderRefs _instance;
    public static ShaderRefs Instance
    {
        get
        {
            if (_instance == null)
                _instance = Resources.Load<ShaderRefs>("ShaderRefs");
            return _instance;
        }
    }
}

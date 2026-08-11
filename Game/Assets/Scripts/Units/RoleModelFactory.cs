using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 四角色程序化低多边形 3D 棋子（无外部模型资源时用原始几何拼装）。
/// </summary>
public static class RoleModelFactory
{
    private static readonly Dictionary<int, Material> MatCache = new Dictionary<int, Material>();

    public static GameObject Build(RoleType role, Transform parent)
    {
        var root = new GameObject("Model3D");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        switch (role)
        {
            case RoleType.Elephant:
                BuildElephant(root.transform);
                break;
            case RoleType.Human:
                BuildHuman(root.transform);
                break;
            case RoleType.Monkey:
                BuildMonkey(root.transform);
                break;
            default:
                BuildCat(root.transform);
                break;
        }

        return root;
    }

    public static GameObject BuildSelectRing(Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "SelectRing";
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0.025f, 0f);
        go.transform.localScale = new Vector3(0.95f, 0.02f, 0.95f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = GetMat(new Color(1f, 1f, 1f, 0.95f));
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.SetActive(false);
        return go;
    }

    private static void BuildElephant(Transform root)
    {
        var skin = new Color(0.62f, 0.64f, 0.7f);
        var dark = new Color(0.45f, 0.46f, 0.52f);
        var ivory = new Color(0.92f, 0.9f, 0.82f);

        // 四腿
        Part(root, "LegFL", PrimitiveType.Cylinder, new Vector3(-0.16f, 0.14f, 0.14f), new Vector3(0.12f, 0.14f, 0.12f), dark);
        Part(root, "LegFR", PrimitiveType.Cylinder, new Vector3(0.16f, 0.14f, 0.14f), new Vector3(0.12f, 0.14f, 0.12f), dark);
        Part(root, "LegBL", PrimitiveType.Cylinder, new Vector3(-0.16f, 0.14f, -0.16f), new Vector3(0.12f, 0.14f, 0.12f), dark);
        Part(root, "LegBR", PrimitiveType.Cylinder, new Vector3(0.16f, 0.14f, -0.16f), new Vector3(0.12f, 0.14f, 0.12f), dark);

        // 躯干
        Part(root, "Body", PrimitiveType.Capsule, new Vector3(0f, 0.42f, -0.02f), new Vector3(0.55f, 0.28f, 0.7f), skin,
            Quaternion.Euler(90f, 0f, 0f));

        // 头
        Part(root, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.55f, 0.32f), new Vector3(0.38f, 0.36f, 0.36f), skin);

        // 耳
        Part(root, "EarL", PrimitiveType.Cube, new Vector3(-0.28f, 0.58f, 0.28f), new Vector3(0.22f, 0.28f, 0.04f), dark,
            Quaternion.Euler(0f, 0f, 18f));
        Part(root, "EarR", PrimitiveType.Cube, new Vector3(0.28f, 0.58f, 0.28f), new Vector3(0.22f, 0.28f, 0.04f), dark,
            Quaternion.Euler(0f, 0f, -18f));

        // 象鼻
        Part(root, "Trunk1", PrimitiveType.Capsule, new Vector3(0f, 0.42f, 0.48f), new Vector3(0.1f, 0.14f, 0.1f), skin,
            Quaternion.Euler(35f, 0f, 0f));
        Part(root, "Trunk2", PrimitiveType.Capsule, new Vector3(0f, 0.28f, 0.58f), new Vector3(0.08f, 0.12f, 0.08f), dark,
            Quaternion.Euler(55f, 0f, 0f));

        // 象牙
        Part(root, "TuskL", PrimitiveType.Capsule, new Vector3(-0.1f, 0.4f, 0.46f), new Vector3(0.045f, 0.1f, 0.045f), ivory,
            Quaternion.Euler(50f, -12f, 0f));
        Part(root, "TuskR", PrimitiveType.Capsule, new Vector3(0.1f, 0.4f, 0.46f), new Vector3(0.045f, 0.1f, 0.045f), ivory,
            Quaternion.Euler(50f, 12f, 0f));

        // 尾
        Part(root, "Tail", PrimitiveType.Capsule, new Vector3(0f, 0.4f, -0.4f), new Vector3(0.05f, 0.1f, 0.05f), dark,
            Quaternion.Euler(-40f, 0f, 0f));
    }

    private static void BuildHuman(Transform root)
    {
        var skin = new Color(0.92f, 0.75f, 0.62f);
        var shirt = new Color(0.32f, 0.52f, 0.92f);
        var pants = new Color(0.25f, 0.3f, 0.42f);
        var hair = new Color(0.22f, 0.16f, 0.12f);

        Part(root, "LegL", PrimitiveType.Capsule, new Vector3(-0.08f, 0.18f, 0f), new Vector3(0.1f, 0.16f, 0.1f), pants);
        Part(root, "LegR", PrimitiveType.Capsule, new Vector3(0.08f, 0.18f, 0f), new Vector3(0.1f, 0.16f, 0.1f), pants);
        Part(root, "Torso", PrimitiveType.Capsule, new Vector3(0f, 0.48f, 0f), new Vector3(0.28f, 0.22f, 0.16f), shirt);
        Part(root, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.78f, 0.02f), new Vector3(0.24f, 0.24f, 0.24f), skin);
        Part(root, "Hair", PrimitiveType.Sphere, new Vector3(0f, 0.86f, -0.02f), new Vector3(0.25f, 0.14f, 0.26f), hair);
        Part(root, "ArmL", PrimitiveType.Capsule, new Vector3(-0.2f, 0.48f, 0f), new Vector3(0.08f, 0.16f, 0.08f), skin,
            Quaternion.Euler(0f, 0f, 12f));
        Part(root, "ArmR", PrimitiveType.Capsule, new Vector3(0.2f, 0.48f, 0f), new Vector3(0.08f, 0.16f, 0.08f), skin,
            Quaternion.Euler(0f, 0f, -12f));
    }

    private static void BuildMonkey(Transform root)
    {
        var fur = new Color(0.72f, 0.45f, 0.22f);
        var face = new Color(0.9f, 0.72f, 0.55f);
        var dark = new Color(0.5f, 0.3f, 0.14f);

        Part(root, "LegL", PrimitiveType.Capsule, new Vector3(-0.08f, 0.14f, 0.02f), new Vector3(0.09f, 0.12f, 0.09f), fur);
        Part(root, "LegR", PrimitiveType.Capsule, new Vector3(0.08f, 0.14f, 0.02f), new Vector3(0.09f, 0.12f, 0.09f), fur);
        Part(root, "Body", PrimitiveType.Capsule, new Vector3(0f, 0.38f, 0f), new Vector3(0.26f, 0.18f, 0.2f), fur);
        Part(root, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.62f, 0.04f), new Vector3(0.26f, 0.24f, 0.24f), fur);
        Part(root, "Face", PrimitiveType.Sphere, new Vector3(0f, 0.58f, 0.14f), new Vector3(0.16f, 0.14f, 0.1f), face);
        Part(root, "EarL", PrimitiveType.Sphere, new Vector3(-0.14f, 0.66f, 0f), new Vector3(0.1f, 0.1f, 0.06f), dark);
        Part(root, "EarR", PrimitiveType.Sphere, new Vector3(0.14f, 0.66f, 0f), new Vector3(0.1f, 0.1f, 0.06f), dark);
        // 长臂
        Part(root, "ArmL", PrimitiveType.Capsule, new Vector3(-0.2f, 0.3f, 0.02f), new Vector3(0.07f, 0.2f, 0.07f), fur,
            Quaternion.Euler(0f, 0f, 28f));
        Part(root, "ArmR", PrimitiveType.Capsule, new Vector3(0.2f, 0.3f, 0.02f), new Vector3(0.07f, 0.2f, 0.07f), fur,
            Quaternion.Euler(0f, 0f, -28f));
        // 卷尾
        Part(root, "Tail1", PrimitiveType.Capsule, new Vector3(0.05f, 0.36f, -0.18f), new Vector3(0.05f, 0.12f, 0.05f), dark,
            Quaternion.Euler(-50f, 20f, 0f));
        Part(root, "Tail2", PrimitiveType.Capsule, new Vector3(0.12f, 0.46f, -0.28f), new Vector3(0.045f, 0.1f, 0.045f), dark,
            Quaternion.Euler(-20f, 40f, 0f));
    }

    private static void BuildCat(Transform root)
    {
        var fur = new Color(0.95f, 0.78f, 0.28f);
        var cream = new Color(0.98f, 0.92f, 0.75f);
        var dark = new Color(0.75f, 0.55f, 0.18f);

        Part(root, "LegFL", PrimitiveType.Capsule, new Vector3(-0.1f, 0.1f, 0.12f), new Vector3(0.07f, 0.1f, 0.07f), fur);
        Part(root, "LegFR", PrimitiveType.Capsule, new Vector3(0.1f, 0.1f, 0.12f), new Vector3(0.07f, 0.1f, 0.07f), fur);
        Part(root, "LegBL", PrimitiveType.Capsule, new Vector3(-0.1f, 0.1f, -0.12f), new Vector3(0.07f, 0.1f, 0.07f), fur);
        Part(root, "LegBR", PrimitiveType.Capsule, new Vector3(0.1f, 0.1f, -0.12f), new Vector3(0.07f, 0.1f, 0.07f), fur);

        Part(root, "Body", PrimitiveType.Capsule, new Vector3(0f, 0.28f, 0f), new Vector3(0.28f, 0.16f, 0.42f), fur,
            Quaternion.Euler(90f, 0f, 0f));
        Part(root, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.4f, 0.26f), new Vector3(0.26f, 0.24f, 0.24f), fur);
        Part(root, "Muzzle", PrimitiveType.Sphere, new Vector3(0f, 0.36f, 0.36f), new Vector3(0.12f, 0.1f, 0.1f), cream);

        // 尖耳
        Part(root, "EarL", PrimitiveType.Cube, new Vector3(-0.08f, 0.54f, 0.24f), new Vector3(0.08f, 0.14f, 0.05f), dark,
            Quaternion.Euler(0f, 0f, -18f));
        Part(root, "EarR", PrimitiveType.Cube, new Vector3(0.08f, 0.54f, 0.24f), new Vector3(0.08f, 0.14f, 0.05f), dark,
            Quaternion.Euler(0f, 0f, 18f));

        Part(root, "Tail1", PrimitiveType.Capsule, new Vector3(0.05f, 0.34f, -0.28f), new Vector3(0.05f, 0.12f, 0.05f), fur,
            Quaternion.Euler(-55f, 15f, 0f));
        Part(root, "Tail2", PrimitiveType.Capsule, new Vector3(0.1f, 0.48f, -0.36f), new Vector3(0.045f, 0.1f, 0.045f), dark,
            Quaternion.Euler(-15f, 25f, 0f));
    }

    private static Transform Part(
        Transform parent, string name, PrimitiveType type,
        Vector3 localPos, Vector3 localScale, Color color,
        Quaternion? localRot = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.transform.localRotation = localRot ?? Quaternion.identity;

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = GetMat(color);
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = true;
        return go.transform;
    }

    private static Material GetMat(Color color)
    {
        // 量化一点，合并相近色，减少材质数量
        int key = ((int)(color.r * 64) << 16) | ((int)(color.g * 64) << 8) | (int)(color.b * 64);
        if (MatCache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var shader = ShaderRefs.Instance != null ? ShaderRefs.Instance.PlayerModelShader : null
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("[RoleModelFactory] No suitable shader found for role model material.");
            return null;
        }
        var mat = new Material(shader) { name = $"RoleMat_{key}" };
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
        mat.color = color;
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.25f);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0.05f);
        MatCache[key] = mat;
        return mat;
    }
}

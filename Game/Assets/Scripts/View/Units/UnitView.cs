using UnityEngine;

/// <summary>
/// 单位的外皮：模型、选中环、名字牌、濒死着色、迷雾隐藏。
/// 从 UnitActor 里整段搬出来——那边只剩数据与结算。
///
/// 由 <see cref="UnityPresentation.AttachUnit"/> 在装配时挂上；
/// 无头模式不挂，逻辑层拿到的 View 是 null，所有视觉调用自动空转。
/// </summary>
public sealed class UnitView : MonoBehaviour, IUnitView
{
    private Transform modelRoot;
    private Renderer[] modelRenderers;
    private Color[] modelBaseColors;
    private Transform selectRing;
    private TextMesh label;
    private MaterialPropertyBlock tintBlock;
    private bool worldVisible = true;

    public void Attach(UnitActor unit)
    {
        // 去掉广告牌：3D 棋子直立于地面
        var billboard = GetComponent<CameraBillboard>();
        if (billboard != null)
            Destroy(billboard);

        if (modelRoot == null)
        {
            var modelGo = RoleModelFactory.Build(unit.Role, transform);
            modelRoot = modelGo.transform;
            modelRenderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            modelBaseColors = new Color[modelRenderers.Length];
            for (int i = 0; i < modelRenderers.Length; i++)
                modelBaseColors[i] = ReadRendererColor(modelRenderers[i]);
        }

        if (selectRing == null)
            selectRing = RoleModelFactory.BuildSelectRing(transform).transform;

        if (label == null)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            label = labelGo.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.14f;
            label.fontSize = 48;
            label.color = Color.white;
            label.fontStyle = FontStyle.Bold;
            label.text = "";
            labelGo.SetActive(false);
        }
    }

    public void Refresh(UnitActor unit)
    {
        if (modelRoot == null)
            return;

        ApplyModelTint(unit.IsDying ? new Color(1f, 0.45f, 0.45f, 1f) : Color.white);
        FaceBoardCenter();
        ApplySortOrder(unit.Cell);
        ApplyWorldVisible();
    }

    public void PlaceAt(UnitActor unit, Int2 cell)
    {
        var grid = GridManager.Instance;
        if (grid == null)
            return;
        // 略抬离地面，减少与地块 Z 冲突
        transform.position = BoardProjection.CellToWorld(cell.ToV2()) + Vector3.up * 0.02f;
        ApplySortOrder(cell);
    }

    public void SetSelected(UnitActor unit, bool selected)
    {
        if (selectRing == null)
            return;
        selectRing.gameObject.SetActive(selected && worldVisible);
    }

    public void SetWorldVisible(UnitActor unit, bool visible)
    {
        worldVisible = visible;
        ApplyWorldVisible();
    }

    // ------------------------------------------------------------ 内部

    private static Color ReadRendererColor(Renderer r)
    {
        if (r == null)
            return Color.white;
        var mat = r.sharedMaterial;
        if (mat == null)
            return Color.white;
        if (mat.HasProperty("_BaseColor"))
            return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_Color"))
            return mat.GetColor("_Color");
        return mat.color;
    }

    private void ApplySortOrder(Int2 cell)
    {
        // 3D 棋子靠深度排序；仅把名字牌抬到最前（若启用）
        if (label == null)
            return;
        var grid = GridManager.Instance;
        if (grid == null)
            return;
        var mr = label.GetComponent<MeshRenderer>();
        if (mr != null)
            mr.sortingOrder = grid.GetSortOrder(cell.ToV2(), 50);
    }

    /// <summary>面朝棋盘中心，四角出生位更易辨认。</summary>
    private void FaceBoardCenter()
    {
        if (modelRoot == null)
            return;
        var grid = GridManager.Instance;
        if (grid == null)
            return;
        Vector3 center = BoardProjection.Origin + new Vector3(
            grid.gridWidth * grid.cellSize * 0.5f,
            0f,
            grid.gridHeight * grid.cellSize * 0.5f);
        Vector3 flat = center - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
            return;
        modelRoot.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
    }

    private void ApplyModelTint(Color mul)
    {
        if (modelRenderers == null || modelBaseColors == null)
            return;
        if (tintBlock == null)
            tintBlock = new MaterialPropertyBlock();

        for (int i = 0; i < modelRenderers.Length; i++)
        {
            var r = modelRenderers[i];
            if (r == null)
                continue;
            Color c = modelBaseColors[i] * mul;
            c.a = modelBaseColors[i].a;
            tintBlock.Clear();
            tintBlock.SetColor("_BaseColor", c);
            tintBlock.SetColor("_Color", c);
            r.SetPropertyBlock(tintBlock);
        }
    }

    private void ApplyWorldVisible()
    {
        if (modelRenderers != null)
        {
            for (int i = 0; i < modelRenderers.Length; i++)
            {
                if (modelRenderers[i] != null)
                    modelRenderers[i].enabled = worldVisible;
            }
        }
        if (selectRing != null && !worldVisible)
            selectRing.gameObject.SetActive(false);
        if (label != null)
            label.gameObject.SetActive(false);
    }
}

using UnityEngine;

/// <summary>汽油火焰地块轻微闪烁。</summary>
public class FlameFlicker : MonoBehaviour
{
    private SpriteRenderer[] renderers;
    private float[] baseAlpha;
    private float phase;

    private void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>();
        baseAlpha = new float[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            baseAlpha[i] = renderers[i] != null ? renderers[i].color.a : 1f;
        phase = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        if (renderers == null)
            return;
        phase += Time.unscaledDeltaTime * 7f;
        float pulse = 0.82f + 0.18f * Mathf.Sin(phase);
        float scale = 0.95f + 0.08f * Mathf.Sin(phase * 1.3f + 0.4f);
        transform.localScale = new Vector3(scale, 1f, scale);

        for (int i = 0; i < renderers.Length; i++)
        {
            var sr = renderers[i];
            if (sr == null)
                continue;
            var c = sr.color;
            c.a = baseAlpha[i] * pulse;
            sr.color = c;
        }
    }
}

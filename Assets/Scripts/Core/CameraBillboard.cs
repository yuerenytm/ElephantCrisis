using UnityEngine;

/// <summary>直立广告牌：只跟随相机水平朝向，供单位 / 掉落 / 陷阱在 2.5D 中面向镜头。</summary>
public class CameraBillboard : MonoBehaviour
{
    private void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null)
            return;

        var e = cam.transform.eulerAngles;
        transform.rotation = Quaternion.Euler(0f, e.y, 0f);
    }
}

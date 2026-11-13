using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 给全屏遮罩开一个「点击洞口」：落在指定矩形里的点击不会被这张遮罩吃掉，
    /// 而是继续往下命中真正的按钮（用于强制流程里只允许玩家查看图鉴）。
    ///
    /// 用法：挂在遮罩的 Image 上即可，不需要改场景层级、也不用挪按钮。
    /// 遮罩本身仍是 raycastTarget，只是在这一小块区域返回「不拦截」。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class DimClickHole : MonoBehaviour, ICanvasRaycastFilter
    {
        private RectTransform hole;

        private void Awake()
        {
            // 图鉴按钮是场景里的常驻对象，Awake 时就能拿到它的矩形。
            CodexPanel codex = FindAnyObjectByType<CodexPanel>(FindObjectsInactive.Include);
            hole = codex != null ? codex.ToggleRect : null;
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (hole == null || !hole.gameObject.activeInHierarchy) return true;
            // 在洞口范围内返回 false = 这点不归遮罩管，射线继续往下找按钮。
            return !RectTransformUtility.RectangleContainsScreenPoint(hole, screenPoint, eventCamera);
        }
    }
}

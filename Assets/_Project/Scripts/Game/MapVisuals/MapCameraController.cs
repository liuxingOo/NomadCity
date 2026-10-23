using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NomadCity.Game
{
    /// <summary>
    /// 地图窗口操作：滚轮以光标位置为中心缩放（有放大上限），鼠标右键按住拖动平移。
    ///
    /// 核心是一个「窗口」：按**未放大时的尺寸**紧贴地图四周圈出来的矩形（和很多游戏的大地图一样）。
    /// 相机的渲染区域被裁到这个窗口上，所以放大之后地图只会出现在窗口里，不会铺到整屏。
    /// 相机位置再钳制成「地图始终盖住窗口」，于是：
    /// - 未放大时地图正好铺满窗口，没地方可拖（进 Play 时地图在屏幕上的大小和加这个功能之前一样）；
    /// - 放大后可以拖，往右拖到地图左边缘贴住窗口左边缘就停住，反过来同理。
    ///
    /// 窗口是按「场景里原本的取景（正交尺寸）」算出来的，所以窗口外面那一圈原有的画面会被裁掉；
    /// 为了避免露出没清理的空白，<see cref="autoWindowChrome"/> 会自动生成一套占位边框
    /// （外圈深色遮罩 + 紧贴窗口的一圈描边）。你自己做好边框图后拖进 <see cref="borderFrame"/>，
    /// 代码会把它自动摆成紧贴窗口，占位边框就可以关掉了。
    ///
    /// 只动相机，不碰任何游戏状态 —— 地图上的标记全是世界空间物体，跟着相机一起缩放。
    /// 与 UI 的分工：HUD 是 Screen Space - Overlay，不受 <see cref="Camera.rect"/> 影响。
    /// </summary>
    public sealed class MapCameraController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("要控制的相机。留空则用 Camera.main。")]
        [SerializeField] private Camera worldCamera;

        [Tooltip("场景里的 MapView。地图是运行时才实例化出来的，靠它拿到地图根节点才能算出地图的世界范围。")]
        [SerializeField] private MapView mapView;

        [Header("缩放")]
        [Tooltip("放大上限（相对未放大时的倍数）。5 = 最多放大到五倍。")]
        [SerializeField, Min(1f)] private float maxZoomIn = 5f;

        [Tooltip("允许比「未放大」再缩小多少：1 = 不能再缩小；1.25 = 最多还能缩到 80%，方便看清整体。")]
        [SerializeField, Range(1f, 3f)] private float minZoomOut = 1.25f;

        [Tooltip("每一格滚轮的缩放比例：0.8 = 每格放大 25%。数字越小缩放越快。")]
        [SerializeField, Range(0.5f, 0.99f)] private float zoomPerNotch = 0.8f;

        [Tooltip("缩放速度倍率。滚一格变化太小就把这个调大，太猛就调小。")]
        [SerializeField, Min(0.1f)] private float zoomSpeed = 1f;

        [Header("窗口位置")]
        [Tooltip("地图窗口相对「屏幕正中」的像素偏移：x 正 = 往右，y 正 = 往上。" +
                 "屏幕坐标系原点在左下角，所以「往下」要填负数。地图、边框、外圈遮罩会一起跟着走。")]
        [SerializeField] private Vector2 windowOffset;

        [Header("边框")]
        [Tooltip("可选：你自己做的边框图。填了之后代码会把它的位置和大小自动摆成紧贴地图窗口，你只要给一张图。")]
        [SerializeField] private RectTransform borderFrame;

        [Tooltip("边框相对窗口向外多留的像素（正数 = 比窗口大一圈）。")]
        [SerializeField] private Vector2 borderPadding;

        [Tooltip("没有接自己的边框图时，是否自动生成一套占位边框（外圈深色遮罩 + 窗口描边）。")]
        [SerializeField] private bool autoWindowChrome = true;

        [Tooltip("占位描边的粗细（像素）。")]
        [SerializeField, Min(0f)] private float frameThickness = 6f;

        [Tooltip("占位描边颜色。")]
        [SerializeField] private Color frameColor = new Color(0.86f, 0.80f, 0.62f, 1f);

        [Tooltip("占位遮罩颜色（窗口外面那一圈）。")]
        [SerializeField] private Color backdropColor = new Color(0.07f, 0.09f, 0.12f, 1f);

        [Tooltip("按世界单位微调地图范围的四条边（x=左 y=下 z=右 w=上）。底图四周留白多、边框显得松时，填正数往里收。")]
        [SerializeField] private Vector4 mapInset;

        [Header("交互")]
        [Tooltip("鼠标停在 UI 面板上时是否禁止缩放 / 平移。")]
        [SerializeField] private bool blockWhenPointerOverUI = true;

        /// <summary>场景里原本的正交尺寸（开局取景）—— 窗口大小是按它算的，所以只记一次。</summary>
        private float startSize = -1f;

        /// <summary>窗口在屏幕上的像素矩形（居中，左下角为原点，和 Screen 坐标系一致）。</summary>
        private Rect windowPixels;

        /// <summary>窗口的宽高比（等于地图宽高比）。不用 camera.aspect，避免裁切后取到旧值。</summary>
        private float windowAspect = 1f;

        /// <summary>未放大时的正交尺寸：地图正好铺满窗口。</summary>
        private float fitSize = 1f;

        /// <summary>正交尺寸上限（缩得最小）。</summary>
        private float zoomOutLimit = 1f;

        /// <summary>正交尺寸下限（放得最大）。</summary>
        private float zoomInLimit = 1f;

        private Vector2 mapMin;
        private Vector2 mapMax;
        private bool ready;
        private bool warned;
        private bool panning;
        private float waited;
        private int screenWidth;
        private int screenHeight;

        // 自动生成的占位边框：[0..3] 窗口外的遮罩，[4..7] 紧贴窗口的描边。
        private RectTransform[] chromeRects;

        private void Update()
        {
            Camera camera = Resolve();
            if (camera == null || !camera.orthographic) return;

            // 开局取景只记一次，后面所有计算都以它为基准。
            if (startSize < 0f) startSize = camera.orthographicSize;

            // 地图是运行时才实例化出来的：拿到地图根节点之前每帧重试一次。
            if (!ready && !TryInitialize(camera)) return;

            // 改了 Game 视图分辨率就重算窗口，否则窗口会按旧分辨率错位。
            if (Screen.width != screenWidth || Screen.height != screenHeight) TryInitialize(camera);

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 screen = mouse.position.ReadValue();
            bool inside = windowPixels.Contains(screen);

            // 只有「在窗口里、且不在 UI 上」按下右键的那一次才算平移，拖到一半经过 HUD 也不中断。
            if (mouse.rightButton.wasPressedThisFrame) panning = inside && !OverUI();
            if (mouse.rightButton.wasReleasedThisFrame) panning = false;

            if (panning)
            {
                Pan(camera, mouse);
                return;
            }

            // 鼠标在窗口外（被边框挡住的区域）或停在 UI 上时不缩放，符合「在地图里滚才缩放」。
            if (!inside || OverUI()) return;
            Zoom(camera, mouse);
        }

        private Camera Resolve() => worldCamera != null ? worldCamera : Camera.main;

        private bool OverUI()
            => blockWhenPointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        /// <summary>
        /// 算出窗口、把相机裁到窗口上、定好缩放上下限，并把相机收到合法范围。
        /// 地图还没生成时返回 false，等下一帧再试。
        /// </summary>
        private bool TryInitialize(Camera camera)
        {
            if (!ResolveMapBounds()) return false;

            screenWidth = Screen.width;
            screenHeight = Screen.height;

            float mapWidth = mapMax.x - mapMin.x;
            float mapHeight = mapMax.y - mapMin.y;

            // ① 未放大时地图在屏幕上占多大：按「全屏相机 + 开局取景尺寸」算，也就是你最初看到的样子。
            float pixelsPerUnit = screenHeight / (2f * Mathf.Max(startSize, 1e-3f));
            var windowSize = new Vector2(mapWidth * pixelsPerUnit, mapHeight * pixelsPerUnit);

            // ② 窗口默认居中，再按 windowOffset 偏移；并把相机渲染区域裁到窗口上 —— 地图从此只会画在窗口里面。
            windowPixels = new Rect((screenWidth - windowSize.x) * 0.5f + windowOffset.x,
                (screenHeight - windowSize.y) * 0.5f + windowOffset.y,
                windowSize.x, windowSize.y);
            camera.rect = new Rect(windowPixels.x / screenWidth, windowPixels.y / screenHeight,
                windowPixels.width / screenWidth, windowPixels.height / screenHeight);

            windowAspect = windowSize.y > 1e-4f ? windowSize.x / windowSize.y : 1f;

            // ③ 缩放上下限。窗口宽高比 = 地图宽高比，所以「地图正好铺满窗口」的正交尺寸就是两者取大。
            fitSize = Mathf.Max(mapHeight * 0.5f, mapWidth * 0.5f / Mathf.Max(windowAspect, 1e-3f));
            zoomOutLimit = fitSize * Mathf.Max(minZoomOut, 1f);
            zoomInLimit = fitSize / Mathf.Max(maxZoomIn, 1f);

            camera.orthographicSize = fitSize; // 未放大 = 地图正好铺满窗口，和加功能之前一样
            CenterAndClamp(camera);

            ApplyBorderFrame();
            LayoutChrome();
            ready = true;
            return true;
        }

        /// <summary>
        /// 从地图实例算出世界范围。底图（根节点下的 Background 容器）的范围就是地图的范围；
        /// 容器没找到就退回用整棵地图子树里的 Sprite（标记都画在地图上，不会把范围撑大）。
        /// </summary>
        private bool ResolveMapBounds()
        {
            Transform root = mapView != null ? mapView.MapRoot : null;
            if (root == null)
            {
                waited += Time.unscaledDeltaTime;
                if (!warned && (mapView == null || waited > 2f))
                {
                    warned = true;
                    Debug.LogWarning("[地图相机] 还没拿到地图根节点（MapView 未指定或地图尚未生成），缩放 / 拖动暂不生效。", this);
                }

                return false;
            }

            Transform background = root.Find("Background");
            SpriteRenderer[] sprites = (background != null ? background : root)
                .GetComponentsInChildren<SpriteRenderer>(true);

            bool any = false;
            Bounds bounds = default;
            foreach (SpriteRenderer sprite in sprites)
            {
                if (sprite == null) continue;

                if (any) bounds.Encapsulate(sprite.bounds);
                else
                {
                    bounds = sprite.bounds;
                    any = true;
                }
            }

            if (!any) return false;

            mapMin = new Vector2(bounds.min.x + mapInset.x, bounds.min.y + mapInset.y);
            mapMax = new Vector2(bounds.max.x - mapInset.z, bounds.max.y - mapInset.w);

            // 收得太狠就没意义了，退回没收缩的范围。
            if (mapMax.x - mapMin.x <= 1e-3f || mapMax.y - mapMin.y <= 1e-3f)
            {
                mapMin = new Vector2(bounds.min.x, bounds.min.y);
                mapMax = new Vector2(bounds.max.x, bounds.max.y);
            }

            return true;
        }

        /// <summary>把你自己接的边框图摆成窗口的位置和大小（居中）。没接就什么都不做。</summary>
        private void ApplyBorderFrame()
        {
            if (borderFrame == null) return;

            var rect = new Rect(windowPixels.xMin - borderPadding.x, windowPixels.yMin - borderPadding.y,
                windowPixels.width + borderPadding.x * 2f, windowPixels.height + borderPadding.y * 2f);

            Canvas canvas = borderFrame.GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            // 屏幕像素 → Canvas 局部单位（Canvas 局部原点在屏幕中心，所以先减掉半屏）。
            borderFrame.anchorMin = new Vector2(0.5f, 0.5f);
            borderFrame.anchorMax = new Vector2(0.5f, 0.5f);
            borderFrame.pivot = new Vector2(0.5f, 0.5f);
            borderFrame.anchoredPosition = new Vector2(rect.center.x - screenWidth * 0.5f,
                rect.center.y - screenHeight * 0.5f) / scale;
            borderFrame.sizeDelta = new Vector2(rect.width, rect.height) / scale;
        }

        // —— 占位边框（自动生成，可关）——

        /// <summary>建一次占位边框：4 块遮罩铺满窗口外面 + 4 条描边紧贴窗口。</summary>
        private void EnsureChrome()
        {
            if (chromeRects != null || !autoWindowChrome) return;

            var canvasGo = new GameObject("MapWindowChrome", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1; // 在 HUD 之下：纯装饰，不能挡住任何 UI

            var canvasRect = (RectTransform)canvasGo.transform;

            chromeRects = new RectTransform[8];
            for (int i = 0; i < chromeRects.Length; i++)
            {
                bool isBackdrop = i < 4;
                var go = new GameObject(isBackdrop ? $"Backdrop_{i}" : $"Frame_{i - 4}",
                    typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(canvasRect, false);
                rt.anchorMin = Vector2.zero;   // 锚在画布左下角
                rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;

                var image = go.GetComponent<Image>();
                image.sprite = null;           // 没图就是一块纯色矩形
                image.raycastTarget = false;   // 纯装饰，绝不拦截点击
                image.color = isBackdrop ? backdropColor : frameColor;

                chromeRects[i] = rt;
            }
        }

        /// <summary>把占位边框摆到当前窗口上。接了自己的边框图时，自动描边让位（遮罩保留）。</summary>
        private void LayoutChrome()
        {
            EnsureChrome();
            if (chromeRects == null) return;

            float t = frameThickness;
            bool showOurFrame = borderFrame == null;

            // 窗口外面的四块遮罩（避免裁切掉的那一圈露出没清理的画面）。
            SetRect(chromeRects[0], 0f, 0f, windowPixels.xMin, screenHeight);
            SetRect(chromeRects[1], windowPixels.xMax, 0f, screenWidth - windowPixels.xMax, screenHeight);
            SetRect(chromeRects[2], windowPixels.xMin, 0f, windowPixels.width, windowPixels.yMin);
            SetRect(chromeRects[3], windowPixels.xMin, windowPixels.yMax, windowPixels.width, screenHeight - windowPixels.yMax);

            // 紧贴窗口的一圈描边（画在窗口外侧，不压住地图）。
            SetRect(chromeRects[4], windowPixels.xMin - t, windowPixels.yMax, windowPixels.width + 2f * t, t);
            SetRect(chromeRects[5], windowPixels.xMin - t, windowPixels.yMin - t, windowPixels.width + 2f * t, t);
            SetRect(chromeRects[6], windowPixels.xMin - t, windowPixels.yMin, t, windowPixels.height);
            SetRect(chromeRects[7], windowPixels.xMax, windowPixels.yMin, t, windowPixels.height);

            for (int i = 4; i < 8; i++) chromeRects[i].gameObject.SetActive(showOurFrame);
        }

        private static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(Mathf.Max(width, 0f), Mathf.Max(height, 0f));
        }

        /// <summary>滚轮缩放：让鼠标底下的那个世界点保持不动，然后立刻收进边界。</summary>
        private void Zoom(Camera camera, Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f)) return;

            // 滚轮一格在 Windows 上一般是 120；有些鼠标/驱动只报 1。统一折算成「格数」。
            float notches = Mathf.Abs(scroll) > 30f ? scroll / 120f : scroll;
            notches *= Mathf.Max(zoomSpeed, 0.1f);

            float target = Mathf.Clamp(camera.orthographicSize * Mathf.Pow(zoomPerNotch, notches),
                zoomInLimit, zoomOutLimit);
            if (Mathf.Approximately(target, camera.orthographicSize)) return;

            Vector2 screen = mouse.position.ReadValue();
            Vector3 before = ScreenToWorldPoint(camera, screen);

            camera.orthographicSize = target;

            Vector3 after = ScreenToWorldPoint(camera, screen);
            camera.transform.position += before - after;

            // 撞到窗口边界时以边界为准，光标居中那点偏移让位。
            ClampPosition(camera);
        }

        /// <summary>右键长按拖动平移：往右拖 = 地图跟着往右走，拖到地图边缘贴住窗口就停住。</summary>
        private void Pan(Camera camera, Mouse mouse)
        {
            Vector2 delta = mouse.delta.ReadValue(); // 屏幕像素
            if (delta.sqrMagnitude <= 0f) return;

            // 视野高 2×正交尺寸 只占窗口高度这么多像素 —— 分母要用窗口高度，不是屏幕高度。
            float worldPerPixel = 2f * camera.orthographicSize / Mathf.Max(windowPixels.height, 1f);
            camera.transform.position -= new Vector3(delta.x, delta.y, 0f) * worldPerPixel;
            ClampPosition(camera);
        }

        /// <summary>开局把相机摆到地图中心（此时视野正好等于地图，中心就是唯一合法位置）。</summary>
        private void CenterAndClamp(Camera camera)
        {
            Vector2 center = (mapMin + mapMax) * 0.5f;
            Vector3 position = camera.transform.position;
            position.x = center.x;
            position.y = center.y;
            camera.transform.position = position;
        }

        /// <summary>
        /// 把相机钳制在「视野刚好不越出地图」的范围内：放大后地图比视野大，可以拖；
        /// 未放大时视野正好等于地图，只能停在地图中心。
        /// </summary>
        private void ClampPosition(Camera camera)
        {
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * Mathf.Max(windowAspect, 1e-3f);

            Vector3 position = camera.transform.position;
            position.x = ClampAxis(position.x, mapMin.x, mapMax.x, halfWidth);
            position.y = ClampAxis(position.y, mapMin.y, mapMax.y, halfHeight);
            camera.transform.position = position;
        }

        private static float ClampAxis(float value, float min, float max, float half)
        {
            float low = min + half;
            float high = max - half;

            // 视野比地图还大时没有可拖的余量，直接锁在地图中心。
            return low > high ? (min + max) * 0.5f : Mathf.Clamp(value, low, high);
        }

        /// <summary>
        /// 屏幕坐标 → 世界坐标。相机被裁成窗口（<see cref="Camera.rect"/>）之后，屏幕坐标必须先换算成
        /// 「相机视口内的比例坐标」再交给相机，否则会整体偏移。<see cref="MapPointerInput"/> 也走这个方法。
        /// </summary>
        public static Vector3 ScreenToWorldPoint(Camera camera, Vector2 screen)
        {
            Rect rect = camera.rect;
            float u = rect.width > 0f ? (screen.x / Mathf.Max(Screen.width, 1) - rect.x) / rect.width : 0f;
            float v = rect.height > 0f ? (screen.y / Mathf.Max(Screen.height, 1) - rect.y) / rect.height : 0f;

            var point = new Vector3(u, v, 0f);
            point.z = Mathf.Abs(camera.transform.position.z); // 正交相机下 z 取到相机平面的距离
            return camera.ViewportToWorldPoint(point);
        }
    }
}

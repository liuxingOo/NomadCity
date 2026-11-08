using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace NomadCity.Game
{
    /// <summary>
    /// 把鼠标左键点击翻译成一次地图命中（<see cref="MapHit"/>）并广播出去。
    ///
    /// 为什么不用 OnMouseDown：项目只启用新 Input System（activeInputHandler = 1），
    /// 传统 OnMouseDown / Input.mousePosition 不可用，所以统一走 <see cref="Mouse"/>。
    ///
    /// 只负责「翻译」，不含任何规则判断：命中的是什么、能不能操作，全由 UI 层的 GameScreenController 决定。
    /// </summary>
    public sealed class MapPointerInput : MonoBehaviour
    {
        [Tooltip("用于把屏幕坐标换算成世界坐标的相机。留空则用 Camera.main。")]
        [SerializeField] private Camera worldCamera;

        [Tooltip("负责解析命中的地图渲染器。")]
        [SerializeField] private MapStateRenderer stateRenderer;

        /// <summary>一次有效的地图点击（已经过滤掉点在 UI 上的情况）。</summary>
        public event Action<MapHit> Clicked;

        /// <summary>
        /// 点在地图上、但没命中任何可点元素（点在资源点左上角的指示物框、空白海面等）。
        /// 交给上层决定要不要提示 —— 沉默的点击最容易被误判成「界面卡住了」。
        /// </summary>
        public event Action<Vector2> Missed;

        private void Reset()
        {
            worldCamera = Camera.main;
            stateRenderer = GetComponent<MapStateRenderer>();
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            // 点在 HUD / 按钮上时直接丢弃，避免穿透到地图。
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (stateRenderer == null || !stateRenderer.IsReady) return;

            Camera camera = worldCamera != null ? worldCamera : Camera.main;
            if (camera == null) return;

            // 换算统一走 MapCameraController：相机被裁成地图窗口后，屏幕坐标必须先转成视口比例坐标，
            // 否则点选会整体偏移。窗口铺满全屏时（未启用缩放）两者等价。
            Vector2 screen = mouse.position.ReadValue();

            // 点在窗口之外（地图已被裁掉的那一圈）时直接丢弃，否则会点到看不见的资源点。
            if (!camera.pixelRect.Contains(screen)) return;

            Vector3 world = MapCameraController.ScreenToWorldPoint(camera, screen);

            if (stateRenderer.TryPick(world, out MapHit hit)) Clicked?.Invoke(hit);
            else Missed?.Invoke(world);
        }
    }
}

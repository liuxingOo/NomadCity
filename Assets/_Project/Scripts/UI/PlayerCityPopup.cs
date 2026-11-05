using System;
using NomadCity.Core.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 「看某位玩家的城市面板」弹窗：由状态栏每行左侧那颗眼睛小按钮打开
    /// （<c>GameHud.SeatInspectRequested</c> → 本组件 <see cref="Show"/>）。
    ///
    /// 里面就是一块和常驻城市面板同款的 12 格面板，只读：不参与建设，也不接管任何流程，
    /// 点空白 / 关闭按钮收起，或者从别的面板打开时由 <see cref="CenteredPanelGroup"/> 顺手关掉。
    ///
    /// 场景结构（由生成器 NomadCity / 界面：生成玩家城市弹窗 建好，也可以自己搭）：
    /// Canvas
    ///   PlayerCityPopup（宿主，挂本脚本，自身永远激活）
    ///     Root              ← 弹窗开关，由 Show / Hide 控制
    ///       Dim             ← 全屏半透明遮罩（Raycast Target 勾上，挡住底下的地图与按钮）
    ///       Panel           ← 底板，Source Image = Art/CityPanel.png
    ///         Title         ← 「某某 的城市」
    ///         CityView      ← 挂 CityPanelView，12 个格子在里面
    ///         CloseButton   ← 右上角关闭
    /// </summary>
    public sealed class PlayerCityPopup : MonoBehaviour, ICenteredPanel
    {
        [Tooltip("弹窗开关物体（宿主 → Root → Dim / Panel）。宿主自身必须保持激活，只切这一个。")]
        [SerializeField] private GameObject root;

        [Tooltip("标题文本：写「某某 的城市」")]
        [SerializeField] private TMP_Text title;

        [Tooltip("右上角关闭按钮（可选）")]
        [SerializeField] private Button closeButton;

        [Tooltip("点遮罩也收起（可选）：把 Dim 那个 Button 拖进来")]
        [SerializeField] private Button dimButton;

        [Tooltip("内容：挂着 CityPanelView 的那块城市面板（只读显示）")]
        [SerializeField] private CityPanelView cityView;

        /// <summary>正在看谁的座位；-1 = 没开。</summary>
        private int seat = -1;

        /// <summary>最近一次拿到的状态：面板开着的时候每次刷新用它重画。</summary>
        private GameState lastState;

        /// <summary>弹窗是不是开着。</summary>
        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>正在看谁的座位（-1 = 没开）。</summary>
        public int Seat => seat;

        /// <summary>
        /// 点了面板上的某一格：参数是（正在看的座位号, 格子下标）。
        /// 由 GameScreenController 接住，弹出对应设施的放大卡面 —— 和常驻城市面板是同一套行为。
        /// </summary>
        public event Action<int, int> SlotClicked;

        private void Awake()
        {
            CenteredPanelGroup.Register(this);

            if (root == null)
                Debug.LogWarning("[玩家城市] 没有接「Root」——弹窗会一直显示。请把弹窗根拖进本组件的 Root 字段。", this);

            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (dimButton != null) dimButton.onClick.AddListener(Hide);

            // 面板里 12 个格子自己会发 SlotClicked，这里转一手，把「正在看的座位」一起带出去。
            if (cityView != null) cityView.SlotClicked += OnInnerSlotClicked;

            // 宿主默认是关着的：Awake 里先收起来，免得一进场就被这块面板盖住。
            if (root != null) root.SetActive(false);
        }

        private void OnDestroy()
        {
            CenteredPanelGroup.Unregister(this);
            if (closeButton != null) closeButton.onClick.RemoveListener(Hide);
            if (dimButton != null) dimButton.onClick.RemoveListener(Hide);
            if (cityView != null) cityView.SlotClicked -= OnInnerSlotClicked;
        }

        /// <summary>把内部面板的点击加了座位号再抛出去；面板没开（座位 -1）时直接丢掉。</summary>
        private void OnInnerSlotClicked(int slot)
        {
            if (seat < 0) return;
            SlotClicked?.Invoke(seat, slot);
        }

        /// <summary>
        /// 打开（或再点一次同一行时收起）。
        /// 打开前先让互斥组把设施供应区 / 城市样式 / 行动记录收起来 —— 它们各自带全屏遮罩，
        /// 叠在一起会互相盖住，看起来像「点了没反应」。
        /// </summary>
        public void Show(GameState state, int targetSeat, string displayName)
        {
            if (root == null) return;

            if (IsOpen && seat == targetSeat)
            {
                Hide();
                return;
            }

            CenteredPanelGroup.BeforeOpen(this);

            seat = targetSeat;
            lastState = state;
            if (title != null)
                title.text = $"{displayName} 的城市";

            root.SetActive(true);
            Refresh(state);
        }

        /// <summary>收起弹窗（关闭按钮 / 遮罩点击都走这里）。</summary>
        public void Hide() => ForceClose();

        /// <summary>互斥组用：无条件收起。</summary>
        public void ForceClose()
        {
            if (root != null && root.activeSelf) root.SetActive(false);
            seat = -1;
        }

        /// <summary>
        /// 按最新状态重画面板内容。控制器每次刷新都会调一次（面板没开就什么都不做），
        /// 所以盖放 / 建设 / 弃牌这些变动不用单独接线，开着的时候也能实时跟上。
        /// </summary>
        public void Refresh(GameState state)
        {
            if (state != null) lastState = state;
            if (!IsOpen || cityView == null || lastState == null) return;
            if (seat < 0 || seat >= lastState.Players.Count) return;

            // buildMode = false：所有格子只读，点上去只会弹放大卡面（这里没接，等于纯展示）。
            cityView.Refresh(lastState, seat, false);
        }
    }
}

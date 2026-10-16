using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 通用的小选择弹窗：中央一个小面板，一行一个选项按钮，外加一个取消按钮。
    /// 用于「路费付给这条航道上的哪一位对手」这类需要玩家当场挑一个对象的小决策（§6.6）。
    ///
    /// 与 <see cref="EventCardPanel"/> 一样只负责展示与发出意图，不做任何规则判断 ——
    /// 玩家点了第几个选项，通过 <see cref="Show"/> 传入的回调交回给 GameScreenController；
    /// 点取消则回调 -1。场景里预先摆好几个选项按钮，面板按需要只启用前 N 个。
    /// </summary>
    public sealed class ChoicePopup : MonoBehaviour
    {
        [Tooltip("整块弹窗的根节点；Show/Hide 直接开关它")]
        [SerializeField] private GameObject root;

        [Tooltip("半透明遮罩；保留 Raycast Target，弹出期间挡住地图点击")]
        [SerializeField] private Image dim;

        [Tooltip("标题行，说明这一次在选什么")]
        [SerializeField] private TMP_Text titleText;

        [Tooltip("选项按钮，按顺序排列；Show 时只启用前 N 个，其余隐藏。至少要能放下一条航道上的全部对手（最多 2 位）")]
        [SerializeField] private Button[] optionButtons = Array.Empty<Button>();

        [Tooltip("取消按钮；点了回调 -1")]
        [SerializeField] private Button cancelButton;

        private Action<int> onChosen;

        /// <summary>弹窗是不是开着。GameScreenController 用它在地图点击时早退。</summary>
        public bool IsVisible => root != null && root.activeSelf;

        private void Awake()
        {
            // 弹出期间遮罩要挡住地图点击（跟展开态的事件卡面板同理）。
            if (dim != null) dim.raycastTarget = true;
            else
                Debug.LogWarning("[ChoicePopup] 没有接线「Dim」遮罩：弹窗背后不会变暗。" +
                                 "地图点击与「确认 / 取消 / 结束回合」按钮在代码里已经挡住，但建议把面板上的半透明遮罩 Image " +
                                 "拖到本组件的「Dim」字段，好让玩家一眼看出该先答这个窗口。", this);
            if (cancelButton != null) cancelButton.onClick.AddListener(() => Choose(-1));

            for (int i = 0; i < optionButtons.Length; i++)
            {
                Button button = optionButtons[i];
                if (button == null) continue;

                int index = i; // 闭包捕获：每个按钮绑定自己的下标
                button.onClick.AddListener(() => Choose(index));
            }

            Hide();
        }

        /// <summary>
        /// 弹出一个小选择：标题 + 一组选项文案。
        /// 玩家点了第 <paramref name="onSelected"/> 个就带出那个下标（0 起）；点取消带出 -1。
        /// </summary>
        public void Show(string title, IReadOnlyList<string> options, Action<int> onSelected)
        {
            onChosen = onSelected;

            // 先把根节点打开再去按钮上找文字 —— 面板平时是隐藏的，
            // 隐藏态下 GetComponentInChildren 有可能取不到子物体上的 TMP_Text。
            if (root != null) root.SetActive(true);

            if (titleText != null) titleText.text = title ?? string.Empty;

            int count = options != null ? options.Count : 0;

            // 下面几种情况都会让弹窗「有标题、有取消键，就是一个选项都没有」，看起来像按钮坏了。
            // 与其静默，不如在 Console 里点名，省掉一轮排查。
            if (count > 0 && optionButtons.Length == 0)
                Debug.LogError($"[ChoicePopup] 这次要显示 {count} 个选项，但 Option Buttons 数组是空的，" +
                               "弹窗里不会有任何选项。请把面板上的选项按钮逐个拖进该数组（Size 与按钮个数一致）。", this);

            if (count > optionButtons.Length && optionButtons.Length > 0)
                Debug.LogError($"[ChoicePopup] 这次要显示 {count} 个选项，但 Option Buttons 只有 {optionButtons.Length} 个槽位，" +
                               $"后面 {count - optionButtons.Length} 个显示不出来。", this);

            for (int i = 0; i < optionButtons.Length; i++)
            {
                Button button = optionButtons[i];
                if (button == null)
                {
                    // 只有「这次真的要用到」的槽位是 None 才值得报错（用不到的槽位留空无所谓）。
                    if (i < count)
                        Debug.LogError($"[ChoicePopup] Option Buttons 第 {i} 个槽位是 None，本次第 {i} 个选项显示不出来，" +
                                       "请在 Inspector 里把它拖好。", this);
                    continue;
                }

                bool used = i < count;
                button.gameObject.SetActive(used);
                if (!used) continue;

                TMP_Text label = button.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = options[i];
                else
                    Debug.LogWarning($"[ChoicePopup] 选项按钮「{button.name}」下没有 TMP_Text，文案写不上去。", this);
            }
        }

        public void Hide()
        {
            onChosen = null;
            if (root != null) root.SetActive(false);
        }

        private void Choose(int index)
        {
            Action<int> callback = onChosen;
            Hide();
            callback?.Invoke(index);
        }
    }
}

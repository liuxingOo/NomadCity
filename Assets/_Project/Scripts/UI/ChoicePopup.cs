using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 通用的小选择弹窗：中央一块面板，若干选项按钮，外加一个取消按钮。
    /// 用于「路费付给这条航道上的哪一位对手」（§6.6）、入场效果的「二选一」与「挑一个落点／对象」（§11）这类
    /// 需要玩家当场挑一个的小决策。
    ///
    /// 与 <see cref="EventCardPanel"/> 一样只负责展示与发出意图，不做任何规则判断 ——
    /// 玩家点了第几个选项，通过 <see cref="Show"/> 传入的回调交回给 GameScreenController；点取消回调 -1。
    ///
    /// 选项个数并不固定（入场效果的选择可能十几项），所以按钮是**运行时按需复制**出来的：
    /// 场景里先摆好 3 个当作模板与前三个槽位，不够时照第 1 个的样子复制到同一父节点下，超过一页自动分页
    /// （末两个槽位变成「上一页 / 下一页」，页标写在标题后面）。因此 Inspector 里第 1 个按钮的位置就是
    /// 整体基准位置，改它即可把整块选项区挪走。
    /// </summary>
    public sealed class ChoicePopup : MonoBehaviour
    {
        [Tooltip("整块弹窗的根节点；Show/Hide 直接开关它")]
        [SerializeField] private GameObject root;

        [Tooltip("半透明遮罩；保留 Raycast Target，弹出期间挡住地图点击")]
        [SerializeField] private Image dim;

        [Tooltip("标题行，说明这一次在选什么")]
        [SerializeField] private TMP_Text titleText;

        [Tooltip("选项按钮：第 1 个是模板与基准位置，后两个是第 2、3 个槽位；不够时照第 1 个自动复制")]
        [SerializeField] private Button[] optionButtons = Array.Empty<Button>();

        [Tooltip("取消按钮；点了回调 -1。这次选择不允许放弃时（如牌面「二选一」）会自动隐藏")]
        [SerializeField] private Button cancelButton;

        [Tooltip("一页最多显示几个选项，超出自动分页（末两个槽位会变成上一页 / 下一页）")]
        [SerializeField] private int pageSize = 12;

        [Tooltip("同一行相邻按钮的水平间距")]
        [SerializeField] private float columnStep = 350f;

        [Tooltip("上下两行按钮的垂直间距")]
        [SerializeField] private float rowStep = 60f;

        /// <summary>选项按钮池：前几项是 Inspector 摆好的，其余是运行时复制出来的。</summary>
        private readonly List<Button> slots = new List<Button>();

        /// <summary>与 <see cref="slots"/> 一一对应的文案控件（按钮隐藏时取不到，所以开池时就抓好）。</summary>
        private readonly List<TMP_Text> slotLabels = new List<TMP_Text>();

        /// <summary>每个槽位对应的选项下标；-2 = 分页按钮，-1 = 空槽。</summary>
        private int[] slotOption = Array.Empty<int>();

        private Action<int> onChosen;
        private string title = string.Empty;
        private IReadOnlyList<string> optionLabels = Array.Empty<string>();
        private int page;
        private int navPrevSlot = -1;
        private int navNextSlot = -1;

        private float baseX;
        private float baseY;
        private Vector2 cancelIdle;

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

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(() => Choose(-1));
                cancelIdle = ((RectTransform)cancelButton.transform).anchoredPosition;
            }

            foreach (Button button in optionButtons)
            {
                if (button == null) continue;
                AddSlot(button);
            }

            if (slots.Count > 0)
            {
                var rect = (RectTransform)slots[0].transform;
                baseX = rect.anchoredPosition.x;
                baseY = rect.anchoredPosition.y;
            }

            Hide();
        }

        /// <summary>把一个按钮登记为槽位（第 0 个同时作为复制的模板）。</summary>
        private void AddSlot(Button button)
        {
            int index = slots.Count;
            slots.Add(button);
            slotLabels.Add(FindLabel(button));
            button.onClick.AddListener(() => OnSlotClicked(index));
        }

        /// <summary>保证至少有 <paramref name="count"/> 个槽位：不够就照第 0 个复制。</summary>
        private void EnsureSlots(int count)
        {
            if (slots.Count == 0)
            {
                Debug.LogError("[ChoicePopup] 这次要弹选项，但 Option Buttons 数组是空的，一个按钮也拿不出来。" +
                               "请在 Inspector 里至少拖 1 个选项按钮进去当模板。", this);
                return;
            }

            Button template = slots[0];
            while (slots.Count < count && template != null)
            {
                Button copy = Instantiate(template, template.transform.parent);
                copy.name = $"Option{slots.Count + 1}";
                copy.onClick.RemoveAllListeners(); // 复制出来的按钮只认自己的槽位下标
                AddSlot(copy);
            }

            if (slotOption.Length < slots.Count) Array.Resize(ref slotOption, slots.Count);
        }

        private static TMP_Text FindLabel(Button button)
        {
            if (button == null) return null;

            TMP_Text[] texts = button.GetComponentsInChildren<TMP_Text>(true);
            return texts.Length > 0 ? texts[0] : null;
        }

        /// <summary>
        /// 弹出一个小选择：标题 + 一组选项文案。
        /// 玩家点了第几个就带出那个下标（0 起）；点取消带出 -1。
        /// <paramref name="allowCancel"/> = false 时隐藏取消键（这一步必须选一个）。
        /// </summary>
        public void Show(string title, IReadOnlyList<string> options, Action<int> onSelected, bool allowCancel = true)
        {
            onChosen = onSelected;
            this.title = title ?? string.Empty;
            optionLabels = options ?? Array.Empty<string>();
            page = 0;

            // 先把根节点打开再去按钮上找文字 —— 面板平时是隐藏的，
            // 隐藏态下 GetComponentInChildren 有可能取不到子物体上的 TMP_Text。
            if (root != null) root.SetActive(true);
            if (cancelButton != null) cancelButton.gameObject.SetActive(allowCancel);

            Render();
        }

        /// <summary>按当前页把选项铺到槽位上：一列放不下就折成多列，放不下整页就上分页键。</summary>
        private void Render()
        {
            int count = optionLabels.Count;
            int perPage = Mathf.Max(1, pageSize);
            int pages = count <= 0 ? 1 : (count + perPage - 1) / perPage;
            page = Mathf.Clamp(page, 0, pages - 1);

            int first = page * perPage;
            int visible = Mathf.Clamp(count - first, 0, perPage);
            bool paging = pages > 1;
            int cells = visible + (paging ? 2 : 0);

            EnsureSlots(Mathf.Max(cells, 1));

            for (int i = 0; i < slots.Count; i++)
            {
                slotOption[i] = -1;
                slots[i].gameObject.SetActive(false);
            }

            navPrevSlot = -1;
            navNextSlot = -1;

            for (int i = 0; i < visible; i++)
            {
                SetSlot(i, optionLabels[first + i]);
                slotOption[i] = first + i;
                slots[i].gameObject.SetActive(true);
            }

            if (paging)
            {
                navPrevSlot = visible;
                navNextSlot = visible + 1;

                SetSlot(navPrevSlot, "◀ 上一页");
                slotOption[navPrevSlot] = -2;
                slots[navPrevSlot].gameObject.SetActive(page > 0);

                SetSlot(navNextSlot, "下一页 ▶");
                slotOption[navNextSlot] = -2;
                slots[navNextSlot].gameObject.SetActive(page < pages - 1);
            }

            Layout(cells);

            if (titleText != null)
                titleText.text = paging ? $"{title}（第 {page + 1}/{pages} 页）" : title;

            if (count == 0 && optionLabels != null)
                Debug.LogWarning("[ChoicePopup] 这次没有可选事项，只会留下一个取消键。", this);
        }

        /// <summary>把 <paramref name="cells"/> 个槽位摆成网格（1 列 / 2 列 / 3 列），并把取消键挪到最下一行之下。</summary>
        private void Layout(int cells)
        {
            if (slots.Count == 0 || cells <= 0) return;

            int columns = cells <= 3 ? 1 : cells <= 6 ? 2 : 3;
            int rows = (cells + columns - 1) / columns;

            for (int i = 0; i < cells && i < slots.Count; i++)
            {
                int row = i / columns;
                int column = i % columns;
                float x = baseX + (column - (columns - 1) * 0.5f) * columnStep;
                float y = baseY - row * rowStep;
                ((RectTransform)slots[i].transform).anchoredPosition = new Vector2(x, y);
            }

            if (cancelButton != null)
            {
                float bottom = baseY - (rows - 1) * rowStep;
                ((RectTransform)cancelButton.transform).anchoredPosition =
                    new Vector2(cancelIdle.x, Mathf.Min(cancelIdle.y, bottom - rowStep));
            }
        }

        private void SetSlot(int index, string label)
        {
            if (index < 0 || index >= slots.Count) return;

            TMP_Text text = slotLabels[index];
            if (text != null) text.text = label ?? string.Empty;
            else
                Debug.LogWarning($"[ChoicePopup] 选项按钮「{slots[index].name}」下没有 TMP_Text，文案写不上去。", this);
        }

        private void OnSlotClicked(int slotIndex)
        {
            if (slotIndex == navPrevSlot)
            {
                page--;
                Render();
                return;
            }

            if (slotIndex == navNextSlot)
            {
                page++;
                Render();
                return;
            }

            if (slotIndex < 0 || slotIndex >= slotOption.Length) return;

            int optionIndex = slotOption[slotIndex];
            if (optionIndex >= 0) Choose(optionIndex);
        }

        public void Hide()
        {
            onChosen = null;
            page = 0;
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

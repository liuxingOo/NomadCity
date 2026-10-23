using System;
using NomadCity.Core.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 开局前的企业家挑选弹窗（单机与联机共用，替换原来的纯颜色选择）。
    ///
    /// 只负责显示与抛点击，不碰网络：「轮到谁挑、挑完下一步做什么」全部由 GameBootstrap 决定。
    /// 四张企业家牌始终全部可见（2／3 人局也不会隐藏），已被别人挑走的牌会置灰并显示占用者。
    /// 企业家决定阵营颜色（Agnes 红 / Paul 蓝 / Martin 黄 / Tiag 绿），颜色由选项上的 Color Index 指定。
    /// </summary>
    public sealed class EntrepreneurChoicePanel : MonoBehaviour
    {
        [Tooltip("宿主容器保持激活，运行时只开关这个 Root（与其它通用弹窗一致）")]
        [SerializeField] private GameObject root;

        [Tooltip("标题：如「玩家 2 请挑选企业家」/「选择你的企业家」")]
        [SerializeField] private TMP_Text titleText;

        [Tooltip("提示行：等待进度、已提交等")]
        [SerializeField] private TMP_Text hintText;

        [Tooltip("行动顺序 / 挑选顺序（多行文本）")]
        [SerializeField] private TMP_Text orderText;

        [Tooltip("四张企业家牌；Color Index 填 Agnes=2(红) / Paul=3(蓝) / Martin=1(黄) / Tiag=0(绿)")]
        [SerializeField] private EntrepreneurOption[] options = Array.Empty<EntrepreneurOption>();

        /// <summary>本机玩家点了一张企业家牌；参数是它对应的阵营颜色。</summary>
        public event Action<PlayerColor> Chosen;

        private void Awake()
        {
            if (root != null) root.SetActive(false);

            ValidateWiring();

            for (int i = 0; i < options.Length; i++)
            {
                EntrepreneurOption option = options[i];
                if (option?.Button == null) continue;

                int index = i;
                option.Button.onClick.AddListener(() => OnOptionClicked(index));
            }
        }

        /// <summary>
        /// 宿主容器/文本/按钮漏接时，表现会是「文字一直是 New Text、挑完窗口也不关」——
        /// 这两种现象都很难从画面上反推原因，所以在 Awake 一次性把缺的字段列出来。
        /// </summary>
        private void ValidateWiring()
        {
            var missing = new System.Collections.Generic.List<string>();

            if (root == null) missing.Add("Root");
            if (titleText == null) missing.Add("Title Text");
            if (hintText == null) missing.Add("Hint Text");
            if (orderText == null) missing.Add("Order Text");
            if (options.Length != 4) missing.Add($"Options（当前 {options.Length} 个，应为 4）");

            for (int i = 0; i < options.Length; i++)
                if (options[i] == null || options[i].Button == null) missing.Add($"Options/{i}/Button");

            if (missing.Count > 0)
                Debug.LogError("[企业家挑选] 以下字段没接线，会出现「文字不变、挑完不关」的假象：" +
                               string.Join("、", missing), this);
        }

        public void Show(string title, string hint, string order)
        {
            if (root != null) root.SetActive(true);
            if (titleText != null) titleText.text = title ?? string.Empty;
            SetOrder(order);
            SetHint(hint);
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        public void SetTitle(string title)
        {
            if (titleText != null) titleText.text = title ?? string.Empty;
        }

        public void SetHint(string hint)
        {
            if (hintText != null) hintText.text = hint ?? string.Empty;
        }

        public void SetOrder(string order)
        {
            if (orderText != null) orderText.text = order ?? string.Empty;
        }

        /// <summary>
        /// 刷新可点状态与占用者。
        /// pickedBySeat = 各座位已挑中的颜色（-1 = 未挑）；names = 座位昵称；
        /// canPick = 现在是否轮到本机玩家挑（不是自己就全部置灰）。
        /// </summary>
        public void Refresh(int[] pickedBySeat, string[] names, bool canPick)
        {
            for (int i = 0; i < options.Length; i++)
            {
                EntrepreneurOption option = options[i];
                if (option?.Button == null) continue;

                int ownerSeat = OwnerOf(pickedBySeat, option.ColorIndex);
                option.Button.interactable = canPick && ownerSeat < 0;

                if (option.Label != null)
                {
                    string name = string.IsNullOrWhiteSpace(option.EntrepreneurName)
                        ? EntrepreneurCatalog.NameOf((PlayerColor)option.ColorIndex)
                        : option.EntrepreneurName;

                    option.Label.text = ownerSeat >= 0
                        ? $"{name}\n{SeatName(names, ownerSeat)}"
                        : name;
                }
            }
        }

        private void OnOptionClicked(int index)
        {
            if (index < 0 || index >= options.Length) return;

            EntrepreneurOption option = options[index];
            if (option?.Button == null || !option.Button.interactable) return;

            Chosen?.Invoke((PlayerColor)option.ColorIndex);
        }

        private static int OwnerOf(int[] pickedBySeat, int color)
        {
            if (pickedBySeat == null) return -1;
            for (int i = 0; i < pickedBySeat.Length; i++)
                if (pickedBySeat[i] == color) return i;
            return -1;
        }

        private static string SeatName(string[] names, int seat)
        {
            if (names != null && seat < names.Length && !string.IsNullOrWhiteSpace(names[seat]))
                return names[seat];
            return $"玩家 {seat + 1}";
        }
    }

    /// <summary>一个企业家选项（按钮 + 卡面 + 名字）。</summary>
    [Serializable]
    public sealed class EntrepreneurOption
    {
        [Tooltip("按钮本体（点它即挑选这名企业家）；卡图直接设在这个按钮自带的 Image 上")]
        public Button Button;

        [Tooltip("文字：企业家姓名 + 占用者（可留空，卡面本身已印名字，占用状态靠按钮置灰表达）")]
        public TMP_Text Label;

        [Tooltip("企业家姓名（留空时按颜色取 Agnes / Paul / Martin / Tiag）")]
        public string EntrepreneurName;

        [Tooltip("它对应的 PlayerColor 整数值：绿 0 / 黄 1 / 红 2 / 蓝 3")]
        [Range(0, 3)] public int ColorIndex;
    }
}

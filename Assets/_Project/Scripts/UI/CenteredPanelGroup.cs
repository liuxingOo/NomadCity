using System.Collections.Generic;

namespace NomadCity.UI
{
    /// <summary>
    /// 同位置居中弹窗的互斥组：设施供应区、城市样式、行动记录三块面板尺寸与位置相同，
    /// 同时打开会各自铺一层全屏遮罩、互相遮挡 —— 后开的被先开的盖住，看起来就像「点了按钮没反应」。
    /// 打开任意一个之前，先把其余成员全部关掉。
    ///
    /// 只做显隐协调，不碰任何规则；成员在 Awake 里自注册，场景里不用额外接线。
    /// 放大卡面（<see cref="CardPreviewPopup"/>）不在此列 —— 它是压在面板之上的只读放大层。
    /// </summary>
    public interface ICenteredPanel
    {
        /// <summary>面板是不是正开着。</summary>
        bool IsOpen { get; }

        /// <summary>无条件收起（不触发任何流程回调）。</summary>
        void ForceClose();
    }

    public static class CenteredPanelGroup
    {
        private static readonly List<ICenteredPanel> Members = new List<ICenteredPanel>();

        /// <summary>成员在 Awake 里注册；重复注册会被忽略。</summary>
        public static void Register(ICenteredPanel panel)
        {
            if (panel == null || Members.Contains(panel)) return;
            Members.Add(panel);
        }

        public static void Unregister(ICenteredPanel panel)
        {
            if (panel == null) return;
            Members.Remove(panel);
        }

        /// <summary>打开 <paramref name="opener"/> 之前调用：把除它以外所有开着的成员关掉。</summary>
        public static void BeforeOpen(ICenteredPanel opener)
        {
            // 倒序遍历：ForceClose 可能连带销毁对象（场景卸载），顺手把空成员清掉。
            for (int i = Members.Count - 1; i >= 0; i--)
            {
                ICenteredPanel member = Members[i];
                if (member == null)
                {
                    Members.RemoveAt(i);
                    continue;
                }

                if (member == opener || !member.IsOpen) continue;
                member.ForceClose();
            }
        }
    }
}

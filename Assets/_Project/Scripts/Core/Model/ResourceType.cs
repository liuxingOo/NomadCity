using System;

namespace NomadCity.Core.Model
{
    /// <summary>
    /// 资源种类。前三种是基础资源，至纯源石是进阶资源（终结结算时 1 个 = 1 分）。
    /// 依据规则规格 2.1 节。
    /// </summary>
    public enum ResourceType
    {
        Orirock = 0,        // 源岩
        Originium = 1,      // 源石
        Oriron = 2,         // 异铁
        OriginitePrime = 3, // 至纯源石
    }

    /// <summary>资源种类常量表，避免各处散落硬编码。</summary>
    public static class ResourceTypes
    {
        /// <summary>三种基础资源 —— 玩家从地图资源点上采集的对象。</summary>
        public static readonly ResourceType[] Basics =
        {
            ResourceType.Orirock,
            ResourceType.Originium,
            ResourceType.Oriron,
        };

        /// <summary>全部资源（含进阶的至纯源石）。</summary>
        public static readonly ResourceType[] All =
        {
            ResourceType.Orirock,
            ResourceType.Originium,
            ResourceType.Oriron,
            ResourceType.OriginitePrime,
        };
    }

    /// <summary>
    /// 一份资源计数。按可变结构体使用：直接对玩家持有的那个字段调用 Add / TrySpend；
    /// 不要先赋给局部变量再累加（结构体是值类型，改动不会回写）。
    /// </summary>
    [Serializable]
    public struct ResourcePool
    {
        public int Orirock;
        public int Originium;
        public int Oriron;
        public int OriginitePrime;

        public int Get(ResourceType type) => type switch
        {
            ResourceType.Orirock => Orirock,
            ResourceType.Originium => Originium,
            ResourceType.Oriron => Oriron,
            ResourceType.OriginitePrime => OriginitePrime,
            _ => 0,
        };

        public void Set(ResourceType type, int value)
        {
            switch (type)
            {
                case ResourceType.Orirock: Orirock = value; break;
                case ResourceType.Originium: Originium = value; break;
                case ResourceType.Oriron: Oriron = value; break;
                case ResourceType.OriginitePrime: OriginitePrime = value; break;
            }
        }

        /// <summary>增减某一种资源。结果不会低于 0（「失去至多 N 个」类效果友好）。</summary>
        public void Add(ResourceType type, int amount) => Set(type, Math.Max(0, Get(type) + amount));

        public bool Has(ResourceType type, int amount) => Get(type) >= amount;

        /// <summary>支付：不足则返回 false 且不做任何改动。</summary>
        public bool TrySpend(ResourceType type, int amount)
        {
            if (!Has(type, amount)) return false;
            Set(type, Get(type) - amount);
            return true;
        }
    }
}

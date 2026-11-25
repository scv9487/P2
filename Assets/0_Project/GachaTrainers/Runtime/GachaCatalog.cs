using System;
using System.Collections.Generic;

namespace GachaTrainers
{
    public enum GachaMachineKind
    {
        Creature,
        Crystal
    }

    public enum CreatureKind
    {
        EmberFox,
        MossTurtle,
        VoltRabbit,
        FrostOwl,
        TideOtter,
        StoneBoar
    }

    public enum CrystalKind
    {
        Ember,
        Protection,
        Reflection,
        DeathThroes,
        Sink,
        Capital,
        MagicSmite,
        Giant,
        Divine,
        Jester,
        Flint,
        Elasticity
    }

    // Shared rarity order for creatures and crystals, matching Dungeon Settlers.
    public enum CrystalRarity
    {
        Common,
        Uncommon,
        Rare,
        Legendary
    }

    public sealed class CreatureDefinition
    {
        public CreatureKind Kind { get; }
        public string Name { get; }
        public string Description { get; }
        public int MaxHealth { get; }
        public float Attack { get; }
        public float AttackSpeed { get; }
        public CrystalRarity Rarity { get; }

        public CreatureDefinition(CreatureKind kind, string name, string description, int maxHealth, float attack, float attackSpeed)
        {
            Kind = kind;
            Name = name;
            Description = description;
            MaxHealth = maxHealth;
            Attack = attack;
            AttackSpeed = attackSpeed;
            Rarity = kind <= CreatureKind.VoltRabbit ? CrystalRarity.Common
                : kind <= CreatureKind.TideOtter ? CrystalRarity.Uncommon : CrystalRarity.Rare;
        }
    }

    public sealed class CrystalDefinition
    {
        public CrystalKind Kind { get; }
        public string Name { get; }
        public string Description { get; }
        public CrystalRarity Rarity { get; }
        public int Weight { get; }
        public string SourceInscriptionKey { get; }

        public CrystalDefinition(CrystalKind kind, string name, string description, CrystalRarity rarity, int weight, string sourceInscriptionKey)
        {
            Kind = kind;
            Name = name;
            Description = description;
            Rarity = rarity;
            Weight = weight;
            SourceInscriptionKey = sourceInscriptionKey;
        }
    }

    public static class GachaCatalog
    {
        public const int MaxPartySize = 3;
        public static int StartingMoney => GachaBalance.Default.StartingGold;
        public static int CreaturePrice => GachaBalance.Default.CreaturePrice;
        public static int CrystalPrice => GachaBalance.Default.CrystalPrice;
        public static int CreatureWeightTotal => GachaBalance.Default.CreatureWeightTotal;
        public static int CrystalWeightTotal => GachaBalance.Default.CrystalWeightTotal;
        public static int LegendaryRollStart => GachaBalance.Default.LegendaryRollStart;
        public static IReadOnlyList<CreatureDefinition> Creatures => GachaBalance.Default.Creatures;
        public static IReadOnlyList<CrystalDefinition> Crystals => GachaBalance.Default.Crystals;

        internal static IReadOnlyList<CreatureDefinition> CreateCreatures(GachaBalance balance)
        {
            return Array.AsReadOnly(new[]
            {
                new CreatureDefinition(CreatureKind.EmberFox, "잿불여우", "작은 몸에 뜨거운 한 방을 품은 공격수.",
                    (int)balance.Creature(CreatureKind.EmberFox, "Health"), balance.Creature(CreatureKind.EmberFox, "PhysicalAttack"), balance.Creature(CreatureKind.EmberFox, "AttackSpeed")),
                new CreatureDefinition(CreatureKind.MossTurtle, "이끼거북", "단단한 등껍질과 넉넉한 체력의 수호자.",
                    (int)balance.Creature(CreatureKind.MossTurtle, "Health"), balance.Creature(CreatureKind.MossTurtle, "PhysicalAttack"), balance.Creature(CreatureKind.MossTurtle, "AttackSpeed")),
                new CreatureDefinition(CreatureKind.VoltRabbit, "번개토끼", "번개처럼 재빠르게 공격하는 속공수.",
                    (int)balance.Creature(CreatureKind.VoltRabbit, "Health"), balance.Creature(CreatureKind.VoltRabbit, "PhysicalAttack"), balance.Creature(CreatureKind.VoltRabbit, "AttackSpeed")),
                new CreatureDefinition(CreatureKind.FrostOwl, "서리올빼미", "긴 사거리의 빙결창으로 상대의 발을 묶는 사냥꾼.",
                    (int)balance.Creature(CreatureKind.FrostOwl, "Health"), balance.Creature(CreatureKind.FrostOwl, "PhysicalAttack"), balance.Creature(CreatureKind.FrostOwl, "AttackSpeed")),
                new CreatureDefinition(CreatureKind.TideOtter, "물결수달", "치유의 물결로 체력을 회복하며 싸우는 생존가.",
                    (int)balance.Creature(CreatureKind.TideOtter, "Health"), balance.Creature(CreatureKind.TideOtter, "PhysicalAttack"), balance.Creature(CreatureKind.TideOtter, "AttackSpeed")),
                new CreatureDefinition(CreatureKind.StoneBoar, "바위멧돼지", "묵직한 지각 파동으로 상대를 밀어내는 돌격수.",
                    (int)balance.Creature(CreatureKind.StoneBoar, "Health"), balance.Creature(CreatureKind.StoneBoar, "PhysicalAttack"), balance.Creature(CreatureKind.StoneBoar, "AttackSpeed")),
            });
        }

        internal static IReadOnlyList<CrystalDefinition> CreateCrystals(GachaBalance balance)
        {
            return Array.AsReadOnly(new[]
            {
                new CrystalDefinition(CrystalKind.Ember, "불씨 수정", DescribeCrystal(CrystalKind.Ember, balance), CrystalRarity.Uncommon, balance.CrystalWeights[(int)CrystalKind.Ember], "AFFECTER_EmberInscription"),
                new CrystalDefinition(CrystalKind.Protection, "보호 수정", DescribeCrystal(CrystalKind.Protection, balance), CrystalRarity.Rare, balance.CrystalWeights[(int)CrystalKind.Protection], "AFFECTER_ProtectionInscription"),
                new CrystalDefinition(CrystalKind.Reflection, "반사 수정", DescribeCrystal(CrystalKind.Reflection, balance), CrystalRarity.Rare, balance.CrystalWeights[(int)CrystalKind.Reflection], "AFFECTER_ReflectionInscription"),
                new CrystalDefinition(CrystalKind.DeathThroes, "발악 수정", DescribeCrystal(CrystalKind.DeathThroes, balance), CrystalRarity.Rare, balance.CrystalWeights[(int)CrystalKind.DeathThroes], "AFFECTER_DeathThroesInscription"),
                new CrystalDefinition(CrystalKind.Sink, "침몰 수정", DescribeCrystal(CrystalKind.Sink, balance), CrystalRarity.Rare, balance.CrystalWeights[(int)CrystalKind.Sink], "AFFECTER_SinkInscription"),
                new CrystalDefinition(CrystalKind.Capital, "사형 수정", DescribeCrystal(CrystalKind.Capital, balance), CrystalRarity.Legendary, balance.CrystalWeights[(int)CrystalKind.Capital], "AFFECTER_CapitalInscription"),
                new CrystalDefinition(CrystalKind.MagicSmite, "마격 수정", DescribeCrystal(CrystalKind.MagicSmite, balance), CrystalRarity.Common, balance.CrystalWeights[(int)CrystalKind.MagicSmite], "AFFECTER_MagicSmiteInscription"),
                new CrystalDefinition(CrystalKind.Giant, "거한 수정", DescribeCrystal(CrystalKind.Giant, balance), CrystalRarity.Common, balance.CrystalWeights[(int)CrystalKind.Giant], "AFFECTER_GiantInscription"),
                new CrystalDefinition(CrystalKind.Divine, "신력 수정", DescribeCrystal(CrystalKind.Divine, balance), CrystalRarity.Common, balance.CrystalWeights[(int)CrystalKind.Divine], "AFFECTER_DivineInscription"),
                new CrystalDefinition(CrystalKind.Jester, "광대 수정", DescribeCrystal(CrystalKind.Jester, balance), CrystalRarity.Uncommon, balance.CrystalWeights[(int)CrystalKind.Jester], "AFFECTER_JesterInscription"),
                new CrystalDefinition(CrystalKind.Flint, "부싯돌 수정", DescribeCrystal(CrystalKind.Flint, balance), CrystalRarity.Uncommon, balance.CrystalWeights[(int)CrystalKind.Flint], "AFFECTER_FlintInscription"),
                new CrystalDefinition(CrystalKind.Elasticity, "탄성 수정", DescribeCrystal(CrystalKind.Elasticity, balance), CrystalRarity.Rare, balance.CrystalWeights[(int)CrystalKind.Elasticity], "AFFECTER_ElasticityInscription"),
            });
        }

        private static string DescribeCrystal(CrystalKind kind, GachaBalance balance)
        {
            string N(string key) => balance.Number(key).ToString("0.##");
            string P(string key) => (balance.Number(key) * 100f).ToString("0.##");
            string burn = N("Battle.BurnSeconds") + "초간 " + N("Battle.BurnInterval") + "초마다 " + N("Battle.BurnDamage") + " 연소 피해";
            return kind switch
            {
                CrystalKind.Ember => "기본 공격 " + N("Crystal.Ember.HitCount") + "회 명중마다 " + burn,
                CrystalKind.Protection => "출전 시 잃은 체력 " + P("Crystal.Protection.ShieldRatio") + "% 보호막 · " + N("Crystal.Protection.Seconds") + "초 / 재발동 " + N("Crystal.Protection.Cooldown") + "초",
                CrystalKind.Reflection => "직접 공격으로 잃은 체력의 " + P("Crystal.Reflection.Ratio") + "% 반사",
                CrystalKind.DeathThroes => "잃은 체력 비율에 따라 직접 공격 피해 최대 " + P("Crystal.DeathThroes.MaxBonus") + "% 증가",
                CrystalKind.Sink => "기본 공격 " + N("Crystal.Sink.HitCount") + "회째 적의 잃은 체력 비율에 따라 피해 최대 " + P("Crystal.Sink.MaxBonus") + "% 증가",
                CrystalKind.Capital => "직접 공격으로 체력 피해를 준 뒤 체력 " + P("Crystal.Capital.Threshold") + "% 이하인 상대 처형",
                CrystalKind.MagicSmite => "기본 공격의 실제 체력 피해 " + P("Crystal.MagicSmite.Ratio") + "%를 추가 마법 피해로 부여",
                CrystalKind.Giant => "기본 공격 시 최대 체력의 " + P("Crystal.Giant.HealthRatio") + "%를 물리 공격력에 추가",
                CrystalKind.Divine => "스킬 사용 시 최대 체력의 " + P("Crystal.Divine.HealthRatio") + "%를 마법 공격력에 추가",
                CrystalKind.Jester => "회피율 +" + N("Crystal.Jester.DodgeChance") + "% · 회피 시 공격자에게 " + N("Battle.SlowSeconds") + "초 감속",
                CrystalKind.Flint => "치명타율 +" + N("Crystal.Flint.CriticalChance") + "% · 치명타 시 " + burn,
                CrystalKind.Elasticity => "가드율 +" + N("Crystal.Elasticity.GuardChance") + "% · 가드 시 잃은 체력 " + P("Crystal.Elasticity.HealRatio") + "% 회복 · " + N("Crystal.Elasticity.Seconds") + "초 피해 " + P("Crystal.Elasticity.Reduction") + "% 감소",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        public static CreatureDefinition GetCreature(CreatureKind kind, GachaBalance balance = null)
        {
            var items = (balance ?? GachaBalance.Default).Creatures;
            if ((int)kind < 0 || (int)kind >= items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return items[(int)kind];
        }

        public static CrystalDefinition GetCrystal(CrystalKind kind, GachaBalance balance = null)
        {
            var items = (balance ?? GachaBalance.Default).Crystals;
            if ((int)kind < 0 || (int)kind >= items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return items[(int)kind];
        }

        public static CreatureDefinition GetCreatureForRoll(int roll, GachaBalance balance = null)
        {
            balance ??= GachaBalance.Default;
            if (roll < 0 || roll >= balance.CreatureWeightTotal)
            {
                throw new ArgumentOutOfRangeException(nameof(roll));
            }
            if (roll >= balance.LegendaryRollStart)
            {
                return null;
            }
            return balance.Creatures[WeightedIndex(roll, balance.CreatureWeights)];
        }

        public static CrystalDefinition GetCrystalForRoll(int roll, GachaBalance balance = null)
        {
            balance ??= GachaBalance.Default;
            if (roll < 0 || roll >= balance.CrystalWeightTotal)
            {
                throw new ArgumentOutOfRangeException(nameof(roll));
            }
            return balance.Crystals[WeightedIndex(roll, balance.CrystalWeights)];
        }

        private static int WeightedIndex(int roll, IReadOnlyList<int> weights)
        {
            for (int i = 0; i < weights.Count; i++)
            {
                if (roll < weights[i])
                {
                    return i;
                }
                roll -= weights[i];
            }
            throw new InvalidOperationException("가챠 추첨 가중치가 올바르지 않습니다.");
        }

        public static int GetCreatureRollStart(CreatureKind kind, GachaBalance balance = null)
        {
            balance ??= GachaBalance.Default;
            GetCreature(kind, balance);
            return RollStart((int)kind, balance.CreatureWeights);
        }

        public static int GetCrystalRollStart(CrystalKind kind, GachaBalance balance = null)
        {
            balance ??= GachaBalance.Default;
            GetCrystal(kind, balance);
            return RollStart((int)kind, balance.CrystalWeights);
        }

        private static int RollStart(int index, IReadOnlyList<int> weights)
        {
            int result = 0;
            for (int i = 0; i < index; i++)
            {
                result += weights[i];
            }
            return result;
        }

        public static float GetCreatureDropPercent(CreatureKind kind, GachaBalance balance = null)
        {
            balance ??= GachaBalance.Default;
            GetCreature(kind, balance);
            return balance.CreatureWeights[(int)kind] * 100f / balance.CreatureWeightTotal;
        }

        public static float GetCrystalDropPercent(CrystalKind kind, GachaBalance balance = null)
        {
            balance ??= GachaBalance.Default;
            return GetCrystal(kind, balance).Weight * 100f / balance.CrystalWeightTotal;
        }

        public static string GetRarityName(CrystalRarity rarity)
        {
            return rarity switch
            {
                CrystalRarity.Common => "일반",
                CrystalRarity.Uncommon => "고급",
                CrystalRarity.Rare => "희귀",
                CrystalRarity.Legendary => "전설",
                _ => throw new ArgumentOutOfRangeException(nameof(rarity))
            };
        }
    }
}

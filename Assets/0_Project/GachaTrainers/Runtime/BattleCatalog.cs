using System;
using System.Collections.Generic;

namespace GachaTrainers
{
    public sealed class BattleDefinition
    {
        public CreatureKind Kind { get; }
        public string BasicName { get; }
        public string SpecialName { get; }
        public string SpecialDescription { get; }
        public float MoveSpeed { get; }
        public float BasicRange { get; }
        public float ProjectileSpeed { get; }
        public float SpecialCooldown { get; }

        internal BattleDefinition(CreatureKind kind, string basicName, string specialName,
            string specialDescription, float moveSpeed, float basicRange, float projectileSpeed, float specialCooldown)
        {
            Kind = kind;
            BasicName = basicName;
            SpecialName = specialName;
            SpecialDescription = specialDescription;
            MoveSpeed = moveSpeed;
            BasicRange = basicRange;
            ProjectileSpeed = projectileSpeed;
            SpecialCooldown = specialCooldown;
        }
    }

    public static class BattleCatalog
    {
        public static float SummonDuration => GachaBalance.Default.Number("Battle.SummonSeconds");
        public static float DodgeCooldown => GachaBalance.Default.Number("Battle.DodgeCooldown");
        public static float DodgeDuration => GachaBalance.Default.Number("Battle.DodgeInvulnerability");
        public static float FighterRadius => GachaBalance.Default.Number("Battle.FighterRadius");
        public const float MinX = -8f;
        public const float MaxX = 8f;
        public const float MinY = -3.5f;
        public const float MaxY = 2.3f;
        public static IReadOnlyList<CreatureKind> TestRoster { get; } = Array.AsReadOnly(new[]
        {
            CreatureKind.EmberFox,
            CreatureKind.MossTurtle,
            CreatureKind.VoltRabbit
        });

        internal static IReadOnlyList<BattleDefinition> CreateDefinitions(GachaBalance balance)
        {
            string N(string key) => balance.Number(key).ToString("0.##");
            string P(string key) => (balance.Number(key) * 100f).ToString("0.##");
            BattleDefinition Make(CreatureKind kind, string basic, string special, string description)
            {
                return new BattleDefinition(kind, basic, special, description, balance.Creature(kind, "MoveSpeed"),
                    balance.Creature(kind, "BasicRange"), balance.Creature(kind, "ProjectileSpeed"), balance.Creature(kind, "SkillCooldown"));
            }
            return Array.AsReadOnly(new[]
            {
                Make(CreatureKind.EmberFox, "잿불탄", "세 갈래 불꽃", "세 방향으로 마법 공격력 " + P("Creature.EmberFox.SkillDamage") + "% 불꽃 발사"),
                Make(CreatureKind.MossTurtle, "이끼 충격파", "수호의 파동", N("Creature.MossTurtle.ShieldSeconds") + "초간 피해 " + P("Creature.MossTurtle.ShieldReduction") + "% 감소 + 마법 공격 " + P("Creature.MossTurtle.SkillDamage") + "% 파동"),
                Make(CreatureKind.VoltRabbit, "전기탄", "번개 질주", "회피하며 마법 공격력 " + P("Creature.VoltRabbit.SkillDamage") + "% 전기탄 발사"),
                Make(CreatureKind.FrostOwl, "서리깃털", "빙결창", "마법 공격력 " + P("Creature.FrostOwl.SkillDamage") + "% 피해 + " + N("Battle.SlowSeconds") + "초 감속"),
                Make(CreatureKind.TideOtter, "물방울", "치유의 물결", "최대 체력 " + P("Creature.TideOtter.HealRatio") + "% 회복 + 마법 공격력 " + P("Creature.TideOtter.SkillDamage") + "% 물결"),
                Make(CreatureKind.StoneBoar, "돌조각", "지각 파동", "마법 공격력 " + P("Creature.StoneBoar.SkillDamage") + "% 파동 + " + N("Creature.StoneBoar.PushDistance") + "만큼 밀어내기")
            });
        }

        public static BattleDefinition Get(CreatureKind kind, GachaBalance balance = null)
        {
            var definitions = (balance ?? GachaBalance.Default).Moves;
            if ((int)kind < 0 || (int)kind >= definitions.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return definitions[(int)kind];
        }
    }
}

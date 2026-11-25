using System;
using UnityEngine;

namespace GachaTrainers
{
    // Percent fields use DS's 0..100 units. Built from species + form + crystal, never saved as mutable totals.
    public sealed class CombatStats
    {
        public int MaxHealth { get; private set; }
        public float PhysicalAttackPower { get; private set; }
        public float MagicAttackPower { get; private set; }
        public float AttackSpeed { get; private set; }
        public float CriticalChance { get; private set; }
        public float CriticalDamageBonus { get; private set; }
        public float CooldownReduction { get; private set; }
        public float HitChance { get; private set; }
        public float DodgeChance { get; private set; }
        public float GuardChance { get; private set; }
        public float GuardDamageReduction { get; private set; }
        public float LifeSteal { get; private set; }
        public float DamageDealtBonus { get; private set; }
        public float DamageTakenBonus { get; private set; }
        public float PhysicalDamageDealtBonus { get; private set; }
        public float MagicDamageDealtBonus { get; private set; }
        public float PhysicalResistance { get; private set; }
        public float MagicResistance { get; private set; }
        public float MovementSpeed { get; private set; }
        public float HealthPercentageRegeneration { get; private set; }
        public float MaxEnergy { get; private set; }
        public float EnergyRegeneration { get; private set; }
        public static float SkillEnergyCost => GachaBalance.Default.Number("Battle.SkillEnergyCost");
        private GachaBalance _balance;

        internal static CombatStats Build(CreatureKind kind, int node, CrystalKind? crystal, GachaBalance balance = null)
        {
            balance ??= GachaBalance.Default;
            var creature = GachaCatalog.GetCreature(kind, balance);
            int rank = CreatureEvolution.Get(kind, node).Rank;
            float growth = balance.Values("Evolution.RankGrowth")[rank - 1];
            var stats = new CombatStats
            {
                _balance = balance,
                MaxHealth = Mathf.RoundToInt(creature.MaxHealth * growth),
                PhysicalAttackPower = creature.Attack * growth,
                MagicAttackPower = balance.Creature(kind, "MagicAttack") * growth,
                AttackSpeed = creature.AttackSpeed,
                MovementSpeed = BattleCatalog.Get(kind, balance).MoveSpeed,
                CriticalChance = balance.Number("Stats.CriticalChance"),
                CriticalDamageBonus = balance.Number("Stats.CriticalDamageBonus"),
                HitChance = balance.Number("Stats.HitChance"),
                DodgeChance = balance.Number("Stats.DodgeChance"),
                GuardChance = balance.Number("Stats.GuardChance"),
                GuardDamageReduction = balance.Number("Stats.GuardDamageReduction"),
                CooldownReduction = balance.Number("Stats.CooldownReduction"),
                LifeSteal = balance.Number("Stats.LifeSteal"),
                DamageDealtBonus = balance.Number("Stats.DamageDealtBonus"),
                DamageTakenBonus = balance.Number("Stats.DamageTakenBonus"),
                PhysicalDamageDealtBonus = balance.Number("Stats.PhysicalDamageDealtBonus"),
                MagicDamageDealtBonus = balance.Number("Stats.MagicDamageDealtBonus"),
                PhysicalResistance = balance.Number("Stats.PhysicalResistance"),
                MagicResistance = balance.Number("Stats.MagicResistance"),
                HealthPercentageRegeneration = balance.Number("Stats.HealthPercentageRegeneration"),
                MaxEnergy = balance.Number("Stats.MaxEnergy"),
                EnergyRegeneration = balance.Number("Stats.EnergyRegeneration")
            };
            if (node == 1 || node == 3 || node == 4)
            {
                stats.PhysicalAttackPower *= balance.Number("Evolution.A.PhysicalMultiplier");
                stats.MagicAttackPower *= balance.Number("Evolution.A.MagicMultiplier");
                stats.AttackSpeed *= balance.Number("Evolution.A.AttackSpeedMultiplier");
                stats.DamageDealtBonus += balance.Number("Evolution.A.DamageBonus");
            }
            if (node == 2 || node == 5 || node == 6)
            {
                stats.MaxHealth = Mathf.RoundToInt(stats.MaxHealth * balance.Number("Evolution.B.HealthMultiplier"));
                stats.PhysicalResistance += balance.Number("Evolution.B.PhysicalResistance");
                stats.MagicResistance += balance.Number("Evolution.B.MagicResistance");
                stats.GuardChance += balance.Number("Evolution.B.GuardChance");
            }
            switch (node)
            {
                case 3:
                    stats.CriticalChance = balance.Number("Evolution.AA.CriticalChance");
                    stats.CriticalDamageBonus = balance.Number("Evolution.AA.CriticalDamageBonus");
                    stats.LifeSteal = balance.Number("Evolution.AA.LifeSteal");
                    stats.HitChance = balance.Number("Evolution.AA.HitChance");
                    stats.DodgeChance = balance.Number("Evolution.AA.DodgeChance");
                    stats.MovementSpeed *= balance.Number("Evolution.AA.MoveMultiplier");
                    break;
                case 4:
                    stats.MagicAttackPower *= balance.Number("Evolution.AB.MagicMultiplier");
                    stats.MagicDamageDealtBonus += balance.Number("Evolution.AB.MagicDamageBonus");
                    stats.CooldownReduction = balance.Number("Evolution.AB.CooldownReduction");
                    stats.MaxEnergy = balance.Number("Evolution.AB.MaxEnergy");
                    stats.EnergyRegeneration = balance.Number("Evolution.AB.EnergyRegeneration");
                    break;
                case 5:
                    stats.PhysicalDamageDealtBonus += balance.Number("Evolution.BA.PhysicalDamageBonus");
                    stats.GuardChance += balance.Number("Evolution.BA.GuardChance");
                    stats.GuardDamageReduction = balance.Number("Evolution.BA.GuardReduction");
                    stats.LifeSteal = balance.Number("Evolution.BA.LifeSteal");
                    break;
                case 6:
                    stats.PhysicalResistance += balance.Number("Evolution.BB.PhysicalResistance");
                    stats.MagicResistance += balance.Number("Evolution.BB.MagicResistance");
                    stats.DamageTakenBonus += balance.Number("Evolution.BB.DamageTakenBonus");
                    stats.HealthPercentageRegeneration += balance.Number("Evolution.BB.HealthRegeneration");
                    break;
            }
            if (crystal == CrystalKind.Jester)
            {
                stats.DodgeChance += balance.Number("Crystal.Jester.DodgeChance");
            }
            if (crystal == CrystalKind.Flint)
            {
                stats.CriticalChance += balance.Number("Crystal.Flint.CriticalChance");
            }
            if (crystal == CrystalKind.Elasticity)
            {
                stats.GuardChance += balance.Number("Crystal.Elasticity.GuardChance");
            }
            if (stats.MaxHealth < 1 || !float.IsFinite(stats.PhysicalAttackPower) || stats.PhysicalAttackPower >= int.MaxValue
                || !float.IsFinite(stats.MagicAttackPower) || !float.IsFinite(stats.AttackSpeed) || stats.AttackSpeed <= 0f)
            {
                throw new FormatException("GachaBalance: " + kind + " 진화 노드 " + node + "의 최종 능력치가 유효 범위를 초과합니다.");
            }
            return stats;
        }

        internal CombatStats WithStageMultipliers(StageDefinition stage)
        {
            double health = (double)MaxHealth * stage.HealthMultiplier;
            double physical = (double)PhysicalAttackPower * stage.PhysicalAttackMultiplier;
            double magic = (double)MagicAttackPower * stage.MagicAttackMultiplier;
            if ((float)health >= int.MaxValue || (float)physical >= int.MaxValue || magic > float.MaxValue)
            {
                throw new FormatException($"GachaStageTable.csv: Stage {stage.Number}의 배율이 전투 능력치 범위를 초과합니다.");
            }
            var result = (CombatStats)MemberwiseClone();
            result.MaxHealth = Math.Max(1, Mathf.RoundToInt((float)health));
            result.PhysicalAttackPower = (float)physical;
            result.MagicAttackPower = (float)magic;
            return result;
        }

        public string Describe()
        {
            return $"최대 체력 {MaxHealth}     물리 공격 {PhysicalAttackPower:0.#}     마법 공격 {MagicAttackPower:0.#}\n"
                + $"공격 속도 {AttackSpeed:0.##}/초     이동 속도 {MovementSpeed:0.##}\n"
                + $"치명타 {CriticalChance:0.#}% / 추가 피해 {CriticalDamageBonus:0.#}%\n"
                + $"명중 {HitChance:0.#}%     회피 {DodgeChance:0.#}%     흡혈 {LifeSteal:0.#}%\n"
                + $"가드 {GuardChance:0.#}% / 피해 감소 {GuardDamageReduction:0.#}%\n"
                + $"물리 저항 {PhysicalResistance:0.#}     마법 저항 {MagicResistance:0.#}\n"
                + $"주는 피해 +{DamageDealtBonus:0.#}%     받는 피해 {DamageTakenBonus:+0.#;-0.#;0}%\n"
                + $"물리 피해 +{PhysicalDamageDealtBonus:0.#}%     마법 피해 +{MagicDamageDealtBonus:0.#}%\n"
                + $"스킬 재사용 감소 {CooldownReduction:0.#}%     체력 재생 {HealthPercentageRegeneration:0.#}%/초\n"
                + $"기력 {MaxEnergy:0} / 재생 {EnergyRegeneration:0.#}/초     스킬 비용 {_balance.Number("Battle.SkillEnergyCost"):0.##}";
        }
    }
}

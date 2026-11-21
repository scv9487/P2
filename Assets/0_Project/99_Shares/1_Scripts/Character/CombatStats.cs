using System;
using System.Collections.Generic;
using Refactor.Util;
using UnityEngine;

namespace Refactor.Main
{
    public enum CombatStatType
    {
        MaxHealth, PhysicalAttackPower, MagicAttackPower, AttackSpeed, CriticalChance, CriticalDamageBonus,
        CooldownReduction, HitChance, DodgeChance, GuardChance, GuardDamageReduction, LifeSteal,
        PhysicalResistance, MagicResistance, DamageDealtBonus, DamageTakenBonus,
        PhysicalDamageDealtBonus, MagicDamageDealtBonus, FinalDamageMultiplier, MovementSpeed, ExpGainedBonus
    }

    // 파생 수치는 저장하지 않는다. 생성/성장 원천과 현재 시트에서 재계산한다.
    public sealed class CombatStats
    {
        private readonly Dictionary<CombatStatType, float> _values;
        internal CombatStats(Dictionary<CombatStatType, float> values)
        {
            _values = new Dictionary<CombatStatType, float>(values);
        }
        public float this[CombatStatType type] => _values[type];
        public float MaxHealth => this[CombatStatType.MaxHealth];
        public float PhysicalAttackPower => this[CombatStatType.PhysicalAttackPower];
        public float MagicAttackPower => this[CombatStatType.MagicAttackPower];
        public float AttackSpeed => this[CombatStatType.AttackSpeed];
        public float CriticalChance => this[CombatStatType.CriticalChance];
        public float CriticalDamageBonus => this[CombatStatType.CriticalDamageBonus];
        public float CooldownReduction => this[CombatStatType.CooldownReduction];
        public float HitChance => this[CombatStatType.HitChance];
        public float DodgeChance => this[CombatStatType.DodgeChance];
        public float GuardChance => this[CombatStatType.GuardChance];
        public float GuardDamageReduction => this[CombatStatType.GuardDamageReduction];
        public float LifeSteal => this[CombatStatType.LifeSteal];
        public float PhysicalResistance => this[CombatStatType.PhysicalResistance];
        public float MagicResistance => this[CombatStatType.MagicResistance];
        public float DamageDealtBonus => this[CombatStatType.DamageDealtBonus];
        public float DamageTakenBonus => this[CombatStatType.DamageTakenBonus];
        public float PhysicalDamageDealtBonus => this[CombatStatType.PhysicalDamageDealtBonus];
        public float MagicDamageDealtBonus => this[CombatStatType.MagicDamageDealtBonus];
        public float FinalDamageMultiplier => this[CombatStatType.FinalDamageMultiplier];
        public float MovementSpeed => this[CombatStatType.MovementSpeed];
        public float ExpGainedBonus => this[CombatStatType.ExpGainedBonus];
        public float ExperienceGainBonus => ExpGainedBonus;

        public CombatStats WithValue(CombatStatType type, float value)
        {
            if (!_values.ContainsKey(type) || float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            var copy = new Dictionary<CombatStatType, float>(_values) { [type] = CombatStatCalculator.Clamp(type, value) };
            return new CombatStats(copy);
        }

        // P2의 기존 던전 번호/층/보스 배율만 적용한다. DS 원형의 명중/방어/치명값은 유지한다.
        public CombatStats WithMultipliers(float health, float attack, float speed)
        {
            if (health <= 0f || attack <= 0f || speed <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(health));
            }
            MajorStats.Validate(health);
            MajorStats.Validate(attack);
            MajorStats.Validate(speed);
            return WithValue(CombatStatType.MaxHealth, MaxHealth * health)
                .WithValue(CombatStatType.PhysicalAttackPower, PhysicalAttackPower * attack)
                .WithValue(CombatStatType.MagicAttackPower, MagicAttackPower * attack)
                .WithValue(CombatStatType.AttackSpeed, AttackSpeed * speed);
        }
    }

    public sealed class CombatStatCalculator
    {
        private readonly CombatStatSheet _sheet;
        public CombatStatCalculator(CombatStatSheet sheet)
        {
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        }

        // DS StatusReader: (기본/고정 + 양수인 주능력치 기여 합계) * (1 + 해당 %보정).
        public CombatStats Calculate(MajorStats stats, IReadOnlyDictionary<CombatStatType, float> additions = null,
            IReadOnlyDictionary<CombatStatType, float> percentages = null)
        {
            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }
            var values = new Dictionary<CombatStatType, float>();
            foreach (CombatStatType type in Enum.GetValues(typeof(CombatStatType)))
            {
                var definition = _sheet.Get(type);
                float contribution = 0f;
                for (int i = 0; i < 6; i++)
                {
                    contribution += stats[(MajorStatType)i] * definition.Coefficients[i];
                }
                float value = definition.DefaultValue + Mathf.Max(0f, contribution) + Read(additions, type);
                if (HasPercentageModifier(type))
                {
                    float multiplier = Read(percentages, type) * 0.01f;
                    // DS MovementSpeed만 배율이 -90% 아래로 내려가지 않는다.
                    if (type == CombatStatType.MovementSpeed)
                    {
                        multiplier = Mathf.Max(-0.9f, multiplier);
                    }
                    value *= 1f + multiplier;
                }
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(stats));
                }
                values.Add(type, Clamp(type, value));
            }
            return new CombatStats(values);
        }

        public CombatStats CalculateEnemy(string id)
        {
            var enemy = _sheet.GetEnemy(id);
            var values = new Dictionary<CombatStatType, float>();
            foreach (CombatStatType type in Enum.GetValues(typeof(CombatStatType)))
            {
                values.Add(type, Clamp(type, enemy.Values.TryGetValue(type, out float value) ? value : _sheet.Get(type).NonPlayerDefaultValue));
            }
            return new CombatStats(values);
        }

        internal static float Clamp(CombatStatType type, float value)
        {
            return type == CombatStatType.DodgeChance || type == CombatStatType.GuardChance || type == CombatStatType.GuardDamageReduction
                ? Mathf.Min(80f, value) : value;
        }

        private static bool HasPercentageModifier(CombatStatType type)
        {
            return type == CombatStatType.MaxHealth || type == CombatStatType.PhysicalAttackPower
                || type == CombatStatType.MagicAttackPower || type == CombatStatType.AttackSpeed
                || type == CombatStatType.CooldownReduction || type == CombatStatType.PhysicalResistance
                || type == CombatStatType.MagicResistance || type == CombatStatType.MovementSpeed || type == CombatStatType.DamageTakenBonus;
        }

        private static float Read(IReadOnlyDictionary<CombatStatType, float> values, CombatStatType type)
        {
            float value = values != null && values.TryGetValue(type, out float found) ? found : 0f;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(values));
            }
            return value;
        }
    }

    public enum CombatDamageType
    {
        Physical, Magic, None
    }
    public enum CombatOutcome
    {
        Normal, Miss, Guard, Critical
    }

    public readonly struct CombatAttackResult
    {
        public CombatOutcome Outcome { get; }
        public float Damage { get; }
        public float LifeSteal { get; }
        internal CombatAttackResult(CombatOutcome outcome, float damage, float lifeSteal)
        {
            Outcome = outcome;
            Damage = damage;
            LifeSteal = lifeSteal;
        }
    }

    public static class CombatCalculator
    {
        // DS DamageSimulator: 명중 → 물리 가드 → 치명타. 가드 성공은 치명타 난수를 소비하지 않는다.
        public static CombatAttackResult ResolveAttack(CombatStats attacker, CombatStats defender, IRandomSource random,
            CombatDamageType type = CombatDamageType.Physical, float coefficient = 1f,
            bool alwaysHit = false, bool alwaysCritical = false, float? powerOverride = null)
        {
            if (attacker == null || defender == null || random == null)
            {
                throw new ArgumentNullException();
            }
            MajorStats.Validate(coefficient);
            if (!Enum.IsDefined(typeof(CombatDamageType), type))
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }
            CombatOutcome outcome = CombatOutcome.Normal;
            if (type == CombatDamageType.None)
            {
                // DS의 무속성 추가 피해는 명중/가드/치명 판정 난수를 사용하지 않는다.
                outcome = CombatOutcome.Normal;
            }
            else if (!alwaysHit && !(Roll(random) < attacker.HitChance - defender.DodgeChance))
            {
                outcome = CombatOutcome.Miss;
            }
            else if (type == CombatDamageType.Physical && Roll(random) < defender.GuardChance)
            {
                outcome = CombatOutcome.Guard;
            }
            else if (alwaysCritical || Roll(random) < attacker.CriticalChance)
            {
                outcome = CombatOutcome.Critical;
            }

            float resultMultiplier = outcome switch
            {
                CombatOutcome.Miss => 0f,
                CombatOutcome.Guard => 1f - defender.GuardDamageReduction * 0.01f,
                CombatOutcome.Critical => 1f + attacker.CriticalDamageBonus * 0.01f,
                _ => 1f
            };
            float power = powerOverride ?? (type == CombatDamageType.Physical ? attacker.PhysicalAttackPower : attacker.MagicAttackPower);
            float resistance = type == CombatDamageType.Physical ? defender.PhysicalResistance
                : type == CombatDamageType.Magic ? defender.MagicResistance : 0f;
            float typedBonus = type == CombatDamageType.Physical ? attacker.PhysicalDamageDealtBonus
                : type == CombatDamageType.Magic ? attacker.MagicDamageDealtBonus : 0f;
            float bonus = (attacker.DamageDealtBonus + typedBonus + Mathf.Max(-99f, defender.DamageTakenBonus)) * 0.01f;
            float damage = Mathf.Max(0f, power * coefficient * (1f + bonus) * resultMultiplier
                * (100f / (100f + resistance)) * attacker.FinalDamageMultiplier);
            // DS Status_LifeSteal은 DamageExecutionLog.TotalDamage(오버킬 포함)를 사용한다.
            float lifeSteal = attacker.LifeSteal > 0f ? damage * attacker.LifeSteal * 0.01f : 0f;
            return new CombatAttackResult(outcome, damage, lifeSteal);
        }

        public static float AttackCycleSeconds(CombatStats stats, CombatAttackDefinition attack)
        {
            if (stats == null || attack == null)
            {
                throw new ArgumentNullException();
            }
            return attack.BaseCycle / stats.AttackSpeed;
        }

        public static float AdjustCooldown(float baseCooldown, CombatStats stats, bool adjustedByAttackSpeed)
        {
            if (stats == null || baseCooldown <= 0f)
            {
                return baseCooldown;
            }
            float rate = adjustedByAttackSpeed ? (stats.AttackSpeed > 0f ? stats.AttackSpeed : 1f) : 1f + stats.CooldownReduction * 0.01f;
            return rate > 0f ? baseCooldown / rate : baseCooldown;
        }

        private static float Roll(IRandomSource random)
        {
            float value = random.NextFloat(0f, 100f);
            if (float.IsNaN(value) || value < 0f || value > 100f)
            {
                throw new InvalidOperationException("전투 난수 범위가 올바르지 않습니다.");
            }
            return value;
        }
    }
}

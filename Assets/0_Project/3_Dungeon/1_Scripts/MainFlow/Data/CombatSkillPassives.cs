using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor.Main
{
    // DS PassiveAction의 사건별 계산을 카드 전투의 동일 사건에 연결한다.
    // 상태의 저장/공격 진행/보상 확정은 이 클래스에서 처리하지 않는다.
    public static class CombatSkillPassives
    {
        public static CombatStats CalculateStats(CombatStatCalculator calculator, MajorStats major,
            SkillSheet sheet, CharacterProgress progress)
        {
            if (calculator == null)
            {
                throw new ArgumentNullException(nameof(calculator));
            }
            if (sheet == null || progress == null)
            {
                return calculator.Calculate(major);
            }
            SkillDefinition Skill(string id) => progress.HasSkill(id) ? sheet.GetSkill(id) : null;
            var additions = new Dictionary<CombatStatType, float>();
            var percentages = PermanentPercentages(Skill, sheet.GetClassForCharacter(progress).WeaponType);
            var resolve = Skill("SKILL_SwordmansResolve");
            if (resolve != null)
            {
                additions[CombatStatType.CooldownReduction] = resolve.Values[0];
            }
            var shield = Skill("SKILL_ShieldHandling");
            if (shield != null)
            {
                additions[CombatStatType.GuardChance] = shield.Values[0];
            }
            var shooter = Skill("SKILL_SharpShooter");
            if (shooter != null)
            {
                float bonus = 0f;
                for (int index = 0; index < shooter.Values.Count - 1; index++)
                {
                    if (progress.BasicAttackKills >= shooter.Values[index])
                    {
                        bonus += shooter.Values[shooter.Values.Count - 1];
                    }
                }
                additions[CombatStatType.CriticalChance] = bonus;
            }
            return calculator.Calculate(major, additions, percentages);
        }

        public static CombatStats EffectiveStats(CombatantData unit)
        {
            var stats = unit.Stats;
            if (unit.Skills == null || stats == null)
            {
                return stats;
            }
            var additions = new Dictionary<CombatStatType, float>();
            var percentages = new Dictionary<CombatStatType, float>();
            if (HasEffect(unit, "AFFECTER_Fortification"))
            {
                Add(additions, CombatStatType.GuardChance, unit.Skills.Setting("FortificationGuardChance"));
            }
            if (HasEffect(unit, "AFFECTER_Disturbance"))
            {
                Add(additions, CombatStatType.HitChance, unit.Skills.Setting("DisturbanceHitChance"));
            }
            if (HasEffect(unit, "AFFECTER_Stiffness"))
            {
                Add(additions, CombatStatType.DodgeChance, unit.Skills.Setting("StiffnessDodgeChance"));
            }
            if (HasEffect(unit, "AFFECTER_Vulnerable"))
            {
                Add(additions, CombatStatType.DamageTakenBonus, unit.Skills.Setting("VulnerableDamageTakenBonus"));
            }
            BoundEffect(unit, percentages, "AFFECTER_SharpEdge", "SKILL_SharpEdge", CombatStatType.PhysicalAttackPower, 0);
            BoundEffect(unit, percentages, "AFFECTER_FirstAssault", "SKILL_FirstAssault", CombatStatType.PhysicalAttackPower, 1);
            BoundEffect(unit, percentages, "AFFECTER_Initiative", "SKILL_Initiative", CombatStatType.AttackSpeed, 1);
            BoundEffect(unit, percentages, "AFFECTER_CombatMomentum", "SKILL_CombatMomentum", CombatStatType.AttackSpeed, 1);
            BoundEffect(unit, percentages, "AFFECTER_BecomeFire", "SKILL_BecomeFire", CombatStatType.MagicAttackPower, 3);
            BoundEffect(unit, additions, "AFFECTER_Executioner", "SKILL_Executioner", CombatStatType.CriticalChance, 1);
            BoundEffect(unit, additions, "AFFECTER_ColdFury", "SKILL_ColdFury", CombatStatType.DamageDealtBonus, 1);
            BoundEffect(unit, additions, "AFFECTER_ShieldUp", "SKILL_ShieldUp", CombatStatType.GuardDamageReduction, 1);
            var withstand = unit.Skill("SKILL_Withstand");
            if (withstand != null && unit.Hp <= unit.MaxHp * unit.Skills.Setting("WithstandHealthRatio"))
            {
                Add(additions, CombatStatType.GuardChance, withstand.Values[0]);
            }
            var permanent = unit.IsAdventurer
                ? PermanentPercentages(unit.Skill, unit.CharacterClass.WeaponType)
                : new Dictionary<CombatStatType, float>();
            // DS의 %보정은 서로 더한 뒤 기초값에 한 번 곱한다.
            foreach (var pair in percentages)
            {
                permanent.TryGetValue(pair.Key, out float previous);
                float original = stats[pair.Key] / (1f + previous * 0.01f);
                stats = stats.WithValue(pair.Key, original * (1f + (previous + pair.Value) * 0.01f));
            }
            foreach (var pair in additions)
            {
                stats = stats.WithValue(pair.Key, stats[pair.Key] + pair.Value);
            }
            return stats;
        }

        public static void OnBattleStart(CombatantData unit)
        {
            StartEffect(unit, "SKILL_FirstAssault", "AFFECTER_FirstAssault");
            StartEffect(unit, "SKILL_Initiative", "AFFECTER_Initiative");
        }

        public static void OnSkillStart(CombatantData unit, SkillDefinition skill)
        {
            if (skill == null || skill.IsBasicAttack)
            {
                return;
            }
            var sharp = unit.Skill("SKILL_SharpEdge");
            if (sharp != null)
            {
                unit.AddEffect("AFFECTER_SharpEdge", sharp.Values[0], 1f, unit.Skills.Setting("SharpEdgeMaxStacks"));
            }
            if (skill.Id != "SKILL_BladeGrinding" && HasEffect(unit, "AFFECTER_BladeGrinding"))
            {
                unit.BattleFlags.Add("BladeGrindingActive");
            }
        }

        public static void OnSkillEnd(CombatantData unit, SkillDefinition skill, CombatantData target)
        {
            if (skill == null || skill.IsBasicAttack)
            {
                return;
            }
            if (unit.BattleFlags.Remove("BladeGrindingActive"))
            {
                unit.RemoveEffect("AFFECTER_BladeGrinding");
            }
            var flow = unit.Skill("SKILL_FlowSeeker");
            if (flow != null && skill.AttackType == SkillAttackType.Melee
                && skill.TargetType == SkillTargetType.SingleEnemy && target != null)
            {
                ApplyEffect(unit, target, "AFFECTER_ExposedFlaw", flow.Values[0], 1f, unit.Skills.Setting("ExposedFlawMaxStacks"));
            }
        }

        public static void BeforeDamage(CombatantData attacker, CombatantData target, SkillDefinition skill,
            ref CombatStats attackStats, ref CombatStats targetStats, ref float coefficient, ref bool alwaysHit)
        {
            bool melee = skill == null || skill.Type == SkillType.Active && skill.AttackType == SkillAttackType.Melee;
            bool physical = skill == null || skill.Type == SkillType.Active && skill.DamageType == CombatDamageType.Physical;
            float valueBonus = 0f;
            var fatal = attacker.Skill("SKILL_FatalVerdict");
            if (fatal != null && melee && physical && target.MaxHp > 0f)
            {
                valueBonus += fatal.Values[0] * (1f - target.Hp / target.MaxHp);
            }
            var ignition = attacker.Skill("SKILL_Ignition");
            if (ignition != null && !target.IsAdventurer)
            {
                valueBonus += ignition.Values[0] * target.EffectStacks("AFFECTER_Burning");
            }
            // DS DataValue의 배율과 가피증/받피증은 같은 가산항이다. 계수에 곱하면 교차항이 생긴다.
            attackStats = attackStats.WithValue(CombatStatType.DamageDealtBonus, attackStats.DamageDealtBonus + valueBonus);
            if (attacker.HasSkill("SKILL_InevitableBlow") && melee && HasEffect(target, "AFFECTER_Stun"))
            {
                alwaysHit = true;
            }
            var grinding = attacker.Skill("SKILL_BladeGrinding");
            if (grinding != null && attacker.BattleFlags.Contains("BladeGrindingActive"))
            {
                attackStats = attackStats.WithValue(CombatStatType.DamageDealtBonus, attackStats.DamageDealtBonus + grinding.Values[0])
                    .WithValue(CombatStatType.HitChance, attackStats.HitChance + grinding.Values[1]);
            }
            var finesse = target.Skill("SKILL_FinesseEdge");
            if (finesse != null && HasNegativeEffect(attacker))
            {
                targetStats = targetStats.WithValue(CombatStatType.DamageTakenBonus, targetStats.DamageTakenBonus - finesse.Values[0]);
            }
        }

        public static void AfterDamage(CombatantData attacker, CombatantData target, SkillDefinition skill,
            CombatAttackResult result, Action<CombatantData, CombatantData, SkillDefinition, float> extraDamage = null)
        {
            bool melee = skill == null || skill.Type == SkillType.Active && skill.AttackType == SkillAttackType.Melee;
            bool physical = skill == null || skill.Type == SkillType.Active && skill.DamageType == CombatDamageType.Physical;
            bool basic = skill == null || skill.IsBasicAttack;
            var spirit = attacker.Skill("SKILL_BladeSpirit");
            if (spirit != null && !basic && melee && physical && result.Outcome == CombatOutcome.Critical)
            {
                attacker.RestoreEnergy(spirit.Values[0]);
            }
            var cinder = attacker.Skill("SKILL_LastCinder");
            if (cinder != null && basic && attacker.Hp <= attacker.MaxHp * cinder.Values[0] * 0.01f)
            {
                ApplyEffect(attacker, target, "AFFECTER_Burning", cinder.Values[2], cinder.Values[1], attacker.Skills.Setting("BurningMaxStacks"));
            }
            var mocking = target.Skill("SKILL_MockingGrin");
            if (mocking != null && HasEffect(attacker, "AFFECTER_Taunt"))
            {
                target.AddBarrier(result.Damage * mocking.Values[0] * 0.01f, mocking.Values[1]);
            }
            if (result.Outcome == CombatOutcome.Guard)
            {
                var elastic = target.Skill("SKILL_ElasticGuard");
                if (elastic != null)
                {
                    float reduction = target.EffectiveStats.GuardDamageReduction * 0.01f;
                    float prevented = result.Damage / (1f - reduction) - result.Damage;
                    target.AddBarrier(prevented * elastic.Values[1] * 0.01f, elastic.Values[0]);
                }
                var counter = target.Skill("SKILL_SwordCounter");
                if (counter != null && melee && HasEffect(target, "AFFECTER_SwordCounter") && target.IsAlive && attacker.IsAlive)
                {
                    extraDamage?.Invoke(target, attacker, counter, counter.Values[1] * 0.01f);
                }
            }
            var cadence = attacker.Skill("SKILL_BattleCadence");
            if (cadence != null && basic)
            {
                float count = attacker.EffectStacks("AFFECTER_BattleCadence");
                if (count >= cadence.Values[0] - 1f)
                {
                    if (target.IsAlive)
                    {
                        extraDamage?.Invoke(attacker, target, cadence, cadence.Values[1] * 0.01f);
                    }
                    attacker.RemoveEffect("AFFECTER_BattleCadence");
                }
                attacker.AddEffect("AFFECTER_BattleCadence", 0f, 1f, cadence.Values[0]);
            }
            CheckHealth(attacker);
            CheckHealth(target);
        }

        public static void OnKill(CombatantData attacker, IReadOnlyList<CombatantData> allies)
        {
            var executioner = attacker.Skill("SKILL_Executioner");
            if (executioner != null)
            {
                float maxStacks = (int)executioner.Values[2] / (int)executioner.Values[1];
                attacker.AddEffect("AFFECTER_Executioner", executioner.Values[0], 1f, maxStacks);
            }
            StartEffect(attacker, "SKILL_ColdFury", "AFFECTER_ColdFury");
            var recoil = attacker.Skill("SKILL_CrimsonRecoil");
            if (recoil != null)
            {
                attacker.AddBarrier((attacker.MaxHp - attacker.Hp) * recoil.Values[1] * 0.01f, recoil.Values[0]);
            }
            var momentum = attacker.Skill("SKILL_CombatMomentum");
            if (momentum != null)
            {
                attacker.AddEffect("AFFECTER_CombatMomentum", momentum.Values[0], 1f, attacker.Skills.Setting("CombatMomentumMaxStacks"));
            }
            if (allies == null)
            {
                return;
            }
            // LifeCycle은 자신이 처치하지 않은 적의 사망도 관찰한다.
            foreach (var owner in allies)
            {
                var cycle = owner.Skill("SKILL_LifeCycle");
                if (cycle == null || !owner.IsAlive)
                {
                    continue;
                }
                float stacks = (float)Math.Ceiling(owner.EffectiveStats.MagicAttackPower * cycle.Values[1] * 0.01f);
                foreach (var ally in allies)
                {
                    ally.AddEffect("AFFECTER_HealthBoost", cycle.Values[0], stacks, owner.Skills.Setting("HealthBoostMaxStacks"), owner.Id);
                }
            }
        }

        public static void OnEffectAdded(CombatantData target, string key, CombatantData source)
        {
            if (target == null || source == null || !target.IsAlive || target.IsAdventurer)
            {
                return;
            }
            var barrier = source.Skill("SKILL_GuardBarrier");
            if (barrier != null && IsNegative(key))
            {
                source.AddBarrier(source.MaxHp * barrier.Values[1] * 0.01f, barrier.Values[0]);
            }
            var flame = source.Skill("SKILL_FlameOfLife");
            if (flame != null && key == "AFFECTER_Burning")
            {
                source.Heal((source.MaxHp - source.Hp) * flame.Values[0] * 0.01f);
            }
        }

        public static void OnBarrierLost(CombatantData unit, float amount)
        {
            var resurge = unit.Skill("SKILL_BarrierResurge");
            if (resurge != null && amount > 0f)
            {
                unit.Heal(amount * resurge.Values[0] * 0.01f);
            }
        }

        public static void CheckHealth(CombatantData unit)
        {
            var savage = unit.Skill("SKILL_SavageBarrier");
            if (savage != null && unit.IsAlive && unit.Hp <= unit.MaxHp * savage.Values[0] * 0.01f
                && unit.BattleFlags.Add("SavageBarrierUsed"))
            {
                unit.AddBarrier(unit.MaxHp * savage.Values[2] * 0.01f, savage.Values[1]);
            }
        }

        private static Dictionary<CombatStatType, float> PermanentPercentages(Func<string, SkillDefinition> skill, string weapon)
        {
            var percentages = new Dictionary<CombatStatType, float>();
            var grip = skill("SKILL_TwoHandGrip");
            if (grip != null && (weapon == "Equipment_Sword" || weapon == "Equipment_Blunt"))
            {
                Add(percentages, CombatStatType.PhysicalAttackPower, grip.Values[0]);
            }
            var fast = skill("SKILL_FastHand");
            if (fast != null)
            {
                Add(percentages, CombatStatType.AttackSpeed, fast.Values[0]);
            }
            var shield = skill("SKILL_ShieldHandling");
            if (shield != null && weapon == "Equipment_Shield")
            {
                Add(percentages, CombatStatType.AttackSpeed, shield.Values[1]);
            }
            return percentages;
        }

        private static void StartEffect(CombatantData unit, string skillId, string effectId)
        {
            var skill = unit.Skill(skillId);
            if (skill != null)
            {
                unit.AddEffect(effectId, skill.Values[0]);
            }
        }

        private static void BoundEffect(CombatantData unit, Dictionary<CombatStatType, float> changes,
            string effectId, string skillId, CombatStatType stat, int valueIndex)
        {
            float stacks = unit.EffectStacks(effectId);
            if (stacks > 0f)
            {
                Add(changes, stat, unit.Skills.GetSkill(skillId).Values[valueIndex] * stacks);
            }
        }

        private static void Add(Dictionary<CombatStatType, float> values, CombatStatType stat, float amount)
        {
            values.TryGetValue(stat, out float previous);
            values[stat] = previous + amount;
        }

        private static void ApplyEffect(CombatantData source, CombatantData target, string key, float duration, float stacks, float maxStacks)
        {
            if (!target.IsAlive)
            {
                return;
            }
            target.AddEffect(key, duration, stacks, maxStacks, source.Id);
            OnEffectAdded(target, key, source);
        }

        private static bool HasEffect(CombatantData unit, string key) => unit.EffectStacks(key) > 0f;

        private static bool HasNegativeEffect(CombatantData unit)
        {
            foreach (string key in unit.Effects.Keys)
            {
                if (IsNegative(key))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsNegative(string key)
        {
            return key == "AFFECTER_Burning" || key == "AFFECTER_Stun" || key == "AFFECTER_Dizziness"
                || key == "AFFECTER_Disturbance" || key == "AFFECTER_Stiffness" || key == "AFFECTER_ExposedFlaw"
                || key == "AFFECTER_Vulnerable" || key == "AFFECTER_Taunt" || key == "AFFECTER_Slow";
        }
    }
}

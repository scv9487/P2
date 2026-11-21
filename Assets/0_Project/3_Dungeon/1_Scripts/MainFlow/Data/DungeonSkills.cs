using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor.Main
{
    public sealed partial class DungeonDataContainer
    {
        private readonly SkillSheet _skills;
        private readonly HashSet<string> _rewardedEnemies = new(StringComparer.Ordinal);
        private bool _passiveDamage;
        private float _skillTickTime;
        internal float TimeUntilSkillTick => _skills == null ? float.PositiveInfinity : _skills.EnergyTickSeconds - _skillTickTime;
        public SkillSheet Skills => _skills;
        public string SelectedCombatSkillId { get; private set; } = string.Empty;

        public bool CanUseCombatSkill(string skillId)
        {
            var hero = Find(_allies, ReadyAllyId);
            if (hero == null || !InCombat || DetailsOpen || ProgressSaveBlocked || !hero.IsPlayerControlled)
            {
                return false;
            }
            if (_skills == null)
            {
                return string.IsNullOrEmpty(skillId) && hero.Gauge >= 1f;
            }
            var skill = FindAvailableSkill(hero, skillId);
            return skill != null && hero.CanSpendSkill(skill);
        }

        public bool CanTargetCombatSkill(string targetId)
        {
            if (!CanUseCombatSkill(SelectedCombatSkillId))
            {
                return false;
            }
            var hero = Find(_allies, ReadyAllyId);
            if (_skills == null)
            {
                return Find(_enemies, targetId)?.IsAlive ?? false;
            }
            return IsSkillTarget(hero, FindAvailableSkill(hero, SelectedCombatSkillId), targetId);
        }

        internal bool TrySelectCombatSkill(string skillId, string readyId)
        {
            if (readyId != ReadyAllyId || !CanUseCombatSkill(skillId))
            {
                return false;
            }
            SelectedCombatSkillId = skillId ?? string.Empty;
            Revision++;
            return true;
        }

        internal bool TryUseCombatSkill(string targetId, string readyId)
        {
            if (readyId != ReadyAllyId || !CanTargetCombatSkill(targetId))
            {
                return false;
            }
            if (_skills == null)
            {
                return TryAttack(targetId, readyId);
            }
            var hero = Find(_allies, ReadyAllyId);
            ExecuteCombatSkill(hero, FindAvailableSkill(hero, SelectedCombatSkillId), Find(_allies, targetId) ?? Find(_enemies, targetId));
            return true;
        }

        private SkillDefinition FindAvailableSkill(CombatantData unit, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                string basic = unit.EffectStacks("AFFECTER_ChangeTwinThrust") > 0 && unit.CharacterClass.WeaponId == "Equipment_Spear"
                    ? "SKILL_TwinThrustAttack" : unit.BasicAttackId;
                return _skills.GetSkill(basic);
            }
            if (!unit.HasSkill(id)) return null;
            var skill = _skills.GetSkill(id);
            return skill.TreeKind == SkillTreeKind.Main
                && skill.TreeId == unit.CharacterClass.ActiveTreeId ? skill : null;
        }

        private bool IsSkillTarget(CombatantData user, SkillDefinition skill, string targetId)
        {
            if (skill == null) return false;
            if (skill.TargetType == SkillTargetType.Self || skill.TargetType == SkillTargetType.AllEnemies
                || skill.TargetType == SkillTargetType.AllAllies || skill.TargetType == SkillTargetType.AllUnits) return targetId == user.Id;
            if (skill.TargetType == SkillTargetType.SingleAlly) return Find(_allies, targetId)?.IsAlive ?? false;
            return Find(_enemies, targetId)?.IsAlive ?? false;
        }

        private bool TryAutomaticSkill(CombatantData unit)
        {
            if (_skills == null) return false;
            var available = new List<SkillDefinition>();
            foreach (string id in unit.LearnedSkillIds)
            {
                var skill = FindAvailableSkill(unit, id);
                if (skill != null && unit.CanSpendSkill(skill)) available.Add(skill);
            }
            available.Sort((a, b) => b.RequiredLevel.CompareTo(a.RequiredLevel));
            foreach (var skill in available)
            {
                var target = AutomaticTarget(unit, skill);
                if (target != null)
                {
                    ExecuteCombatSkill(unit, skill, target);
                    return true;
                }
            }
            var basic = FindAvailableSkill(unit, null);
            var basicTarget = AutomaticTarget(unit, basic);
            if (basicTarget == null || !unit.CanSpendSkill(basic)) return false;
            ExecuteCombatSkill(unit, basic, basicTarget);
            return true;
        }

        private CombatantData AutomaticTarget(CombatantData user, SkillDefinition skill)
        {
            if (skill.TargetType == SkillTargetType.Self || skill.TargetType == SkillTargetType.AllEnemies
                || skill.TargetType == SkillTargetType.AllAllies || skill.TargetType == SkillTargetType.AllUnits) return user;
            if (skill.TargetType == SkillTargetType.SingleAlly)
            {
                CombatantData lowest = null;
                foreach (var ally in _allies)
                {
                    if (ally.IsAlive && (lowest == null || ally.Hp / ally.MaxHp < lowest.Hp / lowest.MaxHp)) lowest = ally;
                }
                return lowest;
            }
            foreach (var enemy in _enemies) if (enemy.IsAlive) return enemy;
            return null;
        }

        // 한 행동의 모든 타격과 보상 사실을 끝낸 뒤 MainFlow가 EXP/의뢰를 FIFO 저장한다.
        private void ExecuteCombatSkill(CombatantData user, SkillDefinition skill, CombatantData target)
        {
            user.SpendSkill(skill);
            CombatSkillPassives.OnSkillStart(user, skill);
            var v = skill.Values;
            switch (skill.Id)
            {
                case "SKILL_SwordCounter":
                    AddCombatEffect(user, user, "AFFECTER_Fortification", v[0]);
                    AddCombatEffect(user, user, "AFFECTER_SwordCounter", v[0]);
                    break;
                case "SKILL_ShieldUp":
                    AddCombatEffect(user, target, "AFFECTER_Fortification", v[0]);
                    AddCombatEffect(user, target, "AFFECTER_Slow", v[0]);
                    AddCombatEffect(user, target, "AFFECTER_ShieldUp", v[0]);
                    break;
                case "SKILL_GuardiansTaunt":
                    foreach (var enemy in _enemies) AddCombatEffect(user, enemy, "AFFECTER_Taunt", v[0]);
                    break;
                case "SKILL_TwinThrust":
                    AddCombatEffect(user, user, "AFFECTER_ChangeTwinThrust", v[0]);
                    break;
                case "SKILL_BladeGrinding":
                    AddCombatEffect(user, user, "AFFECTER_BladeGrinding", 0f);
                    break;
                case "SKILL_IronClad":
                    target.AddBarrier((target.MaxHp - target.Hp) * v[1] * 0.01f, v[0]);
                    break;
                case "SKILL_BecomeFire":
                    AddCombatEffect(user, user, "AFFECTER_Burning", v[0], v[1], float.MaxValue);
                    AddCombatEffect(user, user, "AFFECTER_BecomeFire", v[2], user.EffectStacks("AFFECTER_Burning"), float.MaxValue);
                    break;
                case "SKILL_HealingTouchAttack":
                    target.Heal(user.EffectiveStats.MagicAttackPower * v[0] * 0.01f);
                    break;
                case "SKILL_ChainSlash":
                case "SKILL_TwinThrustAttack":
                    for (int i = 0; i < (int)v[1] && target.IsAlive; i++) ResolveSkillDamage(user, target, skill, v[0] * 0.01f);
                    break;
                case "SKILL_WildFlurry":
                    for (int i = 0; i < (int)v[2] && target.IsAlive; i++) ResolveSkillDamage(user, target, skill, v[1] * 0.01f, -v[0]);
                    break;
                case "SKILL_Incinerate":
                    float burning = target.EffectStacks("AFFECTER_Burning");
                    ResolveSkillDamage(user, target, skill, v[0] * 0.01f);
                    if (target.IsAlive) ResolveSkillDamage(user, target, skill, burning * v[1] * 0.01f);
                    target.RemoveEffect("AFFECTER_Burning");
                    break;
                case "SKILL_BattleStomp":
                case "SKILL_Explosion":
                    var targets = new List<CombatantData>(_enemies);
                    if (skill.TargetType == SkillTargetType.AllUnits) targets.AddRange(_allies);
                    foreach (var enemy in targets)
                    {
                        if (!enemy.IsAlive) continue;
                        ResolveSkillDamage(user, enemy, skill, v[0] * 0.01f);
                        AddCombatEffect(user, enemy, skill.Id == "SKILL_Explosion" ? "AFFECTER_Burning" : "AFFECTER_Dizziness", v[1], v[2], float.MaxValue);
                        if (skill.Id == "SKILL_BattleStomp") AddCombatEffect(user, enemy, "AFFECTER_Disturbance", v[1]);
                    }
                    break;
                default:
                    var result = ResolveSkillDamage(user, target, skill, v[0] * 0.01f);
                    if (skill.Id == "SKILL_FireMissileAttack") AddCombatEffect(user, target, "AFFECTER_Burning", v[1], v[2], float.MaxValue);
                    if (skill.Id == "SKILL_PommelStrike") AddCombatEffect(user, target, "AFFECTER_Stun", v[1]);
                    if (skill.Id == "SKILL_Slay") AddCombatEffect(user, user, "AFFECTER_ExposedFlaw", v[1], v[2], float.MaxValue);
                    if (skill.Id == "SKILL_Hammering")
                    {
                        AddCombatEffect(user, target, "AFFECTER_Stiffness", v[1], v[2], float.MaxValue);
                        AddCombatEffect(user, target, "AFFECTER_Dizziness", v[1], v[3], float.MaxValue);
                    }
                    if (skill.Id == "SKILL_VileInsult" || skill.Id == "SKILL_OpeningStrike") AddCombatEffect(user, target, "AFFECTER_Vulnerable", v[1]);
                    if (skill.Id == "SKILL_VileInsult") AddCombatEffect(user, target, "AFFECTER_Taunt", v[1]);
                    if (skill.Id == "SKILL_BasicClubAttack" && result.Outcome == CombatOutcome.Critical) AddCombatEffect(user, target, "AFFECTER_Dizziness", v[1], v[2], float.MaxValue);
                    break;
            }
            CombatSkillPassives.OnSkillEnd(user, skill, target);
            if (skill.Id == "SKILL_Slay" && !target.IsAlive) user.ReduceCooldown(skill.Id, 1f - _skills.Setting("SlayKillCooldownReduction") * 0.01f);
            SelectedCombatSkillId = string.Empty;
            Status = string.Format(_skills.Text("battle.used_format"), user.Name, skill.Name);
            Revision++;
            FinishSkillAction();
        }

        private CombatAttackResult ResolveSkillDamage(CombatantData attacker, CombatantData target, SkillDefinition skill, float coefficient, float hitBonus = 0f)
        {
            if (!target.IsAlive) return default;
            var attackStats = attacker.EffectiveStats;
            var targetStats = target.EffectiveStats;
            if (hitBonus != 0) attackStats = attackStats.WithValue(CombatStatType.HitChance, attackStats.HitChance + hitBonus);
            bool alwaysHit = false;
            CombatSkillPassives.BeforeDamage(attacker, target, skill, ref attackStats, ref targetStats, ref coefficient, ref alwaysHit);
            bool exposed = ConsumeExposedFlaw(target);
            var damageType = skill.Type == SkillType.Passive ? CombatDamageType.None : skill.DamageType;
            float? power = skill.Type == SkillType.Passive ? attackStats.PhysicalAttackPower
                : skill.Id == "SKILL_VileInsult" ? attackStats.MagicAttackPower : null;
            var result = CombatCalculator.ResolveAttack(attackStats, targetStats, _random, damageType, coefficient,
                alwaysHit || exposed, exposed, power);
            target.TakeDamage(result.Damage);
            attacker.Heal(result.LifeSteal);
            CombatSkillPassives.AfterDamage(attacker, target, skill, result, _passiveDamage ? null : ApplyPassiveDamage);
            RecordSkillKill(attacker, target, skill);
            RecordSkillKill(target, attacker);
            return result;
        }

        private void ApplyPassiveDamage(CombatantData attacker, CombatantData target, SkillDefinition skill, float coefficient)
        {
            if (_passiveDamage || !attacker.IsAlive || !target.IsAlive) return;
            _passiveDamage = true;
            try
            {
                ResolveSkillDamage(attacker, target, skill, coefficient);
            }
            finally
            {
                _passiveDamage = false;
            }
        }

        private CombatAttackResult ResolveRatSkillAttack(CombatantData attacker, CombatantData target)
        {
            var attackStats = attacker.EffectiveStats;
            var targetStats = target.EffectiveStats;
            float coefficient = attacker.AttackCoefficient;
            bool alwaysHit = false;
            CombatSkillPassives.BeforeDamage(attacker, target, null, ref attackStats, ref targetStats, ref coefficient, ref alwaysHit);
            bool exposed = ConsumeExposedFlaw(target);
            var result = CombatCalculator.ResolveAttack(attackStats, targetStats, _random, CombatDamageType.Physical, coefficient, alwaysHit || exposed, exposed);
            target.TakeDamage(result.Damage);
            attacker.Heal(result.LifeSteal);
            CombatSkillPassives.AfterDamage(attacker, target, null, result, ApplyPassiveDamage);
            RecordSkillKill(target, attacker);
            return result;
        }

        private static bool ConsumeExposedFlaw(CombatantData target)
        {
            if (!target.Effects.TryGetValue("AFFECTER_ExposedFlaw", out var effect) || effect.Stacks <= 0)
            {
                return false;
            }
            effect.Stacks -= 1f;
            if (effect.Stacks <= 0f)
            {
                target.RemoveEffect("AFFECTER_ExposedFlaw");
            }
            return true;
        }

        private void AddCombatEffect(CombatantData source, CombatantData target, string key, float duration, float stacks = 1f, float maxStacks = 1f)
        {
            target.AddEffect(key, duration, stacks, maxStacks, source.Id);
            CombatSkillPassives.OnEffectAdded(target, key, source);
        }

        private void RecordSkillKill(CombatantData attacker, CombatantData target, SkillDefinition skill = null)
        {
            if (target.IsAlive || !_enemies.Contains(target) || !_rewardedEnemies.Add(target.Id)) return;
            if (_lootSheet != null)
            {
                foreach (var drop in _lootSheet.GetDrops("gray_rat"))
                {
                    _loot.TryGetValue(drop.ItemId, out int previous);
                    _loot[drop.ItemId] = checked(previous + drop.Quantity);
                }
            }
            QueueKillExperience(attacker, skill);
            QueueMonsterKilled(target.Id);
            CombatSkillPassives.OnKill(attacker, _allies);
        }

        private void FinishSkillAction()
        {
            if (!AnyAlive(_allies)) EndCombat(false);
            else if (!AnyAlive(_enemies)) EndCombat(true);
            else SelectReadyAlly();
        }

        private void TickSkillEffects(float seconds)
        {
            if (_skills == null || !InCombat) return;
            _skillTickTime += seconds;
            if (_skillTickTime + 0.00001f < _skills.EnergyTickSeconds) return;
            _skillTickTime = 0f;
            var units = new List<CombatantData>(_allies);
            units.AddRange(_enemies);
            foreach (var unit in units)
            {
                if (!unit.IsAlive) continue;
                if (unit.Effects.TryGetValue("AFFECTER_Burning", out var burn))
                {
                    float amount = burn.Stacks * _skills.Setting("BurningDamagePerStack");
                    var fireProof = unit.Skill("SKILL_FireProof");
                    amount *= 1f - (fireProof == null ? 0f : fireProof.Values[1]) * 0.01f;
                    amount *= 100f / (100f + unit.EffectiveStats.MagicResistance);
                    amount *= 1f + Math.Max(-99, unit.EffectiveStats.DamageTakenBonus) * 0.01f;
                    unit.TakeHealthDamage(amount);
                    CombatSkillPassives.CheckHealth(unit);
                    var owner = Find(_allies, burn.SourceId);
                    if (owner != null) RecordSkillKill(owner, unit);
                }
                if (unit.IsAlive && unit.EffectStacks("AFFECTER_HealthBoost") > 0)
                {
                    unit.Heal(unit.EffectStacks("AFFECTER_HealthBoost") * _skills.Setting("HealthBoostPerStack"));
                }
            }
            FinishSkillAction();
        }
    }
}

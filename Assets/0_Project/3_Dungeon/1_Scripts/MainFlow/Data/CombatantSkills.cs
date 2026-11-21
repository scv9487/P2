using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor.Main
{
    internal sealed class CombatEffectState
    {
        internal string Key;
        internal float Duration;
        internal float Stacks;
        internal string SourceId;
    }

    public sealed partial class CombatantData
    {
        private CharacterProgress _skillProgress;
        private readonly Dictionary<string, float> _cooldowns = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, CombatEffectState> Effects = new(StringComparer.Ordinal);
        private float _barrierDuration;
        private float _energyTick;
        internal readonly HashSet<string> BattleFlags = new(StringComparer.Ordinal);
        public SkillSheet Skills { get; private set; }
        internal bool IsAdventurer => _skillProgress != null;
        public IReadOnlyList<string> LearnedSkillIds => _skillProgress?.LearnedSkillIds ?? Array.Empty<string>();
        public float Energy { get; private set; }
        public float MaxEnergy { get; private set; }
        public float Barrier { get; private set; }
        public float GlobalCooldown { get; private set; }
        public float StunRemaining => Effects.TryGetValue("AFFECTER_Stun", out var effect) ? effect.Duration : 0f;
        public CombatStats EffectiveStats => Skills == null ? Stats : CombatSkillPassives.EffectiveStats(this);
        public SkillClassDefinition CharacterClass => IsAdventurer && Skills != null ? Skills.GetClassForCharacter(_skillProgress) : null;
        public string BasicAttackId => Skills == null || !IsAdventurer ? null
            : HasSkill("SKILL_HealingTouch") && CharacterClass.WeaponId == "Equipment_NatureStaff"
                ? "SKILL_HealingTouchAttack" : CharacterClass.BasicAttackId;
        internal void ConfigureEnemySkills(SkillSheet sheet) => Skills = sheet;
        public bool HasSkill(string id) => _skillProgress?.HasSkill(id) ?? false;
        internal SkillDefinition Skill(string id) => HasSkill(id) ? Skills.GetSkill(id) : null;
        public float GetCooldown(string id) => id != null && _cooldowns.TryGetValue(id, out float value) ? value : 0f;
        internal float EffectStacks(string key) => Effects.TryGetValue(key, out var value) ? value.Stacks : 0f;

        internal void ConfigureSkills(SkillSheet sheet, CharacterProgress progress)
        {
            bool first = Skills == null;
            Skills = sheet;
            _skillProgress = progress;
            if (sheet == null)
            {
                return;
            }
            float energyContribution = 0f;
            for (int index = 0; index < 6; index++)
            {
                energyContribution += progress.Stats[(MajorStatType)index] * sheet.Setting("MaxEnergy" + (MajorStatType)index);
            }
            MaxEnergy = sheet.MaxEnergyBase + Math.Max(0, energyContribution);
            Energy = first ? MaxEnergy : Math.Min(Energy, MaxEnergy);
            var basic = sheet.GetSkill(BasicAttackId);
            _baseSpeed = 1f / CombatCalculator.AdjustCooldown(basic.BaseCycle, Stats, basic.AdjustedByAttackSpeed);
            AttackCoefficient = basic.Values[0] * 0.01f;
        }

        internal void BeginSkillBattle()
        {
            Effects.Clear();
            BattleFlags.Clear();
            Barrier = 0f;
            _barrierDuration = 0f;
            if (Skills != null)
            {
                CombatSkillPassives.OnBattleStart(this);
            }
        }

        internal void AddEffect(string key, float duration, float stacks = 1f, float maxStacks = 1f, string sourceId = null)
        {
            if (!IsAlive || stacks <= 0f)
            {
                return;
            }
            if (Skills != null && key == "AFFECTER_Burning")
            {
                maxStacks = Skills.Setting("BurningMaxStacks");
                var fireProof = Skill("SKILL_FireProof");
                stacks *= 1f + (fireProof == null ? 0f : fireProof.Values[0]) * 0.01f;
            }
            if (Skills != null)
            {
                maxStacks = key switch
                {
                    "AFFECTER_Dizziness" => Skills.Setting("DizzinessMaxStacks"),
                    "AFFECTER_ExposedFlaw" => Skills.Setting("ExposedFlawMaxStacks"),
                    "AFFECTER_HealthBoost" => Skills.Setting("HealthBoostMaxStacks"),
                    "AFFECTER_BecomeFire" => Skills.Setting("BecomeFireMaxStacks"),
                    _ => maxStacks
                };
            }
            stacks = (int)stacks;
            if (stacks <= 0f)
            {
                return;
            }
            if (Effects.TryGetValue(key, out var effect))
            {
                effect.Stacks = Math.Min(maxStacks, effect.Stacks + stacks);
                effect.Duration = Math.Max(effect.Duration, duration);
                effect.SourceId = sourceId ?? effect.SourceId;
            }
            else
            {
                Effects.Add(key, new CombatEffectState { Key = key, Duration = duration,
                    Stacks = Math.Min(maxStacks, stacks), SourceId = sourceId });
            }
            if (Skills != null && key == "AFFECTER_Dizziness"
                && EffectStacks(key) >= Skills.Setting("DizzinessThreshold"))
            {
                Effects[key].Stacks -= Skills.Setting("DizzinessThreshold");
                if (Effects[key].Stacks <= 0) Effects.Remove(key);
                AddEffect("AFFECTER_Stun", Skills.Setting("DizzinessStunSeconds"));
            }
        }

        internal void RemoveEffect(string key) => Effects.Remove(key);

        internal void RestoreEnergy(float amount)
        {
            Energy = Math.Max(0f, Math.Min(MaxEnergy, Energy + amount));
        }

        internal void AddBarrier(float amount, float duration)
        {
            if (IsAlive && amount > 0f)
            {
                var fortified = Skill("SKILL_Fortified");
                amount = (int)(amount * (1f + (fortified == null ? 0f : fortified.Values[0]) * 0.01f));
                if (amount <= 0f)
                {
                    return;
                }
                float previous = Barrier;
                Barrier = Math.Min(Skills == null ? float.MaxValue : Skills.Setting("BarrierMaxStacks"), Barrier + amount);
                _barrierDuration = Math.Max(_barrierDuration, duration);
                var cycle = Skill("SKILL_WaveCicle");
                if (cycle != null && Barrier > previous)
                {
                    float multiplier = Math.Max(0, 1f - cycle.Values[0] * 0.01f);
                    foreach (string key in new List<string>(_cooldowns.Keys)) _cooldowns[key] *= multiplier;
                }
            }
        }

        private float AbsorbBarrier(float amount)
        {
            if (Barrier <= 0f)
            {
                return amount;
            }
            if (amount > Barrier)
            {
                amount -= Barrier;
                Barrier = 0f;
                return amount;
            }
            // DS의 보호막은 정수 스택이다. 남은 보호막 이하 피해는 정수 부분만 차감한다.
            Barrier -= (int)amount;
            return 0f;
        }

        internal bool CanSpendSkill(SkillDefinition skill)
        {
            return IsAlive && StunRemaining <= 0f && Gauge >= 1f && (skill.IsBasicAttack
                || (GlobalCooldown <= 0f && GetCooldown(skill.Id) <= 0f && Energy >= skill.EnergyCost));
        }

        internal void SpendSkill(SkillDefinition skill)
        {
            RestoreEnergy(-skill.EnergyCost);
            if (!skill.IsBasicAttack)
            {
                _cooldowns[skill.Id] = CombatCalculator.AdjustCooldown(skill.Cooldown, EffectiveStats, skill.AdjustedByAttackSpeed);
                GlobalCooldown = CombatCalculator.AdjustCooldown(Skills.GlobalCooldown, EffectiveStats, false);
            }
            ResetGauge();
        }

        internal void ReduceCooldown(string skillId, float multiplier)
        {
            _cooldowns[skillId] = GetCooldown(skillId) * multiplier;
            GlobalCooldown *= multiplier;
        }

        internal void TakeHealthDamage(float amount)
        {
            Hp = Math.Max(0, Hp - Math.Max(0, amount));
            if (!IsAlive) ResetGauge();
        }

        internal void TickSkills(float seconds)
        {
            GlobalCooldown = Math.Max(0, GlobalCooldown - seconds);
            foreach (string key in new List<string>(_cooldowns.Keys))
            {
                _cooldowns[key] = Math.Max(0, _cooldowns[key] - seconds);
            }
            foreach (string key in new List<string>(Effects.Keys))
            {
                var effect = Effects[key];
                if (effect.Duration > 0f)
                {
                    effect.Duration = Math.Max(0, effect.Duration - seconds);
                    if (effect.Duration <= 0f)
                    {
                        Effects.Remove(key);
                    }
                }
            }
            if (_barrierDuration > 0)
            {
                _barrierDuration = Math.Max(0, _barrierDuration - seconds);
                if (_barrierDuration <= 0)
                {
                    float lost = Barrier;
                    Barrier = 0;
                    if (Skills != null) CombatSkillPassives.OnBarrierLost(this, lost);
                }
            }
            if (Skills != null)
            {
                _energyTick += seconds;
                while (_energyTick >= Skills.EnergyTickSeconds)
                {
                    _energyTick -= Skills.EnergyTickSeconds;
                    RestoreEnergy(Skills.EnergyRegeneration);
                }
            }
        }
    }
}

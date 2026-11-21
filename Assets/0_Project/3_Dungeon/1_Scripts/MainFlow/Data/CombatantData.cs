using System;

namespace Refactor.Main
{
    // 읽는 쪽에는 값만 노출한다. HP/게이지 변경은 요청 처리 또는 CombatTickFlow 단계에 한정한다.
    public sealed partial class CombatantData
    {
        public string Id { get; }
        public string Name { get; }
        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public float Attack { get; private set; }
        private float _baseSpeed;
        public float Speed
        {
            get
            {
                if (Stats == null)
                {
                    return _baseSpeed;
                }
                float baseline = Stats.AttackSpeed > 0f ? Stats.AttackSpeed : 1f;
                float current = EffectiveStats.AttackSpeed > 0f ? EffectiveStats.AttackSpeed : 1f;
                return _baseSpeed * current / baseline;
            }
        }
        public CombatStats Stats { get; private set; }
        public float AttackCoefficient { get; private set; } = 1f;
        public float Gauge { get; private set; }
        public int PortraitIndex { get; }
        public bool IsBoss { get; }
        public bool IsPlayerControlled { get; }
        public bool IsAlive => Hp > 0;

        internal CombatantData(string id, string name, float hp, float attack, float speed, int portraitIndex, bool boss = false, bool isPlayerControlled = false)
        {
            Id = id;
            Name = name;
            MaxHp = hp;
            Hp = hp;
            Attack = attack;
            _baseSpeed = speed;
            PortraitIndex = portraitIndex;
            IsBoss = boss;
            IsPlayerControlled = isPlayerControlled;
        }

        internal CombatantData(string id, string name, CombatStats stats, Refactor.Util.CombatAttackDefinition attack,
            int portraitIndex, bool boss = false, bool isPlayerControlled = false)
            : this(id, name, stats.MaxHealth, stats.PhysicalAttackPower, 1f / CombatCalculator.AttackCycleSeconds(stats, attack),
                portraitIndex, boss, isPlayerControlled)
        {
            Stats = stats;
            AttackCoefficient = attack.Coefficient;
        }

        internal void RefreshStats(CombatStats stats, Refactor.Util.CombatAttackDefinition attack)
        {
            Stats = stats;
            MaxHp = stats.MaxHealth;
            Hp = Math.Min(Hp, MaxHp);
            Attack = stats.PhysicalAttackPower;
            _baseSpeed = 1f / CombatCalculator.AttackCycleSeconds(stats, attack);
            AttackCoefficient = attack.Coefficient;
        }

        internal void Charge(float deltaTime)
        {
            if (IsAlive && StunRemaining <= 0f)
            {
                Gauge = Math.Min(1f, Gauge + Speed * deltaTime);
            }
        }

        internal void ResetGauge()
        {
            Gauge = 0;
        }

        internal void TakeDamage(float amount)
        {
            float previousBarrier = Barrier;
            amount = AbsorbBarrier(Math.Max(0, amount));
            Hp = Math.Max(0, Hp - amount);
            if (!IsAlive)
            {
                ResetGauge();
            }
            if (Skills != null && previousBarrier > Barrier)
            {
                CombatSkillPassives.OnBarrierLost(this, previousBarrier - Barrier);
            }
        }

        internal void Heal(float amount)
        {
            if (IsAlive)
            {
                var innerGrove = Skill("SKILL_InnerGrove");
                amount *= 1f + (innerGrove == null ? 0f : innerGrove.Values[0]) * 0.01f;
                Hp = Math.Min(MaxHp, Hp + amount);
            }
        }
    }
}

using System;
using Refactor.Util;
using UnityEngine;

namespace Refactor.Main
{
    public enum MajorStatType
    {
        Strength, Constitution, WillPower, Intelligence, Agility, Perception
    }

    public interface IRandomSource
    {
        int NextInt(int minInclusive, int maxExclusive);
        float NextFloat(float minInclusive, float maxInclusive);
    }

    public sealed class UnityRandomSource : IRandomSource
    {
        public int NextInt(int minInclusive, int maxExclusive) => UnityEngine.Random.Range(minInclusive, maxExclusive);
        public float NextFloat(float minInclusive, float maxInclusive) => UnityEngine.Random.Range(minInclusive, maxInclusive);
    }

    // 저장 원천이다. 최종 전투 수치와 달리 시트 재계산으로 성장 난수를 다시 뽑지 않는다.
    public sealed class MajorStats
    {
        public float Strength { get; }
        public float Constitution { get; }
        public float WillPower { get; }
        public float Intelligence { get; }
        public float Agility { get; }
        public float Perception { get; }

        public MajorStats(float strength, float constitution, float willPower, float intelligence, float agility, float perception)
        {
            Validate(strength);
            Validate(constitution);
            Validate(willPower);
            Validate(intelligence);
            Validate(agility);
            Validate(perception);
            Strength = strength;
            Constitution = constitution;
            WillPower = willPower;
            Intelligence = intelligence;
            Agility = agility;
            Perception = perception;
        }

        public float this[MajorStatType stat] => stat switch
        {
            MajorStatType.Strength => Strength,
            MajorStatType.Constitution => Constitution,
            MajorStatType.WillPower => WillPower,
            MajorStatType.Intelligence => Intelligence,
            MajorStatType.Agility => Agility,
            MajorStatType.Perception => Perception,
            _ => throw new ArgumentOutOfRangeException(nameof(stat))
        };

        internal static void Validate(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "능력치는 0 이상의 유한한 값이어야 합니다.");
            }
        }
    }

    public sealed class CharacterGrowthState
    {
        public int Level { get; }
        public float Experience { get; }
        public MajorStats Stats { get; }

        public CharacterGrowthState(int level, float experience, MajorStats stats)
        {
            if (level < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }
            MajorStats.Validate(experience);
            Level = level;
            Experience = experience;
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }
    }

    public sealed class CharacterGrowthCalculator
    {
        private readonly CharacterGrowthSheet _sheet;
        public CharacterGrowthCalculator(CharacterGrowthSheet sheet)
        {
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        }

        public float RequiredExperience(int level) => _sheet.RequiredExperience(level);

        // DS: Status_Exp의 현재 레벨 상한, LevelUpHelper의 순서/정수 난수/반올림/EXP=0.
        // P2 사용자 요구: 제물/수동 승인 없이 최대 EXP 도달 즉시 한 번 성장한다. 초과 EXP는 이월하지 않는다.
        public CharacterGrowthState GainExperience(CharacterGrowthState state, float amount, IRandomSource random, float bonusPercent = 0f)
        {
            return GainExperience(state, amount, random, out _, bonusPercent);
        }

        // DS AddProgressionValue에 전달되는 추첨 증가량을 원천 그대로 돌려준다.
        public CharacterGrowthState GainExperience(CharacterGrowthState state, float amount, IRandomSource random,
            out MajorStats increases, float bonusPercent = 0f)
        {
            increases = new MajorStats(0, 0, 0, 0, 0, 0);
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }
            MajorStats.Validate(amount);
            if (float.IsNaN(bonusPercent) || float.IsInfinity(bonusPercent))
            {
                throw new ArgumentOutOfRangeException(nameof(bonusPercent));
            }
            float maximum = RequiredExperience(state.Level);
            if (state.Experience > maximum)
            {
                throw new ArgumentException("현재 레벨 경험치 상한을 넘었습니다.", nameof(state));
            }
            float gain = amount * (1f + bonusPercent * 0.01f);
            if (float.IsNaN(gain) || float.IsInfinity(gain))
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }
            float experience = Mathf.Clamp(state.Experience + gain, 0f, maximum);
            if (state.Level == _sheet.MaxLevel || experience < maximum)
            {
                return new CharacterGrowthState(state.Level, experience, state.Stats);
            }

            var values = new float[6];
            var growthValues = new float[6];
            for (int i = 0; i < values.Length; i++)
            {
                var type = (MajorStatType)i;
                var range = _sheet.Get(type);
                int roll = random.NextInt(range.MinIncrease, range.MaxIncrease + 1);
                if (roll < range.MinIncrease || roll > range.MaxIncrease)
                {
                    throw new InvalidOperationException("성장 난수 범위가 올바르지 않습니다.");
                }
                growthValues[i] = Mathf.RoundToInt(roll * _sheet.GrowthMultiplier);
                values[i] = state.Stats[type] + growthValues[i];
                MajorStats.Validate(values[i]);
            }
            increases = new MajorStats(growthValues[0], growthValues[1], growthValues[2], growthValues[3], growthValues[4], growthValues[5]);
            return new CharacterGrowthState(state.Level + 1, 0f,
                new MajorStats(values[0], values[1], values[2], values[3], values[4], values[5]));
        }
    }
}

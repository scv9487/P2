using System;
using System.Collections.Generic;

namespace Refactor.Main
{
    public sealed class CharacterProgress
    {
        private readonly MajorStats _baseStats;
        private readonly CharacterGrowthState _growth;
        public string Id { get; }
        public int Affinity { get; }
        public int Level { get; }
        public float Experience { get; }
        public MajorStats GrowthStats { get; }
        public CharacterGrowthState Growth => _growth
            ?? throw new InvalidOperationException("최종 인물 능력치를 조회하려면 CharacterSheet와 함께 진행 데이터를 불러와야 합니다.");
        public MajorStats Stats => Growth.Stats;
        public string EquippedItemId { get; }
        public string WeaponId { get; }
        internal CharacterSkillProgress SkillProgress { get; }
        public int MainSkillPoints => SkillProgress?.MainSkillPoints ?? 0;
        public int SubSkillPoints => SkillProgress?.SubSkillPoints ?? 0;
        public int BasicAttackKills => SkillProgress?.BasicAttackKills ?? 0;
        public IReadOnlyList<string> LearnedSkillIds => SkillProgress?.LearnedSkillIds ?? CharacterSkillProgress.NoSkills;
        public bool HasSkill(string id) => SkillProgress != null && SkillProgress.HasSkill(id);

        // DS의 기본값 + Generated + Progression 경계. 현재 생성 난수가 없으므로
        // Generated는 0이며 GrowthStats에 누적 Progression만 영속화한다.
        internal CharacterProgress(string id, int affinity, int level, float experience, MajorStats growthStats, MajorStats baseStats,
            CharacterSkillProgress skills = null)
            : this(id, affinity, level, experience, growthStats, baseStats, skills, null, null)
        {
        }

        internal CharacterProgress(string id, int affinity, int level, float experience, MajorStats growthStats, MajorStats baseStats,
            CharacterSkillProgress skills, string equippedItemId, string weaponId)
        {
            Id = id;
            Affinity = affinity;
            Level = level;
            Experience = experience;
            GrowthStats = growthStats ?? throw new ArgumentNullException(nameof(growthStats));
            _baseStats = baseStats;
            SkillProgress = skills;
            EquippedItemId = equippedItemId;
            WeaponId = weaponId;
            if (baseStats != null)
            {
                _growth = new CharacterGrowthState(level, experience, Add(baseStats, growthStats));
            }
        }

        internal CharacterProgress WithAffinity(int affinity)
        {
            return new CharacterProgress(Id, affinity, Level, Experience, GrowthStats, _baseStats, SkillProgress, EquippedItemId, WeaponId);
        }

        internal CharacterProgress WithGrowth(CharacterGrowthState growth, MajorStats increases)
        {
            return new CharacterProgress(Id, Affinity, growth.Level, growth.Experience, Add(GrowthStats, increases), _baseStats, SkillProgress, EquippedItemId, WeaponId);
        }

        internal CharacterProgress WithSkills(CharacterSkillProgress skills)
        {
            return new CharacterProgress(Id, Affinity, Level, Experience, GrowthStats, _baseStats, skills, EquippedItemId, WeaponId);
        }

        internal CharacterProgress WithEquipment(string itemId, string weaponId)
        {
            return new CharacterProgress(Id, Affinity, Level, Experience, GrowthStats, _baseStats, SkillProgress, itemId, weaponId);
        }

        private static MajorStats Add(MajorStats left, MajorStats right)
        {
            return new MajorStats(left.Strength + right.Strength, left.Constitution + right.Constitution,
                left.WillPower + right.WillPower, left.Intelligence + right.Intelligence,
                left.Agility + right.Agility, left.Perception + right.Perception);
        }
    }

    public enum CharacterActionResult
    {
        Saved,
        AlreadyApplied,
        RecruitFailed,
        AlreadyInParty,
        NotInParty,
        PartyFull,
        InvalidTarget,
        InvalidRequest,
        SaveFailed,
        LoadBlocked
    }
}

using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor.Main
{
    // 잔여 SP와 습득 ID만 원천으로 저장한다. 비용/설명/효과는 현재 시트에서 읽는다.
    internal sealed class CharacterSkillProgress
    {
        internal static readonly IReadOnlyList<string> NoSkills = Array.AsReadOnly(Array.Empty<string>());
        public int MainSkillPoints { get; }
        public int SubSkillPoints { get; }
        public int BasicAttackKills { get; }
        public IReadOnlyList<string> LearnedSkillIds { get; }

        public CharacterSkillProgress(int main, int sub, IEnumerable<string> learned, int basicAttackKills = 0)
        {
            MainSkillPoints = main;
            SubSkillPoints = sub;
            BasicAttackKills = basicAttackKills;
            LearnedSkillIds = new List<string>(learned).AsReadOnly();
        }

        public bool HasSkill(string id)
        {
            foreach (string learned in LearnedSkillIds)
            {
                if (learned == id)
                {
                    return true;
                }
            }
            return false;
        }

        public static CharacterSkillProgress CreateInitial(SkillSheet sheet, int level)
        {
            int main = (int)Math.Min(sheet.MaxSkillPoints, (long)sheet.InitialMainSkillPoints + (long)(level - 1) * sheet.MainSkillPointsPerLevel);
            int sub = (int)Math.Min(sheet.MaxSkillPoints, (long)sheet.InitialSubSkillPoints + (long)(level - 1) * sheet.SubSkillPointsPerLevel);
            return new CharacterSkillProgress(main, sub, NoSkills);
        }

        public CharacterSkillProgress GrantLevelPoints(SkillSheet sheet, int levels)
        {
            int main = (int)Math.Min(sheet.MaxSkillPoints, (long)MainSkillPoints + (long)levels * sheet.MainSkillPointsPerLevel);
            int sub = (int)Math.Min(sheet.MaxSkillPoints, (long)SubSkillPoints + (long)levels * sheet.SubSkillPointsPerLevel);
            return new CharacterSkillProgress(main, sub, LearnedSkillIds, BasicAttackKills);
        }

        public CharacterSkillProgress Learn(SkillDefinition skill)
        {
            var learned = new List<string>(LearnedSkillIds) { skill.Id };
            return new CharacterSkillProgress(MainSkillPoints - (skill.TreeKind == SkillTreeKind.Main ? skill.Cost : 0),
                SubSkillPoints - (skill.TreeKind == SkillTreeKind.Sub ? skill.Cost : 0), learned, BasicAttackKills);
        }

        public CharacterSkillProgress AddBasicAttackKills(int count)
        {
            int total = (int)Math.Min(int.MaxValue, (long)BasicAttackKills + count);
            return new CharacterSkillProgress(MainSkillPoints, SubSkillPoints, LearnedSkillIds, total);
        }
    }

    public enum SkillLearnFailure
    {
        None,
        AlreadyLearned,
        WrongTree,
        LevelRequired,
        PrerequisiteRequired,
        InsufficientPoints,
        InvalidSkill
    }

    public static class SkillLearningRules
    {
        public static bool CanLearn(SkillSheet sheet, CharacterProgress character, string skillId)
        {
            return GetLearnFailure(sheet, character, skillId) == SkillLearnFailure.None;
        }

        public static SkillLearnFailure GetLearnFailure(SkillSheet sheet, CharacterProgress character, string skillId)
        {
            if (sheet == null || character == null || character.SkillProgress == null)
            {
                return SkillLearnFailure.InvalidSkill;
            }
            SkillDefinition skill = null;
            foreach (var definition in sheet.Skills)
            {
                if (definition.Id == skillId)
                {
                    skill = definition;
                    break;
                }
            }
            if (skill == null)
            {
                return SkillLearnFailure.InvalidSkill;
            }
            if (character.HasSkill(skillId))
            {
                return SkillLearnFailure.AlreadyLearned;
            }
            if (skill.TreeKind == SkillTreeKind.Main && skill.TreeId != sheet.GetClassForCharacter(character).ActiveTreeId)
            {
                return SkillLearnFailure.WrongTree;
            }
            foreach (string prerequisite in skill.PrerequisiteIds)
            {
                if (!character.HasSkill(prerequisite))
                {
                    return SkillLearnFailure.PrerequisiteRequired;
                }
            }
            if (character.Level < skill.RequiredLevel)
            {
                return SkillLearnFailure.LevelRequired;
            }
            int points = skill.TreeKind == SkillTreeKind.Main ? character.MainSkillPoints : character.SubSkillPoints;
            return points >= skill.Cost ? SkillLearnFailure.None : SkillLearnFailure.InsufficientPoints;
        }
    }

    public enum SkillLearnResult
    {
        Saved,
        AlreadyApplied,
        PlayerOnly,
        InvalidRequest,
        AlreadyLearned,
        WrongTree,
        LevelRequired,
        PrerequisiteRequired,
        InsufficientPoints,
        SaveFailed,
        LoadBlocked
    }
}

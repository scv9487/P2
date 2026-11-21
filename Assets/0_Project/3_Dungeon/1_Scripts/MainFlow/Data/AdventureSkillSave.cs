using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor
{
    public static partial class AdventureSave
    {
        // 저장 형식 범위와 현재 시트의 지급/보유 한도를 분리한다.
        public const int MaximumStoredSkillPoints = 1000000000;

        private static bool HasValidSkillData(AdventureSaveData dto, SkillSheet skills)
        {
            if (dto.SkillCharacterIds == null || dto.CharacterMainSkillPoints == null || dto.CharacterSubSkillPoints == null || dto.CharacterBasicAttackKills == null
                || dto.LearnedSkillCharacterIds == null || dto.LearnedSkillIds == null || dto.SkillActionIds == null
                || dto.SkillCharacterIds.Length != dto.CharacterMainSkillPoints.Length
                || dto.SkillCharacterIds.Length != dto.CharacterSubSkillPoints.Length
                || dto.SkillCharacterIds.Length != dto.CharacterBasicAttackKills.Length
                || dto.LearnedSkillCharacterIds.Length != dto.LearnedSkillIds.Length)
            {
                return false;
            }
            // 스킬 시트를 사용하지 않은 기반 저장은 아직 미초기화된 빈 원천을 보존한다.
            if (dto.SkillCharacterIds.Length == 0)
            {
                return dto.LearnedSkillIds.Length == 0 && dto.SkillActionIds.Length == 0;
            }
            if (dto.SkillCharacterIds.Length != dto.CharacterIds.Length)
            {
                return false;
            }
            var characterIds = new HashSet<string>(dto.CharacterIds, StringComparer.Ordinal);
            var learned = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            for (int i = 0; i < dto.SkillCharacterIds.Length; i++)
            {
                string id = dto.SkillCharacterIds[i];
                if (!IsValidSettlementId(id) || !characterIds.Contains(id) || learned.ContainsKey(id)
                    || dto.CharacterMainSkillPoints[i] < 0 || dto.CharacterMainSkillPoints[i] > MaximumStoredSkillPoints
                    || dto.CharacterSubSkillPoints[i] < 0 || dto.CharacterSubSkillPoints[i] > MaximumStoredSkillPoints
                    || dto.CharacterBasicAttackKills[i] < 0)
                {
                    return false;
                }
                learned.Add(id, new HashSet<string>(StringComparer.Ordinal));
            }
            var knownSkills = new HashSet<string>(StringComparer.Ordinal);
            if (skills != null)
            {
                foreach (var skill in skills.Skills)
                {
                    knownSkills.Add(skill.Id);
                }
            }
            for (int i = 0; i < dto.LearnedSkillIds.Length; i++)
            {
                string characterId = dto.LearnedSkillCharacterIds[i];
                string skillId = dto.LearnedSkillIds[i];
                if (characterId == null || !learned.TryGetValue(characterId, out var set)
                    || !IsValidSettlementId(skillId) || !set.Add(skillId) || (skills != null && !knownSkills.Contains(skillId)))
                {
                    return false;
                }
                // 이미 구매한 스킬은 현재 비용/요구 레벨/선행 조건 조정 후에도 유지한다.
                // 현재 직업의 트리 제한은 새 구매를 검사하는 SkillLearningRules가 책임진다.
            }
            var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in dto.SkillActionIds)
            {
                if (!IsValidSettlementId(id) || !actions.Add(id))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

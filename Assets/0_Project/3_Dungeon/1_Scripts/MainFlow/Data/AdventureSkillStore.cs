using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Refactor.Util;

namespace Refactor.Main
{
    public sealed partial class AdventureProgressDataContainer
    {
        private readonly HashSet<string> _skillActions = new HashSet<string>(StringComparer.Ordinal);
        internal bool HasSkillAction(string id) => _skillActions.Contains(id);
        internal void RecordSkillAction(string id) => _skillActions.Add(id);

        private void CopySkillsFrom(AdventureProgressDataContainer source)
        {
            _skillActions.Clear();
            _skillActions.UnionWith(source._skillActions);
        }

        private void WriteSkillSave(AdventureSaveData dto)
        {
            var characters = new List<string>();
            var main = new List<int>();
            var sub = new List<int>();
            var kills = new List<int>();
            var learnedCharacters = new List<string>();
            var learnedSkills = new List<string>();
            foreach (string id in dto.CharacterIds)
            {
                var character = _characters[id];
                if (character.SkillProgress == null)
                {
                    continue;
                }
                characters.Add(id);
                main.Add(character.MainSkillPoints);
                sub.Add(character.SubSkillPoints);
                kills.Add(character.BasicAttackKills);
                foreach (string skillId in character.LearnedSkillIds)
                {
                    learnedCharacters.Add(id);
                    learnedSkills.Add(skillId);
                }
            }
            dto.SkillCharacterIds = characters.ToArray();
            dto.CharacterMainSkillPoints = main.ToArray();
            dto.CharacterSubSkillPoints = sub.ToArray();
            dto.CharacterBasicAttackKills = kills.ToArray();
            dto.LearnedSkillCharacterIds = learnedCharacters.ToArray();
            dto.LearnedSkillIds = learnedSkills.ToArray();
            dto.SkillActionIds = new string[_skillActions.Count];
            _skillActions.CopyTo(dto.SkillActionIds);
            Array.Sort(dto.SkillActionIds, StringComparer.Ordinal);
        }

        private void ReadSkillSave(AdventureSaveData dto)
        {
            if (dto.SkillCharacterIds.Length == 0)
            {
                if (_skillSheet != null)
                {
                    foreach (string id in new List<string>(_characters.Keys))
                    {
                        var previous = _characters[id];
                        SetCharacter(previous.WithSkills(CharacterSkillProgress.CreateInitial(_skillSheet, previous.Level)));
                    }
                }
                return;
            }
            var learned = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string id in dto.SkillCharacterIds)
            {
                learned.Add(id, new List<string>());
            }
            for (int i = 0; i < dto.LearnedSkillIds.Length; i++)
            {
                learned[dto.LearnedSkillCharacterIds[i]].Add(dto.LearnedSkillIds[i]);
            }
            for (int i = 0; i < dto.SkillCharacterIds.Length; i++)
            {
                string id = dto.SkillCharacterIds[i];
                int main = _skillSheet == null ? dto.CharacterMainSkillPoints[i] : Math.Min(dto.CharacterMainSkillPoints[i], _skillSheet.MaxSkillPoints);
                int sub = _skillSheet == null ? dto.CharacterSubSkillPoints[i] : Math.Min(dto.CharacterSubSkillPoints[i], _skillSheet.MaxSkillPoints);
                var source = new CharacterSkillProgress(main, sub, learned[id], dto.CharacterBasicAttackKills[i]);
                SetCharacter(_characters[id].WithSkills(source));
            }
            _skillActions.UnionWith(dto.SkillActionIds);
        }
    }

    public sealed partial class AdventureProgressStore
    {
        private readonly SkillSheet _skills;

        public SkillLearnResult TryLearnSkill(string characterId, string skillId, string requestId, int expectedRevision)
        {
            if (!CanWrite)
            {
                return SkillLearnResult.LoadBlocked;
            }
            if (_skills == null || !AdventureSave.IsValidSettlementId(requestId))
            {
                return SkillLearnResult.InvalidRequest;
            }
            if (_progress.HasSkillAction(requestId))
            {
                return SkillLearnResult.AlreadyApplied;
            }
            if (expectedRevision != _progress.Revision || characterId == null || !_progress.Characters.ContainsKey(characterId))
            {
                return SkillLearnResult.InvalidRequest;
            }
            if (characterId != _sheet.PlayerMemberId)
            {
                return SkillLearnResult.PlayerOnly;
            }
            var previous = _progress.GetCharacter(characterId);
            if (previous.EquippedItemId != null && previous.WeaponId == null)
            {
                return SkillLearnResult.InvalidRequest;
            }
            var failure = SkillLearningRules.GetLearnFailure(_skills, previous, skillId);
            if (failure != SkillLearnFailure.None)
            {
                switch (failure)
                {
                    case SkillLearnFailure.AlreadyLearned: return SkillLearnResult.AlreadyLearned;
                    case SkillLearnFailure.WrongTree: return SkillLearnResult.WrongTree;
                    case SkillLearnFailure.LevelRequired: return SkillLearnResult.LevelRequired;
                    case SkillLearnFailure.PrerequisiteRequired: return SkillLearnResult.PrerequisiteRequired;
                    case SkillLearnFailure.InsufficientPoints: return SkillLearnResult.InsufficientPoints;
                    default: return SkillLearnResult.InvalidRequest;
                }
            }
            var candidate = _progress.Copy();
            candidate.SetCharacter(previous.WithSkills(previous.SkillProgress.Learn(_skills.GetSkill(skillId))));
            candidate.RecordSkillAction(requestId);
            try
            {
                AdventureSave.Save(_path, candidate.ToSaveData(), _sheet, _shops, _characters, _quests, _skills, _equipment);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is SecurityException || exception is ArgumentException || exception is NotSupportedException)
            {
                return SkillLearnResult.SaveFailed;
            }
            _progress.Replace(candidate);
            return SkillLearnResult.Saved;
        }

        // 같은 EXP 후보 안에서 SP 지급과 습득도 확정한다. 저장 실패 재시도는 이 결과를 재사용한다.
        private CharacterProgress ApplyLevelSkills(CharacterProgress previous, CharacterProgress grown, IRandomSource random)
        {
            int levels = grown.Level - previous.Level;
            if (_skills == null || levels <= 0)
            {
                return grown;
            }
            var result = grown.WithSkills(grown.SkillProgress.GrantLevelPoints(_skills, levels));
            if (_characters.GetCharacter(grown.Id).IsPlayer)
            {
                return result;
            }
            while (true)
            {
                var candidates = new List<SkillDefinition>();
                int highestLevel = -1;
                foreach (var skill in _skills.Skills)
                {
                    if (!SkillLearningRules.CanLearn(_skills, result, skill.Id) || skill.RequiredLevel < highestLevel)
                    {
                        continue;
                    }
                    if (skill.RequiredLevel > highestLevel)
                    {
                        highestLevel = skill.RequiredLevel;
                        candidates.Clear();
                    }
                    candidates.Add(skill);
                }
                if (candidates.Count == 0)
                {
                    return result;
                }
                candidates.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
                int selected = random.NextInt(0, candidates.Count);
                if (selected < 0 || selected >= candidates.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(random), "스킬 추첨 결과가 후보 범위를 벗어났습니다.");
                }
                result = result.WithSkills(result.SkillProgress.Learn(candidates[selected]));
            }
        }
    }
}

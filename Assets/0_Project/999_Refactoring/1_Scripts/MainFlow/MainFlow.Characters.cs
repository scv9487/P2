using System;
using System.Collections.Generic;
using Refactor.Main.Event;
using Refactor.Main.InputModule;
using Refactor.Util;

namespace Refactor.Main
{
    public sealed partial class MainFlow
    {
        private CharacterSheet _characterSheet;
        private IRandomSource _characterRandom;
        private readonly CharacterInteractionData _characterInteraction = new();
        public CharacterSheet CharacterSheet => _characterSheet;
        public ICharacterInteractionReader CharacterInteraction => _characterInteraction;

        private bool CanOpenCharacter()
        {
            if (_characterSheet == null || _characterInteraction.Open || _heroMenu.Open || IsTransitioning)
            {
                return false;
            }
            if (_sceneType == SceneType.Town)
            {
                return !_townData.ShopOpen && !_townData.DungeonSelectionOpen && !_townData.QuestBoardOpen
                    && string.IsNullOrEmpty(_townData.CommandId) && string.IsNullOrEmpty(_townData.CharacterId);
            }
            return _sceneType == SceneType.Dungeon && !_dungeonData.SettlementOpen && !_dungeonData.DetailsOpen
                && !_dungeonData.ProgressSaveBlocked && !_dungeonData.IsDefeated && !_dungeonData.IsCleared
                && _dungeonData.PendingExperience == null && _dungeonData.PendingQuestFact == null;
        }

        // TownData의 방문자 검사 직후에만 호출된다.
        private void OpenTownCharacter(string id)
        {
            if (_townData.CharacterId == id && !_adventureStore.Progress.IsInParty(id))
            {
                SelectCharacter(id, false);
            }
        }

        private void OpenDungeonCharacter(string id)
        {
            if (CanOpenCharacter() && _dungeonData.CanTalk && id == _dungeonData.VisitorId)
            {
                SelectCharacter(id, false);
            }
        }

        private void SelectCharacter(string id, bool party)
        {
            _characterInteraction.Select(id, party, _adventureStore.Progress.Revision);
            if (!_adventureStore.CanWrite)
            {
                _characterInteraction.ShowError(_characterSheet.Text("status.load_failed"));
            }
        }

        private void CloseCharacter()
        {
            _characterInteraction.Close();
            if (_townData != null && !string.IsNullOrEmpty(_townData.CharacterId))
            {
                _townData.CloseDetails();
            }
        }

        private void HandleCharacterAction(CharacterActionRequested request)
        {
            if (_characterSheet == null || IsTransitioning || _heroMenu.Open)
            {
                return;
            }
            if (request.Action == UIInputType.PartyCharacterOpen)
            {
                if (request.Context == "party" && CanOpenCharacter() && _adventureStore.Progress.IsInParty(request.TargetId))
                {
                    SelectCharacter(request.TargetId, true);
                }
                return;
            }
            if (request.Action == UIInputType.CharacterOpen)
            {
                if (!CanOpenCharacter())
                {
                    return;
                }
                if (_sceneType == SceneType.Town)
                {
                    if (_townData.TrySelectCharacter(request.Context, request.TargetId))
                    {
                        OpenTownCharacter(request.TargetId);
                    }
                }
                else if (request.Context == _dungeonData.Context)
                {
                    OpenDungeonCharacter(request.TargetId);
                }
                return;
            }
            if (!_characterInteraction.Open || request.Context != _characterInteraction.Context
                || (!string.IsNullOrEmpty(request.TargetId) && request.TargetId != _characterInteraction.CharacterId))
            {
                return;
            }
            if (request.Action == UIInputType.CharacterClose)
            {
                CloseCharacter();
                return;
            }
            string id = _characterInteraction.CharacterId;
            var definition = _characterSheet.GetCharacter(id);
            if (definition.IsPlayer)
            {
                return;
            }
            var progress = _adventureStore.Progress;
            int beforeAffinity = progress.GetCharacter(id).Affinity;
            CharacterActionResult result;
            switch (request.Action)
            {
                case UIInputType.CharacterTalk:
                    result = _adventureStore.TryTalk(id, request.Context, _characterInteraction.SourceRevision);
                    break;
                case UIInputType.CharacterRecruit:
                    if (_characterInteraction.FromParty)
                    {
                        return;
                    }
                    if (!_characterInteraction.RecruitmentRoll.HasValue)
                    {
                        _characterInteraction.RecruitmentRoll = _characterRandom.NextInt(0, 100);
                    }
                    result = _adventureStore.TryRecruit(id, request.Context, _characterInteraction.SourceRevision,
                        _characterInteraction.RecruitmentRoll.Value);
                    break;
                case UIInputType.CharacterDismiss:
                    if (!_characterInteraction.FromParty)
                    {
                        return;
                    }
                    result = _adventureStore.TryDismiss(id, request.Context, _characterInteraction.SourceRevision);
                    break;
                default:
                    return;
            }
            if (result == CharacterActionResult.Saved || result == CharacterActionResult.AlreadyApplied)
            {
                _dungeonData?.SynchronizeParty();
                if (request.Action == UIInputType.CharacterDismiss)
                {
                    _status = string.Format(_characterSheet.Text("status.dismissed_format"), definition.Name);
                    CloseCharacter();
                }
                else if (request.Action == UIInputType.CharacterRecruit)
                {
                    _characterInteraction.Select(id, true, progress.Revision);
                    _characterInteraction.Advance(progress.Revision,
                        string.Format(_characterSheet.Text("status.recruited_format"), definition.Name));
                }
                else
                {
                    _characterInteraction.Advance(progress.Revision,
                        string.Format(_characterSheet.Text("status.talked_format"), definition.Name,
                            progress.GetCharacter(id).Affinity - beforeAffinity), true);
                }
                return;
            }
            if (result == CharacterActionResult.RecruitFailed)
            {
                _characterInteraction.Advance(progress.Revision, _characterSheet.Text("status.recruit_failed"));
                return;
            }
            string key = result == CharacterActionResult.LoadBlocked ? "status.load_failed"
                : result == CharacterActionResult.SaveFailed ? "status.save_failed"
                : result == CharacterActionResult.PartyFull ? "status.party_full" : "status.invalid_request";
            _characterInteraction.ShowError(_characterSheet.Text(key));
        }

        // 처치 보상은 EventQueue 또는 TickFlow가 기록한 뒤 이 후속 단계에서 원자적으로 적용한다.
        private bool CommitDungeonExperience(bool retry)
        {
            if (_dungeonData == null || _adventureStore == null)
            {
                return true;
            }
            if (_dungeonData.ProgressSaveBlocked && !retry)
            {
                return false;
            }
            while (_dungeonData.PendingExperience != null)
            {
                var reward = _dungeonData.PendingExperience;
                var result = _adventureStore.TryGrantExperience(reward.Id, reward.Grants, _characterRandom, reward.BasicAttackKills);
                if (result != CharacterActionResult.Saved && result != CharacterActionResult.AlreadyApplied)
                {
                    _dungeonData.ShowProgressSaveFailure(_characterSheet.Text("status.exp_save_failed"));
                    return false;
                }
                _dungeonData.CompleteExperienceReward(reward.Id);
                _dungeonData.SynchronizeParty();
            }
            _dungeonData.ClearProgressSaveFailure();
            return true;
        }

        private IReadOnlyList<LootShareDefinition> ExpeditionShares()
        {
            var participants = _dungeonData.ExpeditionMemberIds;
            var shares = new List<LootShareDefinition>();
            var percentages = new int[participants.Count];
            var order = new List<int>();
            long totalWeight = 0;
            foreach (string id in participants)
            {
                totalWeight += _characterSheet.GetCharacter(id).LootWeight;
            }
            int used = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                percentages[i] = (int)(_characterSheet.GetCharacter(participants[i]).LootWeight * 100L / totalWeight);
                used += percentages[i];
                order.Add(i);
            }
            order.Sort((left, right) =>
            {
                long a = _characterSheet.GetCharacter(participants[left]).LootWeight * 100L % totalWeight;
                long b = _characterSheet.GetCharacter(participants[right]).LootWeight * 100L % totalWeight;
                return a == b ? left.CompareTo(right) : b.CompareTo(a);
            });
            for (int i = 0; i < 100 - used; i++)
            {
                percentages[order[i]]++;
            }
            for (int i = 0; i < participants.Count; i++)
            {
                var character = _characterSheet.GetCharacter(participants[i]);
                shares.Add(new LootShareDefinition(character.Id, character.Name, percentages[i]));
            }
            return shares.AsReadOnly();
        }
    }
}

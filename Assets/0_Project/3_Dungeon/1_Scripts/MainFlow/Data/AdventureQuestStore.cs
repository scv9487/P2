using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Refactor.Util;

namespace Refactor.Main
{
    public sealed partial class AdventureProgressDataContainer
    {
        private readonly HashSet<string> _questActions = new HashSet<string>(StringComparer.Ordinal);
        public QuestProgress Quest { get; private set; } = new QuestProgress(string.Empty, string.Empty, 0, Array.Empty<string>());

        internal bool HasQuestAction(string id) => _questActions.Contains(id);
        internal void RecordQuestAction(string id) => _questActions.Add(id);
        internal void SetQuest(QuestProgress value) => Quest = value;

        private void CopyQuestFrom(AdventureProgressDataContainer source)
        {
            Quest = source.Quest;
            _questActions.Clear();
            _questActions.UnionWith(source._questActions);
        }

        private void WriteQuestSave(AdventureSaveData dto)
        {
            bool active = !string.IsNullOrEmpty(Quest.ActiveQuestId);
            dto.ActiveQuestIds = active ? new[] { Quest.ActiveQuestId } : Array.Empty<string>();
            dto.QuestAcceptanceIds = active ? new[] { Quest.AcceptanceId } : Array.Empty<string>();
            dto.QuestProgressCounts = active ? new[] { Quest.ProgressCount } : Array.Empty<int>();
            dto.CompletedQuestIds = new List<string>(Quest.CompletedQuestIds).ToArray();
            dto.QuestActionIds = new string[_questActions.Count];
            _questActions.CopyTo(dto.QuestActionIds);
            Array.Sort(dto.CompletedQuestIds, StringComparer.Ordinal);
            Array.Sort(dto.QuestActionIds, StringComparer.Ordinal);
        }

        private void ReadQuestSave(AdventureSaveData dto)
        {
            bool active = dto.ActiveQuestIds.Length > 0;
            Quest = new QuestProgress(active ? dto.ActiveQuestIds[0] : string.Empty,
                active ? dto.QuestAcceptanceIds[0] : string.Empty,
                active ? dto.QuestProgressCounts[0] : 0, dto.CompletedQuestIds);
            _questActions.UnionWith(dto.QuestActionIds);
        }
    }

    public sealed partial class AdventureProgressStore
    {
        private readonly QuestSheet _quests;

        public QuestActionResult TryAcceptQuest(string questId, string requestId, int expectedRevision)
        {
            var validation = ValidateQuestRequest(questId, requestId, expectedRevision, out var quest);
            if (validation != QuestActionResult.Saved)
            {
                return validation;
            }
            if (_progress.Quest.IsCompleted(questId))
            {
                return QuestActionResult.AlreadyCompleted;
            }
            if (!string.IsNullOrEmpty(_progress.Quest.ActiveQuestId))
            {
                return QuestActionResult.ActiveQuestExists;
            }
            var candidate = _progress.Copy();
            candidate.SetQuest(new QuestProgress(quest.Id, requestId, 0, candidate.Quest.CompletedQuestIds));
            return SaveQuestChange(candidate, requestId);
        }

        public QuestActionResult TryReportQuest(string questId, string requestId, int expectedRevision)
        {
            var validation = ValidateQuestRequest(questId, requestId, expectedRevision, out var quest);
            if (validation != QuestActionResult.Saved)
            {
                return validation;
            }
            if (_progress.Quest.IsCompleted(questId))
            {
                return QuestActionResult.AlreadyCompleted;
            }
            if (_progress.Quest.ActiveQuestId != questId)
            {
                return QuestActionResult.NotActive;
            }
            if (!QuestRules.CanReport(quest, _progress))
            {
                return QuestActionResult.NotReady;
            }
            if (_progress.PlayerGold > long.MaxValue - quest.RewardGold)
            {
                return QuestActionResult.InvalidRequest;
            }
            var candidate = _progress.Copy();
            if (quest.Kind == QuestKind.DeliverItems)
            {
                candidate.SetItem(quest.TargetId, candidate.Items[quest.TargetId] - quest.RequiredCount);
            }
            candidate.SetGold(_sheet.PlayerMemberId, candidate.PlayerGold + quest.RewardGold);
            var completed = new List<string>(candidate.Quest.CompletedQuestIds) { questId };
            candidate.SetQuest(new QuestProgress(string.Empty, string.Empty, 0, completed));
            return SaveQuestChange(candidate, requestId);
        }

        public QuestActionResult TryApplyQuestFact(QuestFact fact)
        {
            if (!CanWrite)
            {
                return QuestActionResult.LoadBlocked;
            }
            if (_quests == null || fact == null || !AdventureSave.IsValidSettlementId(fact.Id)
                || !Enum.IsDefined(typeof(QuestFactType), fact.Type) || fact.DungeonId < 1 || fact.DungeonId > 9999
                || fact.Floor < 0 || fact.Floor > 3 || fact.Count < 1 || fact.Count > 1000000
                || fact.TargetId == null || (fact.TargetId.Length > 0 && !AdventureSave.IsValidSettlementId(fact.TargetId))
                || (!string.IsNullOrEmpty(fact.AcceptanceId) && !AdventureSave.IsValidSettlementId(fact.AcceptanceId)))
            {
                return QuestActionResult.InvalidRequest;
            }
            if (_progress.HasQuestAction(fact.Id))
            {
                return QuestActionResult.AlreadyApplied;
            }
            var state = _progress.Quest;
            if (string.IsNullOrEmpty(state.ActiveQuestId) || string.IsNullOrEmpty(fact.AcceptanceId) || state.AcceptanceId != fact.AcceptanceId)
            {
                return QuestActionResult.Ignored;
            }
            var quest = _quests.GetQuest(state.ActiveQuestId);
            bool matches = (quest.DungeonId == 0 || quest.DungeonId == fact.DungeonId)
                && (quest.Floor == 0 || quest.Floor == fact.Floor);
            switch (quest.Kind)
            {
                case QuestKind.ClearDungeon:
                    matches &= fact.Type == QuestFactType.DungeonCleared && fact.TargetId == string.Empty;
                    break;
                case QuestKind.RescueAdventurers:
                    matches &= fact.Type == QuestFactType.AdventurersRescued && fact.TargetId == quest.TargetId;
                    break;
                case QuestKind.KillMonster:
                    matches &= fact.Type == QuestFactType.KillMonster && fact.TargetId == quest.TargetId;
                    break;
                default:
                    matches = false;
                    break;
            }
            if (!matches || state.ProgressCount >= quest.RequiredCount)
            {
                return QuestActionResult.Ignored;
            }
            var candidate = _progress.Copy();
            int count = (int)Math.Min((long)quest.RequiredCount, (long)state.ProgressCount + fact.Count);
            candidate.SetQuest(new QuestProgress(state.ActiveQuestId, state.AcceptanceId, count, state.CompletedQuestIds));
            return SaveQuestChange(candidate, fact.Id);
        }

        private QuestActionResult ValidateQuestRequest(string questId, string requestId, int expectedRevision, out QuestDefinition quest)
        {
            quest = null;
            if (!CanWrite)
            {
                return QuestActionResult.LoadBlocked;
            }
            if (_quests == null || !AdventureSave.IsValidSettlementId(requestId))
            {
                return QuestActionResult.InvalidRequest;
            }
            if (_progress.HasQuestAction(requestId))
            {
                return QuestActionResult.AlreadyApplied;
            }
            if (expectedRevision != _progress.Revision)
            {
                return QuestActionResult.InvalidRequest;
            }
            foreach (var definition in _quests.Quests)
            {
                if (definition.Id == questId)
                {
                    quest = definition;
                    return QuestActionResult.Saved;
                }
            }
            return QuestActionResult.InvalidRequest;
        }

        private QuestActionResult SaveQuestChange(AdventureProgressDataContainer candidate, string requestId)
        {
            candidate.RecordQuestAction(requestId);
            try
            {
                AdventureSave.Save(_path, candidate.ToSaveData(), _sheet, _shops, _characters, _quests, _skills, _equipment);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is SecurityException || exception is ArgumentException || exception is NotSupportedException)
            {
                return QuestActionResult.SaveFailed;
            }
            _progress.Replace(candidate);
            return QuestActionResult.Saved;
        }
    }
}

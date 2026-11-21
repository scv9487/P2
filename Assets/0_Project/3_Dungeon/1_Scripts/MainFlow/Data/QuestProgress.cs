using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor.Main
{
    public enum QuestFactType
    {
        KillMonster,
        DungeonCleared,
        AdventurersRescued
    }

    public sealed class QuestFact
    {
        public QuestFactType Type { get; }
        public string Id { get; }
        public int DungeonId { get; }
        public string TargetId { get; }
        public int Count { get; }
        public string AcceptanceId { get; }
        public int Floor { get; }

        public QuestFact(QuestFactType type, string id, int dungeonId, string targetId, int count, string acceptanceId, int floor = 0)
        {
            Type = type;
            Id = id;
            DungeonId = dungeonId;
            TargetId = targetId;
            Count = count;
            AcceptanceId = acceptanceId;
            Floor = floor;
        }
    }

    public sealed class QuestProgress
    {
        public string ActiveQuestId { get; }
        public string AcceptanceId { get; }
        public int ProgressCount { get; }
        public IReadOnlyList<string> CompletedQuestIds { get; }

        internal QuestProgress(string activeQuestId, string acceptanceId, int count, IEnumerable<string> completed)
        {
            ActiveQuestId = activeQuestId;
            AcceptanceId = acceptanceId;
            ProgressCount = count;
            CompletedQuestIds = new List<string>(completed).AsReadOnly();
        }

        public bool IsCompleted(string id)
        {
            foreach (string completed in CompletedQuestIds)
            {
                if (completed == id)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public static class QuestRules
    {
        public static int GetProgress(QuestDefinition quest, IAdventureProgressReader progress)
        {
            if (quest == null || progress == null)
            {
                return 0;
            }
            if (progress.Quest.IsCompleted(quest.Id))
            {
                return quest.RequiredCount;
            }
            if (progress.Quest.ActiveQuestId != quest.Id)
            {
                return 0;
            }
            int value = progress.Quest.ProgressCount;
            if (quest.Kind == QuestKind.DeliverItems)
            {
                progress.Items.TryGetValue(quest.TargetId, out value);
            }
            return Math.Min(value, quest.RequiredCount);
        }

        public static bool CanAccept(QuestDefinition quest, IAdventureProgressReader progress)
        {
            return quest != null && progress != null && string.IsNullOrEmpty(progress.Quest.ActiveQuestId) && !progress.Quest.IsCompleted(quest.Id);
        }

        public static bool CanReport(QuestDefinition quest, IAdventureProgressReader progress)
        {
            return quest != null && progress != null && progress.Quest.ActiveQuestId == quest.Id
                && !progress.Quest.IsCompleted(quest.Id) && GetProgress(quest, progress) >= quest.RequiredCount;
        }
    }

    public enum QuestActionResult
    {
        Saved,
        AlreadyApplied,
        Ignored,
        ActiveQuestExists,
        AlreadyCompleted,
        NotActive,
        NotReady,
        InvalidRequest,
        SaveFailed,
        LoadBlocked
    }
}

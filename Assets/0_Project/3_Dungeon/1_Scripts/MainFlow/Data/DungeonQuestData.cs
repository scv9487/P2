using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor.Main
{
    public sealed partial class DungeonDataContainer
    {
        private readonly Dictionary<int, IReadOnlyList<DungeonNodeDefinition>> _instanceFloors = new();
        private readonly Dictionary<string, DungeonNodeDefinition> _instanceNodes = new(StringComparer.Ordinal);
        private readonly Queue<QuestFact> _questFacts = new();
        private readonly HashSet<string> _recordedQuestFacts = new(StringComparer.Ordinal);
        private QuestSheet _quests;
        private IAdventureProgressReader _questProgress;
        private QuestRescueNodeDefinition _rescueNode;

        public QuestFact PendingQuestFact => _questFacts.Count == 0 ? null : _questFacts.Peek();

        private int? RescueEnemyCount => CurrentNode.Type == DungeonNodeType.Rescue ? _rescueNode?.EnemyCount : null;

        // 원본 시트와 각 탐험의 그래프를 분리한다. 추가 노드의 연결도 이 인스턴스만 소유한다.
        private void InitializeQuests(QuestSheet quests, IAdventureProgressReader progress)
        {
            _quests = quests;
            _questProgress = progress;
            if (quests != null && progress == null)
            {
                throw new ArgumentNullException(nameof(progress));
            }
            for (int floor = 1; floor <= Dungeon.FloorCount; floor++)
            {
                var copies = new List<DungeonNodeDefinition>();
                foreach (var source in _sheet.GetNodes(floor))
                {
                    var copy = CopyNode(source, new List<string>(source.NeighborIds));
                    copies.Add(copy);
                    _instanceNodes.Add(floor + ":" + copy.Id, copy);
                }
                _instanceFloors.Add(floor, copies.AsReadOnly());
            }
            if (quests == null || string.IsNullOrEmpty(progress.Quest.ActiveQuestId))
            {
                return;
            }
            var state = progress.Quest;
            var definition = quests.GetQuest(state.ActiveQuestId);
            if (definition.Kind != QuestKind.RescueAdventurers || definition.DungeonId != DungeonId
                || state.IsCompleted(definition.Id) || state.ProgressCount >= definition.RequiredCount)
            {
                return;
            }
            _rescueNode = quests.GetRescueNode(definition.Id);
            var nodes = new List<DungeonNodeDefinition>(_instanceFloors[_rescueNode.Floor]);
            for (int index = 0; index < nodes.Count; index++)
            {
                var source = nodes[index];
                if (source.Id != _rescueNode.AnchorId)
                {
                    continue;
                }
                var neighbors = new List<string>(source.NeighborIds) { _rescueNode.Id };
                var anchor = CopyNode(source, neighbors);
                nodes[index] = anchor;
                _instanceNodes[_rescueNode.Floor + ":" + anchor.Id] = anchor;
                break;
            }
            var rescue = new DungeonNodeDefinition(_rescueNode.Floor, _rescueNode.Id, DungeonNodeType.Rescue,
                _rescueNode.Name, _rescueNode.Description, _rescueNode.X, _rescueNode.Y,
                new List<string> { _rescueNode.AnchorId }, string.Empty, _rescueNode.Icon);
            nodes.Add(rescue);
            _instanceNodes.Add(rescue.Floor + ":" + rescue.Id, rescue);
            _instanceFloors[rescue.Floor] = nodes.AsReadOnly();
        }

        private static DungeonNodeDefinition CopyNode(DungeonNodeDefinition source, List<string> neighbors)
        {
            return new DungeonNodeDefinition(source.Floor, source.Id, source.Type, source.Name, source.Description,
                source.X, source.Y, neighbors, source.CharacterId, source.Icon);
        }

        private void QueueMonsterKilled(string enemyId)
        {
            QueueQuestFact(QuestFactType.KillMonster, "gray_rat", "kill:" + Context + ":" + enemyId);
        }

        private void CompleteQuestBattle()
        {
            if (CurrentNode.Type == DungeonNodeType.Rescue && _rescueNode != null)
            {
                QueueQuestFact(QuestFactType.AdventurersRescued, _rescueNode.Id, "rescue:" + Context);
                ShowDetails(_quests.Text("rescue.success_title"), _quests.Text("rescue.success_body"));
            }
            else if (IsCleared)
            {
                QueueQuestFact(QuestFactType.DungeonCleared, string.Empty, "clear:" + Context);
            }
        }

        // 사건 발생 당시의 수락 ID를 고정한다. 이후 수락한 의뢰로 과거 사건을 이전하지 않는다.
        private void QueueQuestFact(QuestFactType type, string targetId, string uniqueKey)
        {
            if (_quests == null || !_recordedQuestFacts.Add(uniqueKey))
            {
                return;
            }
            _questFacts.Enqueue(new QuestFact(type, Guid.NewGuid().ToString("N"), DungeonId, targetId, 1,
                _questProgress.Quest.AcceptanceId, Floor));
        }

        // MainFlow의 저장 후속 단계만 확인 응답한다. 다른 사건의 ID로 큐를 건너뛰지 않는다.
        internal void CompleteQuestFact(string id)
        {
            if (PendingQuestFact != null && PendingQuestFact.Id == id)
            {
                _questFacts.Dequeue();
            }
        }
    }
}

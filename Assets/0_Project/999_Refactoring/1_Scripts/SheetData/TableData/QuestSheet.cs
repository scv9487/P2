using System;
using System.Collections.Generic;
using System.Globalization;
using Util.Sheet;

namespace Refactor.Util
{
    public enum QuestKind
    {
        ClearDungeon,
        DeliverItems,
        RescueAdventurers,
        KillMonster
    }

    public sealed class QuestDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public QuestKind Kind { get; }
        public int DungeonId { get; }
        public int Floor { get; }
        public string TargetId { get; }
        public string TargetName { get; }
        public int RequiredCount { get; }
        public long RewardGold { get; }

        internal QuestDefinition(string[] row, QuestKind kind, int dungeonId, int floor, int requiredCount, long rewardGold, string targetName)
        {
            Id = row[0];
            Name = row[1];
            Description = row[2];
            Kind = kind;
            DungeonId = dungeonId;
            Floor = floor;
            TargetId = row[6];
            TargetName = targetName;
            RequiredCount = requiredCount;
            RewardGold = rewardGold;
        }
    }

    // 원본 던전 그래프에 포함하지 않는다. 수락한 탐험의 그래프 사본에만 추가한다.
    public sealed class QuestRescueNodeDefinition
    {
        public string QuestId { get; }
        public int DungeonId { get; }
        public int Floor { get; }
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Icon { get; }
        public float X { get; }
        public float Y { get; }
        public string AnchorId { get; }
        public int EnemyCount { get; }

        internal QuestRescueNodeDefinition(string[] row, QuestDefinition quest, int floor, float x, float y, int enemyCount)
        {
            QuestId = quest.Id;
            DungeonId = quest.DungeonId;
            Floor = floor;
            Id = row[2];
            Name = row[3];
            Description = row[4];
            Icon = row[5];
            X = x;
            Y = y;
            AnchorId = row[8];
            EnemyCount = enemyCount;
        }
    }

    public sealed class QuestSheet
    {
        private readonly Dictionary<string, QuestDefinition> _quests = new Dictionary<string, QuestDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, QuestRescueNodeDefinition> _rescueNodes = new Dictionary<string, QuestRescueNodeDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);
        public IReadOnlyList<QuestDefinition> Quests { get; }
        public IReadOnlyList<QuestRescueNodeDefinition> RescueNodes { get; }
        public LootSheet Items { get; }

        public QuestSheet(string definitions, string rescueNodes, string texts, DungeonSheet dungeons, LootSheet items)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            if (dungeons == null)
            {
                throw new ArgumentNullException(nameof(dungeons));
            }
            var list = new List<QuestDefinition>();
            foreach (var row in CsvTable.Read(definitions, "QuestDefinitionTable", "Id", "Name", "Description", "Kind", "DungeonId", "Floor", "TargetId", "RequiredCount", "RewardGold"))
            {
                Required(row[0]);
                Required(row[1]);
                Required(row[2]);
                if (!Enum.TryParse(row[3], false, out QuestKind kind) || !Enum.IsDefined(typeof(QuestKind), kind)
                    || row[3] != kind.ToString())
                {
                    throw new FormatException("의뢰 종류가 올바르지 않습니다: " + row[3]);
                }
                int dungeonId = Integer(row[4], 0, 9999);
                int floor = Integer(row[5], 0, 3);
                int count = Integer(row[7], 1, 1000000);
                if (!long.TryParse(row[8], NumberStyles.None, CultureInfo.InvariantCulture, out long reward) || reward < 0 || reward > 1000000000000L)
                {
                    throw new FormatException("의뢰 금화 보상 범위가 올바르지 않습니다.");
                }
                string targetName = string.Empty;
                switch (kind)
                {
                    case QuestKind.ClearDungeon:
                        dungeons.GetDungeon(dungeonId);
                        if (floor != 0 || row[6] != string.Empty)
                        {
                            throw new FormatException("던전 공략 의뢰는 던전 ID와 최종 보스 클리어 횟수를 사용합니다.");
                        }
                        break;
                    case QuestKind.DeliverItems:
                        targetName = items.GetItem(Required(row[6])).Name;
                        if (dungeonId != 0 || floor != 0)
                        {
                            throw new FormatException("물품 납부 의뢰에는 던전/층 조건을 지정할 수 없습니다.");
                        }
                        break;
                    case QuestKind.RescueAdventurers:
                        if (floor < 1 || floor > dungeons.GetDungeon(dungeonId).FloorCount)
                        {
                            throw new FormatException("구조 의뢰의 던전/층/횟수가 올바르지 않습니다.");
                        }
                        Required(row[6]);
                        break;
                    case QuestKind.KillMonster:
                        Required(row[6]);
                        if (items.GetDrops(row[6]).Count == 0 || floor != 0)
                        {
                            throw new FormatException("처치 의뢰의 몬스터 참조/층 조건이 올바르지 않습니다.");
                        }
                        if (dungeonId != 0)
                        {
                            dungeons.GetDungeon(dungeonId);
                        }
                        break;
                }
                var definition = new QuestDefinition(row, kind, dungeonId, floor, count, reward, targetName);
                if (!_quests.TryAdd(definition.Id, definition))
                {
                    throw new FormatException("의뢰 ID 중복: " + definition.Id);
                }
                list.Add(definition);
            }
            if (list.Count == 0)
            {
                throw new FormatException("의뢰 시트에 의뢰가 없습니다.");
            }
            Quests = list.AsReadOnly();
            var nodeList = new List<QuestRescueNodeDefinition>();
            foreach (var row in CsvTable.Read(rescueNodes, "QuestRescueNodeTable", "QuestId", "Floor", "Id", "Name", "Description", "Icon", "X", "Y", "AnchorId", "EnemyCount"))
            {
                var quest = GetQuest(Required(row[0]));
                int floor = Integer(row[1], 1, 3);
                foreach (int column in new[] { 2, 3, 4, 5, 8 })
                {
                    Required(row[column]);
                }
                if (quest.Kind != QuestKind.RescueAdventurers || floor != quest.Floor || row[2] != quest.TargetId)
                {
                    throw new FormatException("구조 노드와 의뢰 목표가 일치하지 않습니다.");
                }
                dungeons.GetNode(floor, row[8]);
                foreach (var original in dungeons.GetNodes(floor))
                {
                    if (original.Id == row[2])
                    {
                        throw new FormatException("구조 노드 ID가 원본 지도 노드와 중복됩니다: " + row[2]);
                    }
                }
                var node = new QuestRescueNodeDefinition(row, quest, floor, Coordinate(row[6]), Coordinate(row[7]), Integer(row[9], 1, 3));
                if (!_rescueNodes.TryAdd(quest.Id, node))
                {
                    throw new FormatException("구조 의뢰의 노드가 중복됩니다: " + quest.Id);
                }
                nodeList.Add(node);
            }
            foreach (var quest in list)
            {
                if (quest.Kind == QuestKind.RescueAdventurers && !_rescueNodes.ContainsKey(quest.Id))
                {
                    throw new FormatException("구조 의뢰의 노드 정의가 없습니다: " + quest.Id);
                }
            }
            RescueNodes = nodeList.AsReadOnly();
            foreach (var row in CsvTable.Read(texts, "QuestTextTable", "Key", "Value"))
            {
                if (!_texts.TryAdd(Required(row[0]), Required(row[1])))
                {
                    throw new FormatException("의뢰 문구 중복: " + row[0]);
                }
            }
            string[] requiredTexts =
            {
                "board.title", "board.active_format", "board.no_active", "board.choose", "board.kind_clear_dungeon",
                "board.kind_deliver_items", "board.kind_rescue_adventurers", "board.kind_kill_monster",
                "board.state_available", "board.state_active", "board.state_reportable", "board.state_completed", "board.state_other_active",
                "board.card_progress_format", "board.progress_format", "board.reward_format", "board.delivery_format", "board.delivery_notice",
                "board.accept", "board.complete", "board.deliver_complete", "board.close", "board.choose_hint", "board.accept_hint",
                "board.delivery_hint_format", "board.progress_hint", "board.report_hint", "board.completed_hint", "status.load_failed",
                "status.save_failed", "status.progress_save_failed", "status.invalid_request", "status.already_active",
                "status.already_completed", "status.not_ready", "status.accepted_format", "status.completed_format",
                "rescue.success_title", "rescue.success_body"
            };
            foreach (string key in requiredTexts)
            {
                Text(key);
            }
        }

        public QuestDefinition GetQuest(string id)
        {
            return id != null && _quests.TryGetValue(id, out var quest) ? quest : throw new KeyNotFoundException("의뢰를 찾을 수 없습니다: " + id);
        }

        public QuestRescueNodeDefinition GetRescueNode(string questId)
        {
            return questId != null && _rescueNodes.TryGetValue(questId, out var node) ? node : throw new KeyNotFoundException("구조 노드를 찾을 수 없습니다: " + questId);
        }

        public string Text(string key)
        {
            return _texts.TryGetValue(key, out string value) ? value : throw new KeyNotFoundException("의뢰 문구 누락: " + key);
        }

        private static string Required(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            {
                throw new FormatException("의뢰 시트의 필수 문자열이 비어 있거나 양끝에 공백이 있습니다.");
            }
            return value;
        }

        private static int Integer(string value, int min, int max)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < min || parsed > max)
            {
                throw new FormatException("의뢰 시트의 정수 범위가 올바르지 않습니다: " + value);
            }
            return parsed;
        }

        private static float Coordinate(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                || float.IsNaN(parsed) || float.IsInfinity(parsed) || parsed < 0 || parsed > 1)
            {
                throw new FormatException("구조 노드 좌표는 0~1 범위여야 합니다.");
            }
            return parsed;
        }
    }
}

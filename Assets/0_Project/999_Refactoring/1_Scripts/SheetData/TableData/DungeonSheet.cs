using System;
using System.Collections.Generic;
using System.Globalization;
using Util.Sheet;

namespace Refactor.Util
{
    public enum DungeonNodeType
    {
        Battle,
        Boss,
        Character,
        Rest,
        Event,
        Stairs,
        Rescue
    }

    public sealed class DungeonDefinition
    {
        public int Id { get; }
        public string Name { get; }
        public string Description { get; }
        public int FloorCount { get; }
        public int RatHp { get; }
        public int RatAttack { get; }
        public float RatSpeed { get; }

        internal DungeonDefinition(int id, string name, string description, int floors, int hp, int attack, float speed)
        {
            Id = id;
            Name = name;
            Description = description;
            FloorCount = floors;
            RatHp = hp;
            RatAttack = attack;
            RatSpeed = speed;
        }
    }

    public sealed class DungeonNodeDefinition
    {
        public int Floor { get; }
        public string Id { get; }
        public DungeonNodeType Type { get; }
        public string Name { get; }
        public string Description { get; }
        public float X { get; }
        public float Y { get; }
        public IReadOnlyList<string> NeighborIds { get; }
        public string CharacterId { get; }
        public string Icon { get; }

        internal DungeonNodeDefinition(int floor, string id, DungeonNodeType type, string name, string description,
            float x, float y, List<string> neighbors, string characterId, string icon = null)
        {
            Floor = floor;
            Id = id;
            Type = type;
            Name = name;
            Description = description;
            X = x;
            Y = y;
            NeighborIds = neighbors.AsReadOnly();
            CharacterId = characterId;
            Icon = icon;
        }
    }

    public sealed class DungeonPartyDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public int MaxHp { get; }
        public int Attack { get; }
        public float Speed { get; }
        public int PortraitIndex { get; }
        public bool IsPlayerControlled { get; }

        internal DungeonPartyDefinition(string id, string name, int hp, int attack, float speed, int portraitIndex, bool isPlayerControlled)
        {
            Id = id;
            Name = name;
            MaxHp = hp;
            Attack = attack;
            Speed = speed;
            PortraitIndex = portraitIndex;
            IsPlayerControlled = isPlayerControlled;
        }
    }

    public sealed class DungeonSettings
    {
        public int NormalEnemyCount { get; }
        public int BossEnemyCount { get; }
        public float BossHpMultiplier { get; }
        public float BossAttackMultiplier { get; }
        public float BossSpeedMultiplier { get; }
        public int FloorHpBonus { get; }
        public int FloorAttackBonus { get; }
        public float FloorSpeedBonus { get; }
        public int RestHealAmount { get; }
        public int EventHealAmount { get; }
        public bool WaitOnReady { get; }

        internal DungeonSettings(Dictionary<string, float> values)
        {
            NormalEnemyCount = (int)Value(values, "NormalEnemyCount", 1, 3, true);
            BossEnemyCount = (int)Value(values, "BossEnemyCount", 1, 3, true);
            BossHpMultiplier = Value(values, "BossHpMultiplier", 1, 10);
            BossAttackMultiplier = Value(values, "BossAttackMultiplier", 1, 10);
            BossSpeedMultiplier = Value(values, "BossSpeedMultiplier", 0.1f, 10);
            FloorHpBonus = (int)Value(values, "FloorHpBonus", 0, 10000, true);
            FloorAttackBonus = (int)Value(values, "FloorAttackBonus", 0, 10000, true);
            FloorSpeedBonus = Value(values, "FloorSpeedBonus", 0, 10);
            RestHealAmount = (int)Value(values, "RestHealAmount", 1, 100000, true);
            EventHealAmount = (int)Value(values, "EventHealAmount", 1, 100000, true);
            WaitOnReady = Value(values, "WaitOnReady", 0, 1, true) == 1;
        }

        private static float Value(Dictionary<string, float> values, string key, float min, float max, bool integer = false)
        {
            if (!values.TryGetValue(key, out float value) || value < min || value > max || (integer && value != Math.Truncate(value)))
            {
                throw new FormatException($"DungeonSettingTable.csv: {key} 값은 {min}~{max} 범위여야 합니다.");
            }
            return value;
        }
    }

    // 공통 세 층 그래프를 여섯 던전이 사용하며 적 능력은 던전 정의와 층 보정으로 결정한다.
    public sealed class DungeonSheet
    {
        private readonly Dictionary<int, DungeonDefinition> _dungeons = new();
        private readonly Dictionary<int, IReadOnlyList<DungeonNodeDefinition>> _floors = new();
        private readonly Dictionary<string, DungeonNodeDefinition> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
        private readonly TownSheet _town;
        public IReadOnlyList<DungeonDefinition> Dungeons { get; }
        public IReadOnlyList<DungeonPartyDefinition> Party { get; }
        public DungeonSettings Settings { get; }

        public DungeonSheet(string definitions, string nodes, string party, string settings, string texts, TownSheet town)
        {
            _town = town ?? throw new ArgumentNullException(nameof(town));
            var dungeonList = new List<DungeonDefinition>();
            foreach (var row in CsvTable.Read(definitions, "DungeonDefinitionTable.csv", "Id", "Name", "Description", "FloorCount", "RatHp", "RatAttack", "RatSpeed"))
            {
                int id = Integer(row[0], 1, 9999);
                var definition = new DungeonDefinition(id, Required(row[1]), Required(row[2]), Integer(row[3], 1, 3),
                    Integer(row[4], 1, 100000), Integer(row[5], 1, 100000), Number(row[6], 0.01f, 10));
                if (!_dungeons.TryAdd(id, definition))
                {
                    throw new FormatException("던전 ID 중복: " + id);
                }
                dungeonList.Add(definition);
            }
            if (dungeonList.Count == 0)
            {
                throw new FormatException("던전 정의가 없습니다.");
            }
            Dungeons = dungeonList.AsReadOnly();
            var partyList = new List<DungeonPartyDefinition>();
            var partyIds = new HashSet<string>(StringComparer.Ordinal);
            int playerCount = 0;
            foreach (var row in CsvTable.Read(party, "DungeonPartyTable.csv", "Id", "Name", "MaxHp", "Attack", "Speed", "PortraitIndex", "IsPlayer"))
            {
                string id = Key(row[0]);
                if (!partyIds.Add(id))
                {
                    throw new FormatException("동료 ID 중복: " + id);
                }
                bool isPlayer = Integer(row[6], 0, 1) == 1;
                playerCount += isPlayer ? 1 : 0;
                partyList.Add(new DungeonPartyDefinition(id, Required(row[1]), Integer(row[2], 1, 100000),
                    Integer(row[3], 1, 100000), Number(row[4], 0.01f, 10), Integer(row[5], 0, 1000), isPlayer));
            }
            if (partyList.Count != 2)
            {
                throw new FormatException("이번 던전 단계의 파티는 두 명이어야 합니다.");
            }
            if (playerCount != 1)
            {
                throw new FormatException("던전 파티에는 IsPlayer=1인 플레이어가 정확히 한 명이어야 합니다.");
            }
            Party = partyList.AsReadOnly();
            var settingValues = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var row in CsvTable.Read(settings, "DungeonSettingTable.csv", "Key", "Value", "Description"))
            {
                if (!settingValues.TryAdd(Key(row[0]), Number(row[1], 0, 100000)))
                {
                    throw new FormatException("던전 설정 키 중복: " + row[0]);
                }
            }
            Settings = new DungeonSettings(settingValues);
            foreach (var row in CsvTable.Read(texts, "DungeonTextTable.csv", "Key", "Value"))
            {
                if (!_texts.TryAdd(Key(row[0]), Required(row[1])))
                {
                    throw new FormatException("던전 문구 키 중복: " + row[0]);
                }
            }
            var floorLists = new Dictionary<int, List<DungeonNodeDefinition>>();
            foreach (var row in CsvTable.Read(nodes, "DungeonNodeTable.csv", "Floor", "Id", "Type", "Name", "Description", "X", "Y", "NeighborIds", "CharacterId"))
            {
                int floor = Integer(row[0], 1, 3);
                string id = Key(row[1]);
                if (!Enum.TryParse(row[2], false, out DungeonNodeType type) || !Enum.IsDefined(typeof(DungeonNodeType), type))
                {
                    throw new FormatException("지원하지 않는 노드 종류: " + row[2]);
                }
                if (type == DungeonNodeType.Rescue)
                {
                    throw new FormatException("구조 노드는 수락한 의뢰의 탐험 인스턴스에만 추가할 수 있습니다.");
                }
                var neighbors = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string neighbor in Required(row[7]).Split(';'))
                {
                    string key = Key(neighbor);
                    if (key == id || !seen.Add(key))
                    {
                        throw new FormatException("노드의 자기 연결 또는 중복 연결: " + id);
                    }
                    neighbors.Add(key);
                }
                if (type == DungeonNodeType.Character)
                {
                    _town.GetCharacter(Key(row[8]));
                }
                else if (!string.IsNullOrEmpty(row[8]))
                {
                    throw new FormatException("인물 노드가 아닌 곳에 인물을 지정했습니다: " + id);
                }
                var node = new DungeonNodeDefinition(floor, id, type, Required(row[3]), Required(row[4]),
                    Number(row[5], 0, 1), Number(row[6], 0, 1), neighbors, row[8]);
                if (!_nodes.TryAdd(floor + ":" + id, node))
                {
                    throw new FormatException("층 안에서 중복된 노드: " + id);
                }
                if (!floorLists.TryGetValue(floor, out var list))
                {
                    list = new List<DungeonNodeDefinition>();
                    floorLists.Add(floor, list);
                }
                list.Add(node);
            }
            foreach (var pair in floorLists)
            {
                _floors.Add(pair.Key, pair.Value.AsReadOnly());
                ValidateFloor(pair.Value);
            }
            foreach (var dungeon in dungeonList)
            {
                for (int floor = 1; floor <= dungeon.FloorCount; floor++)
                {
                    bool boss = false;
                    bool stairs = false;
                    foreach (var node in GetNodes(floor))
                    {
                        boss |= node.Type == DungeonNodeType.Boss;
                        stairs |= node.Type == DungeonNodeType.Stairs;
                    }
                    if (floor == dungeon.FloorCount ? !boss || stairs : boss || !stairs)
                    {
                        throw new FormatException("보스는 마지막 층에만, 계단은 이전 층에만 있어야 합니다: " + dungeon.Id);
                    }
                }
            }
            foreach (string key in RequiredTexts)
            {
                Text(key);
            }
        }

        public DungeonDefinition GetDungeon(int id)
        {
            return _dungeons.TryGetValue(id, out var value) ? value : throw new FormatException("던전 ID가 없습니다: " + id);
        }

        public IReadOnlyList<DungeonNodeDefinition> GetNodes(int floor)
        {
            return _floors.TryGetValue(floor, out var value) ? value : throw new FormatException("던전 층이 없습니다: " + floor);
        }

        public DungeonNodeDefinition GetNode(int floor, string id)
        {
            return _nodes.TryGetValue(floor + ":" + id, out var value) ? value : throw new FormatException("던전 노드가 없습니다: " + id);
        }

        public TownCharacterDefinition GetCharacter(string id) => _town.GetCharacter(id);

        public string Text(string key)
        {
            return key != null && _texts.TryGetValue(key, out var value) ? value : throw new FormatException("던전 문구가 없습니다: " + key);
        }

        private void ValidateFloor(List<DungeonNodeDefinition> nodes)
        {
            if (nodes.Count == 0 || nodes[0].Type != DungeonNodeType.Rest)
            {
                throw new FormatException("각 층의 첫 노드는 휴식 지점이어야 합니다.");
            }
            foreach (var node in nodes)
            {
                foreach (string neighbor in node.NeighborIds)
                {
                    var destination = GetNode(node.Floor, neighbor);
                    bool back = false;
                    foreach (string other in destination.NeighborIds)
                    {
                        back |= other == node.Id;
                    }
                    if (!back)
                    {
                        throw new FormatException("노드 연결은 양방향이어야 합니다: " + node.Id + " / " + neighbor);
                    }
                }
            }
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>();
            pending.Enqueue(nodes[0].Id);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!visited.Add(id))
                {
                    continue;
                }
                foreach (string neighbor in GetNode(nodes[0].Floor, id).NeighborIds)
                {
                    pending.Enqueue(neighbor);
                }
            }
            if (visited.Count != nodes.Count)
            {
                throw new FormatException("시작 지점에서 도달할 수 없는 노드가 있습니다.");
            }
        }

        private static string Required(string value)
        {
            return !string.IsNullOrWhiteSpace(value) ? value : throw new FormatException("던전 시트의 필수 값이 비어 있습니다.");
        }

        private static string Key(string value)
        {
            Required(value);
            if (value != value.Trim() || value.IndexOf(':') >= 0 || value.IndexOf(';') >= 0)
            {
                throw new FormatException("던전 시트 ID 형식이 올바르지 않습니다: " + value);
            }
            return value;
        }

        private static int Integer(string value, int min, int max)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < min || parsed > max)
            {
                throw new FormatException($"던전 시트 정수는 {min}~{max} 범위여야 합니다: {value}");
            }
            return parsed;
        }

        private static float Number(string value, float min, float max)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                || float.IsNaN(parsed) || float.IsInfinity(parsed) || parsed < min || parsed > max)
            {
                throw new FormatException($"던전 시트 숫자는 {min}~{max} 범위여야 합니다: {value}");
            }
            return parsed;
        }

        private static readonly string[] RequiredTexts =
        {
            "selector.title", "selector.description", "selector.confirm", "selector.cancel", "selector.floor_format", "selector.card_title_format",
            "map.floor_format", "map.title", "map.hint", "map.legend", "map.current_format", "map.return_town", "node.commands", "node.visitors",
            "command.rest", "command.talk", "command.next_floor", "command.close", "command.attack",
            "battle.title", "battle.enemies", "battle.allies", "battle.ready_format", "battle.waiting", "battle.target_hint",
            "battle.hp_format", "battle.gauge_format", "battle.speed_format", "battle.defeated", "battle.cleared",
            "battle.player_name_format", "battle.companion_name_format",
            "icon.battle", "icon.boss", "icon.character", "icon.rest", "icon.event", "icon.stairs",
            "enemy.rat", "enemy.boss", "enemy.boss_name_format", "status.enter_format", "status.move_format", "battle.start", "battle.attack_format",
            "battle.victory", "battle.victory_body", "battle.defeated_body", "battle.cleared_body", "rest.body_format",
            "event.body_format", "dialogue.title_format", "battle.damage_format", "status.rest", "status.event",
            "battle.miss_format", "battle.guard_format", "battle.critical_format"
        };
    }
}

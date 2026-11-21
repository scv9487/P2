using System;
using System.Collections.Generic;
using System.Globalization;
using Util.Sheet;

namespace Refactor.Util
{
    public sealed class TownLocationDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Subtitle { get; }
        public string Description { get; }
        public IReadOnlyList<string> CommandIds { get; }
        public IReadOnlyList<string> VisitorIds { get; }
        public int ArtIndex { get; }

        internal TownLocationDefinition(string id, string name, string subtitle, string description,
            List<string> commandIds, List<string> visitorIds, int artIndex)
        {
            Id = id;
            Name = name;
            Subtitle = subtitle;
            Description = description;
            CommandIds = commandIds.AsReadOnly();
            VisitorIds = visitorIds.AsReadOnly();
            ArtIndex = artIndex;
        }
    }

    public sealed class TownCommandDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Title { get; }
        public string Body { get; }

        internal TownCommandDefinition(string id, string name, string title, string body)
        {
            Id = id;
            Name = name;
            Title = title;
            Body = body;
        }
    }

    public sealed class TownCharacterDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Role { get; }
        public int PortraitIndex { get; }
        public string Greeting { get; }
        public string Dialogue { get; }

        internal TownCharacterDefinition(string id, string name, string role, int portraitIndex, string greeting, string dialogue)
        {
            Id = id;
            Name = name;
            Role = role;
            PortraitIndex = portraitIndex;
            Greeting = greeting;
            Dialogue = dialogue;
        }
    }

    // 시트는 불변 콘텐츠다. 현재 방문 장소/선택 대화 같은 실행 상태는 TownDataContainer가 소유한다.
    public sealed class TownSheet
    {
        private static readonly HashSet<string> SupportedCommands = new(StringComparer.Ordinal)
        {
            "trade", "rumor", "leave", "rest", "quest_accept", "quest_complete", "enter_dungeon"
        };
        private static readonly string[] RequiredTextKeys =
        {
            "title.name", "title.tagline", "title.start", "title.quit", "title.footer", "town.name", "town.subtitle",
            "town.description", "town.choose", "town.return_title", "location.visitors", "location.commands",
            "location.close", "panel.close", "dialogue.title", "dialogue.reply", "empty.visitors",
            "status.loading", "status.failed"
        };
        private readonly Dictionary<string, TownLocationDefinition> _locations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TownCommandDefinition> _commands = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TownCharacterDefinition> _characters = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);

        public IReadOnlyList<TownLocationDefinition> Locations { get; }
        public IReadOnlyList<TownCharacterDefinition> Characters { get; }

        public TownSheet(string locations, string commands, string characters, string text)
        {
            foreach (var row in CsvTable.Read(commands, "TownCommandTable.csv", "Id", "Name", "Title", "Body"))
            {
                string id = Key(row[0], "TownCommandTable.csv");
                if (!SupportedCommands.Contains(id))
                {
                    throw new FormatException("TownCommandTable.csv: 지원하지 않는 명령 " + id);
                }
                Add(_commands, id, new TownCommandDefinition(id, Content(row[1], id), Content(row[2], id), Content(row[3], id)));
            }
            var characterList = new List<TownCharacterDefinition>();
            foreach (var row in CsvTable.Read(characters, "TownCharacterTable.csv", "Id", "Name", "Role", "PortraitIndex", "Greeting", "Dialogue"))
            {
                string id = Key(row[0], "TownCharacterTable.csv");
                var character = new TownCharacterDefinition(id, Content(row[1], id), Content(row[2], id),
                    Index(row[3], id), Content(row[4], id), Content(row[5], id));
                Add(_characters, id, character);
                characterList.Add(character);
            }
            Characters = characterList.AsReadOnly();
            var locationList = new List<TownLocationDefinition>();
            foreach (var row in CsvTable.Read(locations, "TownLocationTable.csv", "Id", "Name", "Subtitle", "Description", "CommandIds", "VisitorIds", "ArtIndex"))
            {
                string id = Key(row[0], "TownLocationTable.csv");
                var commandIds = References(row[4], id);
                var visitorIds = References(row[5], id);
                foreach (string commandId in commandIds)
                {
                    if (!_commands.ContainsKey(commandId))
                    {
                        throw new FormatException($"TownLocationTable.csv: {id}의 명령 {commandId}가 없습니다.");
                    }
                }
                foreach (string visitorId in visitorIds)
                {
                    if (!_characters.ContainsKey(visitorId))
                    {
                        throw new FormatException($"TownLocationTable.csv: {id}의 방문 인물 {visitorId}가 없습니다.");
                    }
                }
                var location = new TownLocationDefinition(id, Content(row[1], id), Content(row[2], id),
                    Content(row[3], id), commandIds, visitorIds, Index(row[6], id));
                Add(_locations, id, location);
                locationList.Add(location);
            }
            if (locationList.Count == 0)
            {
                throw new FormatException("TownLocationTable.csv: 장소가 하나 이상 필요합니다.");
            }
            Locations = locationList.AsReadOnly();
            foreach (var row in CsvTable.Read(text, "TownTextTable.csv", "Key", "Value"))
            {
                string key = Key(row[0], "TownTextTable.csv");
                Add(_texts, key, Content(row[1], key));
            }
            foreach (string key in RequiredTextKeys)
            {
                Text(key);
            }
        }

        public TownLocationDefinition GetLocation(string id)
        {
            return Find(_locations, id, "장소");
        }

        public TownCommandDefinition GetCommand(string id)
        {
            return Find(_commands, id, "명령");
        }

        public TownCharacterDefinition GetCharacter(string id)
        {
            return Find(_characters, id, "인물");
        }

        public string Text(string key)
        {
            return Find(_texts, key, "화면 문자열");
        }

        private static T Find<T>(Dictionary<string, T> table, string key, string label)
        {
            if (key == null || !table.TryGetValue(key, out var value))
            {
                throw new FormatException($"마을 시트: {label} 키 '{key}'가 없습니다.");
            }
            return value;
        }

        private static void Add<T>(Dictionary<string, T> table, string key, T value)
        {
            if (!table.TryAdd(key, value))
            {
                throw new FormatException("마을 시트: 중복된 키 " + key);
            }
        }

        private static string Key(string value, string tableName)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            {
                throw new FormatException(tableName + ": 키는 앞뒤 공백이 없는 문자열이어야 합니다.");
            }
            foreach (char c in value)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '.' && c != '-')
                {
                    throw new FormatException(tableName + ": 키에 허용되지 않는 문자가 있습니다: " + value);
                }
            }
            return value;
        }

        private static string Content(string value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new FormatException("마을 시트: " + key + "의 표시 문자열이 비어 있습니다.");
            }
            return value;
        }

        private static int Index(string value, string key)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0)
            {
                throw new FormatException("마을 시트: " + key + "의 이미지 번호는 0 이상의 정수여야 합니다.");
            }
            return index;
        }

        private static List<string> References(string value, string owner)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(value))
            {
                return result;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string element in value.Split(';'))
            {
                string id = Key(element, owner);
                if (!seen.Add(id))
                {
                    throw new FormatException("마을 시트: " + owner + "에서 중복 참조한 키 " + id);
                }
                result.Add(id);
            }
            return result;
        }
    }
}

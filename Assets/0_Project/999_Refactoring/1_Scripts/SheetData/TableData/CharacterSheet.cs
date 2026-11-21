using System;
using System.Collections.Generic;
using System.Globalization;
using Refactor.Main;
using Util.Sheet;

namespace Refactor.Util
{
    public sealed class CharacterDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Role { get; }
        public string Gender { get; }
        public int Age { get; }
        public int PortraitIndex { get; }
        public bool IsPlayer { get; }
        public MajorStats BaseStats { get; }
        public int InitialAffinity { get; }
        public int LootWeight { get; }

        internal CharacterDefinition(string id, string name, string role, string gender, int age, int portraitIndex,
            bool isPlayer, MajorStats stats, int affinity, int lootWeight)
        {
            Id = id;
            Name = name;
            Role = role;
            Gender = gender;
            Age = age;
            PortraitIndex = portraitIndex;
            IsPlayer = isPlayer;
            BaseStats = stats;
            InitialAffinity = affinity;
            LootWeight = lootWeight;
        }
    }

    public sealed class CharacterSheet
    {
        private readonly Dictionary<string, CharacterDefinition> _characters = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);
        public IReadOnlyList<CharacterDefinition> Characters { get; }
        public string PlayerId { get; }
        public int MaxPartySize { get; }
        public int TalkAffinityGain { get; }
        public CharacterGrowthSheet Growth { get; }
        public CombatStatSheet Combat { get; }

        public CharacterSheet(string characters, string settings, string texts, TownSheet town, CharacterGrowthSheet growth, CombatStatSheet combat)
        {
            if (town == null)
            {
                throw new ArgumentNullException(nameof(town));
            }
            Growth = growth ?? throw new ArgumentNullException(nameof(growth));
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            foreach (var row in CsvTable.Read(texts, "CharacterTextTable", "Key", "Value"))
            {
                if (_texts.ContainsKey(row[0]) || string.IsNullOrWhiteSpace(row[0]) || string.IsNullOrWhiteSpace(row[1]))
                {
                    throw new FormatException("인물 문구가 중복되거나 비어 있습니다.");
                }
                _texts.Add(row[0], row[1]);
            }
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in CsvTable.Read(settings, "CharacterSettingTable", "Key", "Value"))
            {
                if (values.ContainsKey(row[0]))
                {
                    throw new FormatException("인물 설정 중복: " + row[0]);
                }
                values.Add(row[0], Integer(row[1], 0, 100, row[0]));
            }
            MaxPartySize = Setting(values, "MaxPartySize", 1, 4);
            TalkAffinityGain = Setting(values, "TalkAffinityGain", 1, 100);
            int heroPortrait = Setting(values, "HeroPortraitIndex", 0, 100);
            if (values.Count != 3)
            {
                throw new FormatException("인물 설정에 지원하지 않는 키가 있습니다.");
            }
            var list = new List<CharacterDefinition>();
            foreach (var row in CsvTable.Read(characters, "CharacterDefinitionTable", "Id", "IsPlayer", "Gender", "Age",
                "Strength", "Constitution", "WillPower", "Intelligence", "Agility", "Perception", "InitialAffinity", "LootWeight"))
            {
                string id = Required(row[0]);
                bool player = Integer(row[1], 0, 1, "IsPlayer") == 1;
                if (_characters.ContainsKey(id) || (player && PlayerId != null))
                {
                    throw new FormatException("인물 ID가 중복되거나 플레이어가 둘 이상입니다.");
                }
                TownCharacterDefinition npc = player ? null : town.GetCharacter(id);
                var stats = new MajorStats(Stat(row[4]), Stat(row[5]), Stat(row[6]), Stat(row[7]), Stat(row[8]), Stat(row[9]));
                var definition = new CharacterDefinition(id, player ? Text("hero.name") : npc.Name,
                    player ? Text("hero.role") : npc.Role, Required(row[2]), Integer(row[3], 1, 200, "Age"),
                    player ? heroPortrait : npc.PortraitIndex, player, stats,
                    Integer(row[10], 0, 100, "InitialAffinity"), Integer(row[11], 1, 100000, "LootWeight"));
                if (player)
                {
                    PlayerId = id;
                    if (definition.InitialAffinity != 0)
                    {
                        throw new FormatException("플레이어 자신의 친밀도는 0이어야 합니다.");
                    }
                }
                list.Add(definition);
                _characters.Add(id, definition);
            }
            if (PlayerId == null)
            {
                throw new FormatException("직접 조작하는 플레이어가 필요합니다.");
            }
            foreach (var npc in town.Characters)
            {
                if (!_characters.ContainsKey(npc.Id) || _characters[npc.Id].IsPlayer)
                {
                    throw new FormatException("마을 인물의 성장 정의가 없습니다: " + npc.Id);
                }
            }
            Characters = list.AsReadOnly();
            string[] requiredTexts =
            {
                "hero.name", "hero.role", "party.count_format", "party.empty", "party.player_name_format",
                "party.member_format", "party.dungeon_member_format", "profile.identity_format", "profile.progress_format",
                "profile.affinity_format", "profile.self", "profile.actions", "profile.dialogue", "action.talk", "action.recruit",
                "action.dismiss", "action.close", "action.interact", "stats.major", "stats.combat", "stats.strength",
                "stats.constitution", "stats.will_power", "stats.intelligence", "stats.agility", "stats.perception",
                "stats.value_format", "stats.percent_format", "stats.multiplier_format", "stats.diminishing_format",
                "status.hero", "status.recruit_hint", "status.party_hint", "status.load_failed", "status.save_failed",
                "status.recruit_failed", "status.recruited_format", "status.dismissed_format", "status.talked_format",
                "status.party_full", "status.invalid_request", "status.exp_save_failed", "status.exp_gained_format"
            };
            foreach (string key in requiredTexts)
            {
                Text(key);
            }
        }

        public CharacterDefinition GetCharacter(string id)
        {
            return id != null && _characters.TryGetValue(id, out var definition) ? definition
                : throw new KeyNotFoundException("인물을 찾을 수 없습니다: " + id);
        }

        public string Text(string key)
        {
            return _texts.TryGetValue(key, out string value) ? value : throw new KeyNotFoundException("인물 문구 누락: " + key);
        }

        private static string Required(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            {
                throw new FormatException("인물 시트에 비어 있거나 양끝 공백이 있는 값이 있습니다.");
            }
            return value;
        }

        private static int Integer(string value, int minimum, int maximum, string key)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < minimum || number > maximum)
            {
                throw new FormatException("인물 시트의 정수 범위 오류: " + key);
            }
            return number;
        }

        private static int Setting(Dictionary<string, int> values, string key, int minimum, int maximum)
        {
            if (!values.TryGetValue(key, out int number) || number < minimum || number > maximum)
            {
                throw new FormatException("인물 설정 누락 또는 범위 오류: " + key);
            }
            return number;
        }

        private static float Stat(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
                || float.IsNaN(number) || float.IsInfinity(number) || number < 1 || number > 1000000)
            {
                throw new FormatException("인물 주요 능력치는 1~1000000 범위여야 합니다.");
            }
            return number;
        }
    }
}

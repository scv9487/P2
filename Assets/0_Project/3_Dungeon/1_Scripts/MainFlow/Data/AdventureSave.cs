using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using Refactor.Main;
using Refactor.Util;

namespace Refactor
{
    [Serializable]
    public sealed class AdventureSaveData
    {
        public string Format;
        public int Version;
        // 같은 순서의 ID/값 배열로 누락된 숫자 필드와 유효한 0을 구분한다.
        public string[] MemberIds;
        public long[] MemberGold;
        public string[] ItemIds;
        public int[] ItemCounts;
        public string[] SettlementIds;
        public string[] MerchantIds = Array.Empty<string>();
        public string[] StockMerchantIds = Array.Empty<string>();
        public string[] StockItemIds = Array.Empty<string>();
        public int[] StockCounts = Array.Empty<int>();
        public string[] TradeIds = Array.Empty<string>();
        public string[] CharacterIds = Array.Empty<string>();
        public int[] CharacterLevels = Array.Empty<int>();
        public float[] CharacterExperience = Array.Empty<float>();
        public int[] CharacterAffinity = Array.Empty<int>();
        public float[] CharacterStrengthGrowth = Array.Empty<float>();
        public float[] CharacterConstitutionGrowth = Array.Empty<float>();
        public float[] CharacterWillPowerGrowth = Array.Empty<float>();
        public float[] CharacterIntelligenceGrowth = Array.Empty<float>();
        public float[] CharacterAgilityGrowth = Array.Empty<float>();
        public float[] CharacterPerceptionGrowth = Array.Empty<float>();
        public string[] PartyIds = Array.Empty<string>();
        public string[] CharacterActionIds = Array.Empty<string>();
        public string[] ActiveQuestIds = Array.Empty<string>();
        public string[] QuestAcceptanceIds = Array.Empty<string>();
        public int[] QuestProgressCounts = Array.Empty<int>();
        public string[] CompletedQuestIds = Array.Empty<string>();
        public string[] QuestActionIds = Array.Empty<string>();
        public string[] SkillCharacterIds = Array.Empty<string>();
        public int[] CharacterMainSkillPoints = Array.Empty<int>();
        public int[] CharacterSubSkillPoints = Array.Empty<int>();
        public int[] CharacterBasicAttackKills = Array.Empty<int>();
        public string[] LearnedSkillCharacterIds = Array.Empty<string>();
        public string[] LearnedSkillIds = Array.Empty<string>();
        public string[] SkillActionIds = Array.Empty<string>();
        public string[] EquipmentCharacterIds = Array.Empty<string>();
        public string[] EquippedItemIds = Array.Empty<string>();
        public string[] ItemActionIds = Array.Empty<string>();
    }

    public static partial class AdventureSave
    {
        public const string CurrentFormat = "P2.Adventure";
        public const int CurrentVersion = 6;
        // 저장 형식의 유한 범위다. 조정 가능한 현재 레벨별 요구 EXP와 구분한다.
        public const float MaximumStoredExperience = 1000000000000f;

        public static void Save(string path, AdventureSaveData dto, LootSheet sheet, ShopSheet shops = null, CharacterSheet characters = null, QuestSheet quests = null, SkillSheet skills = null, EquipmentSheet equipment = null)
        {
            if (dto == null || dto.Version != CurrentVersion || !IsValid(dto, sheet, shops, characters, quests, skills, equipment))
            {
                throw new ArgumentException("모험 저장 데이터가 올바르지 않습니다.", nameof(dto));
            }
            SaveLoadHelper.SaveJson(path, dto);
        }

        public static AdventureLoadResult Load(string path, LootSheet sheet, out AdventureSaveData dto, ShopSheet shops = null, CharacterSheet characters = null, QuestSheet quests = null, SkillSheet skills = null, EquipmentSheet equipment = null)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }
            dto = null;
            try
            {
                // 빈 배열은 유효하지만 필드 누락은 유효하지 않다. Overwrite가 생략된
                // 배열을 유지하도록 각 배열에 검증을 통과할 수 없는 초기값을 둔다.
                var parsed = new AdventureSaveData
                {
                    Version = int.MinValue,
                    MemberIds = new[] { string.Empty },
                    MemberGold = new long[] { -1 },
                    ItemIds = new[] { string.Empty },
                    ItemCounts = new[] { -1 },
                    SettlementIds = new[] { string.Empty },
                    MerchantIds = new[] { string.Empty },
                    StockMerchantIds = new[] { string.Empty },
                    StockItemIds = new[] { string.Empty },
                    StockCounts = new[] { -1 },
                    TradeIds = new[] { string.Empty },
                    CharacterIds = new[] { string.Empty },
                    CharacterLevels = new[] { -1 },
                    CharacterExperience = new float[] { -1 },
                    CharacterAffinity = new[] { -1 },
                    CharacterStrengthGrowth = new float[] { -1 },
                    CharacterConstitutionGrowth = new float[] { -1 },
                    CharacterWillPowerGrowth = new float[] { -1 },
                    CharacterIntelligenceGrowth = new float[] { -1 },
                    CharacterAgilityGrowth = new float[] { -1 },
                    CharacterPerceptionGrowth = new float[] { -1 },
                    PartyIds = new[] { string.Empty },
                    CharacterActionIds = new[] { string.Empty },
                    ActiveQuestIds = new[] { string.Empty },
                    QuestAcceptanceIds = new[] { string.Empty },
                    QuestProgressCounts = new[] { -1 },
                    CompletedQuestIds = new[] { string.Empty },
                    QuestActionIds = new[] { string.Empty },
                    SkillCharacterIds = new[] { string.Empty },
                    CharacterMainSkillPoints = new[] { -1 },
                    CharacterSubSkillPoints = new[] { -1 },
                    CharacterBasicAttackKills = new[] { -1 },
                    LearnedSkillCharacterIds = new[] { string.Empty },
                    LearnedSkillIds = new[] { string.Empty },
                    SkillActionIds = new[] { string.Empty },
                    EquipmentCharacterIds = new[] { string.Empty },
                    EquippedItemIds = new[] { string.Empty },
                    ItemActionIds = new[] { string.Empty }
                };
                var validFields = new bool[CurrentVersion + 1];
                SaveLoadHelper.LoadJsonInto(path, parsed, json =>
                {
                    for (int version = 1; version <= CurrentVersion; version++)
                    {
                        validFields[version] = HasRequiredArrayFields(json, version);
                    }
                });
                if (parsed.Format != CurrentFormat || parsed.Version < 1)
                {
                    return AdventureLoadResult.Corrupt;
                }
                if (parsed.Version > CurrentVersion)
                {
                    return AdventureLoadResult.UnsupportedVersion;
                }
                if (!validFields[parsed.Version] || !IsValid(parsed, sheet, shops, characters, quests, skills, equipment))
                {
                    return AdventureLoadResult.Corrupt;
                }
                if (parsed.Version == 1)
                {
                    // 파일은 변경하지 않는다. 상인 초기 재고는 DataContainer가 시트에서 복사한다.
                    parsed.MerchantIds = Array.Empty<string>();
                    parsed.StockMerchantIds = Array.Empty<string>();
                    parsed.StockItemIds = Array.Empty<string>();
                    parsed.StockCounts = Array.Empty<int>();
                    parsed.TradeIds = Array.Empty<string>();
                }
                if (parsed.Version < 3)
                {
                    parsed.CharacterIds = Array.Empty<string>();
                    parsed.CharacterLevels = Array.Empty<int>();
                    parsed.CharacterExperience = Array.Empty<float>();
                    parsed.CharacterAffinity = Array.Empty<int>();
                    parsed.CharacterStrengthGrowth = Array.Empty<float>();
                    parsed.CharacterConstitutionGrowth = Array.Empty<float>();
                    parsed.CharacterWillPowerGrowth = Array.Empty<float>();
                    parsed.CharacterIntelligenceGrowth = Array.Empty<float>();
                    parsed.CharacterAgilityGrowth = Array.Empty<float>();
                    parsed.CharacterPerceptionGrowth = Array.Empty<float>();
                    parsed.PartyIds = Array.Empty<string>();
                    parsed.CharacterActionIds = Array.Empty<string>();
                }
                if (parsed.Version < 4)
                {
                    parsed.ActiveQuestIds = Array.Empty<string>();
                    parsed.QuestAcceptanceIds = Array.Empty<string>();
                    parsed.QuestProgressCounts = Array.Empty<int>();
                    parsed.CompletedQuestIds = Array.Empty<string>();
                    parsed.QuestActionIds = Array.Empty<string>();
                }
                if (parsed.Version < 5)
                {
                    parsed.SkillCharacterIds = Array.Empty<string>();
                    parsed.CharacterMainSkillPoints = Array.Empty<int>();
                    parsed.CharacterSubSkillPoints = Array.Empty<int>();
                    parsed.CharacterBasicAttackKills = Array.Empty<int>();
                    parsed.LearnedSkillCharacterIds = Array.Empty<string>();
                    parsed.LearnedSkillIds = Array.Empty<string>();
                    parsed.SkillActionIds = Array.Empty<string>();
                }
                if (parsed.Version < 6)
                {
                    parsed.EquipmentCharacterIds = Array.Empty<string>();
                    parsed.EquippedItemIds = Array.Empty<string>();
                    parsed.ItemActionIds = Array.Empty<string>();
                }
                parsed.Version = CurrentVersion;
                dto = parsed;
                return AdventureLoadResult.Loaded;
            }
            catch (FileNotFoundException)
            {
                return AdventureLoadResult.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return AdventureLoadResult.Missing;
            }
            catch (ArgumentException)
            {
                return AdventureLoadResult.Corrupt;
            }
            catch (FormatException)
            {
                return AdventureLoadResult.Corrupt;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is SecurityException || exception is NotSupportedException)
            {
                return AdventureLoadResult.IOError;
            }
        }

        internal static bool IsValidSettlementId(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && id == id.Trim() && id.Length <= 128;
        }

        // Unity는 명시적 null 배열도 []로 바꿀 수 있다. 여기서는 최상위 필수
        // 배열의 존재/타입/중복만 확인한다. JSON 문법과 값의 파싱은 JsonUtility가 맡는다.
        private static bool HasRequiredArrayFields(string json, int version)
        {
            var required = new HashSet<string>(StringComparer.Ordinal)
            {
                "MemberIds", "MemberGold", "ItemIds", "ItemCounts", "SettlementIds"
            };
            if (version >= 2)
            {
                required.UnionWith(new[] { "MerchantIds", "StockMerchantIds", "StockItemIds", "StockCounts", "TradeIds" });
            }
            if (version >= 3)
            {
                required.UnionWith(new[]
                {
                    "CharacterIds", "CharacterLevels", "CharacterExperience", "CharacterAffinity", "CharacterStrengthGrowth",
                    "CharacterConstitutionGrowth", "CharacterWillPowerGrowth", "CharacterIntelligenceGrowth", "CharacterAgilityGrowth", "CharacterPerceptionGrowth",
                    "PartyIds", "CharacterActionIds"
                });
            }
            if (version >= 4)
            {
                required.UnionWith(new[] { "ActiveQuestIds", "QuestAcceptanceIds", "QuestProgressCounts", "CompletedQuestIds", "QuestActionIds" });
            }
            if (version >= 5)
            {
                required.UnionWith(new[] { "SkillCharacterIds", "CharacterMainSkillPoints", "CharacterSubSkillPoints", "CharacterBasicAttackKills",
                    "LearnedSkillCharacterIds", "LearnedSkillIds", "SkillActionIds" });
            }
            if (version >= 6)
            {
                required.UnionWith(new[] { "EquipmentCharacterIds", "EquippedItemIds", "ItemActionIds" });
            }
            var found = new HashSet<string>(StringComparer.Ordinal);
            int depth = 0;
            bool started = false;
            for (int index = 0; index < json.Length; index++)
            {
                char current = json[index];
                if (char.IsWhiteSpace(current))
                {
                    continue;
                }
                if (!started)
                {
                    if (current != '{')
                    {
                        return false;
                    }
                    started = true;
                }
                if (current == '"')
                {
                    string field = ReadQuotedText(json, ref index);
                    if (depth != 1 || !required.Contains(field))
                    {
                        continue;
                    }
                    int next = index + 1;
                    while (next < json.Length && char.IsWhiteSpace(json[next]))
                    {
                        next++;
                    }
                    if (next >= json.Length || json[next] != ':')
                    {
                        continue;
                    }
                    next++;
                    while (next < json.Length && char.IsWhiteSpace(json[next]))
                    {
                        next++;
                    }
                    if (next >= json.Length || json[next] != '[' || !found.Add(field))
                    {
                        return false;
                    }
                }
                else if (current == '{' || current == '[')
                {
                    depth++;
                }
                else if (current == '}' || current == ']')
                {
                    depth--;
                }
            }
            return started && depth == 0 && found.Count == required.Count;
        }

        // 문자열 내부의 괄호/콜론/따옴표는 필드로 세지 않으며 Unicode escape 키도 구분한다.
        private static string ReadQuotedText(string json, ref int index)
        {
            var text = new StringBuilder();
            while (++index < json.Length)
            {
                char current = json[index];
                if (current == '"')
                {
                    return text.ToString();
                }
                if (current != '\\')
                {
                    text.Append(current);
                    continue;
                }
                if (++index >= json.Length)
                {
                    break;
                }
                char escaped = json[index];
                if (escaped == 'u')
                {
                    if (index + 4 >= json.Length || !int.TryParse(json.Substring(index + 1, 4),
                        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
                    {
                        throw new FormatException("저장 JSON의 Unicode escape가 올바르지 않습니다.");
                    }
                    text.Append((char)value);
                    index += 4;
                }
                else
                {
                    switch (escaped)
                    {
                        case '"': case '\\': case '/': text.Append(escaped); break;
                        case 'b': text.Append('\b'); break;
                        case 'f': text.Append('\f'); break;
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        default: throw new FormatException("저장 JSON의 escape가 올바르지 않습니다.");
                    }
                }
            }
            throw new FormatException("저장 JSON 문자열이 닫히지 않았습니다.");
        }

        private static bool IsValid(AdventureSaveData dto, LootSheet sheet, ShopSheet shops, CharacterSheet characters, QuestSheet quests, SkillSheet skills, EquipmentSheet equipment)
        {
            if (dto == null || sheet == null || dto.Format != CurrentFormat || dto.Version < 1 || dto.Version > CurrentVersion
                || dto.MemberIds == null || dto.MemberGold == null || dto.MemberIds.Length != dto.MemberGold.Length
                || dto.ItemIds == null || dto.ItemCounts == null
                || dto.ItemIds.Length != dto.ItemCounts.Length || dto.SettlementIds == null)
            {
                return false;
            }
            var expectedMembers = new HashSet<string>(StringComparer.Ordinal);
            if (dto.Version >= 3 && characters != null)
            {
                foreach (var character in characters.Characters)
                {
                    expectedMembers.Add(character.Id);
                }
            }
            else if (dto.Version >= 3 && dto.CharacterIds != null && dto.CharacterIds.Length > 0)
            {
                expectedMembers.UnionWith(dto.CharacterIds);
            }
            else
            {
                foreach (var member in sheet.Shares)
                {
                    expectedMembers.Add(member.MemberId);
                }
            }
            if (dto.MemberIds.Length != expectedMembers.Count || !expectedMembers.Contains(sheet.PlayerMemberId))
            {
                return false;
            }
            for (int i = 0; i < dto.MemberIds.Length; i++)
            {
                if (dto.MemberIds[i] == null || !expectedMembers.Remove(dto.MemberIds[i]) || dto.MemberGold[i] < 0)
                {
                    return false;
                }
            }
            var expectedItems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in sheet.Items)
            {
                expectedItems.Add(item.Id);
            }
            for (int i = 0; i < dto.ItemIds.Length; i++)
            {
                if (dto.ItemIds[i] == null || !expectedItems.Remove(dto.ItemIds[i]) || dto.ItemCounts[i] <= 0)
                {
                    return false;
                }
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in dto.SettlementIds)
            {
                if (!IsValidSettlementId(id) || !seen.Add(id))
                {
                    return false;
                }
            }
            return (dto.Version == 1 || HasValidShopData(dto, sheet, shops))
                && (dto.Version < 3 || HasValidCharacterData(dto, sheet, characters))
                && (dto.Version < 4 || HasValidQuestData(dto, quests))
                && (dto.Version < 5 || HasValidSkillData(dto, skills))
                && (dto.Version < 6 || HasValidEquipmentData(dto, sheet, equipment));
        }

        private static bool HasValidQuestData(AdventureSaveData dto, QuestSheet quests)
        {
            if (dto.ActiveQuestIds == null || dto.QuestAcceptanceIds == null || dto.QuestProgressCounts == null
                || dto.CompletedQuestIds == null || dto.QuestActionIds == null || dto.ActiveQuestIds.Length > 1
                || dto.ActiveQuestIds.Length != dto.QuestAcceptanceIds.Length || dto.ActiveQuestIds.Length != dto.QuestProgressCounts.Length)
            {
                return false;
            }
            var definitions = new Dictionary<string, QuestDefinition>(StringComparer.Ordinal);
            if (quests != null)
            {
                foreach (var definition in quests.Quests)
                {
                    definitions.Add(definition.Id, definition);
                }
            }
            var completed = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in dto.CompletedQuestIds)
            {
                if (!IsValidSettlementId(id) || !completed.Add(id) || (quests != null && !definitions.ContainsKey(id)))
                {
                    return false;
                }
            }
            var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in dto.QuestActionIds)
            {
                if (!IsValidSettlementId(id) || !actions.Add(id))
                {
                    return false;
                }
            }
            if (dto.ActiveQuestIds.Length == 1)
            {
                string id = dto.ActiveQuestIds[0];
                int count = dto.QuestProgressCounts[0];
                if (!IsValidSettlementId(id) || completed.Contains(id) || (quests != null && !definitions.ContainsKey(id))
                    || !IsValidSettlementId(dto.QuestAcceptanceIds[0]) || !actions.Contains(dto.QuestAcceptanceIds[0])
                    || count < 0 || count > 1000000)
                {
                    return false;
                }
                // 납부 진척은 현재 인벤토리에서 계산하며 별도 누적 수를 갖지 않는다.
                // 요구량을 낮춘 시트로도 기존 진척을 읽을 수 있도록 현재 요구량 상한은 적용하지 않는다.
                if (quests != null && definitions[id].Kind == QuestKind.DeliverItems && count != 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasValidCharacterData(AdventureSaveData dto, LootSheet sheet, CharacterSheet characters)
        {
            if (dto.CharacterIds == null || dto.CharacterLevels == null || dto.CharacterExperience == null || dto.CharacterAffinity == null
                || dto.PartyIds == null || dto.CharacterActionIds == null)
            {
                return false;
            }
            int count = dto.CharacterIds.Length;
            if (dto.CharacterLevels.Length != count || dto.CharacterExperience.Length != count || dto.CharacterAffinity.Length != count
                || (characters != null && count != characters.Characters.Count))
            {
                return false;
            }
            float[][] stats = { dto.CharacterStrengthGrowth, dto.CharacterConstitutionGrowth, dto.CharacterWillPowerGrowth,
                dto.CharacterIntelligenceGrowth, dto.CharacterAgilityGrowth, dto.CharacterPerceptionGrowth };
            foreach (var values in stats)
            {
                if (values == null || values.Length != count)
                {
                    return false;
                }
            }
            var known = new HashSet<string>(StringComparer.Ordinal);
            if (characters != null)
            {
                foreach (var definition in characters.Characters)
                {
                    known.Add(definition.Id);
                }
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string id = dto.CharacterIds[i];
                int level = dto.CharacterLevels[i];
                float experience = dto.CharacterExperience[i];
                if (!IsValidSettlementId(id) || !ids.Add(id) || (characters != null && !known.Contains(id))
                    || level < 1 || level > (characters?.Growth.MaxLevel ?? 10000)
                    || !IsFinite(experience) || experience < 0 || experience > MaximumStoredExperience
                    || dto.CharacterAffinity[i] < 0 || dto.CharacterAffinity[i] > 100
                    || (id == sheet.PlayerMemberId && dto.CharacterAffinity[i] != 0))
                {
                    return false;
                }
                for (int stat = 0; stat < stats.Length; stat++)
                {
                    float increase = stats[stat][i];
                    if (!IsFinite(increase) || increase < 0 || increase > 10000000)
                    {
                        return false;
                    }
                    if (characters != null)
                    {
                        float finalValue = characters.GetCharacter(id).BaseStats[(MajorStatType)stat] + increase;
                        if (!IsFinite(finalValue) || finalValue < 1 || finalValue > 10000000)
                        {
                            return false;
                        }
                    }
                }
            }
            if (count == 0)
            {
                if (dto.PartyIds.Length != 0 || dto.CharacterActionIds.Length != 0)
                {
                    return false;
                }
            }
            else if (dto.PartyIds.Length == 0 || dto.PartyIds.Length > (characters?.MaxPartySize ?? 4)
                || dto.PartyIds[0] != sheet.PlayerMemberId)
            {
                return false;
            }
            var party = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in dto.PartyIds)
            {
                if (id == null || !ids.Contains(id) || !party.Add(id))
                {
                    return false;
                }
            }
            var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in dto.CharacterActionIds)
            {
                if (!IsValidSettlementId(id) || !actions.Add(id))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool HasValidShopData(AdventureSaveData dto, LootSheet sheet, ShopSheet shops)
        {
            if (dto.MerchantIds == null || dto.StockMerchantIds == null || dto.StockItemIds == null
                || dto.StockCounts == null || dto.TradeIds == null || dto.StockMerchantIds.Length != dto.StockItemIds.Length
                || dto.StockMerchantIds.Length != dto.StockCounts.Length)
            {
                return false;
            }
            var knownMerchants = new HashSet<string>(StringComparer.Ordinal);
            if (shops != null)
            {
                foreach (var merchant in shops.Merchants)
                {
                    knownMerchants.Add(merchant.Id);
                }
            }
            var merchantItems = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string id in dto.MerchantIds)
            {
                if (!IsValidSettlementId(id) || merchantItems.ContainsKey(id) || (shops != null && !knownMerchants.Contains(id)))
                {
                    return false;
                }
                merchantItems.Add(id, new HashSet<string>(StringComparer.Ordinal));
            }
            var knownItems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in sheet.Items)
            {
                knownItems.Add(item.Id);
            }
            for (int i = 0; i < dto.StockMerchantIds.Length; i++)
            {
                if (dto.StockMerchantIds[i] == null || !merchantItems.TryGetValue(dto.StockMerchantIds[i], out var items)
                    || dto.StockItemIds[i] == null || !knownItems.Contains(dto.StockItemIds[i]) || !items.Add(dto.StockItemIds[i])
                    || dto.StockCounts[i] <= 0)
                {
                    return false;
                }
            }
            var seenTrades = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in dto.TradeIds)
            {
                if (!IsValidSettlementId(id) || !seenTrades.Add(id))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

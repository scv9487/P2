using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using Refactor.Util;

namespace Refactor.Main
{
    public interface IAdventureProgressReader
    {
        IReadOnlyDictionary<string, long> Gold { get; }
        IReadOnlyDictionary<string, int> Items { get; }
        int GetAvailableItemCount(string itemId);
        long PlayerGold { get; }
        long GetGold(string memberId);
        bool HasSettlement(string id);
        IReadOnlyDictionary<string, int> GetMerchantStock(string merchantId);
        bool HasTrade(string id);
        IReadOnlyDictionary<string, CharacterProgress> Characters { get; }
        IReadOnlyList<string> PartyIds { get; }
        CharacterProgress GetCharacter(string id);
        bool IsInParty(string id);
        QuestProgress Quest { get; }
        int Revision { get; }
    }

    public enum AdventureLoadResult
    {
        Loaded,
        Missing,
        Corrupt,
        UnsupportedVersion,
        IOError
    }

    public enum SettlementCommitResult
    {
        Saved,
        AlreadyCommitted,
        InsufficientGold,
        InvalidSelection,
        SaveFailed,
        LoadBlocked
    }

    public sealed partial class AdventureProgressDataContainer : IAdventureProgressReader
    {
        private readonly LootSheet _sheet;
        private readonly ShopSheet _shops;
        private readonly CharacterSheet _characterSheet;
        private readonly SkillSheet _skillSheet;
        private readonly Dictionary<string, long> _gold = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _items = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _settlements = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _trades = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, int>> _merchantStock = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyDictionary<string, int>> _merchantReaders = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        private readonly Dictionary<string, CharacterProgress> _characters = new Dictionary<string, CharacterProgress>(StringComparer.Ordinal);
        private readonly List<string> _party = new List<string>();
        private readonly HashSet<string> _characterActions = new HashSet<string>(StringComparer.Ordinal);
        public int Revision { get; private set; }

        public IReadOnlyDictionary<string, long> Gold { get; }
        public IReadOnlyDictionary<string, int> Items { get; }
        public long PlayerGold => GetGold(_sheet.PlayerMemberId);
        public IReadOnlyDictionary<string, CharacterProgress> Characters { get; }
        public IReadOnlyList<string> PartyIds { get; }

        internal AdventureProgressDataContainer(LootSheet sheet, ShopSheet shops = null, CharacterSheet characters = null, SkillSheet skills = null, EquipmentSheet equipment = null)
        {
            _sheet = sheet;
            _shops = shops;
            _characterSheet = characters;
            _skillSheet = skills;
            _equipmentSheet = equipment;
            foreach (var member in sheet.Shares)
            {
                _gold.Add(member.MemberId, member.MemberId == sheet.PlayerMemberId ? sheet.StartingGold : 0);
            }
            Gold = new ReadOnlyDictionary<string, long>(_gold);
            Items = new ReadOnlyDictionary<string, int>(_items);
            Characters = new ReadOnlyDictionary<string, CharacterProgress>(_characters);
            PartyIds = _party.AsReadOnly();
            if (characters != null)
            {
                foreach (var definition in characters.Characters)
                {
                    if (!_gold.ContainsKey(definition.Id))
                    {
                        _gold.Add(definition.Id, 0);
                    }
                    _characters.Add(definition.Id, new CharacterProgress(definition.Id, definition.InitialAffinity,
                        1, 0, new MajorStats(0, 0, 0, 0, 0, 0), definition.BaseStats,
                        skills == null ? null : CharacterSkillProgress.CreateInitial(skills, 1)));
                }
                _party.Add(characters.PlayerId);
            }
            if (shops != null)
            {
                foreach (var merchant in shops.Merchants)
                {
                    ReplaceMerchantStock(merchant.Id, merchant.InitialStock);
                }
            }
            InitializeEquipment();
        }

        public long GetGold(string memberId)
        {
            return _gold.TryGetValue(memberId, out long amount) ? amount : throw new KeyNotFoundException("금화 소유자를 찾을 수 없습니다: " + memberId);
        }

        public bool HasSettlement(string id)
        {
            return id != null && _settlements.Contains(id);
        }

        public IReadOnlyDictionary<string, int> GetMerchantStock(string merchantId)
        {
            return merchantId != null && _merchantReaders.TryGetValue(merchantId, out var stock) ? stock
                : throw new KeyNotFoundException("상인 재고를 찾을 수 없습니다: " + merchantId);
        }

        public bool HasTrade(string id)
        {
            return id != null && _trades.Contains(id);
        }

        public CharacterProgress GetCharacter(string id)
        {
            return id != null && _characters.TryGetValue(id, out var character) ? character
                : throw new KeyNotFoundException("저장된 인물 상태를 찾을 수 없습니다: " + id);
        }

        public bool IsInParty(string id)
        {
            return id != null && _party.Contains(id);
        }

        internal bool HasCharacterAction(string id) => _characterActions.Contains(id);
        internal void RecordCharacterAction(string id) => _characterActions.Add(id);
        internal void SetCharacter(CharacterProgress character) => _characters[character.Id] = character;
        internal void AddPartyMember(string id) => _party.Add(id);
        internal void RemovePartyMember(string id) => _party.Remove(id);

        internal void SetGold(string memberId, long amount)
        {
            _gold[memberId] = amount;
        }

        internal void SetItem(string itemId, int amount)
        {
            if (amount == 0)
            {
                _items.Remove(itemId);
            }
            else
            {
                _items[itemId] = amount;
            }
        }

        internal void SetMerchantItem(string merchantId, string itemId, int amount)
        {
            if (amount == 0)
            {
                _merchantStock[merchantId].Remove(itemId);
            }
            else
            {
                _merchantStock[merchantId][itemId] = amount;
            }
        }

        private void ReplaceMerchantStock(string id, IReadOnlyDictionary<string, int> stock)
        {
            var copy = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in stock)
            {
                copy.Add(pair.Key, pair.Value);
            }
            _merchantStock[id] = copy;
            _merchantReaders[id] = new ReadOnlyDictionary<string, int>(copy);
        }

        internal void RecordTrade(string id)
        {
            _trades.Add(id);
        }

        internal void RecordSettlement(string id)
        {
            _settlements.Add(id);
        }

        internal AdventureProgressDataContainer Copy()
        {
            var copy = new AdventureProgressDataContainer(_sheet, _shops, _characterSheet, _skillSheet, _equipmentSheet);
            copy.Replace(this);
            return copy;
        }

        internal void Replace(AdventureProgressDataContainer source)
        {
            _gold.Clear();
            _items.Clear();
            _settlements.Clear();
            _trades.Clear();
            _merchantStock.Clear();
            _merchantReaders.Clear();
            _characters.Clear();
            _party.Clear();
            _characterActions.Clear();
            foreach (var pair in source.Gold)
            {
                _gold.Add(pair.Key, pair.Value);
            }
            foreach (var pair in source.Items)
            {
                _items.Add(pair.Key, pair.Value);
            }
            _settlements.UnionWith(source._settlements);
            _trades.UnionWith(source._trades);
            foreach (var pair in source._merchantReaders)
            {
                ReplaceMerchantStock(pair.Key, pair.Value);
            }
            foreach (var pair in source._characters)
            {
                _characters.Add(pair.Key, pair.Value);
            }
            _party.AddRange(source._party);
            _characterActions.UnionWith(source._characterActions);
            CopyQuestFrom(source);
            CopySkillsFrom(source);
            CopyEquipmentFrom(source);
            Revision++;
        }

        internal AdventureSaveData ToSaveData()
        {
            var dto = new AdventureSaveData
            {
                Format = AdventureSave.CurrentFormat,
                Version = AdventureSave.CurrentVersion,
                MemberIds = new string[_gold.Count],
                MemberGold = new long[_gold.Count],
                ItemIds = new string[_items.Count],
                ItemCounts = new int[_items.Count],
                SettlementIds = new string[_settlements.Count],
                TradeIds = new string[_trades.Count],
                MerchantIds = new string[_merchantStock.Count],
                CharacterIds = new string[_characters.Count],
                CharacterLevels = new int[_characters.Count],
                CharacterExperience = new float[_characters.Count],
                CharacterAffinity = new int[_characters.Count],
                CharacterStrengthGrowth = new float[_characters.Count],
                CharacterConstitutionGrowth = new float[_characters.Count],
                CharacterWillPowerGrowth = new float[_characters.Count],
                CharacterIntelligenceGrowth = new float[_characters.Count],
                CharacterAgilityGrowth = new float[_characters.Count],
                CharacterPerceptionGrowth = new float[_characters.Count],
                PartyIds = _party.ToArray(),
                CharacterActionIds = new string[_characterActions.Count]
            };
            int index = 0;
            var memberIds = new List<string>(_gold.Keys);
            memberIds.Sort(StringComparer.Ordinal);
            foreach (string memberId in memberIds)
            {
                dto.MemberIds[index] = memberId;
                dto.MemberGold[index++] = _gold[memberId];
            }
            index = 0;
            foreach (var item in _sheet.Items)
            {
                if (_items.TryGetValue(item.Id, out int count))
                {
                    dto.ItemIds[index] = item.Id;
                    dto.ItemCounts[index++] = count;
                }
            }
            _settlements.CopyTo(dto.SettlementIds);
            Array.Sort(dto.SettlementIds, StringComparer.Ordinal);
            _trades.CopyTo(dto.TradeIds);
            Array.Sort(dto.TradeIds, StringComparer.Ordinal);
            _merchantStock.Keys.CopyTo(dto.MerchantIds, 0);
            Array.Sort(dto.MerchantIds, StringComparer.Ordinal);
            var stockMerchants = new List<string>();
            var stockItems = new List<string>();
            var stockCounts = new List<int>();
            foreach (string merchantId in dto.MerchantIds)
            {
                foreach (var item in _sheet.Items)
                {
                    if (_merchantStock[merchantId].TryGetValue(item.Id, out int count))
                    {
                        stockMerchants.Add(merchantId);
                        stockItems.Add(item.Id);
                        stockCounts.Add(count);
                    }
                }
            }
            dto.StockMerchantIds = stockMerchants.ToArray();
            dto.StockItemIds = stockItems.ToArray();
            dto.StockCounts = stockCounts.ToArray();
            _characters.Keys.CopyTo(dto.CharacterIds, 0);
            Array.Sort(dto.CharacterIds, StringComparer.Ordinal);
            for (int i = 0; i < dto.CharacterIds.Length; i++)
            {
                var character = _characters[dto.CharacterIds[i]];
                dto.CharacterLevels[i] = character.Level;
                dto.CharacterExperience[i] = character.Experience;
                dto.CharacterAffinity[i] = character.Affinity;
                dto.CharacterStrengthGrowth[i] = character.GrowthStats.Strength;
                dto.CharacterConstitutionGrowth[i] = character.GrowthStats.Constitution;
                dto.CharacterWillPowerGrowth[i] = character.GrowthStats.WillPower;
                dto.CharacterIntelligenceGrowth[i] = character.GrowthStats.Intelligence;
                dto.CharacterAgilityGrowth[i] = character.GrowthStats.Agility;
                dto.CharacterPerceptionGrowth[i] = character.GrowthStats.Perception;
            }
            _characterActions.CopyTo(dto.CharacterActionIds);
            Array.Sort(dto.CharacterActionIds, StringComparer.Ordinal);
            WriteQuestSave(dto);
            WriteSkillSave(dto);
            WriteEquipmentSave(dto);
            return dto;
        }

        internal static AdventureProgressDataContainer FromSaveData(LootSheet sheet, AdventureSaveData dto, ShopSheet shops = null, CharacterSheet characters = null, SkillSheet skills = null, EquipmentSheet equipment = null)
        {
            var result = new AdventureProgressDataContainer(sheet, shops, characters, skills, equipment);
            result._items.Clear();
            for (int i = 0; i < dto.MemberIds.Length; i++)
            {
                result.SetGold(dto.MemberIds[i], dto.MemberGold[i]);
            }
            for (int i = 0; i < dto.ItemIds.Length; i++)
            {
                result.SetItem(dto.ItemIds[i], dto.ItemCounts[i]);
            }
            foreach (string id in dto.SettlementIds)
            {
                result.RecordSettlement(id);
            }
            foreach (string id in dto.MerchantIds)
            {
                result.ReplaceMerchantStock(id, new Dictionary<string, int>());
            }
            for (int i = 0; i < dto.StockMerchantIds.Length; i++)
            {
                result.SetMerchantItem(dto.StockMerchantIds[i], dto.StockItemIds[i], dto.StockCounts[i]);
            }
            foreach (string id in dto.TradeIds)
            {
                result.RecordTrade(id);
            }
            if (dto.CharacterIds.Length > 0)
            {
                result._characters.Clear();
                result._party.Clear();
                for (int i = 0; i < dto.CharacterIds.Length; i++)
                {
                    var stats = new MajorStats(dto.CharacterStrengthGrowth[i], dto.CharacterConstitutionGrowth[i], dto.CharacterWillPowerGrowth[i],
                        dto.CharacterIntelligenceGrowth[i], dto.CharacterAgilityGrowth[i], dto.CharacterPerceptionGrowth[i]);
                    // DS처럼 현재 시트의 EXP 상한에 맞추되 로드에서 성장/난수/저장을 실행하지 않는다.
                    float experience = characters == null ? dto.CharacterExperience[i]
                        : Math.Min(dto.CharacterExperience[i], characters.Growth.RequiredExperience(dto.CharacterLevels[i]));
                    result.SetCharacter(new CharacterProgress(dto.CharacterIds[i], dto.CharacterAffinity[i],
                        dto.CharacterLevels[i], experience, stats, characters?.GetCharacter(dto.CharacterIds[i]).BaseStats));
                }
                result._party.AddRange(dto.PartyIds);
            }
            result._characterActions.UnionWith(dto.CharacterActionIds);
            result.ReadQuestSave(dto);
            result.ReadSkillSave(dto);
            result.ReadEquipmentSave(dto);
            return result;
        }
    }

    // MainFlow가 씬마다 생성/불러오며 EventQueue 단계에서만 정산 확정을 호출한다.
    public sealed partial class AdventureProgressStore
    {
        private readonly LootSheet _sheet;
        private readonly ShopSheet _shops;
        private readonly CharacterSheet _characters;
        private readonly Dictionary<string, PendingExperience> _pendingExperience = new Dictionary<string, PendingExperience>(StringComparer.Ordinal);
        private readonly string _path;
        private readonly AdventureProgressDataContainer _progress;
        public IAdventureProgressReader Progress => _progress;
        public bool CanWrite { get; private set; }

        public AdventureProgressStore(LootSheet sheet, string path, ShopSheet shops = null, CharacterSheet characters = null, QuestSheet quests = null, SkillSheet skills = null, EquipmentSheet equipment = null)
        {
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _shops = shops;
            _characters = characters;
            _quests = quests;
            _skills = skills;
            _equipment = equipment;
            if (equipment != null && (shops == null || characters == null || skills == null))
            {
                throw new ArgumentException("장비 진행을 초기화하려면 상점/인물/스킬 시트가 필요합니다.", nameof(equipment));
            }
            if (skills != null && characters == null)
            {
                throw new ArgumentException("스킬 진행을 초기화하려면 CharacterSheet가 필요합니다.", nameof(characters));
            }
            if (characters != null && characters.PlayerId != sheet.PlayerMemberId)
            {
                throw new ArgumentException("인물과 전리품 시트의 플레이어 ID가 다릅니다.", nameof(characters));
            }
            if (shops != null)
            {
                if (shops.Items.PlayerMemberId != sheet.PlayerMemberId || shops.Items.Items.Count != sheet.Items.Count)
                {
                    throw new ArgumentException("상점과 진행 데이터의 아이템/플레이어 시트가 다릅니다.", nameof(shops));
                }
                foreach (var item in shops.Items.Items)
                {
                    if (sheet.GetItem(item.Id).Price != item.Price)
                    {
                        throw new ArgumentException("상점과 진행 데이터의 아이템 정가가 다릅니다.", nameof(shops));
                    }
                }
            }
            _progress = new AdventureProgressDataContainer(sheet, shops, characters, skills, equipment);
        }

        public AdventureLoadResult Load()
        {
            _pendingExperience.Clear();
            AdventureLoadResult result = AdventureSave.Load(_path, _sheet, out var dto, _shops, _characters, _quests, _skills, _equipment);
            if (result == AdventureLoadResult.Loaded)
            {
                _progress.Replace(AdventureProgressDataContainer.FromSaveData(_sheet, dto, _shops, _characters, _skills, _equipment));
            }
            CanWrite = result == AdventureLoadResult.Loaded || result == AdventureLoadResult.Missing;
            return result;
        }

        public SettlementCommitResult TryCommit(LootSettlement settlement)
        {
            if (!CanWrite)
            {
                return SettlementCommitResult.LoadBlocked;
            }
            if (settlement == null || !ReferenceEquals(settlement.Sheet, _sheet) || !ReferenceEquals(settlement.Progress, _progress))
            {
                return SettlementCommitResult.InvalidSelection;
            }
            if (_progress.HasSettlement(settlement.Id))
            {
                settlement.MarkCommitted();
                return SettlementCommitResult.AlreadyCommitted;
            }
            if (!settlement.IsCurrent)
            {
                return SettlementCommitResult.InvalidSelection;
            }
            if (settlement.FinalPlayerGold < 0)
            {
                return SettlementCommitResult.InsufficientGold;
            }
            if (!settlement.TryCreateCandidate(_progress, out var candidate))
            {
                return SettlementCommitResult.InvalidSelection;
            }
            try
            {
                AdventureSave.Save(_path, candidate.ToSaveData(), _sheet, _shops, _characters, _quests, _skills, _equipment);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is SecurityException || exception is ArgumentException || exception is NotSupportedException)
            {
                return SettlementCommitResult.SaveFailed;
            }
            _progress.Replace(candidate);
            settlement.MarkCommitted();
            return SettlementCommitResult.Saved;
        }

        public ShopTradeResult TryTrade(ShopTrade trade)
        {
            if (!CanWrite)
            {
                return ShopTradeResult.LoadBlocked;
            }
            if (trade == null || _shops == null || !ReferenceEquals(trade.Sheet, _shops) || !ReferenceEquals(trade.Progress, _progress))
            {
                return ShopTradeResult.InvalidSelection;
            }
            if (_progress.HasTrade(trade.Id))
            {
                return ShopTradeResult.AlreadyCommitted;
            }
            var result = trade.TryCreateCandidate(_progress, out var candidate);
            if (result != ShopTradeResult.Saved)
            {
                return result;
            }
            try
            {
                AdventureSave.Save(_path, candidate.ToSaveData(), _sheet, _shops, _characters, _quests, _skills, _equipment);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is SecurityException || exception is ArgumentException || exception is NotSupportedException)
            {
                return ShopTradeResult.SaveFailed;
            }
            _progress.Replace(candidate);
            return ShopTradeResult.Saved;
        }

        public CharacterActionResult TryTalk(string characterId, string requestId, int expectedRevision)
        {
            var validation = ValidateCharacterRequest(characterId, requestId, expectedRevision);
            if (validation != CharacterActionResult.Saved)
            {
                return validation;
            }
            var previous = _progress.GetCharacter(characterId);
            var candidate = _progress.Copy();
            candidate.SetCharacter(previous.WithAffinity(Math.Min(100, previous.Affinity + _characters.TalkAffinityGain)));
            return SaveCharacterChange(candidate, requestId, CharacterActionResult.Saved);
        }

        public CharacterActionResult TryRecruit(string characterId, string requestId, int expectedRevision, int roll0To99)
        {
            var validation = ValidateCharacterRequest(characterId, requestId, expectedRevision);
            if (validation != CharacterActionResult.Saved)
            {
                return validation;
            }
            if (roll0To99 < 0 || roll0To99 > 99)
            {
                return CharacterActionResult.InvalidRequest;
            }
            if (_progress.IsInParty(characterId))
            {
                return CharacterActionResult.AlreadyInParty;
            }
            if (_progress.PartyIds.Count >= _characters.MaxPartySize)
            {
                return CharacterActionResult.PartyFull;
            }
            bool joined = roll0To99 < _progress.GetCharacter(characterId).Affinity;
            var candidate = _progress.Copy();
            if (joined)
            {
                candidate.AddPartyMember(characterId);
            }
            return SaveCharacterChange(candidate, requestId, joined ? CharacterActionResult.Saved : CharacterActionResult.RecruitFailed);
        }

        public CharacterActionResult TryDismiss(string characterId, string requestId, int expectedRevision)
        {
            var validation = ValidateCharacterRequest(characterId, requestId, expectedRevision);
            if (validation != CharacterActionResult.Saved)
            {
                return validation;
            }
            if (!_progress.IsInParty(characterId))
            {
                return CharacterActionResult.NotInParty;
            }
            var candidate = _progress.Copy();
            candidate.RemovePartyMember(characterId);
            return SaveCharacterChange(candidate, requestId, CharacterActionResult.Saved);
        }

        public CharacterActionResult TryGrantExperience(string rewardId, IReadOnlyList<string> recipients, float amount, IRandomSource random)
        {
            if (recipients == null)
            {
                return CharacterActionResult.InvalidRequest;
            }
            var grants = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (string id in recipients)
            {
                if (id == null || grants.ContainsKey(id))
                {
                    return CharacterActionResult.InvalidRequest;
                }
                grants.Add(id, amount);
            }
            return TryGrantExperience(rewardId, grants, random);
        }

        public CharacterActionResult TryGrantExperience(string rewardId, IReadOnlyDictionary<string, float> grants, IRandomSource random,
            IReadOnlyDictionary<string, int> basicAttackKills = null)
        {
            if (!CanWrite)
            {
                return CharacterActionResult.LoadBlocked;
            }
            if (_characters == null || !AdventureSave.IsValidSettlementId(rewardId) || grants == null || grants.Count == 0 || random == null)
            {
                return CharacterActionResult.InvalidRequest;
            }
            if (_progress.HasCharacterAction(rewardId))
            {
                return CharacterActionResult.AlreadyApplied;
            }
            foreach (var pair in grants)
            {
                if (pair.Key == null || !_progress.Characters.ContainsKey(pair.Key) || float.IsNaN(pair.Value) || float.IsInfinity(pair.Value) || pair.Value < 0)
                {
                    return CharacterActionResult.InvalidRequest;
                }
                if (_skills == null && pair.Value > 0 && _progress.GetCharacter(pair.Key).SkillProgress != null)
                {
                    // 지급 시트 없이 이미 초기화된 SP 원천의 레벨 보상을 누락시키지 않는다.
                    return CharacterActionResult.InvalidRequest;
                }
                if (pair.Value > 0 && _progress.GetCharacter(pair.Key).EquippedItemId != null && _equipment == null)
                {
                    return CharacterActionResult.InvalidRequest;
                }
            }
            if (basicAttackKills != null)
            {
                foreach (var pair in basicAttackKills)
                {
                    if (_skills == null || pair.Key == null || !_progress.Characters.ContainsKey(pair.Key)
                        || pair.Value < 1 || !_progress.GetCharacter(pair.Key).HasSkill("SKILL_SharpShooter"))
                    {
                        return CharacterActionResult.InvalidRequest;
                    }
                }
            }
            if (!_pendingExperience.TryGetValue(rewardId, out var pending))
            {
                var candidate = _progress.Copy();
                var calculator = new CharacterGrowthCalculator(_characters.Growth);
                var ids = new List<string>(grants.Keys);
                ids.Sort(StringComparer.Ordinal);
                foreach (string id in ids)
                {
                    if (grants[id] == 0)
                    {
                        continue;
                    }
                    var previous = _progress.GetCharacter(id);
                    var growth = calculator.GainExperience(previous.Growth, grants[id], random, out var increases);
                    candidate.SetCharacter(ApplyLevelSkills(previous, previous.WithGrowth(growth, increases), random));
                }
                if (basicAttackKills != null)
                {
                    foreach (var pair in basicAttackKills)
                    {
                        var character = candidate.GetCharacter(pair.Key);
                        candidate.SetCharacter(character.WithSkills(character.SkillProgress.AddBasicAttackKills(pair.Value)));
                    }
                }
                pending = new PendingExperience(_progress.Revision, grants, basicAttackKills, candidate);
                _pendingExperience.Add(rewardId, pending);
            }
            else if (!pending.Matches(_progress.Revision, grants, basicAttackKills))
            {
                return CharacterActionResult.InvalidRequest;
            }
            var result = SaveCharacterChange(pending.Candidate, rewardId, CharacterActionResult.Saved);
            if (result == CharacterActionResult.Saved)
            {
                _pendingExperience.Remove(rewardId);
            }
            return result;
        }

        private CharacterActionResult ValidateCharacterRequest(string characterId, string requestId, int expectedRevision)
        {
            if (!CanWrite)
            {
                return CharacterActionResult.LoadBlocked;
            }
            if (_characters == null || !AdventureSave.IsValidSettlementId(requestId))
            {
                return CharacterActionResult.InvalidRequest;
            }
            if (_progress.HasCharacterAction(requestId))
            {
                return CharacterActionResult.AlreadyApplied;
            }
            if (expectedRevision != _progress.Revision)
            {
                return CharacterActionResult.InvalidRequest;
            }
            if (characterId == null || characterId == _characters.PlayerId || !_progress.Characters.ContainsKey(characterId))
            {
                return CharacterActionResult.InvalidTarget;
            }
            return CharacterActionResult.Saved;
        }

        private CharacterActionResult SaveCharacterChange(AdventureProgressDataContainer candidate, string requestId, CharacterActionResult result)
        {
            candidate.RecordCharacterAction(requestId);
            try
            {
                AdventureSave.Save(_path, candidate.ToSaveData(), _sheet, _shops, _characters, _quests, _skills, _equipment);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is SecurityException || exception is ArgumentException || exception is NotSupportedException)
            {
                return CharacterActionResult.SaveFailed;
            }
            _progress.Replace(candidate);
            return result;
        }

        private sealed class PendingExperience
        {
            private readonly int _revision;
            private readonly Dictionary<string, float> _grants = new Dictionary<string, float>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _basicAttackKills = new Dictionary<string, int>(StringComparer.Ordinal);
            public AdventureProgressDataContainer Candidate { get; }

            public PendingExperience(int revision, IReadOnlyDictionary<string, float> grants, IReadOnlyDictionary<string, int> basicAttackKills, AdventureProgressDataContainer candidate)
            {
                _revision = revision;
                Candidate = candidate;
                foreach (var pair in grants)
                {
                    _grants.Add(pair.Key, pair.Value);
                }
                if (basicAttackKills != null)
                {
                    foreach (var pair in basicAttackKills)
                    {
                        _basicAttackKills.Add(pair.Key, pair.Value);
                    }
                }
            }

            public bool Matches(int revision, IReadOnlyDictionary<string, float> grants, IReadOnlyDictionary<string, int> basicAttackKills)
            {
                if (revision != _revision || grants.Count != _grants.Count || (basicAttackKills?.Count ?? 0) != _basicAttackKills.Count)
                {
                    return false;
                }
                foreach (var pair in grants)
                {
                    if (!_grants.TryGetValue(pair.Key, out float amount) || pair.Value != amount)
                    {
                        return false;
                    }
                }
                if (basicAttackKills != null)
                {
                    foreach (var pair in basicAttackKills)
                    {
                        if (!_basicAttackKills.TryGetValue(pair.Key, out int amount) || amount != pair.Value)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }
    }
}

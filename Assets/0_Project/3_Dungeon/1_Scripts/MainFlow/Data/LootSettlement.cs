using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor.Main
{
    public interface ILootSettlementReader
    {
        string Id { get; }
        IReadOnlyList<LootSettlementItem> Items { get; }
        IReadOnlyList<LootSettlementShare> Shares { get; }
        long TotalValue { get; }
        long PurchaseCost { get; }
        long PlayerGold { get; }
        long PlayerShare { get; }
        long FinalPlayerGold { get; }
        bool CanConfirm { get; }
        bool IsCommitted { get; }
        int Revision { get; }
    }

    public sealed class LootSettlementItem
    {
        public string ItemId { get; }
        public string Name { get; }
        public long Price { get; }
        public int Acquired { get; }
        public int Purchased { get; internal set; }
        public int ExternalQuantity => Acquired - Purchased;

        internal LootSettlementItem(LootItemDefinition item, int acquired)
        {
            ItemId = item.Id;
            Name = item.Name;
            Price = item.Price;
            Acquired = acquired;
        }
    }

    public sealed class LootSettlementShare
    {
        public string MemberId { get; }
        public string Name { get; }
        public int Percent { get; }
        public long Gold { get; internal set; }

        internal LootSettlementShare(LootShareDefinition member, long gold)
        {
            MemberId = member.MemberId;
            Name = member.Name;
            Percent = member.Percent;
            Gold = gold;
        }
    }

    // 선택 수량은 정산의 임시 상태다. 영속 데이터는 Store가 저장 성공 후에만 변경한다.
    public sealed class LootSettlement : ILootSettlementReader
    {
        private readonly Dictionary<string, LootSettlementItem> _itemsById = new Dictionary<string, LootSettlementItem>(StringComparer.Ordinal);
        private readonly long _startingGold;
        private readonly int _sourceRevision;
        internal LootSheet Sheet { get; }
        internal IAdventureProgressReader Progress { get; }
        internal bool IsCurrent => Progress is AdventureProgressDataContainer source && source.Revision == _sourceRevision;

        public string Id { get; }
        public IReadOnlyList<LootSettlementItem> Items { get; }
        public IReadOnlyList<LootSettlementShare> Shares { get; }
        public long TotalValue { get; }
        public long PurchaseCost { get; private set; }
        public long PlayerGold => _startingGold;
        public long PlayerShare { get; }
        private decimal FinalGoldValue => (decimal)PlayerGold + PlayerShare - PurchaseCost;
        public long FinalPlayerGold => FinalGoldValue > long.MaxValue ? long.MaxValue : (long)FinalGoldValue;
        public bool CanConfirm => !IsCommitted && CanApply();
        public bool IsCommitted { get; private set; }
        public int Revision { get; private set; }

        public LootSettlement(LootSheet sheet, string settlementId, IReadOnlyDictionary<string, int> acquired, IAdventureProgressReader progress,
            IReadOnlyList<LootShareDefinition> participants = null)
        {
            Sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            if (!AdventureSave.IsValidSettlementId(settlementId))
            {
                throw new ArgumentException("정산 ID가 올바르지 않습니다.", nameof(settlementId));
            }
            if (acquired == null)
            {
                throw new ArgumentNullException(nameof(acquired));
            }
            Id = settlementId;
            _startingGold = progress.PlayerGold;
            _sourceRevision = progress is AdventureProgressDataContainer source ? source.Revision : -1;
            foreach (var pair in acquired)
            {
                sheet.GetItem(pair.Key);
                if (pair.Value < 0)
                {
                    throw new ArgumentException("전리품 수량은 음수일 수 없습니다.", nameof(acquired));
                }
            }
            var items = new List<LootSettlementItem>();
            long total = 0;
            foreach (var item in sheet.Items)
            {
                if (acquired.TryGetValue(item.Id, out int count) && count > 0)
                {
                    var row = new LootSettlementItem(item, count);
                    items.Add(row);
                    _itemsById.Add(item.Id, row);
                    total = checked(total + checked(item.Price * count));
                }
            }
            Items = items.AsReadOnly();
            TotalValue = total;

            var shares = new List<LootSettlementShare>();
            var remainderOrder = new List<int>();
            var remainders = new List<long>();
            long distributed = 0;
            var members = participants ?? sheet.Shares;
            int percentTotal = 0;
            var memberIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                if (!memberIds.Add(member.MemberId) || !progress.Gold.ContainsKey(member.MemberId) || member.Percent < 0 || member.Percent > 100)
                {
                    throw new ArgumentException("정산 참가자 또는 비율이 올바르지 않습니다.", nameof(participants));
                }
                percentTotal += member.Percent;
            }
            if (percentTotal != 100 || !memberIds.Contains(sheet.PlayerMemberId))
            {
                throw new ArgumentException("정산 비율은 플레이어를 포함해 합계 100이어야 합니다.", nameof(participants));
            }
            foreach (var member in members)
            {
                // total * percent의 중간 오버플로 없이 몫과 나머지를 구한다.
                long fractional = total % 100 * member.Percent;
                long gold = total / 100 * member.Percent + fractional / 100;
                shares.Add(new LootSettlementShare(member, gold));
                remainderOrder.Add(shares.Count - 1);
                remainders.Add(fractional % 100);
                distributed += gold;
            }
            // 최대 나머지 방식. 동률은 분배 시트의 행 순서를 유지한다.
            remainderOrder.Sort((left, right) => remainders[left] == remainders[right]
                ? left.CompareTo(right) : remainders[right].CompareTo(remainders[left]));
            for (int i = 0; i < total - distributed; i++)
            {
                shares[remainderOrder[i]].Gold++;
            }
            foreach (var share in shares)
            {
                if (share.MemberId == sheet.PlayerMemberId)
                {
                    PlayerShare = share.Gold;
                }
            }
            Shares = shares.AsReadOnly();
            IsCommitted = progress.HasSettlement(Id);
        }

        public bool TryAdjustPurchase(string itemId, int delta)
        {
            if (itemId == null || !_itemsById.TryGetValue(itemId, out var item))
            {
                return false;
            }
            long next = (long)item.Purchased + delta;
            return next >= 0 && next <= int.MaxValue && TrySetPurchase(itemId, (int)next);
        }

        public bool TrySetPurchase(string itemId, int count)
        {
            if (IsCommitted || !IsCurrent || itemId == null || !_itemsById.TryGetValue(itemId, out var item)
                || count < 0 || count > item.Acquired || count == item.Purchased)
            {
                return false;
            }
            PurchaseCost = checked(PurchaseCost - item.Price * item.Purchased + item.Price * count);
            item.Purchased = count;
            Revision++;
            return true;
        }

        private bool CanApply()
        {
            if (!IsCurrent || FinalGoldValue < 0 || FinalGoldValue > long.MaxValue)
            {
                return false;
            }
            foreach (var share in Shares)
            {
                if (share.MemberId != Sheet.PlayerMemberId && (decimal)Progress.GetGold(share.MemberId) + share.Gold > long.MaxValue)
                {
                    return false;
                }
            }
            foreach (var item in Items)
            {
                Progress.Items.TryGetValue(item.ItemId, out int existing);
                if ((long)existing + item.Purchased > int.MaxValue)
                {
                    return false;
                }
            }
            return true;
        }

        internal bool TryCreateCandidate(AdventureProgressDataContainer source, out AdventureProgressDataContainer candidate)
        {
            candidate = null;
            if (!ReferenceEquals(Progress, source) || !CanApply())
            {
                return false;
            }
            candidate = source.Copy();
            foreach (var share in Shares)
            {
                candidate.SetGold(share.MemberId, share.MemberId == Sheet.PlayerMemberId
                    ? (long)FinalGoldValue : checked(source.GetGold(share.MemberId) + share.Gold));
            }
            foreach (var item in Items)
            {
                if (item.Purchased > 0)
                {
                    candidate.Items.TryGetValue(item.ItemId, out int existing);
                    candidate.SetItem(item.ItemId, checked(existing + item.Purchased));
                }
            }
            candidate.RecordSettlement(Id);
            return true;
        }

        internal void MarkCommitted()
        {
            if (!IsCommitted)
            {
                IsCommitted = true;
                Revision++;
            }
        }
    }
}

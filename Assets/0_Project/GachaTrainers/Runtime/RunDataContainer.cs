using System;
using System.Collections.Generic;

namespace GachaTrainers
{
    public interface IReadOnlyRunData
    {
        GachaBalance Balance { get; }
        int CurrentVictoryGold { get; }
        int Money { get; }
        IReadOnlyList<PartyCreature> Party { get; }
        IReadOnlyList<PartyCreature> Owned { get; }
        int SelectedOwnedIndex { get; }
        int LegendaryTokens { get; }
        bool EvolutionPending { get; }
        int EvolutionCandidateIndex { get; }
        int GetCrystalCount(CrystalKind kind);
        int SelectedPartyIndex { get; }
        string Status { get; }
        int Revision { get; }
        bool BattleInProgress { get; }
        bool AutoBattle { get; }
        int StageNumber { get; }
        int ClearedStages { get; }
        bool RunCompleted { get; }
    }

    // A party entry is a value snapshot. Only RunDataContainer replaces entries.
    public sealed class PartyCreature
    {
        public CreatureKind Kind { get; }
        public GachaBalance Balance { get; }
        public CrystalKind? EquippedCrystal { get; }
        public int InstanceId { get; }
        public int FormNode { get; }
        public CreatureForm Form => CreatureEvolution.Get(Kind, FormNode);
        public int Rank => Form.Rank;
        public string Name => Form.Name;
        public CrystalRarity Rarity => GachaCatalog.GetCreature(Kind, Balance).Rarity;
        public CombatStats Stats { get; }
        public int MaxHealth => Stats.MaxHealth;
        public int Attack => UnityEngine.Mathf.RoundToInt(Stats.PhysicalAttackPower);
        public float AttackSpeed => Stats.AttackSpeed;

        internal PartyCreature(CreatureKind kind, CrystalKind? equippedCrystal = null, int formNode = 0, int instanceId = 0)
            : this(kind, GachaBalance.Default, equippedCrystal, formNode, instanceId)
        {
        }

        internal PartyCreature(CreatureKind kind, GachaBalance balance, CrystalKind? equippedCrystal = null, int formNode = 0, int instanceId = 0)
        {
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
            Kind = kind;
            EquippedCrystal = equippedCrystal;
            FormNode = CreatureEvolution.Get(kind, formNode).Node;
            InstanceId = instanceId;
            Stats = CombatStats.Build(kind, formNode, equippedCrystal, Balance);
        }
    }

    public sealed class RunDataContainer : IReadOnlyRunData
    {
        // Compatibility for neutral test fixtures. Actual settlement uses the current stage's snapshot.
        public static int VictoryPrize => GachaBalance.Default.VictoryGold(1);
        private readonly List<PartyCreature> _party = new();
        private readonly List<PartyCreature> _owned = new();
        private int _nextId;
        private readonly int[] _crystals = new int[Enum.GetValues(typeof(CrystalKind)).Length];

        public GachaBalance Balance { get; }
        public int CurrentVictoryGold => Balance.VictoryGold(StageNumber);
        public int Money { get; private set; }
        public IReadOnlyList<PartyCreature> Party { get; }
        public IReadOnlyList<PartyCreature> Owned { get; }
        public int SelectedOwnedIndex { get; private set; } = -1;
        public int LegendaryTokens { get; private set; }
        public bool EvolutionPending => EvolutionCandidateIndex >= 0;
        public int EvolutionCandidateIndex
        {
            get
            {
                if (HasSelection() && CanEvolve(_owned[SelectedOwnedIndex]))
                {
                    return SelectedOwnedIndex;
                }
                var counts = new Dictionary<(CreatureKind, int), int>();
                foreach (var creature in _owned)
                {
                    if (creature.Rank < 3)
                    {
                        var key = (creature.Kind, creature.FormNode);
                        counts.TryGetValue(key, out int count);
                        counts[key] = count + 1;
                    }
                }
                return _owned.FindIndex(creature => creature.Rank < 3 && counts[(creature.Kind, creature.FormNode)] >= 3);
            }
        }
        public int SelectedPartyIndex { get; private set; } = -1;
        public string Status { get; private set; } = "가챠 머신에 다가가 E로 상호작용하세요.";
        public int Revision { get; private set; }
        public bool BattleInProgress { get; private set; }
        public bool AutoBattle { get; private set; }
        public int ClearedStages { get; private set; }
        public int StageNumber => Math.Min(ClearedStages + 1, GachaStageSheet.StageCount);
        public bool RunCompleted => ClearedStages == GachaStageSheet.StageCount;

        public RunDataContainer(GachaBalance balance = null)
        {
            Balance = balance ?? GachaBalance.Default;
            Money = Balance.StartingGold;
            Party = _party.AsReadOnly();
            Owned = _owned.AsReadOnly();
        }

        public void SetAutoBattle(bool auto)
        {
            if (AutoBattle == auto)
            {
                return;
            }
            AutoBattle = auto;
            Changed(auto ? "자동 전투를 선택했습니다." : "직접 조작을 선택했습니다.");
        }

        // Battle.unity can be opened directly for a playable three-creature preview.
        public static RunDataContainer CreateBattleTestRun(GachaBalance balance = null)
        {
            var run = new RunDataContainer(balance);
            foreach (var kind in BattleCatalog.TestRoster)
            {
                var creature = new PartyCreature(kind, run.Balance, instanceId: ++run._nextId);
                run._party.Add(creature);
                run._owned.Add(creature);
            }
            run.SelectedPartyIndex = 0;
            run.SelectedOwnedIndex = 0;
            run.SetStatus("기본 크리쳐 3종으로 아레나에 입장합니다.");
            return run;
        }

        public bool TryBeginBattle()
        {
            if (RunCompleted)
            {
                SetStatus("10개 스테이지를 모두 완료했습니다. 새 모험을 시작하세요.");
                return false;
            }
            if (BattleInProgress || _party.Count == 0)
            {
                SetStatus(_party.Count == 0 ? "전투에 참여할 크리쳐를 먼저 뽑으세요." : "이미 전투에 참가 중입니다.");
                return false;
            }
            BattleInProgress = true;
            Changed("스테이지 " + StageNumber + "/" + GachaStageSheet.StageCount + " · 파티 순서대로 한 마리씩 소환합니다.");
            return true;
        }

        public bool CompleteBattle(bool victory)
        {
            if (!BattleInProgress)
            {
                return false;
            }
            BattleInProgress = false;
            if (victory)
            {
                int prize = CurrentVictoryGold;
                ClearedStages++;
                Money = (int)Math.Min((long)Money + prize, int.MaxValue);
                Changed(RunCompleted ? "10개 스테이지 완료! 상금 " + prize + " G 획득 · 모험 성공!"
                    : "승리! 상금 " + prize + " G 획득 · 다음은 스테이지 " + StageNumber + "/" + GachaStageSheet.StageCount + "입니다.");
            }
            else
            {
                Changed("이번 모험이 끝났습니다.");
            }
            return true;
        }

        public int GetCrystalCount(CrystalKind kind)
        {
            if (!IsCrystalKind(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return _crystals[(int)kind];
        }

        // Roll is supplied by the handler. Validation happens before randomness and payment.
        public bool TryPurchase(GachaMachineKind kind, Func<int, int> roll)
        {
            if (!CanManageParty())
            {
                return false;
            }
            if (kind != GachaMachineKind.Creature && kind != GachaMachineKind.Crystal)
            {
                SetStatus("알 수 없는 가챠 머신입니다.");
                return false;
            }
            int price = kind == GachaMachineKind.Creature ? Balance.CreaturePrice : Balance.CrystalPrice;
            if (Money < price)
            {
                SetStatus("돈이 부족합니다. 필요한 금액: " + price + " G");
                return false;
            }
            if (roll == null)
            {
                SetStatus("가챠를 진행할 수 없습니다.");
                return false;
            }
            int count = kind == GachaMachineKind.Creature ? Balance.CreatureWeightTotal : Balance.CrystalWeightTotal;
            int result = roll(count);
            if (result < 0 || result >= count)
            {
                SetStatus("가챠 결과가 올바르지 않아 결제하지 않았습니다.");
                return false;
            }

            Money -= price;
            if (kind == GachaMachineKind.Creature)
            {
                var definition = GachaCatalog.GetCreatureForRoll(result, Balance);
                if (definition == null)
                {
                    LegendaryTokens++;
                    Changed("[전설] 선택권 획득 · 현재 전설 크리쳐 0종 / 이 런의 보관함에 보관됩니다.");
                }
                else
                {
                    var creature = new PartyCreature(definition.Kind, Balance, instanceId: ++_nextId);
                    _owned.Add(creature);
                    if (_party.Count < GachaCatalog.MaxPartySize)
                    {
                        _party.Add(creature);
                    }
                    SetSelection(_owned.Count - 1);
                    Changed("[" + GachaCatalog.GetRarityName(creature.Rarity) + "] " + creature.Name + " 획득!"
                        + (EvolutionPending ? " 같은 형태 3마리 · A/B 진화를 선택하세요." : " 보관함에서 출전 파티를 편성하세요."));
                }
            }
            else
            {
                var definition = GachaCatalog.GetCrystalForRoll(result, Balance);
                _crystals[(int)definition.Kind]++;
                Changed("[" + GachaCatalog.GetRarityName(definition.Rarity) + "] " + definition.Name + " 획득! 크리쳐를 선택해 장착하세요.");
            }
            return true;
        }

        public void SelectParty(int index)
        {
            if (index < 0 || index >= _party.Count)
            {
                SetStatus("선택할 크리쳐가 없습니다.");
                return;
            }
            SelectOwned(_owned.IndexOf(_party[index]));
        }

        public bool ReleaseSelectedCreature()
        {
            if (!CanManageParty())
            {
                return false;
            }
            if (!HasSelection())
            {
                SetStatus("먼저 파티의 크리쳐를 선택하세요.");
                return false;
            }
            var creature = _owned[SelectedOwnedIndex];
            ReturnCrystal(creature.EquippedCrystal);
            _party.Remove(creature);
            _owned.RemoveAt(SelectedOwnedIndex);
            SetSelection(_owned.Count == 0 ? -1 : Math.Min(SelectedOwnedIndex, _owned.Count - 1));
            Changed(GachaCatalog.GetCreature(creature.Kind, Balance).Name + " 방출 · 장착 수정은 반환됩니다. 돈은 환급되지 않습니다.");
            return true;
        }

        public bool TryEquipCrystal(CrystalKind kind)
        {
            if (!CanManageParty())
            {
                return false;
            }
            if (!IsCrystalKind(kind))
            {
                SetStatus("알 수 없는 수정입니다.");
                return false;
            }
            if (!HasSelection())
            {
                SetStatus("먼저 파티의 크리쳐를 선택하세요.");
                return false;
            }
            var creature = _owned[SelectedOwnedIndex];
            if (creature.EquippedCrystal == kind)
            {
                SetStatus("이미 같은 수정을 장착하고 있습니다.");
                return false;
            }
            if (_crystals[(int)kind] == 0)
            {
                SetStatus("보유한 " + GachaCatalog.GetCrystal(kind, Balance).Name + "이 없습니다.");
                return false;
            }
            _crystals[(int)kind]--;
            ReturnCrystal(creature.EquippedCrystal);
            ReplaceSelected(new PartyCreature(creature.Kind, Balance, kind, creature.FormNode, creature.InstanceId));
            Changed(GachaCatalog.GetCreature(creature.Kind, Balance).Name + "에게 " + GachaCatalog.GetCrystal(kind, Balance).Name + " 장착 · " + GachaCatalog.GetCrystal(kind, Balance).Description);
            return true;
        }

        public bool UnequipSelectedCrystal()
        {
            if (!CanManageParty())
            {
                return false;
            }
            if (!HasSelection() || !_owned[SelectedOwnedIndex].EquippedCrystal.HasValue)
            {
                SetStatus("해제할 수정이 없습니다.");
                return false;
            }
            var creature = _owned[SelectedOwnedIndex];
            ReturnCrystal(creature.EquippedCrystal);
            ReplaceSelected(new PartyCreature(creature.Kind, Balance, null, creature.FormNode, creature.InstanceId));
            Changed("수정 해제 · 보관함으로 반환했습니다.");
            return true;
        }

        public void SelectOwned(int index)
        {
            if (index < 0 || index >= _owned.Count)
            {
                return;
            }
            SetSelection(index);
            Changed(_owned[index].Name + " 선택");
        }

        private void SetSelection(int index)
        {
            SelectedOwnedIndex = index;
            SelectedPartyIndex = index < 0 ? -1 : _party.IndexOf(_owned[index]);
        }

        private void ReplaceSelected(PartyCreature replacement)
        {
            int partyIndex = _party.IndexOf(_owned[SelectedOwnedIndex]);
            _owned[SelectedOwnedIndex] = replacement;
            if (partyIndex >= 0)
            {
                _party[partyIndex] = replacement;
            }
        }

        public bool AssignSelected(int slot)
        {
            if (!CanManageParty() || !HasSelection() || slot < 0 || slot >= GachaCatalog.MaxPartySize || slot > _party.Count)
            {
                return false;
            }
            var creature = _owned[SelectedOwnedIndex];
            int previous = _party.IndexOf(creature);
            if (previous == slot)
            {
                return false;
            }
            if (slot == _party.Count)
            {
                if (previous >= 0)
                {
                    _party.RemoveAt(previous);
                }
                _party.Add(creature);
            }
            else
            {
                if (previous >= 0)
                {
                    _party[previous] = _party[slot];
                }
                _party[slot] = creature;
            }
            SetSelection(SelectedOwnedIndex);
            Changed(creature.Name + " 출전 편성 완료");
            return true;
        }

        public bool BenchSelected()
        {
            if (!CanManageParty() || !HasSelection() || !_party.Remove(_owned[SelectedOwnedIndex]))
            {
                return false;
            }
            SetSelection(SelectedOwnedIndex);
            Changed("출전 해제 · 크리쳐는 보관함에 남아 있습니다.");
            return true;
        }

        public bool CanEvolve(PartyCreature creature)
        {
            if (creature == null || creature.Rank >= 3)
            {
                return false;
            }
            int count = 0;
            foreach (var other in _owned)
            {
                if (other.Kind == creature.Kind && other.FormNode == creature.FormNode && ++count >= 3)
                {
                    return true;
                }
            }
            return false;
        }

        public bool TryEvolve(int branch)
        {
            if (!CanManageParty() || branch < 0 || branch > 1)
            {
                return false;
            }
            int candidateIndex = EvolutionCandidateIndex;
            if (candidateIndex < 0)
            {
                return false;
            }
            var primary = _owned[candidateIndex];
            var ingredients = new List<PartyCreature> { primary };
            foreach (var other in _owned)
            {
                if (ingredients.Count == 3)
                {
                    break;
                }
                if (!ReferenceEquals(primary, other) && primary.Kind == other.Kind && primary.FormNode == other.FormNode)
                {
                    ingredients.Add(other);
                }
            }
            var form = CreatureEvolution.Next(primary, branch);
            var evolved = new PartyCreature(primary.Kind, Balance, primary.EquippedCrystal, form.Node, primary.InstanceId);
            int partySlot = _party.FindIndex(ingredients.Contains);
            int ownedSlot = _owned.IndexOf(primary);
            // Commit the complete recipe once: preserve the primary's crystal and return the other two.
            foreach (var ingredient in ingredients)
            {
                if (!ReferenceEquals(ingredient, primary))
                {
                    ReturnCrystal(ingredient.EquippedCrystal);
                }
            }
            _party.RemoveAll(ingredients.Contains);
            _owned.RemoveAll(ingredients.Contains);
            _owned.Insert(Math.Min(ownedSlot, _owned.Count), evolved);
            if (partySlot >= 0)
            {
                _party.Insert(Math.Min(partySlot, _party.Count), evolved);
            }
            SetSelection(_owned.IndexOf(evolved));
            Changed(evolved.Name + " 랭크 " + evolved.Rank + " 진화! 희귀도 유지 · 재료 수정 반환");
            return true;
        }

        public void SetStatus(string message)
        {
            message ??= string.Empty;
            if (Status != message)
            {
                Changed(message);
            }
        }

        private void Changed(string message)
        {
            Status = message;
            Revision++;
        }

        private bool HasSelection()
        {
            return SelectedOwnedIndex >= 0 && SelectedOwnedIndex < _owned.Count;
        }

        private bool CanManageParty()
        {
            if (!BattleInProgress)
            {
                return true;
            }
            SetStatus("전투 중에는 파티와 수정을 변경할 수 없습니다.");
            return false;
        }

        private void ReturnCrystal(CrystalKind? kind)
        {
            if (kind.HasValue)
            {
                _crystals[(int)kind.Value]++;
            }
        }

        private static bool IsCrystalKind(CrystalKind kind)
        {
            return (int)kind >= 0 && (int)kind < GachaCatalog.Crystals.Count;
        }
    }
}

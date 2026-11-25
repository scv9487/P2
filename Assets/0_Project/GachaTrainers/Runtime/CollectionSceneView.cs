using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GachaTrainers
{
    // Only serialized presentation and queued intents; ownership and recipes stay in RunDataContainer.
    public sealed class CollectionSceneView : MonoBehaviour
    {
        [SerializeField] private GameObject _collectionPanel;
        [SerializeField] private GameObject _evolutionPanel;
        [SerializeField] private Button _openButton;
        [SerializeField] private Button[] _closeButtons;
        [SerializeField] private Button[] _rows;
        [SerializeField] private Text[] _rowLabels;
        [SerializeField] private Image[] _rowIcons;
        [SerializeField] private Text _summary;
        [SerializeField] private Text _pageLabel;
        [SerializeField] private Button _previous;
        [SerializeField] private Button _next;
        [SerializeField] private Text _selectedName;
        [SerializeField] private Text _selectedStats;
        [SerializeField] private Text _selectedSpecialty;
        [SerializeField] private Button[] _assignButtons;
        [SerializeField] private Button _benchButton;
        [SerializeField] private Button _releaseButton;
        [SerializeField] private Button _unequipButton;
        [SerializeField] private Button[] _crystalButtons;
        [SerializeField] private Text[] _crystalLabels;
        [SerializeField] private Button _evolveButton;
        [SerializeField] private Text _evolveLabel;
        [SerializeField] private Text _evolutionSummary;
        [SerializeField] private Text[] _formNames;
        [SerializeField] private Text[] _formDescriptions;
        [SerializeField] private Image[] _formImages;
        [SerializeField] private Button[] _formButtons;
        [SerializeField] private CreatureAnimationLibrary _animations;
        private readonly List<(Button button, UnityAction action)> _bindings = new();
        private readonly int[] _indices = new int[8];

        public void Bind(Action<GachaAction, int> enqueue)
        {
            Unbind();
            Bind(_openButton, () => enqueue(GachaAction.OpenCollection, 0));
            foreach (var button in _closeButtons)
            {
                Bind(button, () => enqueue(GachaAction.CloseModal, 0));
            }
            for (int i = 0; i < _rows.Length; i++)
            {
                int row = i;
                Bind(_rows[i], () => enqueue(GachaAction.SelectOwned, _indices[row]));
            }
            for (int i = 0; i < _assignButtons.Length; i++)
            {
                int slot = i;
                Bind(_assignButtons[i], () => enqueue(GachaAction.AssignCreature, slot));
            }
            for (int i = 0; i < _crystalButtons.Length; i++)
            {
                int kind = i;
                Bind(_crystalButtons[i], () => enqueue(GachaAction.EquipCrystal, kind));
            }
            for (int i = 0; i < _formButtons.Length; i++)
            {
                int branch = i;
                Bind(_formButtons[i], () => enqueue(GachaAction.EvolveCreature, branch));
            }
            Bind(_previous, () => enqueue(GachaAction.ChangeCollectionPage, -1));
            Bind(_next, () => enqueue(GachaAction.ChangeCollectionPage, 1));
            Bind(_benchButton, () => enqueue(GachaAction.BenchCreature, 0));
            Bind(_releaseButton, () => enqueue(GachaAction.ReleaseCreature, 0));
            Bind(_unequipButton, () => enqueue(GachaAction.UnequipCrystal, 0));
            Bind(_evolveButton, () => enqueue(GachaAction.OpenEvolution, 0));
        }

        private void Bind(Button button, UnityAction action)
        {
            button.onClick.AddListener(action);
            _bindings.Add((button, action));
        }

        public void Unbind()
        {
            foreach (var binding in _bindings)
            {
                if (binding.button != null)
                {
                    binding.button.onClick.RemoveListener(binding.action);
                }
            }
            _bindings.Clear();
        }

        public void Render(IReadOnlyRunData run, IShopDataReader shop)
        {
            bool collection = shop.Modal == ShopModal.Collection;
            bool evolution = shop.Modal == ShopModal.Evolution;
            _collectionPanel.SetActive(collection);
            _evolutionPanel.SetActive(evolution);
            if (!collection && !evolution)
            {
                return;
            }
            if (evolution)
            {
                RenderEvolution(run);
                return;
            }
            int page = Mathf.Min(shop.CollectionPage, Mathf.Max(0, (run.Owned.Count - 1) / 8));
            _summary.text = $"보유 {run.Owned.Count}마리 · 출전 {run.Party.Count}/3 · 보유 한도 없음     |     전설 선택권 {run.LegendaryTokens}장 (현재 전설 0종)";
            _pageLabel.text = $"{page + 1} / {Mathf.Max(1, (run.Owned.Count + 7) / 8)}";
            _previous.interactable = page > 0;
            _next.interactable = (page + 1) * 8 < run.Owned.Count;
            for (int row = 0; row < _rows.Length; row++)
            {
                int index = page * 8 + row;
                _indices[row] = index;
                bool occupied = index < run.Owned.Count;
                _rows[row].interactable = occupied;
                _rowIcons[row].enabled = occupied;
                _rows[row].GetComponent<Image>().color = index == run.SelectedOwnedIndex && occupied
                    ? new Color(0.16f, 0.32f, 0.33f) : new Color(0.10f, 0.15f, 0.23f);
                if (!occupied)
                {
                    _rowLabels[row].text = "빈 보관 슬롯";
                    continue;
                }
                var creature = run.Owned[index];
                int slot = PartyIndex(run, creature);
                _rowLabels[row].text = $"{creature.Name}\n<size=17>[{GachaCatalog.GetRarityName(creature.Rarity)}] 랭크 {creature.Rank} {creature.Form.Path}\n"
                    + (slot < 0 ? "보관 중" : $"출전 {slot + 1}") + (creature.EquippedCrystal.HasValue ? " · 수정 장착" : "") + "</size>";
                Portrait(_rowIcons[row], creature.Form);
            }
            bool selected = run.SelectedOwnedIndex >= 0 && run.SelectedOwnedIndex < run.Owned.Count;
            PartyCreature entry = selected ? run.Owned[run.SelectedOwnedIndex] : null;
            _selectedName.text = selected ? $"{entry.Name}  ·  랭크 {entry.Rank} / {GachaCatalog.GetRarityName(entry.Rarity)}" : "크리쳐를 선택하세요";
            _selectedSpecialty.text = selected ? entry.Form.Specialty + "\n" + (entry.EquippedCrystal.HasValue
                ? GachaCatalog.GetCrystal(entry.EquippedCrystal.Value, run.Balance).Name + " 장착 중" : "수정 미장착") : "가챠 머신에서 동료를 모아보세요.";
            _selectedStats.text = selected ? entry.Stats.Describe() : "출전 파티는 최대 3마리입니다.\n같은 형태 3마리를 모으면 A/B 진화를 선택합니다.\n다른 진화 계통은 서로 합성되지 않습니다.";
            for (int i = 0; i < _assignButtons.Length; i++)
            {
                _assignButtons[i].interactable = selected && i <= run.Party.Count && PartyIndex(run, entry) != i;
            }
            _benchButton.interactable = selected && PartyIndex(run, entry) >= 0;
            _releaseButton.interactable = selected;
            _unequipButton.interactable = selected && entry.EquippedCrystal.HasValue;
            for (int i = 0; i < _crystalButtons.Length; i++)
            {
                var crystal = GachaCatalog.GetCrystal((CrystalKind)i, run.Balance);
                int count = run.GetCrystalCount(crystal.Kind);
                _crystalLabels[i].text = $"[{GachaCatalog.GetRarityName(crystal.Rarity)}] {crystal.Name} ×{count}\n<size=16>{crystal.Description}</size>";
                _crystalLabels[i].color = GachaSceneView.CrystalColor(crystal.Kind);
                _crystalButtons[i].interactable = selected && count > 0 && entry.EquippedCrystal != crystal.Kind;
            }
            _evolveButton.interactable = run.EvolutionPending;
            _evolveLabel.text = run.EvolutionPending ? "진화 가능!  A / B 선택하기" : "진화: 같은 형태 3마리 필요 · 최대 랭크 3";
        }

        private void RenderEvolution(IReadOnlyRunData run)
        {
            int index = run.EvolutionCandidateIndex;
            bool available = index >= 0;
            foreach (var button in _formButtons)
            {
                button.interactable = available;
            }
            if (!available)
            {
                _evolutionSummary.text = "진화 가능한 크리쳐가 없습니다.";
                return;
            }
            var creature = run.Owned[index];
            _evolutionSummary.text = $"{creature.Name} 3마리 → 랭크 {creature.Rank + 1} 1마리\n희귀도 [{GachaCatalog.GetRarityName(creature.Rarity)}] 유지 · 선택한 개체의 수정 유지 / 나머지 수정 반환";
            for (int branch = 0; branch < 2; branch++)
            {
                var form = CreatureEvolution.Next(creature, branch);
                var stats = CombatStats.Build(form.Kind, form.Node, creature.EquippedCrystal, run.Balance);
                _formNames[branch].text = (branch == 0 ? "A · " : "B · ") + form.Name;
                _formDescriptions[branch].text = form.Specialty + "\n\n"
                    + $"체력 {creature.MaxHealth} → {stats.MaxHealth}\n물리 공격 {creature.Stats.PhysicalAttackPower:0.#} → {stats.PhysicalAttackPower:0.#}\n"
                    + $"마법 공격 {creature.Stats.MagicAttackPower:0.#} → {stats.MagicAttackPower:0.#}\n공격 속도 {creature.AttackSpeed:0.##} → {stats.AttackSpeed:0.##}\n"
                    + $"물리 / 마법 저항 {stats.PhysicalResistance:0} / {stats.MagicResistance:0}\n치명타 {stats.CriticalChance:0}% · 가드 {stats.GuardChance:0}%\n"
                    + $"흡혈 {stats.LifeSteal:0}% · 재사용 감소 {stats.CooldownReduction:0}%";
                Portrait(_formImages[branch], form);
            }
        }

        private void Portrait(Image image, CreatureForm form)
        {
            image.sprite = _animations.Get(form.Kind, CreatureFacing.South, CreatureAnimationState.Idle, 0);
            image.color = form.Tint;
            image.preserveAspect = true;
        }

        private static int PartyIndex(IReadOnlyRunData run, PartyCreature creature)
        {
            for (int i = 0; i < run.Party.Count; i++)
            {
                if (ReferenceEquals(run.Party[i], creature))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}

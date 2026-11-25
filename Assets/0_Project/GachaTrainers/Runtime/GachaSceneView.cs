using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GachaTrainers
{
    // Serialized scene presentation only. Buttons enqueue intent; readers supply all game state.
    public sealed class GachaSceneView : MonoBehaviour
    {
        [SerializeField] private Button _startButton;
        [SerializeField] private Text _startingRulesText;
        [SerializeField] private Text _creatureMachinePriceText;
        [SerializeField] private Text _crystalMachinePriceText;
        private GachaBalance _displayedBalance;
        [SerializeField] private Button _quitButton;
        [SerializeField] private Button _returnTitleButton;
        [SerializeField] private Button _battleButton;
        [SerializeField] private Text _battleButtonText;
        [SerializeField] private CanvasGroup _shopContent;
        [SerializeField] private Text _currencyText;
        [SerializeField] private Text _partyCountText;
        [SerializeField] private Button[] _partyButtons;
        [SerializeField] private Text[] _partyNames;
        [SerializeField] private Text[] _partyStats;
        [SerializeField] private Text[] _partyPassives;
        [SerializeField] private Image[] _partyIcons;
        [SerializeField] private Button[] _crystalButtons;
        [SerializeField] private Text[] _crystalTexts;
        [SerializeField] private Button _releaseButton;
        [SerializeField] private Button _unequipButton;
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _promptText;
        [SerializeField] private Transform _player;
        [SerializeField] private Transform _playerVisual;
        [SerializeField] private SpriteRenderer[] _machineHighlights;
        [SerializeField] private GameObject _modalRoot;
        [SerializeField] private GameObject _machinePanel;
        [SerializeField] private GameObject _returnPanel;
        [SerializeField] private Text _modalTitle;
        [SerializeField] private Text _modalSubtitle;
        [SerializeField] private Text[] _poolNames;
        [SerializeField] private Text[] _poolDescriptions;
        [SerializeField] private Image[] _poolIcons;
        [SerializeField] private RectTransform[] _poolCards;
        [SerializeField] private Sprite _circleSprite;
        [SerializeField] private Sprite _squareSprite;
        [SerializeField] private Text _modalStatusText;
        [SerializeField] private Button _buyButton;
        [SerializeField] private Text _buyButtonText;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _confirmReturnButton;
        [SerializeField] private Button _cancelReturnButton;
        private readonly List<(Button button, UnityAction action)> _bindings = new();
        [SerializeField] private CollectionSceneView _collectionView;
        [SerializeField] private Button _poolPageButton;
        [SerializeField] private Text _poolPageLabel;
        private int _lastPoolPage = -1;
        private int _lastRevision = -1;
        private ShopModal _lastModal = (ShopModal)(-1);
        private GachaMachineKind _lastMachine;
        private static readonly Color Mint = new(0.37f, 0.93f, 0.76f);
        private static readonly Color Violet = new(0.73f, 0.63f, 1f);
        private static readonly Color Panel = new(0.105f, 0.15f, 0.23f);

        public void Bind(Action<GachaAction, int> enqueue)
        {
            Unbind();
            _collectionView?.Bind(enqueue);
            Bind(_poolPageButton, () => enqueue(GachaAction.ChangePoolPage, 0));
            Bind(_startButton, () => enqueue(GachaAction.StartRun, 0));
            Bind(_quitButton, () => enqueue(GachaAction.Quit, 0));
            Bind(_returnTitleButton, () => enqueue(GachaAction.ReturnTitle, 0));
            Bind(_battleButton, () => enqueue(GachaAction.BeginBattle, 0));
            Bind(_releaseButton, () => enqueue(GachaAction.ReleaseCreature, 0));
            Bind(_unequipButton, () => enqueue(GachaAction.UnequipCrystal, 0));
            Bind(_buyButton, () => enqueue(GachaAction.Buy, 0));
            Bind(_closeButton, () => enqueue(GachaAction.CloseModal, 0));
            Bind(_confirmReturnButton, () => enqueue(GachaAction.ConfirmReturnTitle, 0));
            Bind(_cancelReturnButton, () => enqueue(GachaAction.CloseModal, 0));
            if (_partyButtons != null)
            {
                for (var i = 0; i < _partyButtons.Length; i++)
                {
                    var index = i;
                    Bind(_partyButtons[i], () => enqueue(GachaAction.SelectParty, index));
                }
            }
            if (_crystalButtons != null)
            {
                for (var i = 0; i < _crystalButtons.Length; i++)
                {
                    var index = i;
                    Bind(_crystalButtons[i], () => enqueue(GachaAction.EquipCrystal, index));
                }
            }
        }

        private void Bind(Button button, UnityAction action)
        {
            if (button == null)
            {
                return;
            }
            button.onClick.AddListener(action);
            _bindings.Add((button, action));
        }

        public void Unbind()
        {
            _collectionView?.Unbind();
            foreach (var binding in _bindings)
            {
                if (binding.button != null)
                {
                    binding.button.onClick.RemoveListener(binding.action);
                }
            }
            _bindings.Clear();
        }

        public void Render(IReadOnlyRunData run, IShopDataReader shop, GachaBalance balance = null)
        {
            balance ??= run?.Balance ?? GachaBalance.Default;
            if (!ReferenceEquals(_displayedBalance, balance))
            {
                _displayedBalance = balance;
                if (_startingRulesText != null)
                {
                    _startingRulesText.text = "시작 자금 " + balance.StartingGold + " G  ·  새 모험마다 모든 진행 초기화";
                }
                if (_creatureMachinePriceText != null)
                {
                    _creatureMachinePriceText.text = "크리쳐 뽑기  ·  " + balance.CreaturePrice + " G";
                }
                if (_crystalMachinePriceText != null)
                {
                    _crystalMachinePriceText.text = "수정 뽑기  ·  " + balance.CrystalPrice + " G";
                }
            }
            if (shop == null || run == null)
            {
                return;
            }
            _collectionView?.Render(run, shop);
            _player.position = new Vector3(shop.Position.x, shop.Position.y, 0f);
            _playerVisual.localPosition = new Vector3(0f,
                shop.IsMoving ? Mathf.Abs(Mathf.Sin(Time.unscaledTime * 13f)) * 0.07f : 0f, 0f);
            var open = shop.Modal != ShopModal.None;
            _shopContent.interactable = !open;
            _shopContent.blocksRaycasts = !open;
            _modalRoot.SetActive(open);
            _machinePanel.SetActive(shop.Modal == ShopModal.Machine);
            _returnPanel.SetActive(shop.Modal == ShopModal.ReturnTitle);
            for (var i = 0; i < _machineHighlights.Length; i++)
            {
                _machineHighlights[i].color = shop.NearestMachine == (GachaMachineKind)i && !open
                    ? new Color(0.45f, 1f, 0.83f, 0.42f)
                    : new Color(0.4f, 0.65f, 0.75f, 0.08f);
            }
            _promptText.text = open ? "ESC  닫기" : shop.NearestMachine.HasValue
                ? "E  " + (shop.NearestMachine == GachaMachineKind.Creature ? "크리쳐 머신" : "특성 수정 머신") + " 이용하기"
                : "W A S D 이동  ·  B 보관함 / 진화  ·  머신 앞으로 다가가세요";
            if (_lastRevision == run.Revision && _lastModal == shop.Modal && _lastMachine == shop.ActiveMachine && _lastPoolPage == shop.PoolPage)
            {
                return;
            }
            _lastRevision = run.Revision;
            _lastModal = shop.Modal;
            _lastMachine = shop.ActiveMachine;
            _lastPoolPage = shop.PoolPage;
            _currencyText.text = run.Money.ToString("N0") + "  G";
            _partyCountText.text = "출전 " + run.Party.Count + "/3  ·  보유 " + run.Owned.Count;
            _statusText.text = run.Status;
            if (_battleButton != null)
            {
                _battleButton.interactable = run.Party.Count > 0 && !run.RunCompleted;
                _battleButtonText.text = run.RunCompleted ? "모험 완료!" : run.Party.Count > 0
                    ? "스테이지 " + run.StageNumber + "/" + GachaStageSheet.StageCount + " 도전  →" : "크리쳐를 먼저 뽑으세요";
            }
            var hasSelected = run.SelectedPartyIndex >= 0 && run.SelectedPartyIndex < run.Party.Count;
            _releaseButton.interactable = hasSelected;
            _unequipButton.interactable = hasSelected && run.Party[run.SelectedPartyIndex].EquippedCrystal.HasValue;
            for (var i = 0; i < _partyButtons.Length; i++)
            {
                var occupied = i < run.Party.Count;
                _partyButtons[i].interactable = occupied;
                _partyButtons[i].GetComponent<Image>().color = i == run.SelectedPartyIndex && occupied
                    ? new Color(0.15f, 0.30f, 0.32f) : Panel;
                _partyIcons[i].color = occupied ? CreatureColor(run.Party[i].Kind) : new Color(0.21f, 0.27f, 0.35f);
                if (!occupied)
                {
                    _partyNames[i].text = "빈 파티 슬롯 " + (i + 1);
                    _partyStats[i].text = "크리쳐 머신에서 동료를 만나세요";
                    _partyPassives[i].text = "—";
                    continue;
                }
                var creature = run.Party[i];
                _partyNames[i].text = creature.Name + "  R" + creature.Rank + (i == run.SelectedPartyIndex ? " •" : "");
                _partyStats[i].text = "체력 " + creature.MaxHealth + "  공격 " + creature.Attack + "  속도 " + creature.AttackSpeed.ToString("0.00");
                if (creature.EquippedCrystal.HasValue)
                {
                    var crystal = GachaCatalog.GetCrystal(creature.EquippedCrystal.Value, balance);
                    _partyPassives[i].text = "[" + GachaCatalog.GetRarityName(crystal.Rarity) + "] " + crystal.Name;
                    _partyPassives[i].color = CrystalColor(crystal.Kind);
                }
                else
                {
                    _partyPassives[i].text = "수정 미장착";
                    _partyPassives[i].color = Mint;
                }
            }
            for (var i = 0; i < _crystalButtons.Length; i++)
            {
                var kind = (CrystalKind)i;
                var count = run.GetCrystalCount(kind);
                var crystal = GachaCatalog.GetCrystal(kind, balance);
                _crystalTexts[i].text = "<color=#" + ColorUtility.ToHtmlStringRGB(CrystalColor(kind)) + ">["
                    + GachaCatalog.GetRarityName(crystal.Rarity) + "] " + crystal.Name + "  ×" + count + "</color>\n"
                    + "<size=15>" + crystal.Description + "</size>";
                _crystalButtons[i].interactable = hasSelected && count > 0;
            }
            if (shop.Modal != ShopModal.Machine)
            {
                return;
            }
            var isCreature = shop.ActiveMachine == GachaMachineKind.Creature;
            var price = isCreature ? balance.CreaturePrice : balance.CrystalPrice;
            _modalTitle.text = isCreature ? "새로운 동료를 만날 시간" : "작은 수정, 새로운 가능성";
            _modalSubtitle.text = isCreature
                ? balance.RaritySummary(true) + " (전설 0종)\n보유 한도 없음 · 출전 최대 3마리 · 같은 형태 3마리 → A/B 진화"
                : "각인 수정 12종 / " + balance.RaritySummary(false) + "\n크리쳐당 수정 1개 · 같은 등급 내 동일 확률";
            if (_poolPageButton != null)
            {
                _poolPageButton.gameObject.SetActive(!isCreature);
                _poolPageLabel.text = "각인 목록 " + (shop.PoolPage + 1) + "/2  →";
            }
            for (var i = 0; i < _poolCards.Length; i++)
            {
                bool visible = !isCreature || i < GachaCatalog.Creatures.Count;
                _poolCards[i].gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }
                LayoutPoolCard(i);
                if (isCreature)
                {
                    CreatureKind kind = (CreatureKind)i;
                    var moves = BattleCatalog.Get(kind, balance);
                    _poolNames[i].text = GachaCatalog.GetCreature(kind, balance).Name + "  ·  " + GachaCatalog.GetCreatureDropPercent(kind, balance).ToString("0.##") + "%";
                    _poolNames[i].color = CreatureColor(kind);
                    _poolDescriptions[i].text = "[" + GachaCatalog.GetRarityName(GachaCatalog.GetCreature(kind, balance).Rarity) + "] " + moves.SpecialName + "\n" + moves.SpecialDescription;
                }
                else
                {
                    var crystal = GachaCatalog.GetCrystal((CrystalKind)(i + shop.PoolPage * 6), balance);
                    _poolNames[i].text = "[" + GachaCatalog.GetRarityName(crystal.Rarity) + "] " + crystal.Name
                        + "  ·  " + GachaCatalog.GetCrystalDropPercent(crystal.Kind, balance).ToString("0.##") + "%";
                    _poolNames[i].color = CrystalColor(crystal.Kind);
                    _poolDescriptions[i].text = crystal.Description;
                }
                _poolIcons[i].color = isCreature ? CreatureColor((CreatureKind)i) : CrystalColor((CrystalKind)(i + shop.PoolPage * 6));
                _poolIcons[i].sprite = isCreature ? _circleSprite : _squareSprite;
                _poolIcons[i].rectTransform.localRotation = Quaternion.Euler(0, 0, isCreature ? 0f : 45f);
                _poolIcons[i].rectTransform.localScale = Vector3.one * (isCreature ? 1f : 0.75f);
            }
            _modalStatusText.text = run.Status;
            _buyButton.interactable = run.Money >= price;
            _buyButtonText.text = run.Money < price ? "골드가 부족합니다" : price + " G  ·  한 번 뽑기";
            _buyButton.GetComponent<Image>().color = isCreature ? Mint : Violet;
        }

        private void LayoutPoolCard(int index)
        {
            _poolCards[index].anchoredPosition = new Vector2(44 + index % 2 * 432, -184 - index / 2 * 124);
            _poolCards[index].sizeDelta = new Vector2(420, 116);
            SetRect(_poolIcons[index].rectTransform, 16, 16, 36, 36);
            _poolIcons[index].rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _poolIcons[index].rectTransform.anchoredPosition = new Vector2(34, -34);
            SetRect(_poolNames[index].rectTransform, 65, 12, 339, 30);
            SetRect(_poolDescriptions[index].rectTransform, 18, 48, 384, 62);
            _poolNames[index].fontSize = 19;
            _poolDescriptions[index].fontSize = 17;
        }

        private static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        public static Color CreatureColor(CreatureKind kind)
        {
            return kind switch
            {
                CreatureKind.EmberFox => new Color(1f, 0.57f, 0.36f),
                CreatureKind.MossTurtle => new Color(0.42f, 0.85f, 0.58f),
                CreatureKind.VoltRabbit => new Color(1f, 0.83f, 0.39f),
                CreatureKind.FrostOwl => new Color(0.58f, 0.86f, 1f),
                CreatureKind.TideOtter => new Color(0.32f, 0.83f, 0.81f),
                CreatureKind.StoneBoar => new Color(0.76f, 0.64f, 0.53f),
                _ => Color.white
            };
        }

        public static Color CrystalColor(CrystalKind kind)
        {
            return GachaCatalog.GetCrystal(kind).Rarity switch
            {
                CrystalRarity.Common => new Color(0.78f, 0.84f, 0.9f),
                CrystalRarity.Uncommon => new Color(0.42f, 0.88f, 0.73f),
                CrystalRarity.Rare => new Color(0.54f, 0.73f, 1f),
                _ => new Color(1f, 0.82f, 0.39f)
            };
        }
    }
}

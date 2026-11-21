using System;
using System.Collections.Generic;
using System.Globalization;
using Refactor.Main;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Refactor.UI
{
    public sealed partial class DungeonView : MonoBehaviour
    {
        [SerializeField] private Image Background;
        [SerializeField] private Text DungeonNameText;
        [SerializeField] private Text FloorText;
        [SerializeField] private Text MapHeadingText;
        [SerializeField] private Text MapHintText;
        [SerializeField] private Text LegendText;
        [SerializeField] private Text CurrentNodeText;
        [SerializeField] private Text NodeNameText;
        [SerializeField] private Text NodeDescriptionText;
        [SerializeField] private Text CommandsHeadingText;
        [SerializeField] private Text VisitorsHeadingText;
        [SerializeField] private Text EmptyVisitorsText;
        [SerializeField] private Text StatusText;
        [SerializeField] private RectTransform NodeButtons;
        [SerializeField] private RectTransform NodeLines;
        [SerializeField] private Button NodeTemplate;
        [SerializeField] private RectTransform VisitorButtons;
        [SerializeField] private Button VisitorTemplate;
        [SerializeField] private Button RestButton;
        [SerializeField] private Button TalkButton;
        [SerializeField] private Button NextFloorButton;
        [SerializeField] private Button DungeonReturnTownButton;
        [SerializeField] private CanvasGroup MapControls;
        [SerializeField] private GameObject BattleRoot;
        [SerializeField] private CanvasGroup BattleControls;
        [SerializeField] private Text BattleTitleText;
        [SerializeField] private Text EnemiesHeadingText;
        [SerializeField] private Text AlliesHeadingText;
        [SerializeField] private Text BattleReadyText;
        [SerializeField] private Text BattleHintText;
        [SerializeField] private RectTransform EnemyCards;
        [SerializeField] private RectTransform AllyCards;
        [SerializeField] private Button UnitCardTemplate;
        [SerializeField] private Button AttackButton;
        [SerializeField] private Button BattleReturnTownButton;
        [SerializeField] private GameObject DungeonDetailsRoot;
        [SerializeField] private CanvasGroup DetailsControls;
        [SerializeField] private Text DetailsTitleText;
        [SerializeField] private Text DetailsBodyText;
        [SerializeField] private Button DungeonDetailsCloseButton;

        private DungeonSheet _sheet;
        private DungeonContent _content;
        private TownSheet _townSheet;
        private TownContent _townContent;
        private UIInputStream _input;
        private readonly List<(Button Button, UnityAction Action)> _bindings = new();
        private readonly Dictionary<string, Button> _nodes = new();
        private readonly List<GameObject> _lines = new();
        private readonly List<Button> _visitors = new();
        private readonly Dictionary<string, UnitCard> _allies = new();
        private readonly Dictionary<string, UnitCard> _enemies = new();
        private int _renderedFloor;
        private string _renderedNode;
        private string _context;
        private string _readyAllyId;
        private bool _choosingTarget;
        private bool _initialized;
        private bool _resultDetails;
        private LootSettlementView _lootSettlementView;
        private CharacterPartyView _characterView;
        private HeroMenuView _heroMenuView;
        private IAdventureProgressReader _characterProgress;
        private string _partyKey;
        private string _nodeCharacterId;

        private sealed class UnitCard
        {
            public Button Button;
            public Text Name;
            public Text Hp;
            public Text Gauge;
            public Text Speed;
            public Text Energy;
            public Text Effects;
            public RectTransform HpFill;
            public RectTransform GaugeFill;
            public Image Portrait;
            public RatGraphic Rat;
        }

        public void Initialize(DungeonSheet sheet, DungeonContent content, TownSheet townSheet, TownContent townContent, UIInputStream input)
        {
            UnbindInputs();
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _content = content != null ? content : throw new ArgumentNullException(nameof(content));
            _townSheet = townSheet ?? throw new ArgumentNullException(nameof(townSheet));
            _townContent = townContent != null ? townContent : throw new ArgumentNullException(nameof(townContent));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            TownView.SetArtwork(Background, content.Background != null ? content.Background : townContent.TownBackground);
            MapHeadingText.text = sheet.Text("map.title");
            MapHintText.text = sheet.Text("map.hint");
            LegendText.text = sheet.Text("map.legend");
            CommandsHeadingText.text = sheet.Text("node.commands");
            VisitorsHeadingText.text = sheet.Text("node.visitors");
            EmptyVisitorsText.text = townSheet.Text("empty.visitors");
            EnemiesHeadingText.text = sheet.Text("battle.enemies");
            AlliesHeadingText.text = sheet.Text("battle.allies");
            SetButtonLabel(RestButton, "command.rest");
            SetButtonLabel(TalkButton, "command.talk");
            SetButtonLabel(NextFloorButton, "command.next_floor");
            SetButtonLabel(AttackButton, "command.attack");
            SetButtonLabel(DungeonReturnTownButton, "map.return_town");
            SetButtonLabel(BattleReturnTownButton, "map.return_town");
            SetButtonLabel(DungeonDetailsCloseButton, "command.close");
            Bind(RestButton, () => _input.AddInput(UIInputType.DungeonRest, null, _context));
            Bind(TalkButton, () => _input.AddInput(_characterView == null ? UIInputType.DungeonTalk : UIInputType.CharacterOpen,
                _characterView == null ? null : _nodeCharacterId, _context));
            Bind(NextFloorButton, () => _input.AddInput(UIInputType.DungeonNextFloor, null, _context));
            Bind(DungeonReturnTownButton, () => _input.AddInput(UIInputType.DungeonReturnTown));
            Bind(BattleReturnTownButton, () => _input.AddInput(UIInputType.DungeonReturnTown));
            Bind(DungeonDetailsCloseButton, () => _input.AddInput(_resultDetails
                ? UIInputType.DungeonReturnTown : UIInputType.DungeonCloseDetails));
            // 대상 선택 모드는 표시 상태일 뿐이다. 실제 공격 권한과 피해는 Handler/전투 데이터가 판정한다.
            Bind(AttackButton, SelectBasicAttack);
            _renderedFloor = 0;
            _renderedNode = null;
            _readyAllyId = null;
            _choosingTarget = false;
            _initialized = true;
        }

        public void Render(IDungeonDataReader state, bool locked, string status)
        {
            bool battleVisible = state.InCombat || state.IsDefeated || state.IsCleared;
            bool modalOpen = state.DetailsOpen || state.SettlementOpen;
            _context = state.Context;
            DungeonNameText.text = Format("selector.card_title_format", state.Dungeon.Id, state.Dungeon.Name);
            FloorText.text = Format("map.floor_format", state.Floor, state.Dungeon.FloorCount);
            StatusText.text = string.IsNullOrEmpty(status) ? state.Status : status;
            // 전투 화면의 제목/카드 뒤로 탐색 화면의 글자가 비치지 않게 표시 계층만 숨긴다.
            MapControls.alpha = battleVisible ? 0f : 1f;
            MapControls.interactable = _initialized && !locked && !battleVisible && !modalOpen;
            MapControls.blocksRaycasts = !battleVisible && !modalOpen;
            if (_renderedFloor != state.Floor)
            {
                BuildMap(state);
                _renderedFloor = state.Floor;
                _renderedNode = null;
            }
            if (_renderedNode != state.NodeId)
            {
                RenderNode(state);
                _renderedNode = state.NodeId;
            }
            foreach (var entry in _nodes)
            {
                bool current = state.NodeId == entry.Key;
                bool canMove = state.CanMoveTo(entry.Key);
                entry.Value.interactable = !locked && !battleVisible && !modalOpen && canMove;
                entry.Value.GetComponent<Image>().color = current
                    ? new Color(0.38f, 0.29f, 0.16f)
                    : canMove ? new Color(0.20f, 0.24f, 0.22f) : new Color(0.09f, 0.105f, 0.11f);
                entry.Value.transform.Find("CurrentMarker").gameObject.SetActive(current);
                entry.Value.transform.Find("ResolvedMarker").gameObject.SetActive(state.IsNodeResolved(entry.Key));
            }
            RestButton.interactable = !locked && state.CanRest;
            TalkButton.interactable = !locked && state.CanTalk;
            NextFloorButton.interactable = !locked && state.CanNextFloor;
            DungeonReturnTownButton.interactable = !locked && state.CanReturnTown;
            RenderBattle(state, locked || state.SettlementOpen, battleVisible);
            DungeonDetailsRoot.SetActive(state.DetailsOpen && !state.SettlementOpen);
            DetailsControls.interactable = _initialized && !locked && !state.SettlementOpen;
            DetailsControls.blocksRaycasts = !state.SettlementOpen;
            _resultDetails = state.IsCleared || state.IsDefeated;
            SetButtonLabel(DungeonDetailsCloseButton, _resultDetails ? "map.return_town" : "command.close");
            if (state.DetailsOpen)
            {
                DetailsTitleText.text = state.DetailsTitle;
                DetailsBodyText.text = state.DetailsBody;
            }
        }

        public void ConfigureLoot(LootSheet sheet, UIInputStream input)
        {
            if (_lootSettlementView == null)
            {
                _lootSettlementView = LootSettlementView.Create(transform, DungeonNameText.font);
            }
            _lootSettlementView.Initialize(sheet, input);
        }

        // MainFlow가 기존 Presenter의 표시 갱신 다음에 호출한다. 정산 원본과 저장은 View 밖에 있다.
        public void RenderLoot(ILootSettlementReader settlement, bool open, bool locked, bool canWrite, string error)
        {
            if (_lootSettlementView != null)
            {
                _lootSettlementView.Render(settlement, open, locked, canWrite, error);
            }
        }

        public void ConfigureCharacters(CharacterSheet sheet, TownContent portraits, UIInputStream input)
        {
            if (_characterView == null)
            {
                _characterView = CharacterPartyView.Create(transform, DungeonNameText.font);
            }
            _characterView.Initialize(sheet, _townSheet, portraits, input);
            _partyKey = null;
            TalkButton.GetComponentInChildren<Text>().text = sheet.Text("action.interact");
            // 지도 안내와 전투 아군 모두 좌하단 플레이어 메뉴의 위쪽에서 끝나게 배치한다.
            ((RectTransform)NodeButtons.parent).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 385);
            NodeButtons.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 385);
            NodeLines.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 385);
            var nodePanel = (RectTransform)NodeNameText.transform.parent;
            nodePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 505);
            CurrentNodeText.rectTransform.anchoredPosition = new Vector2(54, -638);
            CurrentNodeText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 34);
            LegendText.rectTransform.anchoredPosition = new Vector2(338, -631);
            LegendText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 24);
            MapHintText.rectTransform.anchoredPosition = new Vector2(338, -655);
            MapHintText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 24);
            StatusText.rectTransform.SetParent(nodePanel, false);
            StatusText.rectTransform.anchoredPosition = new Vector2(22, -454);
            StatusText.rectTransform.sizeDelta = new Vector2(214, 44);
            StatusText.fontSize = 12;
            BattleTitleText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 56);
            EnemiesHeadingText.rectTransform.anchoredPosition = new Vector2(64, -88);
            EnemiesHeadingText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 29);
            EnemyCards.anchoredPosition = new Vector2(64, -125);
            BattleReadyText.rectTransform.anchoredPosition = new Vector2(64, -356);
            BattleReadyText.rectTransform.sizeDelta = new Vector2(830, 49);
            ((RectTransform)AttackButton.transform).anchoredPosition = new Vector2(930, -356);
            ((RectTransform)AttackButton.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 50);
            ((RectTransform)BattleReturnTownButton.transform).anchoredPosition = new Vector2(930, -356);
            var battleDivider = AttackButton.transform.parent.Find("Line") as RectTransform;
            if (battleDivider != null)
            {
                battleDivider.anchoredPosition = new Vector2(64, -350);
            }
            AlliesHeadingText.rectTransform.anchoredPosition = new Vector2(64, -414);
            AlliesHeadingText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 29);
            AllyCards.anchoredPosition = new Vector2(64, -448);
            BattleHintText.rectTransform.anchoredPosition = new Vector2(360, -702);
            BattleHintText.rectTransform.sizeDelta = new Vector2(856, 62);
            _renderedFloor = 0;
        }

        public void ConfigureHeroMenu(SkillSheet skills, LootSheet loot, CharacterSheet characters,
            TownContent portraits, UIInputStream input)
        {
            if (_heroMenuView == null)
            {
                _heroMenuView = HeroMenuView.Create(transform, DungeonNameText.font);
            }
            _heroMenuView.Initialize(skills, loot, characters, portraits, input);
            _characterView.ConfigureSkills(skills);
            ConfigureBattleSkills(skills);
        }

        public void RenderHeroMenu(IAdventureProgressReader progress, IHeroMenuReader menu, bool locked, bool canWrite)
        {
            if (_heroMenuView != null)
            {
                _heroMenuView.Render(progress, menu, locked, canWrite);
            }
        }

        public void ConfigureEquipment(EquipmentSheet equipment)
        {
            if (_heroMenuView != null)
            {
                _heroMenuView.ConfigureEquipment(equipment);
            }
        }

        public void RenderCharacters(IAdventureProgressReader progress, ICharacterInteractionReader interaction,
            bool locked, bool canWrite, IDungeonDataReader dungeon = null)
        {
            if (_characterView == null || dungeon == null)
            {
                return;
            }
            _characterProgress = progress;
            string partyKey = string.Join(",", progress.PartyIds);
            if (_partyKey != partyKey)
            {
                _partyKey = partyKey;
                RenderNode(dungeon);
            }
            _characterView.Render(progress, interaction, locked, canWrite, dungeon.DetailsOpen || dungeon.SettlementOpen, dungeon);
            if (interaction.Open)
            {
                MapControls.interactable = false;
                MapControls.blocksRaycasts = false;
                BattleControls.interactable = false;
                BattleControls.blocksRaycasts = false;
                DetailsControls.interactable = false;
                DetailsControls.blocksRaycasts = false;
                DungeonDetailsRoot.SetActive(false);
            }
        }

        private void BuildMap(IDungeonDataReader state)
        {
            foreach (var button in _nodes.Values)
            {
                DestroyButton(button);
            }
            _nodes.Clear();
            foreach (var line in _lines)
            {
                Destroy(line);
            }
            _lines.Clear();
            foreach (var node in state.Nodes)
            {
                foreach (var neighborId in node.NeighborIds)
                {
                    if (string.CompareOrdinal(node.Id, neighborId) < 0)
                    {
                        var neighbor = FindNode(state, neighborId);
                        DrawLine(node.Id, neighborId, NodePosition(node), NodePosition(neighbor));
                    }
                }
                var button = Instantiate(NodeTemplate, NodeButtons);
                button.name = "Node_" + node.Id;
                ((RectTransform)button.transform).anchoredPosition = NodePosition(node);
                button.transform.Find("Icon").GetComponent<Text>().text = !string.IsNullOrEmpty(node.Icon)
                    ? node.Icon : _sheet.Text("icon." + node.Type.ToString().ToLowerInvariant());
                button.transform.Find("Name").GetComponent<Text>().text = node.Name;
                string targetId = node.Id;
                button.onClick.AddListener(() => _input.AddInput(UIInputType.DungeonMove, targetId, _context));
                button.gameObject.SetActive(true);
                _nodes.Add(node.Id, button);
            }
        }

        private Vector2 NodePosition(DungeonNodeDefinition node)
        {
            return new Vector2(62f + node.X * (NodeButtons.rect.width - 124f), -53f - node.Y * (NodeButtons.rect.height - 106f));
        }

        private static DungeonNodeDefinition FindNode(IDungeonDataReader state, string id)
        {
            foreach (var node in state.Nodes)
            {
                if (node.Id == id)
                {
                    return node;
                }
            }
            throw new InvalidOperationException("던전 표시 노드를 찾을 수 없습니다: " + id);
        }

        private void DrawLine(string fromId, string toId, Vector2 from, Vector2 to)
        {
            var line = new GameObject("Line_" + fromId + "_" + toId, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)line.transform;
            rect.SetParent(NodeLines, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = from;
            Vector2 delta = to - from;
            rect.sizeDelta = new Vector2(delta.magnitude, 2f);
            rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            var image = line.GetComponent<Image>();
            image.color = new Color(0.39f, 0.36f, 0.27f, 0.8f);
            image.raycastTarget = false;
            _lines.Add(line);
        }

        private void RenderNode(IDungeonDataReader state)
        {
            var node = FindNode(state, state.NodeId);
            _nodeCharacterId = state.VisitorId;
            NodeNameText.text = node.Name;
            NodeDescriptionText.text = node.Description;
            CurrentNodeText.text = Format("map.current_format", node.Name);
            RestButton.gameObject.SetActive(node.Type == DungeonNodeType.Rest);
            bool visitorAvailable = !string.IsNullOrEmpty(state.VisitorId);
            TalkButton.gameObject.SetActive(node.Type == DungeonNodeType.Character && visitorAvailable);
            NextFloorButton.gameObject.SetActive(node.Type == DungeonNodeType.Stairs);
            foreach (var button in _visitors)
            {
                DestroyButton(button);
            }
            _visitors.Clear();
            EmptyVisitorsText.gameObject.SetActive(!visitorAvailable);
            if (visitorAvailable)
            {
                var character = _sheet.GetCharacter(state.VisitorId);
                var button = Instantiate(VisitorTemplate, VisitorButtons);
                button.name = "Visitor_" + character.Id;
                button.transform.Find("Name").GetComponent<Text>().text = character.Name;
                TownView.SetArtwork(button.transform.Find("PortraitClip/Portrait").GetComponent<Image>(), _townContent.Portraits[character.PortraitIndex]);
                button.onClick.AddListener(() => _input.AddInput(_characterView == null ? UIInputType.DungeonTalk : UIInputType.CharacterOpen,
                    _characterView == null ? null : character.Id, _context));
                button.gameObject.SetActive(true);
                _visitors.Add(button);
            }
        }

        private void RenderBattle(IDungeonDataReader state, bool locked, bool visible)
        {
            _battleState = state;
            if (_readyAllyId != state.ReadyAllyId || !state.InCombat || !state.IsAwaitingCommand)
            {
                _choosingTarget = false;
            }
            _readyAllyId = state.ReadyAllyId;
            BattleRoot.SetActive(visible);
            BattleControls.interactable = _initialized && !locked && !state.DetailsOpen;
            BattleControls.blocksRaycasts = !state.DetailsOpen;
            if (!visible)
            {
                return;
            }
            BattleTitleText.text = _sheet.Text(state.IsDefeated ? "battle.defeated" : state.IsCleared ? "battle.cleared" : "battle.title");
            string readyName = string.Empty;
            foreach (var ally in state.Allies)
            {
                if (ally.Id == state.ReadyAllyId)
                {
                    readyName = ally.Name;
                }
            }
            BattleReadyText.text = state.IsDefeated || state.IsCleared ? state.Status
                : state.IsAwaitingCommand ? Format("battle.ready_format", readyName) : _sheet.Text("battle.waiting");
            BattleHintText.text = _choosingTarget ? _sheet.Text("battle.target_hint") : state.Status;
            AttackButton.gameObject.SetActive(state.InCombat);
            AttackButton.interactable = !locked && state.IsAwaitingCommand && !state.DetailsOpen;
            BattleReturnTownButton.gameObject.SetActive(state.CanReturnTown);
            BattleReturnTownButton.interactable = !locked && state.CanReturnTown && !state.DetailsOpen;
            RenderBattleSkills(state, locked || state.DetailsOpen);
            RenderUnits(state.Enemies, _enemies, EnemyCards, true, locked || state.DetailsOpen);
            RenderUnits(state.Allies, _allies, AllyCards, false, locked || state.DetailsOpen);
        }

        private void RenderUnits(IReadOnlyList<CombatantData> units, Dictionary<string, UnitCard> cards, RectTransform parent, bool enemy, bool locked)
        {
            var currentIds = new HashSet<string>();
            float width = Mathf.Min(272f, (parent.rect.width - Mathf.Max(0, units.Count - 1) * 20f) / Mathf.Max(1, units.Count));
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                currentIds.Add(unit.Id);
                if (!cards.TryGetValue(unit.Id, out var card))
                {
                    var button = Instantiate(UnitCardTemplate, parent);
                    button.name = (enemy ? "Enemy_" : "Ally_") + unit.Id;
                    card = new UnitCard
                    {
                        Button = button,
                        Name = button.transform.Find("Name").GetComponent<Text>(),
                        Hp = button.transform.Find("HpText").GetComponent<Text>(),
                        Gauge = button.transform.Find("GaugeText").GetComponent<Text>(),
                        Speed = button.transform.Find("SpeedText").GetComponent<Text>(),
                        HpFill = (RectTransform)button.transform.Find("HpBar/HpFill"),
                        GaugeFill = (RectTransform)button.transform.Find("GaugeBar/GaugeFill"),
                        Portrait = button.transform.Find("PortraitClip/Portrait").GetComponent<Image>(),
                        Rat = button.transform.Find("RatGraphic").GetComponent<RatGraphic>()
                    };
                    string targetId = unit.Id;
                    button.onClick.AddListener(() => OnCombatUnitPressed(targetId, enemy));
                    // HP 숫자 → HP 막대 → 에너지 → 속도 순서로 각 표시의 세로 영역을 분리한다.
                    var hpBar = (RectTransform)card.HpFill.parent;
                    hpBar.anchoredPosition = new Vector2(118, -88);
                    card.Energy = AdventureUIFactory.Text("EnergyText", button.transform, card.Name.font,
                        118, 100, 136, 19, 12, AdventureUIFactory.PaleGold);
                    card.Speed.rectTransform.anchoredPosition = new Vector2(118, -123);
                    card.Speed.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 20);
                    card.Effects = AdventureUIFactory.Text("EffectsText", button.transform, card.Name.font,
                        18, 145, 236, 18, 11, AdventureUIFactory.PaleGold);
                    card.Gauge.rectTransform.anchoredPosition = new Vector2(18, -164);
                    card.Gauge.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 24);
                    button.gameObject.SetActive(true);
                    cards.Add(unit.Id, card);
                }
                var rect = (RectTransform)card.Button.transform;
                float rowWidth = units.Count * width + Mathf.Max(0, units.Count - 1) * 20f;
                rect.anchoredPosition = new Vector2((parent.rect.width - rowWidth) * 0.5f + i * (width + 20f), 0);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                card.Name.text = enemy
                    ? unit.IsBoss ? Format("enemy.boss_name_format", unit.Name, _sheet.Text("enemy.boss")) : unit.Name
                    : Format(unit.IsPlayerControlled ? "battle.player_name_format" : "battle.companion_name_format", unit.Name);
                card.Hp.text = Format("battle.hp_format", unit.Hp.ToString("0.##", CultureInfo.InvariantCulture),
                    unit.MaxHp.ToString("0.##", CultureInfo.InvariantCulture));
                card.Gauge.text = Format("battle.gauge_format", Mathf.RoundToInt(unit.Gauge * 100f));
                card.Speed.text = Format("battle.speed_format", unit.Speed.ToString("0.##", CultureInfo.InvariantCulture));
                RenderUnitResources(card, unit);
                SetFill(card.HpFill, unit.MaxHp > 0 ? (float)unit.Hp / unit.MaxHp : 0f);
                SetFill(card.GaugeFill, unit.Gauge);
                card.Portrait.transform.parent.gameObject.SetActive(!enemy);
                card.Rat.gameObject.SetActive(enemy);
                if (!enemy)
                {
                    TownView.SetArtwork(card.Portrait, _townContent.Portraits[unit.PortraitIndex]);
                }
                card.Button.interactable = !locked && CanSelectUnit(unit, enemy);
                card.Button.GetComponent<Image>().color = !unit.IsAlive
                    ? new Color(0.055f, 0.06f, 0.065f)
                    : (_choosingTarget && CanSelectUnit(unit, enemy)) || (!enemy && unit.Id == _readyAllyId)
                        ? new Color(0.27f, 0.23f, 0.16f) : new Color(0.10f, 0.115f, 0.12f);
                card.Rat.color = unit.IsBoss ? new Color(0.61f, 0.59f, 0.55f) : new Color(0.55f, 0.57f, 0.58f);
            }
            var removed = new List<string>();
            foreach (var entry in cards)
            {
                if (!currentIds.Contains(entry.Key))
                {
                    DestroyButton(entry.Value.Button);
                    removed.Add(entry.Key);
                }
            }
            foreach (string id in removed)
            {
                cards.Remove(id);
            }
        }

        private static void SetFill(RectTransform fill, float value)
        {
            fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            fill.offsetMax = Vector2.zero;
        }

        private string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, _sheet.Text(key), args);
        }

        private void SetButtonLabel(Button button, string key)
        {
            button.GetComponentInChildren<Text>(true).text = _sheet.Text(key);
        }

        private void Bind(Button button, UnityAction action)
        {
            button.onClick.AddListener(action);
            _bindings.Add((button, action));
        }

        private static void DestroyButton(Button button)
        {
            if (button == null)
            {
                return;
            }
            button.onClick.RemoveAllListeners();
            button.gameObject.SetActive(false);
            Destroy(button.gameObject);
        }

        public void UnbindInputs()
        {
            ClearBattleSkills();
            if (_heroMenuView != null)
            {
                _heroMenuView.UnbindInputs();
            }
            if (_characterView != null)
            {
                _characterView.UnbindInputs();
            }
            if (_lootSettlementView != null)
            {
                _lootSettlementView.UnbindInputs();
            }
            foreach (var binding in _bindings)
            {
                if (binding.Button != null)
                {
                    binding.Button.onClick.RemoveListener(binding.Action);
                }
            }
            _bindings.Clear();
            foreach (var button in _nodes.Values)
            {
                DestroyButton(button);
            }
            _nodes.Clear();
            foreach (var line in _lines)
            {
                if (line != null)
                {
                    Destroy(line);
                }
            }
            _lines.Clear();
            foreach (var button in _visitors)
            {
                DestroyButton(button);
            }
            _visitors.Clear();
            foreach (var card in _allies.Values)
            {
                DestroyButton(card.Button);
            }
            foreach (var card in _enemies.Values)
            {
                DestroyButton(card.Button);
            }
            _allies.Clear();
            _enemies.Clear();
            _choosingTarget = false;
            _initialized = false;
            _input = null;
            _characterProgress = null;
            _partyKey = null;
        }

        public void ShowInitializationError(string message)
        {
            UnbindInputs();
            MapControls.interactable = false;
            BattleRoot.SetActive(false);
            DungeonDetailsRoot.SetActive(false);
            StatusText.text = "초기화 실패: " + message;
        }

        private void OnDestroy()
        {
            UnbindInputs();
        }
    }
}

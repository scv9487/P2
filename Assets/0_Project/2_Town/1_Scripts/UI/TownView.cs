using System;
using System.Collections.Generic;
using System.Globalization;
using Refactor.Main;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Refactor.UI
{
    // 시트는 표시 내용, Reader는 현재 화면 선택의 원본이다. View에는 편집/게임 상태를 저장하지 않는다.
    public sealed class TownView : MonoBehaviour
    {
        [SerializeField] private GameObject OverviewRoot;
        [SerializeField] private GameObject LocationRoot;
        [SerializeField] private Image TownBackground;
        [SerializeField] private Image LocationBackground;
        [SerializeField] private Text TownNameText;
        [SerializeField] private Text TownSubtitleText;
        [SerializeField] private Text LocationsHeadingText;
        [SerializeField] private Text VisitorsHeadingText;
        [SerializeField] private Text CommandsHeadingText;
        [SerializeField] private Text ReturnTitleText;
        [SerializeField] private Text LeaveLocationText;
        [SerializeField] private Text LocationNameText;
        [SerializeField] private Text LocationSubtitleText;
        [SerializeField] private Text LocationDescriptionText;
        [SerializeField] private Text StatusText;
        [SerializeField] private Button ReturnTitleButton;
        [SerializeField] private Button LeaveLocationButton;
        [SerializeField] private Button LocationButtonTemplate;
        [SerializeField] private Button CommandButtonTemplate;
        [SerializeField] private Button CharacterButtonTemplate;
        [SerializeField] private RectTransform LocationButtons;
        [SerializeField] private RectTransform CommandButtons;
        [SerializeField] private RectTransform CharacterButtons;
        [SerializeField] private CanvasGroup OverviewControls;
        [SerializeField] private CanvasGroup LocationControls;
        [SerializeField] private GameObject DetailsRoot;
        [SerializeField] private GameObject CommandDetails;
        [SerializeField] private GameObject CharacterDetails;
        [SerializeField] private CanvasGroup DetailsControls;
        [SerializeField] private Text CommandTitleText;
        [SerializeField] private Text CommandBodyText;
        [SerializeField] private Text CommandCloseText;
        [SerializeField] private Button CommandCloseButton;
        [SerializeField] private Image CharacterPortrait;
        [SerializeField] private Text CharacterNameText;
        [SerializeField] private Text CharacterRoleText;
        [SerializeField] private Text CharacterGreetingText;
        [SerializeField] private Text CharacterDialogueText;
        [SerializeField] private Text CharacterCloseText;
        [SerializeField] private Text DialogueHeadingText;
        [SerializeField] private Button CharacterCloseButton;
        [SerializeField] private GameObject DungeonSelectionRoot;
        [SerializeField] private CanvasGroup DungeonSelectionControls;
        [SerializeField] private Text DungeonSelectionTitle;
        [SerializeField] private Text DungeonSelectionDescription;
        [SerializeField] private RectTransform DungeonCards;
        [SerializeField] private Button DungeonCardTemplate;
        [SerializeField] private Button DungeonConfirmButton;
        [SerializeField] private Button DungeonCancelButton;

        private TownSheet _sheet;
        private TownContent _content;
        private UIInputStream _input;
        private DungeonSheet _dungeonSheet;
        private readonly List<(Button Button, UnityAction Action)> _fixedBindings = new();
        private readonly List<Button> _locations = new();
        private readonly List<Button> _commands = new();
        private readonly List<Button> _characters = new();
        private readonly List<Button> _dungeons = new();
        private string _renderedLocation;
        private string _renderedCommand;
        private string _renderedCharacter;
        private bool _initialized;
        private bool _selectionWasOpen;
        private AdventureInventoryView _inventoryView;
        private ShopView _shopView;
        private QuestBoardView _questView;
        private HeroMenuView _heroMenuView;
        private CharacterPartyView _characterView;
        private IAdventureProgressReader _characterProgress;
        private string _partyKey;
        private bool _townModalOpen;

        public void Initialize(TownSheet sheet, TownContent content, UIInputStream input, DungeonSheet dungeonSheet = null)
        {
            UnbindInputs();
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _content = content != null ? content : throw new ArgumentNullException(nameof(content));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _dungeonSheet = dungeonSheet;
            SetArtwork(TownBackground, content.TownBackground);
            TownNameText.text = sheet.Text("town.name");
            TownSubtitleText.text = sheet.Text("town.subtitle");
            LocationsHeadingText.text = sheet.Text("town.choose");
            VisitorsHeadingText.text = sheet.Text("location.visitors");
            CommandsHeadingText.text = sheet.Text("location.commands");
            ReturnTitleText.text = sheet.Text("town.return_title");
            LeaveLocationText.text = sheet.Text("location.close");
            CommandCloseText.text = sheet.Text("panel.close");
            CharacterCloseText.text = sheet.Text("panel.close");
            DialogueHeadingText.text = sheet.Text("dialogue.title");
            Bind(ReturnTitleButton, () => input.AddInput(UIInputType.ReturnTitle));
            Bind(LeaveLocationButton, () => input.AddInput(UIInputType.LeaveLocation));
            Bind(CommandCloseButton, () => input.AddInput(UIInputType.CloseDetails));
            Bind(CharacterCloseButton, () => input.AddInput(UIInputType.CloseDetails));
            if (_dungeonSheet != null)
            {
                DungeonSelectionTitle.text = _dungeonSheet.Text("selector.title");
                DungeonSelectionDescription.text = _dungeonSheet.Text("selector.description");
                DungeonConfirmButton.GetComponentInChildren<Text>(true).text = _dungeonSheet.Text("selector.confirm");
                DungeonCancelButton.GetComponentInChildren<Text>(true).text = _dungeonSheet.Text("selector.cancel");
                Bind(DungeonConfirmButton, () => input.AddInput(UIInputType.ConfirmDungeon));
                Bind(DungeonCancelButton, () => input.AddInput(UIInputType.CancelDungeon));
                CreateDungeonCards();
            }
            CreateLocations();
            _renderedLocation = null;
            _renderedCommand = null;
            _renderedCharacter = null;
            _selectionWasOpen = false;
            _initialized = true;
        }

        public void Render(ITownDataReader state, bool locked, string status)
        {
            StatusText.text = status;
            bool inLocation = !string.IsNullOrEmpty(state.LocationId);
            bool commandOpen = !string.IsNullOrEmpty(state.CommandId);
            bool characterOpen = !string.IsNullOrEmpty(state.CharacterId);
            bool detailsOpen = commandOpen || characterOpen || state.DungeonSelectionOpen || state.ShopOpen || state.QuestBoardOpen;
            _townModalOpen = detailsOpen;
            OverviewRoot.SetActive(!inLocation);
            LocationRoot.SetActive(inLocation);
            OverviewControls.interactable = _initialized && !locked && !detailsOpen;
            LocationControls.interactable = _initialized && !locked && !detailsOpen;
            OverviewControls.blocksRaycasts = !detailsOpen;
            LocationControls.blocksRaycasts = !detailsOpen;
            DetailsControls.interactable = _initialized && !locked && !state.ShopOpen && !state.QuestBoardOpen;

            if (_renderedLocation != state.LocationId)
            {
                ClearButtons(_commands);
                ClearButtons(_characters);
                _renderedLocation = state.LocationId;
                if (inLocation)
                {
                    ShowLocation(state.LocationId);
                    EventSystem.current?.SetSelectedGameObject(null);
                }
            }
            bool legacyCharacterOpen = characterOpen && _characterView == null;
            DetailsRoot.SetActive((commandOpen || legacyCharacterOpen) && !state.ShopOpen && !state.QuestBoardOpen);
            RenderDungeonSelection(state, locked);
            CommandDetails.SetActive(commandOpen);
            CharacterDetails.SetActive(legacyCharacterOpen);
            if (commandOpen && _renderedCommand != state.CommandId)
            {
                var command = _sheet.GetCommand(state.CommandId);
                CommandTitleText.text = command.Title;
                CommandBodyText.text = command.Body;
                EventSystem.current?.SetSelectedGameObject(CommandCloseButton.gameObject);
            }
            if (legacyCharacterOpen && _renderedCharacter != state.CharacterId)
            {
                var character = _sheet.GetCharacter(state.CharacterId);
                CharacterNameText.text = character.Name;
                CharacterRoleText.text = character.Role;
                CharacterGreetingText.text = character.Greeting;
                CharacterDialogueText.text = character.Dialogue;
                SetArtwork(CharacterPortrait, _content.Portraits[character.PortraitIndex]);
                EventSystem.current?.SetSelectedGameObject(CharacterCloseButton.gameObject);
            }
            if (!detailsOpen && (!string.IsNullOrEmpty(_renderedCommand) || !string.IsNullOrEmpty(_renderedCharacter)))
            {
                EventSystem.current?.SetSelectedGameObject(null);
            }
            _renderedCommand = state.CommandId;
            _renderedCharacter = state.CharacterId;
        }

        public void ConfigureInventory(LootSheet sheet)
        {
            if (_inventoryView == null)
            {
                // 보유 내역의 오른쪽은 기존 타이틀 복귀 버튼, 아래는 마을 부제다.
                // 긴 마을 제목도 보유 내역 영역에 들어오지 않도록 표시 폭을 구분한다.
                TownNameText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 550f);
                _inventoryView = AdventureInventoryView.Create(OverviewControls.transform, TownNameText.font);
            }
            _inventoryView.Initialize(sheet);
        }

        public void RenderInventory(IAdventureProgressReader progress)
        {
            if (_inventoryView != null)
            {
                _inventoryView.Render(progress);
            }
        }

        public void ConfigureShop(ShopSheet sheet, UIInputStream input)
        {
            if (_shopView == null)
            {
                _shopView = ShopView.Create(transform, TownNameText.font);
            }
            _shopView.Initialize(sheet, input);
        }

        public void RenderShop(IShopDataReader shop, bool open, bool locked, bool canWrite, string status)
        {
            if (_shopView != null)
            {
                _shopView.Render(shop, open, locked, canWrite, status);
            }
        }

        public void ConfigureCharacters(CharacterSheet sheet, TownContent portraits, UIInputStream input)
        {
            if (_characterView == null)
            {
                _characterView = CharacterPartyView.Create(transform, TownNameText.font);
            }
            _characterView.Initialize(sheet, _sheet, portraits, input);
            _partyKey = null;
            // 하단 파티 영역과 장소 카드/설명/상태 문구가 겹치지 않도록 기존 프레임 안에서 재배치한다.
            LocationsHeadingText.rectTransform.anchoredPosition = new Vector2(64, -392);
            LocationButtons.anchoredPosition = new Vector2(64, -446);
            ((RectTransform)LocationDescriptionText.transform.parent).anchoredPosition = new Vector2(338, -494);
            ((RectTransform)CommandButtons.parent).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 506);
            StatusText.rectTransform.anchoredPosition = new Vector2(64, -642);
            StatusText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 32);
        }

        public void ConfigureQuests(QuestSheet sheet, UIInputStream input)
        {
            if (_questView == null)
            {
                _questView = QuestBoardView.Create(transform, TownNameText.font);
            }
            _questView.Initialize(sheet, input);
        }

        public void ConfigureHeroMenu(SkillSheet skills, LootSheet loot, CharacterSheet characters,
            TownContent portraits, UIInputStream input)
        {
            if (_heroMenuView == null)
            {
                _heroMenuView = HeroMenuView.Create(transform, TownNameText.font);
            }
            _heroMenuView.Initialize(skills, loot, characters, portraits, input);
            _characterView.ConfigureSkills(skills);
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
            if (_shopView != null)
            {
                _shopView.ConfigureEquipment(equipment);
            }
            if (_inventoryView != null)
            {
                _inventoryView.ConfigureEquipment(equipment);
            }
        }

        public void RenderQuests(IAdventureProgressReader progress, IQuestBoardReader board, bool locked, bool canWrite)
        {
            if (_questView != null)
            {
                _questView.Render(progress, board, locked, canWrite);
            }
        }

        public void RenderCharacters(IAdventureProgressReader progress, ICharacterInteractionReader interaction,
            bool locked, bool canWrite, IDungeonDataReader dungeon = null)
        {
            if (_characterView == null)
            {
                return;
            }
            _characterProgress = progress;
            string partyKey = string.Join(",", progress.PartyIds);
            if (_partyKey != partyKey)
            {
                _partyKey = partyKey;
                if (!string.IsNullOrEmpty(_renderedLocation))
                {
                    CreateVisitors(_renderedLocation);
                }
            }
            _characterView.Render(progress, interaction, locked, canWrite, _townModalOpen);
            if (interaction.Open)
            {
                OverviewControls.interactable = false;
                OverviewControls.blocksRaycasts = false;
                LocationControls.interactable = false;
                LocationControls.blocksRaycasts = false;
                DetailsControls.interactable = false;
                DetailsControls.blocksRaycasts = false;
                DetailsRoot.SetActive(false);
            }
            else
            {
                DetailsControls.blocksRaycasts = true;
            }
        }

        private void CreateLocations()
        {
            float gap = 16f;
            float width = (LocationButtons.rect.width - gap * (_sheet.Locations.Count - 1)) / _sheet.Locations.Count;
            for (int i = 0; i < _sheet.Locations.Count; i++)
            {
                var location = _sheet.Locations[i];
                var button = Instantiate(LocationButtonTemplate, LocationButtons);
                button.name = "Location_" + location.Id;
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(i * (width + gap), 0);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                var line = button.transform.Find("Line") as RectTransform;
                if (line != null)
                {
                    line.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                }
                button.transform.Find("Name").GetComponent<Text>().text = location.Name;
                button.transform.Find("Subtitle").GetComponent<Text>().text = location.Subtitle;
                SetArtwork(button.transform.Find("ArtClip/Artwork").GetComponent<Image>(), _content.LocationBackgrounds[location.ArtIndex]);
                button.onClick.AddListener(() => _input.AddInput(UIInputType.EnterLocation, location.Id));
                button.gameObject.SetActive(true);
                _locations.Add(button);
            }
        }

        private void CreateDungeonCards()
        {
            for (int i = 0; i < _dungeonSheet.Dungeons.Count; i++)
            {
                var dungeon = _dungeonSheet.Dungeons[i];
                var button = Instantiate(DungeonCardTemplate, DungeonCards);
                button.name = "DungeonSelect_" + dungeon.Id;
                ((RectTransform)button.transform).anchoredPosition = new Vector2(i % 3 * 314f, -(i / 3) * 167f);
                button.transform.Find("Name").GetComponent<Text>().text = string.Format(CultureInfo.InvariantCulture,
                    _dungeonSheet.Text("selector.card_title_format"), dungeon.Id, dungeon.Name);
                button.transform.Find("Description").GetComponent<Text>().text = dungeon.Description;
                button.transform.Find("FloorCount").GetComponent<Text>().text = string.Format(CultureInfo.InvariantCulture,
                    _dungeonSheet.Text("selector.floor_format"), dungeon.FloorCount);
                button.onClick.AddListener(() => _input.AddInput(UIInputType.SelectDungeon, dungeon.Id.ToString(CultureInfo.InvariantCulture)));
                button.gameObject.SetActive(true);
                _dungeons.Add(button);
            }
        }

        private void RenderDungeonSelection(ITownDataReader state, bool locked)
        {
            if (DungeonSelectionRoot == null)
            {
                return;
            }
            DungeonSelectionRoot.SetActive(state.DungeonSelectionOpen);
            DungeonSelectionControls.interactable = _initialized && !locked;
            DungeonConfirmButton.interactable = _initialized && !locked && state.SelectedDungeonId != 0;
            for (int i = 0; i < _dungeons.Count; i++)
            {
                bool selected = _dungeonSheet.Dungeons[i].Id == state.SelectedDungeonId;
                _dungeons[i].GetComponent<Image>().color = selected
                    ? new Color(0.34f, 0.28f, 0.18f, 1f)
                    : new Color(0.10f, 0.105f, 0.11f, 1f);
                _dungeons[i].transform.Find("SelectionMark").gameObject.SetActive(selected);
            }
            if (state.DungeonSelectionOpen != _selectionWasOpen)
            {
                EventSystem.current?.SetSelectedGameObject(state.DungeonSelectionOpen ? DungeonCancelButton.gameObject : null);
                _selectionWasOpen = state.DungeonSelectionOpen;
            }
        }

        private void ShowLocation(string locationId)
        {
            var location = _sheet.GetLocation(locationId);
            LocationNameText.text = location.Name;
            LocationSubtitleText.text = location.Subtitle;
            LocationDescriptionText.text = location.Description;
            VisitorsHeadingText.text = _sheet.Text(location.VisitorIds.Count == 0 ? "empty.visitors" : "location.visitors");
            SetArtwork(LocationBackground, _content.LocationBackgrounds[location.ArtIndex]);
            for (int i = 0; i < location.CommandIds.Count; i++)
            {
                var command = _sheet.GetCommand(location.CommandIds[i]);
                var button = Instantiate(CommandButtonTemplate, CommandButtons);
                button.name = "Command_" + command.Id;
                ((RectTransform)button.transform).anchoredPosition = new Vector2(0, -i * 64f);
                button.GetComponentInChildren<Text>(true).text = command.Name;
                button.onClick.AddListener(() => _input.AddInput(UIInputType.LocationCommand, command.Id, location.Id));
                button.gameObject.SetActive(true);
                _commands.Add(button);
            }
            CreateVisitors(locationId);
        }

        private void CreateVisitors(string locationId)
        {
            ClearButtons(_characters);
            var location = _sheet.GetLocation(locationId);
            foreach (string id in location.VisitorIds)
            {
                if (_characterProgress != null && _characterProgress.IsInParty(id))
                {
                    continue;
                }
                var character = _sheet.GetCharacter(id);
                var button = Instantiate(CharacterButtonTemplate, CharacterButtons);
                button.name = "Character_" + character.Id;
                ((RectTransform)button.transform).anchoredPosition = new Vector2(_characters.Count * 136f, 0);
                button.transform.Find("Name").GetComponent<Text>().text = character.Name;
                SetArtwork(button.transform.Find("PortraitClip/Portrait").GetComponent<Image>(), _content.Portraits[character.PortraitIndex]);
                button.onClick.AddListener(() => _input.AddInput(UIInputType.SelectCharacter, character.Id, location.Id));
                button.gameObject.SetActive(true);
                _characters.Add(button);
            }
            VisitorsHeadingText.text = _sheet.Text(_characters.Count == 0 ? "empty.visitors" : "location.visitors");
        }

        private void Bind(Button button, UnityAction action)
        {
            button.onClick.AddListener(action);
            _fixedBindings.Add((button, action));
        }

        private static void ClearButtons(List<Button> buttons)
        {
            foreach (var button in buttons)
            {
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.gameObject.SetActive(false);
                    Destroy(button.gameObject);
                }
            }
            buttons.Clear();
        }

        public void UnbindInputs()
        {
            if (_heroMenuView != null)
            {
                _heroMenuView.UnbindInputs();
            }
            if (_questView != null)
            {
                _questView.UnbindInputs();
            }
            if (_characterView != null)
            {
                _characterView.UnbindInputs();
            }
            if (_shopView != null)
            {
                _shopView.UnbindInputs();
            }
            foreach (var binding in _fixedBindings)
            {
                if (binding.Button != null)
                {
                    binding.Button.onClick.RemoveListener(binding.Action);
                }
            }
            _fixedBindings.Clear();
            ClearButtons(_locations);
            ClearButtons(_commands);
            ClearButtons(_characters);
            ClearButtons(_dungeons);
            _initialized = false;
            _input = null;
            _characterProgress = null;
            _partyKey = null;
        }

        public void ShowInitializationError(string message)
        {
            UnbindInputs();
            OverviewControls.interactable = false;
            LocationControls.interactable = false;
            DetailsRoot.SetActive(false);
            if (DungeonSelectionRoot != null)
            {
                DungeonSelectionRoot.SetActive(false);
            }
            StatusText.text = "초기화 실패: " + message;
        }

        // Sprite atlas의 잘린 영역을 기준으로 화면/초상화를 채운다. 원본 픽셀을 복제하지 않는다.
        public static void SetArtwork(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            var fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter != null && sprite != null)
            {
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            }
        }

        private void OnDestroy()
        {
            UnbindInputs();
        }
    }
}

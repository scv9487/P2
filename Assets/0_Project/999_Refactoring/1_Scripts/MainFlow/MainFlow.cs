using System;
using System.IO;
using Refactor.Main.Event;
using Refactor.Main.InputModule;
using Refactor.UI;
using Refactor.Util;
using Refactor.View;
using UnityEngine;
using EventHandler = Refactor.Main.Event.EventHandler;

namespace Refactor.Main
{
    // 씬별 조립 및 실행 순서 소유자. 각 씬은 직접 Play 가능하며 Single 전환 때 재생성된다.
    public sealed partial class MainFlow : MonoBehaviour
    {
        [SerializeField] private SceneType _sceneType;
        [SerializeField] private TextAsset _gameSystemTable;
        [SerializeField] private FoundationView _view;
        [SerializeField] private TownContent _townContent;
        [SerializeField] private TitleMenuView _titleView;
        [SerializeField] private TownView _townView;
        [SerializeField] private DungeonContent _dungeonContent;
        [SerializeField] private DungeonView _dungeonView;
        private static MainFlow _active;
        private static int _pendingDungeonId;
        private FoundationDataContainer _data;
        private InputStream _stream;
        private InputFilter _filter;
        private InputDispatcher _input;
        private EventQueue _queue;
        private FoundationViewPresenter _presenter;
        private TownDataContainer _townData;
        private TownSheet _townSheet;
        private TownViewPresenter _townPresenter;
        private DungeonDataContainer _dungeonData;
        private DungeonSheet _dungeonSheet;
        private DungeonViewPresenter _dungeonPresenter;
        private CombatTickFlow _combatTick;
        private SceneLoader _sceneLoader;
        private LootSheet _lootSheet;
        private AdventureProgressStore _adventureStore;
        private LootSettlement _settlement;
        private ShopSheet _shopSheet;
        private ShopDataContainer _shop;
        private string _shopStatus = string.Empty;
        private string _settlementError = string.Empty;
        private string _status = string.Empty;

        public bool IsReady { get; private set; }
        public UIInputStream UIInput { get; private set; }
        public IFoundationDataReader Data => _data;
        public string SavePath { get; private set; }
        public SceneType CurrentScene => _sceneType;
        public bool IsTransitioning { get; private set; }
        public ITownDataReader TownData => _townData;
        public TownSheet TownSheet => _townSheet;
        public IDungeonDataReader DungeonData => _dungeonData;
        public DungeonSheet DungeonSheet => _dungeonSheet;
        public LootSheet LootSheet => _lootSheet;
        public IAdventureProgressReader AdventureProgress => _adventureStore?.Progress;
        public ILootSettlementReader Settlement => _settlement;
        public ShopSheet ShopSheet => _shopSheet;
        public IShopDataReader Shop => _shop;
        public string AdventureSavePath { get; private set; }
        // 테스트/검증 Player는 별도 경로를 주입하여 실제 플레이 저장 파일과 분리한다.
        public static string AdventureSavePathOverride { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active = null;
            _pendingDungeonId = 0;
            AdventureSavePathOverride = null;
            DI.Init();
        }

        private void Awake()
        {
            if (_active != null && _active != this)
            {
                Debug.LogError("MainFlow는 씬에 하나만 배치해야 합니다.", this);
                enabled = false;
                return;
            }
            _active = this;
            DI.Init();
            try
            {
                _stream = new InputStream();
                UIInput = new UIInputStream(_stream);
                if (_sceneType == SceneType.FoundationTest)
                {
                    InitFoundation();
                }
                else
                {
                    InitGameScene();
                }
                IsReady = true;
                RefreshView();
            }
            catch (Exception exception)
            {
                _view?.ShowInitializationError(exception.Message);
                _titleView?.ShowInitializationError(exception.Message);
                _townView?.ShowInitializationError(exception.Message);
                _dungeonView?.ShowInitializationError(exception.Message);
                Debug.LogException(exception, this);
                enabled = false;
            }
        }

        private void InitFoundation()
        {
            if (_view == null || _gameSystemTable == null)
            {
                throw new InvalidOperationException("MainFlow의 View와 GameSystemTable 연결을 확인해 주세요.");
            }
            var settings = new GameSystemSheet(new global::Util.Sheet.GameSystemTable(_gameSystemTable.text));
            _data = new FoundationDataContainer(settings);
            DI.PubSub(_data);
            _filter = new InputFilter(() => _data.PopupOpen) { Focused = Application.isFocused };
            SavePath = Path.Combine(Application.persistentDataPath, "P2Foundation", "save.json");
            _presenter = new FoundationViewPresenter(_view, _data, SavePath);
            var handler = new FoundationEventHandler(_filter, settings, SavePath, _presenter.RequestDraftRefresh);
            _queue = new EventQueue(new EventHandler(handler));
            _input = new InputDispatcher(_stream, _filter, _queue);
            _view.ConfigureInput(settings.LabelMaxLength);
            _view.BindInputs(UIInput);
        }

        private void InitGameScene()
        {
            if (_townContent == null || (_sceneType == SceneType.Title && _titleView == null)
                || (_sceneType == SceneType.Town && _townView == null)
                || (_sceneType == SceneType.Dungeon && _dungeonView == null)
                || (_sceneType != SceneType.Title && _dungeonContent == null))
            {
                throw new InvalidOperationException("MainFlow의 씬 콘텐츠와 View 연결을 확인해 주세요.");
            }
            if (_sceneType != SceneType.Title && _sceneType != SceneType.Town && _sceneType != SceneType.Dungeon)
            {
                throw new InvalidOperationException("지원하지 않는 MainFlow 씬 유형입니다.");
            }
            _townSheet = _townContent.Load();
            _sceneLoader = new SceneLoader();
            _filter = new InputFilter(() => false) { Focused = Application.isFocused };
            if (_sceneType != SceneType.Title)
            {
                _dungeonSheet = _dungeonContent.Load();
                _lootSheet = _dungeonContent.LoadLoot();
                _shopSheet = _dungeonContent.LoadShop();
                _characterSheet = _dungeonContent.LoadCharacters();
                _questSheet = _dungeonContent.LoadQuests();
                _skillSheet = _dungeonContent.LoadSkills();
                _equipmentSheet = _dungeonContent.LoadEquipment();
                _characterRandom = new UnityRandomSource();
                AdventureSavePath = string.IsNullOrEmpty(AdventureSavePathOverride)
                    ? Path.Combine(Application.persistentDataPath, "P2Adventure", "progress.json") : AdventureSavePathOverride;
                _adventureStore = new AdventureProgressStore(_lootSheet, AdventureSavePath, _shopSheet, _characterSheet, _questSheet, _skillSheet, _equipmentSheet);
                _adventureStore.Load();
                if (!_adventureStore.CanWrite)
                {
                    _status = _lootSheet.Text("status.load_failed");
                }
            }
            if (_sceneType == SceneType.Dungeon)
            {
                InitDungeon();
                return;
            }
            if (_sceneType == SceneType.Town)
            {
                _townData = new TownDataContainer(_townSheet, _dungeonSheet, _shopSheet, _adventureStore.Progress.IsInParty, _questSheet);
                DI.PubSub(_townData);
            }
            var handler = new TownEventHandler(_sceneType, _filter, BeginSceneTransition, QuitGame, BeginDungeon,
                OpenShop, HandleShopAction, CloseShop, () => _characterInteraction.Open, OpenTownCharacter, CloseCharacter,
                OpenQuests, CloseQuests, () => _heroMenu.Open, _heroMenu.Close);
            _queue = new EventQueue(new EventHandler(handler,
                _sceneType == SceneType.Title ? null : new CharacterEventHandler(_filter, HandleCharacterAction),
                _sceneType == SceneType.Title ? null : new QuestEventHandler(_filter, HandleQuestAction),
                _sceneType == SceneType.Title ? null : new HeroMenuEventHandler(_filter, HandleHeroMenuAction)));
            _input = new InputDispatcher(_stream, _filter, _queue,
                _sceneType == SceneType.Title ? InputProfile.Title : InputProfile.Town);
            if (_sceneType == SceneType.Title)
            {
                _titleView.Initialize(_townSheet, _townContent, UIInput);
            }
            else
            {
                _townView.Initialize(_townSheet, _townContent, UIInput, _dungeonSheet);
                _townView.ConfigureInventory(_lootSheet);
                _townView.ConfigureShop(_shopSheet, UIInput);
                _townView.ConfigureCharacters(_characterSheet, _townContent, UIInput);
                _townView.ConfigureQuests(_questSheet, UIInput);
                _townView.ConfigureHeroMenu(_skillSheet, _lootSheet, _characterSheet, _townContent, UIInput);
                _townView.ConfigureEquipment(_equipmentSheet);
                _townPresenter = new TownViewPresenter(_townView, _townData);
            }
        }

        private void InitDungeon()
        {
            // 씬 전환에는 선택 ID만 넘긴다. 직접 Play할 때는 첫 번째 던전부터 새 탐험을 시작한다.
            int dungeonId = _pendingDungeonId == 0 ? _dungeonSheet.Dungeons[0].Id : _pendingDungeonId;
            _pendingDungeonId = 0;
            _dungeonData = new DungeonDataContainer(_dungeonSheet, dungeonId, _lootSheet, _characterSheet,
                _adventureStore.Progress, _characterRandom, _questSheet, _skillSheet);
            DI.PubSub(_dungeonData);
            _queue = new EventQueue(new EventHandler(new DungeonEventHandler(_filter, OpenSettlement,
                AdjustLootPurchase, ConfirmSettlement, CancelSettlement, () => _characterInteraction.Open,
                CloseCharacter, OpenDungeonCharacter, () => CommitDungeonProgress(true), () => _heroMenu.Open, _heroMenu.Close),
                new CharacterEventHandler(_filter, HandleCharacterAction),
                new HeroMenuEventHandler(_filter, HandleHeroMenuAction)));
            _input = new InputDispatcher(_stream, _filter, _queue, InputProfile.Dungeon);
            _combatTick = new CombatTickFlow(_dungeonData);
            _dungeonView.Initialize(_dungeonSheet, _dungeonContent, _townSheet, _townContent, UIInput);
            _dungeonView.ConfigureLoot(_lootSheet, UIInput);
            _dungeonView.ConfigureCharacters(_characterSheet, _townContent, UIInput);
            _dungeonView.ConfigureHeroMenu(_skillSheet, _lootSheet, _characterSheet, _townContent, UIInput);
            _dungeonView.ConfigureEquipment(_equipmentSheet);
            _dungeonPresenter = new DungeonViewPresenter(_dungeonView, _dungeonData);
        }

        private void BeginDungeon(int id)
        {
            if (_sceneType != SceneType.Town || IsTransitioning)
            {
                return;
            }
            _dungeonSheet.GetDungeon(id);
            _pendingDungeonId = id;
            BeginSceneTransition(SceneType.Dungeon);
            if (!IsTransitioning)
            {
                _pendingDungeonId = 0;
            }
        }

        private void OpenShop(string merchantId)
        {
            _shop = new ShopDataContainer(_shopSheet, merchantId, _adventureStore.Progress);
            _shopStatus = _adventureStore.CanWrite ? string.Empty : _shopSheet.Text("status.load_failed");
        }

        private void CloseShop()
        {
            _shop = null;
            _shopStatus = string.Empty;
        }

        // 상점 입력에도 선택 시점의 문맥을 전달한다. 수량 변경/거래 뒤 이전 요청은 폐기한다.
        private void HandleShopAction(TownActionRequested request)
        {
            if (_shop == null || !_townData.ShopOpen || request.LocationId != _shop.Context)
            {
                return;
            }
            bool changed = false;
            switch (request.Action)
            {
                case UIInputType.ShopSelectBuy:
                case UIInputType.ShopSelectSell:
                    changed = _shop.TrySelectItem(request.TargetId, request.Action == UIInputType.ShopSelectBuy);
                    break;
                case UIInputType.ShopIncrease:
                case UIInputType.ShopDecrease:
                    changed = _shop.TryAdjustQuantity(request.Action == UIInputType.ShopIncrease ? 1 : -1);
                    break;
                case UIInputType.ShopConfirm:
                    ConfirmShopTrade();
                    return;
                case UIInputType.ShopClose:
                    _townData.CloseDetails();
                    CloseShop();
                    return;
            }
            if (changed)
            {
                _shopStatus = _adventureStore.CanWrite ? string.Empty : _shopSheet.Text("status.load_failed");
            }
        }

        private void ConfirmShopTrade()
        {
            if (string.IsNullOrEmpty(_shop.SelectedItemId) || _shop.Quantity <= 0)
            {
                _shopStatus = _shopSheet.Text("status.invalid_selection");
                return;
            }
            string itemName = _lootSheet.GetItem(_shop.SelectedItemId).Name;
            int quantity = _shop.Quantity;
            bool buying = _shop.IsBuying;
            var result = _adventureStore.TryTrade(_shop.CreateTrade());
            if (result == ShopTradeResult.Saved || result == ShopTradeResult.AlreadyCommitted)
            {
                _shop.ResetSelection();
                _shopStatus = string.Format(_shopSheet.Text(buying ? "status.buy_success_format" : "status.sell_success_format"),
                    itemName, quantity);
                return;
            }
            _shopStatus = _shopSheet.Text(result == ShopTradeResult.InsufficientGold ? "status.insufficient_gold"
                : result == ShopTradeResult.InsufficientStock ? (buying ? "status.no_stock" : "status.not_owned")
                : result == ShopTradeResult.LoadBlocked ? "status.load_failed"
                : result == ShopTradeResult.SaveFailed ? "status.save_failed" : "status.invalid_selection");
        }

        // 정산도 명시적 UI 요청의 실행 단계에서만 변경/저장한다.
        private void OpenSettlement()
        {
            _settlement = new LootSettlement(_lootSheet, _dungeonData.SettlementContext, _dungeonData.Loot,
                _adventureStore.Progress, ExpeditionShares());
            _settlementError = _adventureStore.CanWrite ? string.Empty : _lootSheet.Text("status.load_failed");
        }

        private void AdjustLootPurchase(string itemId, int delta)
        {
            if (_settlement == null || !_dungeonData.SettlementOpen || _dungeonData.SettlementCommitted)
            {
                return;
            }
            if (_settlement.TryAdjustPurchase(itemId, delta))
            {
                _settlementError = _adventureStore.CanWrite ? string.Empty : _lootSheet.Text("status.load_failed");
            }
        }

        private void CancelSettlement()
        {
            _settlement = null;
            _settlementError = string.Empty;
        }

        private void ConfirmSettlement()
        {
            if (_settlement == null || !_dungeonData.SettlementOpen)
            {
                return;
            }
            if (!_sceneLoader.CanMove(SceneType.Town))
            {
                _settlementError = _lootSheet.Text("status.return_failed");
                return;
            }
            if (!_dungeonData.SettlementCommitted)
            {
                var result = _adventureStore.TryCommit(_settlement);
                if (result != SettlementCommitResult.Saved && result != SettlementCommitResult.AlreadyCommitted)
                {
                    _settlementError = _lootSheet.Text(result == SettlementCommitResult.InsufficientGold ? "status.insufficient_gold"
                        : result == SettlementCommitResult.LoadBlocked ? "status.load_failed"
                        : result == SettlementCommitResult.InvalidSelection ? "status.invalid_selection" : "status.save_failed");
                    return;
                }
                _dungeonData.MarkSettlementCommitted();
            }
            // 저장 후 전환이 실패해도 다음 확인은 씬 이동만 재시도한다.
            BeginSceneTransition(SceneType.Town);
            if (!IsTransitioning)
            {
                _settlementError = _lootSheet.Text("status.return_failed");
            }
        }

        private void Update()
        {
            if (!IsReady)
            {
                return;
            }
            _input.Update();
            _queue.Update();
            CommitDungeonProgress(false);
            // 자동 게이지/동료·적 공격은 EventQueue 다음 TickFlow 단계에서만 실행한다.
            if (_filter.Focused && !_filter.IsLocked && !IsTransitioning && !_characterInteraction.Open && !_heroMenu.Open)
            {
                _combatTick?.Update(Time.deltaTime);
            }
            CommitDungeonProgress(false);
            RefreshView();
        }

        private void RefreshView()
        {
            RefreshInventoryState();
            if (_sceneType == SceneType.FoundationTest)
            {
                _presenter?.Refresh();
            }
            else if (_sceneType == SceneType.Title)
            {
                _titleView.Render(IsTransitioning, _status);
            }
            else if (_sceneType == SceneType.Town)
            {
                _townPresenter?.Refresh(IsTransitioning || _heroMenu.Open, _status);
                _townView.RenderInventory(_adventureStore.Progress);
                _townView.RenderShop(_shop, _townData.ShopOpen, IsTransitioning || _heroMenu.Open, _adventureStore.CanWrite, _shopStatus);
                _townView.RenderCharacters(_adventureStore.Progress, _characterInteraction, IsTransitioning || _heroMenu.Open, _adventureStore.CanWrite);
                _townView.RenderQuests(_adventureStore.Progress, _questBoard, IsTransitioning || _heroMenu.Open, _adventureStore.CanWrite);
                _townView.RenderHeroMenu(_adventureStore.Progress, _heroMenu, IsTransitioning, _adventureStore.CanWrite);
            }
            else
            {
                _dungeonPresenter?.Refresh(IsTransitioning || _heroMenu.Open, _status);
                _dungeonView.RenderLoot(_settlement, _dungeonData.SettlementOpen, IsTransitioning || _heroMenu.Open,
                    _adventureStore.CanWrite, _settlementError);
                _dungeonView.RenderCharacters(_adventureStore.Progress, _characterInteraction, IsTransitioning || _heroMenu.Open,
                    _adventureStore.CanWrite, _dungeonData);
                _dungeonView.RenderHeroMenu(_adventureStore.Progress, _heroMenu, IsTransitioning, _adventureStore.CanWrite);
            }
        }

        // Handler가 중앙 큐의 실행 단계에서 호출한다. 새로운 요청을 큐에 재등록하지 않는다.
        private void BeginSceneTransition(SceneType target)
        {
            if (IsTransitioning || target == _sceneType)
            {
                return;
            }
            if (!_sceneLoader.CanMove(target))
            {
                _status = _townSheet.Text("status.failed") + " " + _sceneLoader.LastError;
                return;
            }
            IsTransitioning = true;
            _filter.IsLocked = true;
            _stream.ClearInputs();
            _queue.ClearPendingSyncEvents();
            _status = _townSheet.Text("status.loading");
            // 새 씬의 초기화는 새 MainFlow.Awake가 담당한다. 사라질 객체의 코루틴 완료에 의존하지 않는다.
            if (_sceneLoader.BeginLoad(target) == null)
            {
                IsTransitioning = false;
                _filter.IsLocked = false;
                _status = _townSheet.Text("status.failed") + " " + _sceneLoader.LastError;
            }
        }

        private void QuitGame()
        {
            _filter.IsLocked = true;
            _stream.ClearInputs();
            _queue.ClearPendingSyncEvents();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnApplicationFocus(bool focused)
        {
            if (_filter == null)
            {
                return;
            }
            _filter.Focused = focused;
            _stream.ClearInputs();
            _queue?.ClearPendingSyncEvents();
        }

        private void OnDisable()
        {
            _stream?.ClearInputs();
            _queue?.ClearPendingSyncEvents();
        }

        private void OnDestroy()
        {
            if (_active != this)
            {
                return;
            }
            IsReady = false;
            _view?.UnbindInputs();
            _titleView?.UnbindInputs();
            _townView?.UnbindInputs();
            _dungeonView?.UnbindInputs();
            _stream?.ClearInputs();
            _queue?.ClearPendingSyncEvents();
            DI.Remove<FoundationEventHandler>();
            DI.Remove<FoundationDataContainer>();
            DI.Remove<TownEventHandler>();
            DI.Remove<TownDataContainer>();
            DI.Remove<DungeonEventHandler>();
            DI.Remove<DungeonDataContainer>();
            _active = null;
        }
    }
}

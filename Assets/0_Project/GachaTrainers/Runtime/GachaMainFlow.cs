using System;
using System.Collections.Generic;
using Refactor.Main;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using EventHandler = Refactor.Main.Event.EventHandler;

namespace GachaTrainers
{
    // 씬 수명, 입력 → 요청 처리 → 표시, 런 시작/종료를 한곳에서 조율한다.
    public sealed class GachaMainFlow : MonoBehaviour
    {
        public const string TitleScenePath = "Assets/0_Project/GachaTrainers/Scenes/Title.unity";
        public const string ShopScenePath = "Assets/0_Project/GachaTrainers/Scenes/GachaShop.unity";
        public const string BattleScenePath = "Assets/0_Project/GachaTrainers/Scenes/Battle.unity";
        [SerializeField] private GachaSceneKind _sceneKind;
        [SerializeField] private GachaSceneView _view;
        [SerializeField] private BattleSceneView _battleView;
        [SerializeField] private TextAsset _stageTable;
        [SerializeField] private GachaBalanceAsset _balanceAsset;
        private GachaBalance _balance;
        private static RunDataContainer _currentRun;
        private readonly Queue<IEventData> _inputs = new();
        private RunDataContainer _run;
        private ShopDataContainer _shop;
        private BattleDataContainer _battle;
        private EventQueue _queue;
        private GachaAction? _pendingNavigation;
        private bool _focused;
        private bool _transitioning;
        private bool _combatInputPending;
        private EventSystem _pointerEventSystem;
        private PointerEventData _pointerEvent;
        private readonly List<RaycastResult> _pointerHits = new();

        public bool IsReady { get; private set; }
        public GachaSceneKind SceneKind => _sceneKind;
        public IReadOnlyRunData Run => _run;
        public IShopDataReader Shop => _shop;
        public IReadOnlyBattleData Battle => _battle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            _currentRun = null;
        }

        private void Awake()
        {
            if ((_sceneKind == GachaSceneKind.Battle && _battleView == null)
                || (_sceneKind != GachaSceneKind.Battle && _view == null))
            {
                Debug.LogError("GachaMainFlow에 씬 View 연결이 필요합니다.", this);
                enabled = false;
                return;
            }
            GachaStageSheet stages;
            try
            {
                if (_balanceAsset == null)
                {
                    throw new FormatException("GachaBalance.asset 연결이 필요합니다.");
                }
                _balance = _sceneKind == GachaSceneKind.Title ? _balanceAsset.Load() : _currentRun?.Balance ?? _balanceAsset.Load();
                stages = new GachaStageSheet(_stageTable != null ? _stageTable.text : null, _balance);
            }
            catch (FormatException error)
            {
                Debug.LogError("밸런스 시트를 확인하세요. " + error.Message, this);
                enabled = false;
                return;
            }
            _focused = Application.isFocused;
            if (_sceneKind == GachaSceneKind.Shop)
            {
                _currentRun ??= new RunDataContainer(_balance);
                _run = _currentRun;
                _shop = new ShopDataContainer(_balance);
            }
            else if (_sceneKind == GachaSceneKind.Battle)
            {
                _currentRun ??= RunDataContainer.CreateBattleTestRun(_balance);
                _run = _currentRun;
                if (_run.RunCompleted)
                {
                    _transitioning = true;
                    SceneManager.LoadSceneAsync(TitleScenePath);
                    return;
                }
                if (_run.Party.Count == 0)
                {
                    _transitioning = true;
                    SceneManager.LoadSceneAsync(ShopScenePath);
                    return;
                }
                if (!_run.BattleInProgress)
                {
                    _run.TryBeginBattle();
                }
                _battle = new BattleDataContainer(_run.Party, stages.GetStage(_run.StageNumber), _run.AutoBattle);
                if (!_focused)
                {
                    _battle.SetPaused(true);
                }
            }
            else
            {
                _currentRun = null;
            }
            var handler = new GachaEventHandler(_sceneKind, _run, _shop, () => _focused,
                action => _pendingNavigation = action, count => UnityEngine.Random.Range(0, count), _battle);
            _queue = new EventQueue(new EventHandler(handler));
            _view?.Bind(Submit);
            _battleView?.Bind(Submit);
            Render();
            IsReady = true;
        }

        public void Submit(GachaAction action, int payload = 0)
        {
            if (IsReady && !_transitioning)
            {
                _inputs.Enqueue(new GachaActionRequested(action, payload));
            }
        }

        public void SubmitMove(Vector2 direction, float deltaTime)
        {
            if (IsReady && !_transitioning)
            {
                _inputs.Enqueue(new GachaMoveRequested(direction, deltaTime));
            }
        }

        public void SubmitCombat(Vector2 direction, bool attackHeld, Vector2? aimPoint = null)
        {
            if (IsReady && !_transitioning)
            {
                _inputs.Enqueue(new BattleCombatRequested(direction, attackHeld, aimPoint));
                _combatInputPending = true;
            }
        }

        private void Update()
        {
            if (!IsReady || _transitioning)
            {
                return;
            }
            _shop?.SetMoving(false);
            CollectInput();
            while (_inputs.TryDequeue(out var request))
            {
                _queue.AddEvent(request);
            }
            _combatInputPending = false;
            _queue.Update();
            if (_focused && !_pendingNavigation.HasValue)
            {
                _battle?.Tick(Time.deltaTime);
            }
            Render();
            if (_pendingNavigation.HasValue)
            {
                Navigate(_pendingNavigation.Value);
            }
        }

        private void CollectInput()
        {
            if (!_focused)
            {
                return;
            }
            if (_sceneKind == GachaSceneKind.Battle)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    Submit(_battle.Paused ? GachaAction.ResumeBattle : GachaAction.BattlePause);
                }
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    Submit(GachaAction.ToggleBattleControl);
                }
                if (!_battle.Paused)
                {
                    for (int index = 0; index < GachaCatalog.MaxPartySize; index++)
                    {
                        if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + index))
                            || Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + index)))
                        {
                            Submit(GachaAction.SwitchBattleCreature, index);
                        }
                    }
                    if (_battle.ControlMode == BattleControlMode.Auto)
                    {
                        return;
                    }
                    var battleDirection = new Vector2(
                        (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                        (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
                    bool pointerOnControls = PointerOverBattleControls(Input.mousePosition);
                    if (!_combatInputPending)
                    {
                        Camera camera = Camera.main;
                        bool insideArena = camera != null && camera.pixelRect.Contains(Input.mousePosition);
                        Vector2? aim = !pointerOnControls && insideArena ? (Vector2)camera.ScreenToWorldPoint(Input.mousePosition) : null;
                        SubmitCombat(battleDirection, Input.GetMouseButton(0) && !pointerOnControls && insideArena, aim);
                    }
                    if (Input.GetMouseButtonDown(1) && !pointerOnControls && Camera.main.pixelRect.Contains(Input.mousePosition))
                    {
                        Submit(GachaAction.BattleSpecial);
                    }
                    if (Input.GetKeyDown(KeyCode.Space))
                    {
                        Submit(GachaAction.BattleDodge);
                    }
                }
                return;
            }
            if (_sceneKind != GachaSceneKind.Shop)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Submit(_shop.Modal == ShopModal.None ? GachaAction.ReturnTitle : GachaAction.CloseModal);
            }
            if (Input.GetKeyDown(KeyCode.B) && _shop.Modal == ShopModal.None)
            {
                Submit(GachaAction.OpenCollection);
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                Submit(GachaAction.Interact);
            }
            var direction = new Vector2(
                (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
            if (direction.sqrMagnitude > 0f)
            {
                SubmitMove(direction, Time.deltaTime);
            }
        }

        private bool PointerOverBattleControls(Vector2 pointerPosition)
        {
            EventSystem current = EventSystem.current;
            if (current == null)
            {
                return false;
            }
            if (_pointerEventSystem != current)
            {
                _pointerEventSystem = current;
                _pointerEvent = new PointerEventData(current);
            }
            _pointerEvent.Reset();
            _pointerEvent.position = pointerPosition;
            _pointerHits.Clear();
            current.RaycastAll(_pointerEvent, _pointerHits);
            foreach (var hit in _pointerHits)
            {
                // Buttons must consume the click even while disabled by a cooldown or automatic mode.
                if (hit.gameObject.GetComponentInParent<Selectable>() != null)
                {
                    return true;
                }
            }
            return false;
        }

        private void Navigate(GachaAction action)
        {
            _pendingNavigation = null;
            _inputs.Clear();
            _transitioning = true;
            if (action == GachaAction.Quit)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return;
            }
            string path;
            if (action == GachaAction.BeginBattle)
            {
                path = BattleScenePath;
            }
            else if (action == GachaAction.FinishBattle && _battle.Phase == BattlePhase.Victory)
            {
                _run.CompleteBattle(true);
                path = _run.RunCompleted ? TitleScenePath : ShopScenePath;
            }
            else if (action == GachaAction.StartRun)
            {
                _currentRun = new RunDataContainer(_balance);
                path = ShopScenePath;
            }
            else
            {
                _run?.CompleteBattle(false);
                _currentRun = null;
                path = TitleScenePath;
            }
            SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
        }

        private void Render()
        {
            _view?.Render(_run, _shop, _balance);
            _battleView?.Render(_run, _battle);
        }

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            _inputs.Clear();
            _queue?.ClearPendingSyncEvents();
            _combatInputPending = false;
            _shop?.SetMoving(false);
            _battle?.ClearInput();
            if (!focused)
            {
                _battle?.SetPaused(true);
            }
        }

        private void OnDisable()
        {
            _inputs.Clear();
            _queue?.ClearPendingSyncEvents();
            _combatInputPending = false;
            _battle?.ClearInput();
        }

        private void OnDestroy()
        {
            IsReady = false;
            _view?.Unbind();
            _battleView?.Unbind();
            _inputs.Clear();
            _queue?.ClearPendingSyncEvents();
        }
    }
}

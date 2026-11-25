using System;
using Refactor.Main;
using Refactor.Main.Event;
using UnityEngine;
using UnityEngine.Scripting;

namespace GachaTrainers
{
    public enum GachaSceneKind
    {
        Title,
        Shop,
        Battle
    }

    public enum GachaAction
    {
        StartRun,
        Quit,
        Interact,
        Buy,
        CloseModal,
        SelectParty,
        ReleaseCreature,
        EquipCrystal,
        UnequipCrystal,
        ReturnTitle,
        ConfirmReturnTitle,
        BeginBattle,
        BattleAttack,
        BattleSpecial,
        BattleDodge,
        BattlePause,
        ResumeBattle,
        LeaveBattle,
        FinishBattle,
        ToggleBattleControl,
        SwitchBattleCreature,
        OpenCollection,
        SelectOwned,
        AssignCreature,
        BenchCreature,
        EvolveCreature,
        OpenEvolution,
        ChangeCollectionPage,
        ChangePoolPage
    }

    public sealed class GachaActionRequested : IEventData
    {
        public GachaAction Action { get; }
        public int Payload { get; }

        public GachaActionRequested(GachaAction action, int payload = 0)
        {
            Action = action;
            Payload = payload;
        }
    }

    public sealed class GachaMoveRequested : IEventData
    {
        public Vector2 Direction { get; }
        public float DeltaTime { get; }

        public GachaMoveRequested(Vector2 direction, float deltaTime)
        {
            Direction = direction;
            DeltaTime = deltaTime;
        }
    }

    public sealed class BattleCombatRequested : IEventData
    {
        public Vector2 Direction { get; }
        public bool AttackHeld { get; }
        public Vector2? AimPoint { get; }

        public BattleCombatRequested(Vector2 direction, bool attackHeld, Vector2? aimPoint = null)
        {
            Direction = direction;
            AttackHeld = attackHeld;
            AimPoint = aimPoint;
        }
    }

    [Preserve]
    public sealed class GachaEventHandler : IEventHandler
    {
        private readonly GachaSceneKind _sceneKind;
        private readonly RunDataContainer _run;
        private readonly ShopDataContainer _shop;
        private readonly Func<bool> _isFocused;
        private readonly Action<GachaAction> _navigate;
        private readonly Func<int, int> _roll;
        private readonly BattleDataContainer _battle;
        private bool _leaving;

        public GachaEventHandler(GachaSceneKind sceneKind, RunDataContainer run, ShopDataContainer shop,
            Func<bool> isFocused, Action<GachaAction> navigate, Func<int, int> roll, BattleDataContainer battle = null)
        {
            _sceneKind = sceneKind;
            _run = run;
            _shop = shop;
            _isFocused = isFocused;
            _navigate = navigate;
            _roll = roll;
            _battle = battle;
        }

        [Preserve]
        public void OnEvent(BattleCombatRequested request)
        {
            if (!_leaving && _isFocused() && _sceneKind == GachaSceneKind.Battle && !_battle.Paused)
            {
                if (request.AimPoint.HasValue)
                {
                    _battle.SetAim(request.AimPoint.Value);
                }
                _battle.SetMovement(request.Direction);
                _battle.SetBasicAttack(request.AttackHeld);
            }
        }

        [Preserve]
        public void OnEvent(GachaMoveRequested request)
        {
            if (!_leaving && _isFocused() && _sceneKind == GachaSceneKind.Shop)
            {
                _shop.Move(request.Direction, request.DeltaTime);
            }
        }

        [Preserve]
        public void OnEvent(GachaActionRequested request)
        {
            if (_leaving || !_isFocused())
            {
                return;
            }
            if (_sceneKind == GachaSceneKind.Title)
            {
                if (request.Action == GachaAction.StartRun || request.Action == GachaAction.Quit)
                {
                    Navigate(request.Action);
                }
                return;
            }
            if (_sceneKind == GachaSceneKind.Battle)
            {
                HandleBattle(request.Action, request.Payload);
                return;
            }
            if (request.Action == GachaAction.CloseModal)
            {
                _shop.CloseModal();
                return;
            }
            if (_shop.Modal == ShopModal.ReturnTitle)
            {
                if (request.Action == GachaAction.ConfirmReturnTitle)
                {
                    Navigate(GachaAction.ConfirmReturnTitle);
                }
                return;
            }
            if (_shop.Modal == ShopModal.Evolution)
            {
                if (request.Action == GachaAction.EvolveCreature && _run.TryEvolve(request.Payload))
                {
                    // Each recipe needs its own visible choice; queued double-clicks cannot choose the next recipe.
                    _shop.OpenCollection();
                }
                return;
            }
            if (_shop.Modal == ShopModal.Collection)
            {
                switch (request.Action)
                {
                    case GachaAction.SelectOwned: _run.SelectOwned(request.Payload); break;
                    case GachaAction.AssignCreature: _run.AssignSelected(request.Payload); break;
                    case GachaAction.BenchCreature: _run.BenchSelected(); break;
                    case GachaAction.ReleaseCreature: _run.ReleaseSelectedCreature(); break;
                    case GachaAction.EquipCrystal: _run.TryEquipCrystal((CrystalKind)request.Payload); break;
                    case GachaAction.UnequipCrystal: _run.UnequipSelectedCrystal(); break;
                    case GachaAction.ChangeCollectionPage: _shop.ChangeCollectionPage(request.Payload, _run.Owned.Count); break;
                    case GachaAction.OpenEvolution:
                        if (_run.EvolutionPending)
                        {
                            _shop.OpenEvolution();
                        }
                        break;
                }
                return;
            }
            if (_shop.Modal == ShopModal.Machine)
            {
                if (request.Action == GachaAction.Buy && _shop.NearestMachine == _shop.ActiveMachine)
                {
                    if (_run.TryPurchase(_shop.ActiveMachine, _roll) && _run.EvolutionPending)
                    {
                        _shop.OpenEvolution();
                    }
                }
                if (request.Action == GachaAction.ChangePoolPage)
                {
                    _shop.ChangePoolPage();
                }
                return;
            }
            switch (request.Action)
            {
                case GachaAction.OpenCollection:
                    _shop.OpenCollection();
                    break;
                case GachaAction.Interact:
                    if (_shop.OpenNearestMachine())
                    {
                        _run.SetStatus(_shop.ActiveMachine == GachaMachineKind.Creature
                            ? "크리쳐 " + _run.Balance.RaritySummary(true)
                            : "각인 수정: " + _run.Balance.RaritySummary(false));
                    }
                    else
                    {
                        _run.SetStatus("가챠 머신 앞에 가까이 다가가 E를 누르세요.");
                    }
                    break;
                case GachaAction.SelectParty:
                    _run.SelectParty(request.Payload);
                    break;
                case GachaAction.ReleaseCreature:
                    _run.ReleaseSelectedCreature();
                    break;
                case GachaAction.EquipCrystal:
                    _run.TryEquipCrystal((CrystalKind)request.Payload);
                    break;
                case GachaAction.UnequipCrystal:
                    _run.UnequipSelectedCrystal();
                    break;
                case GachaAction.ReturnTitle:
                    _shop.OpenReturnTitle();
                    break;
                case GachaAction.BeginBattle:
                    if (_run.TryBeginBattle())
                    {
                        Navigate(GachaAction.BeginBattle);
                    }
                    break;
            }
        }

        private void HandleBattle(GachaAction action, int payload)
        {
            bool finished = _battle.Phase == BattlePhase.Victory || _battle.Phase == BattlePhase.Defeat
                || _battle.Phase == BattlePhase.Draw;
            if (finished)
            {
                if (action == GachaAction.FinishBattle)
                {
                    Navigate(action);
                }
                return;
            }
            if (action == GachaAction.BattlePause)
            {
                _battle.SetPaused(true);
                return;
            }
            if (action == GachaAction.ToggleBattleControl)
            {
                bool automatic = _battle.ControlMode != BattleControlMode.Auto;
                _battle.SetControlMode(automatic ? BattleControlMode.Auto : BattleControlMode.Manual);
                _run.SetAutoBattle(automatic);
                return;
            }
            if (_battle.Paused)
            {
                if (action == GachaAction.ResumeBattle)
                {
                    _battle.SetPaused(false);
                }
                else if (action == GachaAction.LeaveBattle)
                {
                    Navigate(action);
                }
                return;
            }
            switch (action)
            {
                case GachaAction.SwitchBattleCreature:
                    _battle.TrySwitchPlayer(payload);
                    break;
                case GachaAction.BattleAttack:
                    _battle.TryBasicAttack();
                    break;
                case GachaAction.BattleSpecial:
                    _battle.TrySpecial();
                    break;
                case GachaAction.BattleDodge:
                    _battle.TryDodge();
                    break;
            }
        }

        private void Navigate(GachaAction action)
        {
            _leaving = true;
            _navigate(action);
        }
    }
}

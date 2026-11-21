using System;
using Refactor.Main.InputModule;
using UnityEngine.Scripting;

namespace Refactor.Main.Event
{
    [DISubscriber, Preserve]
    public sealed class DungeonEventHandler : IEventHandler
    {
        [Inject, Preserve] private DungeonDataContainer _data;
        private readonly InputFilter _filter;
        private readonly Action _openSettlement;
        private readonly Action<string, int> _adjustPurchase;
        private readonly Action _confirmSettlement;
        private readonly Action _cancelSettlement;
        private readonly Func<bool> _isCharacterOpen;
        private readonly Action _closeCharacter;
        private readonly Action<string> _openCharacter;
        private readonly Func<bool> _beforeCloseDetails;
        private readonly Func<bool> _isHeroMenuOpen;
        private readonly Action _closeHeroMenu;

        public DungeonEventHandler(InputFilter filter, Action openSettlement, Action<string, int> adjustPurchase = null,
            Action confirmSettlement = null, Action cancelSettlement = null,
            Func<bool> isCharacterOpen = null, Action closeCharacter = null, Action<string> openCharacter = null,
            Func<bool> beforeCloseDetails = null, Func<bool> isHeroMenuOpen = null, Action closeHeroMenu = null)
        {
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
            _openSettlement = openSettlement ?? throw new ArgumentNullException(nameof(openSettlement));
            _adjustPurchase = adjustPurchase;
            _confirmSettlement = confirmSettlement;
            _cancelSettlement = cancelSettlement;
            _isCharacterOpen = isCharacterOpen;
            _closeCharacter = closeCharacter;
            _openCharacter = openCharacter;
            _beforeCloseDetails = beforeCloseDetails;
            _isHeroMenuOpen = isHeroMenuOpen;
            _closeHeroMenu = closeHeroMenu;
        }

        [Preserve]
        public void OnEvent(DungeonActionRequested request)
        {
            if (_data == null || !_filter.IsInputAllowed(request.Action))
            {
                return;
            }
            if (_isHeroMenuOpen?.Invoke() ?? false)
            {
                if (request.Action == UIInputType.DungeonBack || request.Action == UIInputType.DungeonCloseDetails)
                {
                    _closeHeroMenu?.Invoke();
                }
                return;
            }
            if (_isCharacterOpen?.Invoke() ?? false)
            {
                if (request.Action == UIInputType.DungeonBack || request.Action == UIInputType.DungeonCloseDetails)
                {
                    _closeCharacter?.Invoke();
                }
                return;
            }
            switch (request.Action)
            {
                case UIInputType.DungeonMove:
                    _data.TryMove(request.TargetId, request.Context);
                    break;
                case UIInputType.DungeonAttack:
                    _data.TryAttack(request.TargetId, request.Context);
                    break;
                case UIInputType.DungeonSelectSkill:
                    _data.TrySelectCombatSkill(request.TargetId, request.Context);
                    break;
                case UIInputType.DungeonUseSkill:
                    _data.TryUseCombatSkill(request.TargetId, request.Context);
                    break;
                case UIInputType.DungeonRest:
                    _data.TryRest(request.Context);
                    break;
                case UIInputType.DungeonNextFloor:
                    _data.TryNextFloor(request.Context);
                    break;
                case UIInputType.DungeonTalk:
                    if (_openCharacter == null)
                    {
                        _data.TryTalk(request.Context);
                    }
                    else if (_data.CanTalk && request.Context == _data.Context)
                    {
                        _openCharacter(_data.VisitorId);
                    }
                    break;
                case UIInputType.DungeonCloseDetails:
                    if (_beforeCloseDetails?.Invoke() ?? true)
                    {
                        _data.CloseDetails();
                    }
                    break;
                case UIInputType.DungeonBack:
                    if (_data.SettlementOpen)
                    {
                        CancelSettlement();
                    }
                    else
                    {
                        if (_beforeCloseDetails?.Invoke() ?? true)
                        {
                            _data.CloseDetails();
                        }
                    }
                    break;
                case UIInputType.DungeonReturnTown:
                    if ((_beforeCloseDetails?.Invoke() ?? true) && _data.BeginSettlement())
                    {
                        _openSettlement();
                    }
                    break;
                case UIInputType.DungeonLootIncrease:
                case UIInputType.DungeonLootDecrease:
                    if (MatchesSettlement(request) && !_data.SettlementCommitted)
                    {
                        _adjustPurchase?.Invoke(request.TargetId, request.Action == UIInputType.DungeonLootIncrease ? 1 : -1);
                    }
                    break;
                case UIInputType.DungeonConfirmSettlement:
                    if (MatchesSettlement(request))
                    {
                        _confirmSettlement?.Invoke();
                    }
                    break;
                case UIInputType.DungeonCancelSettlement:
                    if (MatchesSettlement(request))
                    {
                        CancelSettlement();
                    }
                    break;
            }
        }

        private bool MatchesSettlement(DungeonActionRequested request)
        {
            return _data.SettlementOpen && request.Context == _data.SettlementContext;
        }

        private void CancelSettlement()
        {
            if (_data.CancelSettlement())
            {
                _cancelSettlement?.Invoke();
            }
        }
    }
}

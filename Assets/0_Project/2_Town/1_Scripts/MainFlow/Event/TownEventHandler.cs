using System;
using System.Globalization;
using Refactor.Main.InputModule;
using UnityEngine.Scripting;

namespace Refactor.Main.Event
{
    [DISubscriber, Preserve]
    public sealed class TownEventHandler : IEventHandler
    {
        [Inject, Preserve] private TownDataContainer _data;
        private readonly SceneType _scene;
        private readonly InputFilter _filter;
        private readonly Action<SceneType> _changeScene;
        private readonly Action _quit;
        private readonly Action<int> _enterDungeon;
        private readonly Action<string> _openShop;
        private readonly Action<TownActionRequested> _shopAction;
        private readonly Action _closeShop;
        private readonly Func<bool> _isCharacterOpen;
        private readonly Action<string> _openCharacter;
        private readonly Action _closeCharacter;
        private readonly Action<string> _openQuests;
        private readonly Action _closeQuests;
        private readonly Func<bool> _isHeroMenuOpen;
        private readonly Action _closeHeroMenu;

        public TownEventHandler(SceneType scene, InputFilter filter, Action<SceneType> changeScene, Action quit, Action<int> enterDungeon = null,
            Action<string> openShop = null, Action<TownActionRequested> shopAction = null, Action closeShop = null,
            Func<bool> isCharacterOpen = null, Action<string> openCharacter = null, Action closeCharacter = null,
            Action<string> openQuests = null, Action closeQuests = null,
            Func<bool> isHeroMenuOpen = null, Action closeHeroMenu = null)
        {
            _scene = scene;
            _filter = filter;
            _changeScene = changeScene;
            _quit = quit;
            _enterDungeon = enterDungeon;
            _openShop = openShop;
            _shopAction = shopAction;
            _closeShop = closeShop;
            _isCharacterOpen = isCharacterOpen;
            _openCharacter = openCharacter;
            _closeCharacter = closeCharacter;
            _openQuests = openQuests;
            _closeQuests = closeQuests;
            _isHeroMenuOpen = isHeroMenuOpen;
            _closeHeroMenu = closeHeroMenu;
        }

        [Preserve]
        public void OnEvent(TownActionRequested request)
        {
            if (!_filter.IsInputAllowed(request.Action))
            {
                return;
            }
            if (_scene == SceneType.Title)
            {
                if (request.Action == UIInputType.StartGame)
                {
                    _changeScene(SceneType.Town);
                }
                else if (request.Action == UIInputType.QuitGame)
                {
                    _quit();
                }
                return;
            }
            if (_scene != SceneType.Town || _data == null)
            {
                return;
            }
            if (_isHeroMenuOpen?.Invoke() ?? false)
            {
                if (request.Action == UIInputType.Back || request.Action == UIInputType.CloseDetails)
                {
                    _closeHeroMenu?.Invoke();
                }
                return;
            }
            if (_data.QuestBoardOpen)
            {
                if (request.Action == UIInputType.Back || request.Action == UIInputType.CloseDetails)
                {
                    _data.CloseDetails();
                    _closeQuests?.Invoke();
                }
                return;
            }
            if (_isCharacterOpen?.Invoke() ?? false)
            {
                if (request.Action == UIInputType.Back || request.Action == UIInputType.CloseDetails)
                {
                    _closeCharacter?.Invoke();
                }
                return;
            }
            if (_data.DungeonSelectionOpen)
            {
                switch (request.Action)
                {
                    case UIInputType.SelectDungeon:
                        if (int.TryParse(request.TargetId, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                        {
                            _data.TrySelectDungeon(id);
                        }
                        break;
                    case UIInputType.ConfirmDungeon:
                        if (_data.CanConfirmDungeon)
                        {
                            _enterDungeon?.Invoke(_data.SelectedDungeonId);
                        }
                        break;
                    case UIInputType.CancelDungeon:
                    case UIInputType.CloseDetails:
                    case UIInputType.Back:
                        _data.CloseDetails();
                        break;
                }
                return;
            }
            if (_data.ShopOpen)
            {
                if (request.Action == UIInputType.Back || request.Action == UIInputType.CloseDetails)
                {
                    _data.CloseDetails();
                    _closeShop?.Invoke();
                }
                else
                {
                    _shopAction?.Invoke(request);
                }
                return;
            }
            // 이미 수집된 뒤쪽 버튼 요청도 모달이 열렸다면 실행하지 않는다.
            bool hasDetails = !string.IsNullOrEmpty(_data.CommandId) || !string.IsNullOrEmpty(_data.CharacterId);
            if (hasDetails && request.Action != UIInputType.CloseDetails && request.Action != UIInputType.Back)
            {
                return;
            }
            switch (request.Action)
            {
                case UIInputType.ReturnTitle:
                    _changeScene(SceneType.Title);
                    break;
                case UIInputType.EnterLocation:
                    _data.TryEnterLocation(request.TargetId);
                    break;
                case UIInputType.LocationCommand:
                    if (_data.TryOpenCommand(request.LocationId, request.TargetId))
                    {
                        if (_data.ShopOpen)
                        {
                            _openShop?.Invoke(_data.ShopId);
                        }
                        else if (_data.QuestBoardOpen)
                        {
                            _openQuests?.Invoke(request.TargetId);
                        }
                    }
                    break;
                case UIInputType.SelectCharacter:
                    if (_data.TrySelectCharacter(request.LocationId, request.TargetId))
                    {
                        _openCharacter?.Invoke(request.TargetId);
                    }
                    break;
                case UIInputType.CloseDetails:
                    _data.CloseDetails();
                    break;
                case UIInputType.LeaveLocation:
                    _data.LeaveLocation();
                    break;
                case UIInputType.Back:
                    _data.Back();
                    break;
            }
        }
    }
}

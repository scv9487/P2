using System;
using System.IO;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine.Scripting;

namespace Refactor.Main.Event
{
    [DISubscriber, Preserve]
    public sealed class FoundationEventHandler : IEventHandler
    {
        [Inject] private FoundationDataContainer _data;
        private readonly InputFilter _filter;
        private readonly GameSystemSheet _settings;
        private readonly string _savePath;
        private readonly Action _requestDraftRefresh;

        public FoundationEventHandler(InputFilter filter, GameSystemSheet settings, string savePath, Action requestDraftRefresh)
        {
            _filter = filter;
            _settings = settings;
            _savePath = savePath;
            _requestDraftRefresh = requestDraftRefresh;
        }

        [Preserve]
        public void OnEvent(SetLabelRequested request)
        {
            if (_filter.IsInputAllowed(UIInputType.ApplyLabel))
            {
                if (_data.TrySetLabel(request.Label))
                {
                    _requestDraftRefresh?.Invoke();
                }
            }
        }

        [Preserve]
        public void OnEvent(UIActionRequested request)
        {
            // 입력을 수집한 뒤 팝업이 열릴 수 있으므로 실행 시점에 다시 확인한다.
            if (!_filter.IsInputAllowed(request.Action))
            {
                return;
            }
            switch (request.Action)
            {
                case UIInputType.Increment:
                    _data.TryAdjustCounter(1);
                    break;
                case UIInputType.Decrement:
                    _data.TryAdjustCounter(-1);
                    break;
                case UIInputType.Reset:
                    _data.Reset();
                    _requestDraftRefresh?.Invoke();
                    break;
                case UIInputType.OpenPopup:
                    _data.SetPopup(true);
                    break;
                case UIInputType.ClosePopup:
                    _data.SetPopup(false);
                    break;
                case UIInputType.Save:
                    Save();
                    break;
                case UIInputType.Load:
                    Load();
                    break;
            }
        }

        private void Save()
        {
            try
            {
                FoundationSave.Save(_savePath, _data);
                _data.SetStatus("저장 완료 · 현재 숫자와 문자열을 기록했습니다.");
            }
            catch (IOException exception)
            {
                _data.SetStatus("저장 실패 · " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                _data.SetStatus("저장 권한 오류 · " + exception.Message);
            }
        }

        private void Load()
        {
            var result = FoundationSave.Load(_savePath, _settings, out var dto);
            if (result == FoundationLoadResult.Loaded)
            {
                _data.Restore(dto);
                _requestDraftRefresh?.Invoke();
                _data.SetStatus("불러오기 완료 · 저장한 데이터로 복원했습니다.");
                return;
            }
            _data.SetStatus(result switch
            {
                FoundationLoadResult.Missing => "저장 파일이 없습니다. 먼저 저장해 주세요.",
                FoundationLoadResult.Corrupt => "저장 데이터가 올바르지 않습니다. 현재 상태를 유지합니다.",
                FoundationLoadResult.UnsupportedVersion => "지원하지 않는 저장 버전입니다. 현재 상태를 유지합니다.",
                _ => "파일을 읽을 수 없습니다. 현재 상태를 유지합니다."
            });
        }
    }
}

using System;
using Refactor.Util;

namespace Refactor.Main
{
    public interface IFoundationDataReader
    {
        int Counter { get; }
        int ProcessedRequestCount { get; }
        string Label { get; }
        bool PopupOpen { get; }
        string Status { get; }
    }

    // 상태의 원본은 이 객체가 소유한다. View에는 읽기 전용 계약만 전달한다.
    [DIPublisher]
    public sealed class FoundationDataContainer : IFoundationDataReader
    {
        private readonly GameSystemSheet _settings;

        public int Counter { get; private set; }
        public int ProcessedRequestCount { get; private set; }
        public string Label { get; private set; }
        public bool PopupOpen { get; private set; }
        public string Status { get; private set; }

        public FoundationDataContainer(GameSystemSheet settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Reset();
            Status = "준비 완료. 숫자와 이름을 변경한 뒤 저장 / 불러오기를 확인하세요.";
        }

        internal bool TryAdjustCounter(int direction)
        {
            if (direction != -1 && direction != 1)
            {
                Status = "숫자 변경 요청이 올바르지 않습니다.";
                return false;
            }
            long next = (long)Counter + (long)direction * _settings.CounterStep;
            if (next < -_settings.CounterLimit || next > _settings.CounterLimit)
            {
                Status = $"숫자는 -{_settings.CounterLimit}~{_settings.CounterLimit} 범위에서 변경할 수 있습니다.";
                return false;
            }
            if (!CanRecordMutation())
            {
                return false;
            }
            Counter = (int)next;
            ProcessedRequestCount++;
            Status = $"숫자 변경 완료: {Counter}";
            return true;
        }

        internal bool TrySetLabel(string label)
        {
            string trimmed = label?.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Length > _settings.LabelMaxLength)
            {
                Status = $"이름은 공백을 제외하고 1~{_settings.LabelMaxLength}자로 입력하세요.";
                return false;
            }
            if (Label == trimmed)
            {
                Status = "현재 이름과 같습니다.";
                return false;
            }
            if (!CanRecordMutation())
            {
                return false;
            }
            Label = trimmed;
            ProcessedRequestCount++;
            Status = "이름 적용 완료.";
            return true;
        }

        private bool CanRecordMutation()
        {
            if (ProcessedRequestCount == int.MaxValue)
            {
                Status = "변경 횟수 한도에 도달했습니다. 초기화 후 다시 시도하세요.";
                return false;
            }
            return true;
        }

        // 메모리 상태만 초기화한다. 디스크의 저장 파일은 유지한다.
        internal void Reset()
        {
            Counter = 0;
            ProcessedRequestCount = 0;
            Label = "P2";
            PopupOpen = false;
            Status = "화면 데이터를 초기화했습니다. 저장 파일은 유지됩니다.";
        }

        internal void SetPopup(bool open)
        {
            PopupOpen = open;
        }

        internal void SetStatus(string status)
        {
            Status = status ?? string.Empty;
        }

        // FoundationSave.Load가 형식과 범위를 검사한 DTO만 요청 처리 단계에서 복원한다.
        internal void Restore(FoundationSaveData dto)
        {
            Counter = dto.Counter;
            ProcessedRequestCount = dto.ProcessedRequestCount;
            Label = dto.Label;
            PopupOpen = false;
            Status = "저장된 숫자와 이름, 변경 횟수를 불러왔습니다.";
        }
    }
}

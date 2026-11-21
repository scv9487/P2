using System;
using Refactor.Main.Event;
using Refactor.Main.InputModule;
using Refactor.Util;

namespace Refactor.Main
{
    public sealed partial class MainFlow
    {
        private QuestSheet _questSheet;
        private readonly QuestBoardData _questBoard = new();
        public QuestSheet QuestSheet => _questSheet;
        public IQuestBoardReader QuestBoard => _questBoard;

        private void OpenQuests(string commandId)
        {
            if (_questSheet == null || !_townData.QuestBoardOpen)
            {
                return;
            }
            _questBoard.Show(_adventureStore.Progress.Quest.ActiveQuestId, _adventureStore.Progress.Revision);
            if (!_adventureStore.CanWrite)
            {
                _questBoard.ShowError(_questSheet.Text("status.load_failed"));
            }
        }

        private void CloseQuests()
        {
            _questBoard.Close();
            if (_townData != null && _townData.QuestBoardOpen)
            {
                _townData.CloseDetails();
            }
        }

        private void HandleQuestAction(QuestActionRequested request)
        {
            if (_sceneType != SceneType.Town || IsTransitioning || _heroMenu.Open || _questSheet == null
                || !_townData.QuestBoardOpen || !_questBoard.Open || request.Context != _questBoard.Context)
            {
                return;
            }
            if (request.Action == UIInputType.QuestClose)
            {
                CloseQuests();
                return;
            }
            QuestDefinition definition = null;
            foreach (var quest in _questSheet.Quests)
            {
                if (quest.Id == request.TargetId)
                {
                    definition = quest;
                    break;
                }
            }
            if (definition == null)
            {
                return;
            }
            if (request.Action == UIInputType.QuestSelect)
            {
                _questBoard.Select(definition.Id, _adventureStore.Progress.Revision);
                return;
            }
            if (request.TargetId != _questBoard.SelectedQuestId)
            {
                return;
            }
            QuestActionResult result;
            if (request.Action == UIInputType.QuestAccept)
            {
                result = _adventureStore.TryAcceptQuest(definition.Id, request.Context, _questBoard.SourceRevision);
            }
            else if (request.Action == UIInputType.QuestComplete)
            {
                result = _adventureStore.TryReportQuest(definition.Id, request.Context, _questBoard.SourceRevision);
            }
            else
            {
                return;
            }
            if (result == QuestActionResult.Saved || result == QuestActionResult.AlreadyApplied)
            {
                string text = request.Action == UIInputType.QuestAccept
                    ? string.Format(_questSheet.Text("status.accepted_format"), definition.Name)
                    : string.Format(_questSheet.Text("status.completed_format"), definition.Name, definition.RewardGold);
                _questBoard.Advance(_adventureStore.Progress.Revision, text);
                return;
            }
            string key = result switch
            {
                QuestActionResult.ActiveQuestExists => "status.already_active",
                QuestActionResult.AlreadyCompleted => "status.already_completed",
                QuestActionResult.NotReady => "status.not_ready",
                QuestActionResult.SaveFailed => "status.save_failed",
                QuestActionResult.LoadBlocked => "status.load_failed",
                _ => "status.invalid_request"
            };
            _questBoard.ShowError(_questSheet.Text(key));
        }

        // 처치 경험치와 의뢰 사건은 중앙 큐 처리/TickFlow 뒤의 저장 단계에서만 확정한다.
        private bool CommitDungeonProgress(bool retry)
        {
            if (!CommitDungeonExperience(retry))
            {
                return false;
            }
            if (_dungeonData == null || _adventureStore == null)
            {
                return true;
            }
            while (_dungeonData.PendingQuestFact != null)
            {
                var fact = _dungeonData.PendingQuestFact;
                var result = _adventureStore.TryApplyQuestFact(fact);
                if (result != QuestActionResult.Saved && result != QuestActionResult.AlreadyApplied
                    && result != QuestActionResult.Ignored)
                {
                    _dungeonData.ShowProgressSaveFailure(_questSheet.Text("status.progress_save_failed"));
                    return false;
                }
                _dungeonData.CompleteQuestFact(fact.Id);
            }
            _dungeonData.ClearProgressSaveFailure();
            return true;
        }
    }
}

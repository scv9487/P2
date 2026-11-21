using Refactor.Main.Event;
using Refactor.Main.InputModule;
using Refactor.Util;

namespace Refactor.Main
{
    public sealed partial class MainFlow
    {
        private SkillSheet _skillSheet;
        private readonly HeroMenuData _heroMenu = new();
        public SkillSheet SkillSheet => _skillSheet;
        public IHeroMenuReader HeroMenu => _heroMenu;

        private bool CanOpenHeroMenu()
        {
            if (_skillSheet == null || IsTransitioning || _heroMenu.Open || _characterInteraction.Open)
            {
                return false;
            }
            if (_sceneType == SceneType.Town)
            {
                return !_townData.ShopOpen && !_townData.QuestBoardOpen && !_townData.DungeonSelectionOpen
                    && string.IsNullOrEmpty(_townData.CommandId) && string.IsNullOrEmpty(_townData.CharacterId);
            }
            return _sceneType == SceneType.Dungeon && !_dungeonData.SettlementOpen && !_dungeonData.DetailsOpen
                && !_dungeonData.ProgressSaveBlocked && !_dungeonData.IsDefeated && !_dungeonData.IsCleared
                && _dungeonData.PendingExperience == null && _dungeonData.PendingQuestFact == null;
        }

        private void HandleHeroMenuAction(HeroMenuActionRequested request)
        {
            if (_skillSheet == null || IsTransitioning)
            {
                return;
            }
            var progress = _adventureStore.Progress;
            if (request.Action == UIInputType.InventoryOpen || request.Action == UIInputType.SkillsOpen)
            {
                if (request.Context == "hero" && request.TargetId == _characterSheet.PlayerId && CanOpenHeroMenu())
                {
                    _heroMenu.Show(request.Action == UIInputType.InventoryOpen ? HeroMenuMode.Inventory : HeroMenuMode.Skills,
                        progress.Revision);
                    if (!_adventureStore.CanWrite)
                    {
                        _heroMenu.ShowError(_skillSheet.Text("status.load_failed"));
                    }
                }
                return;
            }
            if (!_heroMenu.Open || request.Context != _heroMenu.Context)
            {
                return;
            }
            if (request.Action == UIInputType.HeroMenuClose)
            {
                _heroMenu.Close();
                return;
            }
            if (_heroMenu.Mode != HeroMenuMode.Skills)
            {
                if (_heroMenu.Mode == HeroMenuMode.Inventory)
                {
                    HandleInventoryAction(request);
                }
                return;
            }
            if (request.Action == UIInputType.SkillSelect)
            {
                foreach (var skill in _skillSheet.Skills)
                {
                    if (skill.Id == request.TargetId)
                    {
                        _heroMenu.Select(skill.Id, progress.Revision);
                        return;
                    }
                }
                return;
            }
            if (request.Action != UIInputType.SkillLearn || request.TargetId != _heroMenu.SelectedSkillId
                || string.IsNullOrEmpty(request.TargetId))
            {
                return;
            }
            var result = _adventureStore.TryLearnSkill(_characterSheet.PlayerId, request.TargetId, request.Context,
                _heroMenu.SourceRevision);
            if (result == SkillLearnResult.Saved || result == SkillLearnResult.AlreadyApplied)
            {
                _dungeonData?.SynchronizeParty();
                _heroMenu.Advance(progress.Revision,
                    string.Format(_skillSheet.Text("status.learned_format"), _skillSheet.GetSkill(request.TargetId).Name));
                return;
            }
            string key = result switch
            {
                SkillLearnResult.AlreadyLearned => "status.already_learned",
                SkillLearnResult.WrongTree => "status.wrong_tree",
                SkillLearnResult.LevelRequired => "status.level_required",
                SkillLearnResult.PrerequisiteRequired => "status.prerequisite_required",
                SkillLearnResult.InsufficientPoints => "status.insufficient_points",
                SkillLearnResult.SaveFailed => "status.save_failed",
                SkillLearnResult.LoadBlocked => "status.load_failed",
                SkillLearnResult.PlayerOnly => "status.player_only",
                _ => "status.invalid_skill"
            };
            _heroMenu.ShowError(_skillSheet.Text(key));
        }
    }
}

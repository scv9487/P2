using System;

namespace Refactor.Main
{
    public sealed partial class DungeonDataContainer
    {
        internal CombatantData PlayerUnit
        {
            get
            {
                foreach (var ally in _allies)
                {
                    if (ally.IsPlayerControlled)
                    {
                        return ally;
                    }
                }
                return null;
            }
        }

        internal bool CanActWithInventory
        {
            get
            {
                var player = PlayerUnit;
                return player != null && player.IsAlive && !ProgressSaveBlocked && PendingExperience == null
                    && PendingQuestFact == null && !DetailsOpen && !SettlementOpen && !IsDefeated && !IsCleared
                    && (!InCombat || ReadyAllyId == player.Id && player.Gauge >= 1f && player.StunRemaining <= 0f);
            }
        }

        // MainFlow는 영속 소모/장착의 저장 성공 뒤에만 이 메서드를 호출한다.
        internal void CompleteInventoryAction(float healing = 0f)
        {
            var player = PlayerUnit;
            if (player == null || !player.IsAlive || healing < 0f || float.IsNaN(healing) || float.IsInfinity(healing))
            {
                throw new InvalidOperationException("플레이어 아이템 행동의 실행 상태가 올바르지 않습니다.");
            }
            if (healing > 0f)
            {
                player.Heal(healing);
            }
            if (InCombat)
            {
                player.ResetGauge();
            }
            SelectedCombatSkillId = string.Empty;
            SelectReadyAlly();
            Revision++;
        }
    }
}

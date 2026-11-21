using System;
using System.Collections.Generic;

namespace Refactor.Main
{
    // 행동 게이지와 동료/적의 자동 공격을 진행한다. 플레이어의 공격은 EventQueue 단계에서 처리한다.
    public sealed class CombatTickFlow
    {
        private readonly DungeonDataContainer _data;

        public CombatTickFlow(DungeonDataContainer data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public void Update(float deltaTime)
        {
            if (!_data.InCombat || _data.ProgressSaveBlocked || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                return;
            }
            _data.SelectReadyAlly();
            float remaining = deltaTime;
            while (_data.InCombat)
            {
                bool attacked = false;
                foreach (var ally in _data.Allies)
                {
                    if (_data.ResolveCompanionAttack(ally))
                    {
                        attacked = true;
                        if (!_data.InCombat || _data.PendingExperience != null || _data.PendingQuestFact != null)
                        {
                            return;
                        }
                    }
                }
                // 선택 중의 시간과 준비 경계 뒤 남은 프레임 시간을 저장하지 않는다.
                // 같은 경계에서 준비된 동료의 자동 행동을 마친 뒤 플레이어의 선택을 기다린다.
                if (_data.WaitOnReady && _data.IsAwaitingCommand)
                {
                    return;
                }
                foreach (var enemy in _data.Enemies)
                {
                    if (enemy.IsAlive && enemy.StunRemaining <= 0f && enemy.Gauge >= 1f)
                    {
                        _data.ResolveEnemyAttack(enemy);
                        attacked = true;
                        if (!_data.InCombat || _data.Skills != null && (_data.PendingExperience != null || _data.PendingQuestFact != null))
                        {
                            return;
                        }
                    }
                }
                if (remaining <= 0)
                {
                    return;
                }
                float untilReady = Math.Min(_data.TimeUntilSkillTick,
                    Math.Min(TimeUntilReady(_data.Allies), TimeUntilReady(_data.Enemies)));
                if (float.IsPositiveInfinity(untilReady))
                {
                    return;
                }
                float elapsed = Math.Min(remaining, untilReady);
                if (float.IsNaN(elapsed) || float.IsInfinity(elapsed) || elapsed < 0f)
                {
                    throw new InvalidOperationException("전투 시간 경계가 유효하지 않습니다. 공격 속도와 효과 갱신 주기를 확인하세요.");
                }
                if (elapsed <= 0 && !attacked)
                {
                    return;
                }
                _data.ChargeGauges(elapsed);
                remaining -= elapsed;
                if (!_data.InCombat || _data.Skills != null && (_data.PendingExperience != null || _data.PendingQuestFact != null))
                {
                    return;
                }
                _data.SelectReadyAlly();
            }
        }

        private static float TimeUntilReady(IReadOnlyList<CombatantData> units)
        {
            float time = float.PositiveInfinity;
            foreach (var unit in units)
            {
                if (unit.IsAlive && unit.StunRemaining > 0f)
                {
                    time = Math.Min(time, unit.StunRemaining);
                }
                else if (unit.IsAlive && unit.Gauge < 1f)
                {
                    time = Math.Min(time, (1f - unit.Gauge) / unit.Speed);
                }
            }
            return time;
        }
    }
}

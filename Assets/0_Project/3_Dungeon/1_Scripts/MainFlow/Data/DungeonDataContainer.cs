using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Refactor.Util;

namespace Refactor.Main
{
    public interface IDungeonDataReader
    {
        DungeonDefinition Dungeon { get; }
        int DungeonId { get; }
        int Floor { get; }
        string NodeId { get; }
        string Context { get; }
        IReadOnlyList<DungeonNodeDefinition> Nodes { get; }
        IReadOnlyList<CombatantData> Allies { get; }
        IReadOnlyList<CombatantData> Enemies { get; }
        IReadOnlyDictionary<string, int> Loot { get; }
        bool SettlementOpen { get; }
        bool SettlementCommitted { get; }
        string SettlementContext { get; }
        bool InCombat { get; }
        string ReadyAllyId { get; }
        bool IsAwaitingCommand { get; }
        bool IsDefeated { get; }
        bool IsCleared { get; }
        bool DetailsOpen { get; }
        string DetailsTitle { get; }
        string DetailsBody { get; }
        string Status { get; }
        int Revision { get; }
        bool CanRest { get; }
        bool CanTalk { get; }
        bool CanNextFloor { get; }
        bool CanReturnTown { get; }
        string VisitorId { get; }
        bool CanMoveTo(string id);
        bool IsNodeResolved(string id);
        bool IsNodeVisited(string id);
        SkillSheet Skills { get; }
        string SelectedCombatSkillId { get; }
        bool CanUseCombatSkill(string skillId);
        bool CanTargetCombatSkill(string targetId);
    }

    [DIPublisher]
    public sealed partial class DungeonDataContainer : IDungeonDataReader
    {
        private readonly DungeonSheet _sheet;
        private readonly LootSheet _lootSheet;
        private readonly Dictionary<string, int> _loot = new(StringComparer.Ordinal);
        private readonly List<CombatantData> _allies = new();
        private readonly List<CombatantData> _enemies = new();
        private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
        private readonly HashSet<string> _resolved = new(StringComparer.Ordinal);
        private int _enemyTargetCursor;

        public DungeonDefinition Dungeon { get; }
        public int DungeonId => Dungeon.Id;
        public int Floor { get; private set; } = 1;
        public string NodeId { get; private set; }
        public string Context => Floor + ":" + NodeId;
        public IReadOnlyList<DungeonNodeDefinition> Nodes => _instanceFloors[Floor];
        public IReadOnlyList<CombatantData> Allies { get; }
        public IReadOnlyList<CombatantData> Enemies { get; }
        public IReadOnlyDictionary<string, int> Loot { get; }
        public bool SettlementOpen { get; private set; }
        public bool SettlementCommitted { get; private set; }
        public string SettlementContext { get; private set; } = string.Empty;
        public bool InCombat { get; private set; }
        public string ReadyAllyId { get; private set; } = string.Empty;
        public bool IsAwaitingCommand => ReadyAllyId.Length != 0;
        public bool IsDefeated { get; private set; }
        public bool IsCleared { get; private set; }
        public bool DetailsOpen { get; private set; }
        public string DetailsTitle { get; private set; } = string.Empty;
        public string DetailsBody { get; private set; } = string.Empty;
        public string Status { get; private set; }
        public int Revision { get; private set; }
        public bool CanRest => CanExplore && CurrentNode.Type == DungeonNodeType.Rest && !_resolved.Contains(Context);
        public string VisitorId => CurrentNode.Type == DungeonNodeType.Character
            && !(_characterProgress?.IsInParty(CurrentNode.CharacterId) ?? false) ? CurrentNode.CharacterId : string.Empty;
        public bool CanTalk => CanExplore && VisitorId.Length != 0;
        public bool CanNextFloor => CanExplore && CurrentNode.Type == DungeonNodeType.Stairs && Floor < Dungeon.FloorCount;
        public bool CanReturnTown => !ProgressSaveBlocked && !SettlementOpen && !SettlementCommitted && !InCombat && (!DetailsOpen || IsDefeated || IsCleared);
        internal bool WaitOnReady => _sheet.Settings.WaitOnReady;
        private bool CanExplore => !ProgressSaveBlocked && !SettlementOpen && !SettlementCommitted && !InCombat && !DetailsOpen && !IsDefeated && !IsCleared;
        private DungeonNodeDefinition CurrentNode => _instanceNodes[Context];

        public DungeonDataContainer(DungeonSheet sheet, int dungeonId, LootSheet lootSheet = null,
            CharacterSheet characters = null, IAdventureProgressReader progress = null, IRandomSource random = null, QuestSheet quests = null,
            SkillSheet skills = null)
        {
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _lootSheet = lootSheet;
            _skills = skills;
            Loot = new ReadOnlyDictionary<string, int>(_loot);
            Dungeon = sheet.GetDungeon(dungeonId);
            InitializeQuests(quests, progress);
            if (characters != null)
            {
                InitializeCharacters(characters, progress, random);
            }
            else
            {
                foreach (var member in sheet.Party)
                {
                    _allies.Add(new CombatantData(member.Id, member.Name, member.MaxHp, member.Attack, member.Speed, member.PortraitIndex,
                        isPlayerControlled: member.IsPlayerControlled));
                }
            }
            Allies = _allies.AsReadOnly();
            Enemies = _enemies.AsReadOnly();
            NodeId = Nodes[0].Id;
            _visited.Add(Context);
            Status = string.Format(_sheet.Text("status.enter_format"), Dungeon.Name, Floor);
        }

        public bool CanMoveTo(string id)
        {
            if (!CanExplore || string.IsNullOrEmpty(id) || id == NodeId)
            {
                return false;
            }
            foreach (string neighbor in CurrentNode.NeighborIds)
            {
                if (neighbor == id)
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsNodeResolved(string id) => _resolved.Contains(Floor + ":" + id);
        public bool IsNodeVisited(string id) => _visited.Contains(Floor + ":" + id);

        internal bool BeginSettlement()
        {
            if (!CanReturnTown)
            {
                return false;
            }
            SettlementOpen = true;
            SettlementContext = Guid.NewGuid().ToString("N");
            Revision++;
            return true;
        }

        internal bool CancelSettlement()
        {
            if (!SettlementOpen || SettlementCommitted)
            {
                return false;
            }
            SettlementOpen = false;
            SettlementContext = string.Empty;
            Revision++;
            return true;
        }

        internal void MarkSettlementCommitted()
        {
            if (SettlementOpen)
            {
                SettlementCommitted = true;
                Revision++;
            }
        }

        internal bool TryMove(string targetId, string context)
        {
            if (context != Context || !CanMoveTo(targetId))
            {
                return false;
            }
            NodeId = targetId;
            _visited.Add(Context);
            Status = string.Format(_sheet.Text("status.move_format"), CurrentNode.Name);
            Revision++;
            if (_resolved.Contains(Context))
            {
                return true;
            }
            if (CurrentNode.Type == DungeonNodeType.Battle || CurrentNode.Type == DungeonNodeType.Boss || CurrentNode.Type == DungeonNodeType.Rescue)
            {
                BeginCombat(CurrentNode.Type == DungeonNodeType.Boss, RescueEnemyCount);
            }
            else if (CurrentNode.Type == DungeonNodeType.Event)
            {
                HealParty(_sheet.Settings.EventHealAmount);
                _resolved.Add(Context);
                ShowDetails(CurrentNode.Name, string.Format(_sheet.Text("event.body_format"), CurrentNode.Description, _sheet.Settings.EventHealAmount));
                Status = _sheet.Text("status.event");
            }
            return true;
        }

        internal bool TryRest(string context)
        {
            if (context != Context || !CanRest)
            {
                return false;
            }
            HealParty(_sheet.Settings.RestHealAmount);
            _resolved.Add(Context);
            ShowDetails(CurrentNode.Name, string.Format(_sheet.Text("rest.body_format"), _sheet.Settings.RestHealAmount));
            Status = _sheet.Text("status.rest");
            Revision++;
            return true;
        }

        internal bool TryTalk(string context)
        {
            if (context != Context || !CanTalk)
            {
                return false;
            }
            var character = _sheet.GetCharacter(CurrentNode.CharacterId);
            ShowDetails(string.Format(_sheet.Text("dialogue.title_format"), character.Name), character.Greeting + "\n\n" + character.Dialogue);
            Revision++;
            return true;
        }

        internal bool TryNextFloor(string context)
        {
            if (context != Context || !CanNextFloor)
            {
                return false;
            }
            Floor++;
            NodeId = Nodes[0].Id;
            _visited.Add(Context);
            _enemies.Clear();
            Status = string.Format(_sheet.Text("status.enter_format"), Dungeon.Name, Floor);
            Revision++;
            return true;
        }

        internal bool CloseDetails()
        {
            if (ProgressSaveBlocked || SettlementOpen || SettlementCommitted || !DetailsOpen || IsCleared || IsDefeated)
            {
                return false;
            }
            DetailsOpen = false;
            DetailsTitle = string.Empty;
            DetailsBody = string.Empty;
            Revision++;
            return true;
        }

        internal bool TryAttack(string enemyId, string readyAllyId)
        {
            if (ProgressSaveBlocked || DetailsOpen || !InCombat || !IsAwaitingCommand || readyAllyId != ReadyAllyId)
            {
                return false;
            }
            var ally = Find(_allies, ReadyAllyId);
            var enemy = Find(_enemies, enemyId);
            if (ally == null || !ally.IsPlayerControlled || !ally.IsAlive || ally.Gauge < 1f || enemy == null || !enemy.IsAlive)
            {
                return false;
            }
            if (_skills != null)
            {
                var skill = FindAvailableSkill(ally, null);
                if (!ally.CanSpendSkill(skill) || !IsSkillTarget(ally, skill, enemyId)) return false;
                ExecuteCombatSkill(ally, skill, enemy);
            }
            else AttackEnemy(ally, enemy);
            return true;
        }

        // 동료는 TickFlow에서만 자동 행동한다. 살아 있는 첫 적을 공격하며 입력을 기다리지 않는다.
        internal bool ResolveCompanionAttack(CombatantData ally)
        {
            if (ProgressSaveBlocked || DetailsOpen || !InCombat || ally.IsPlayerControlled || !ally.IsAlive || ally.StunRemaining > 0f || ally.Gauge < 1f)
            {
                return false;
            }
            if (_skills != null) return TryAutomaticSkill(ally);
            foreach (var enemy in _enemies)
            {
                if (enemy.IsAlive)
                {
                    AttackEnemy(ally, enemy);
                    return true;
                }
            }
            return false;
        }

        private void AttackEnemy(CombatantData ally, CombatantData enemy)
        {
            var attack = ApplyAttack(ally, enemy);
            if (!enemy.IsAlive && _lootSheet != null)
            {
                // 모든 적이 회색쥐인 현재 콘텐츠의 처치 보상. 플레이어와 자동 동료가 이 경로를 공유한다.
                foreach (var drop in _lootSheet.GetDrops("gray_rat"))
                {
                    _loot.TryGetValue(drop.ItemId, out int previous);
                    _loot[drop.ItemId] = checked(previous + drop.Quantity);
                }
            }
            if (!enemy.IsAlive)
            {
                QueueKillExperience();
                QueueMonsterKilled(enemy.Id);
            }
            ally.ResetGauge();
            Status = FormatAttackStatus(ally, enemy, attack, false);
            Revision++;
            if (!AnyAlive(_enemies))
            {
                EndCombat(true);
            }
            else
            {
                SelectReadyAlly();
            }
        }

        private void BeginCombat(bool boss, int? enemyCount = null)
        {
            _enemies.Clear();
            _rewardedEnemies.Clear();
            _skillTickTime = 0f;
            SelectedCombatSkillId = string.Empty;
            var settings = _sheet.Settings;
            int count = enemyCount ?? (boss ? settings.BossEnemyCount : settings.NormalEnemyCount);
            int hp = (int)Math.Ceiling((Dungeon.RatHp + (Floor - 1) * settings.FloorHpBonus) * (boss ? settings.BossHpMultiplier : 1));
            int attack = (int)Math.Ceiling((Dungeon.RatAttack + (Floor - 1) * settings.FloorAttackBonus) * (boss ? settings.BossAttackMultiplier : 1));
            float speed = (Dungeon.RatSpeed + (Floor - 1) * settings.FloorSpeedBonus) * (boss ? settings.BossSpeedMultiplier : 1);
            for (int i = 0; i < count; i++)
            {
                _enemies.Add(_characters == null
                    ? new CombatantData("rat_" + i, _sheet.Text("enemy.rat"), hp, attack, speed, -1, boss)
                    : CreateRat(i, boss));
            }
            foreach (var ally in _allies)
            {
                ally.ResetGauge();
                ally.BeginSkillBattle();
            }
            ReadyAllyId = string.Empty;
            _enemyTargetCursor = 0;
            InCombat = true;
            Status = _sheet.Text("battle.start");
        }

        // 수동 입력을 기다리는 아군은 IsPlayerControlled인 한 명뿐이다.
        internal void SelectReadyAlly()
        {
            ReadyAllyId = string.Empty;
            foreach (var ally in _allies)
            {
                if (ally.IsPlayerControlled && ally.IsAlive && ally.StunRemaining <= 0f && ally.Gauge >= 1f)
                {
                    ReadyAllyId = ally.Id;
                    break;
                }
            }
        }

        internal void ResolveEnemyAttack(CombatantData enemy)
        {
            if (ProgressSaveBlocked || DetailsOpen || !InCombat || !enemy.IsAlive || enemy.StunRemaining > 0f || enemy.Gauge < 1f)
            {
                return;
            }
            for (int step = 0; step < _allies.Count; step++)
            {
                int index = (_enemyTargetCursor + step) % _allies.Count;
                var target = _allies[index];
                if (enemy.Effects.TryGetValue("AFFECTER_Taunt", out var taunt))
                {
                    var forced = Find(_allies, taunt.SourceId);
                    if (forced != null && forced.IsAlive) target = forced;
                }
                if (!target.IsAlive)
                {
                    continue;
                }
                _enemyTargetCursor = (index + 1) % _allies.Count;
                var attack = _skills == null ? ApplyAttack(enemy, target) : ResolveRatSkillAttack(enemy, target);
                enemy.ResetGauge();
                Status = FormatAttackStatus(enemy, target, attack, true);
                Revision++;
                if (!AnyAlive(_allies))
                {
                    EndCombat(false);
                }
                else if (!AnyAlive(_enemies))
                {
                    EndCombat(true);
                }
                else
                {
                    SelectReadyAlly();
                }
                return;
            }
        }

        internal void ChargeGauges(float seconds)
        {
            foreach (var ally in _allies)
            {
                ally.Charge(seconds);
                ally.TickSkills(seconds);
            }
            foreach (var enemy in _enemies)
            {
                enemy.Charge(seconds);
                enemy.TickSkills(seconds);
            }
            TickSkillEffects(seconds);
            Revision++;
        }

        private void EndCombat(bool won)
        {
            InCombat = false;
            ReadyAllyId = string.Empty;
            if (won)
            {
                _resolved.Add(Context);
                IsCleared = CurrentNode.Type == DungeonNodeType.Boss && Floor == Dungeon.FloorCount;
                ShowDetails(_sheet.Text(IsCleared ? "battle.cleared" : "battle.victory"),
                    _sheet.Text(IsCleared ? "battle.cleared_body" : "battle.victory_body"));
                CompleteQuestBattle();
            }
            else
            {
                IsDefeated = true;
                ShowDetails(_sheet.Text("battle.defeated"), _sheet.Text("battle.defeated_body"));
            }
            Status = DetailsTitle;
            Revision++;
        }

        private void ShowDetails(string title, string body)
        {
            DetailsOpen = true;
            DetailsTitle = title;
            DetailsBody = body;
        }

        private void HealParty(int amount)
        {
            foreach (var ally in _allies)
            {
                ally.Heal(amount);
            }
        }

        private static bool AnyAlive(List<CombatantData> units)
        {
            foreach (var unit in units)
            {
                if (unit.IsAlive)
                {
                    return true;
                }
            }
            return false;
        }

        private static CombatantData Find(List<CombatantData> units, string id)
        {
            foreach (var unit in units)
            {
                if (unit.Id == id)
                {
                    return unit;
                }
            }
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Refactor.Util;

namespace Refactor.Main
{
    public sealed class DungeonExperienceReward
    {
        public string Id { get; }
        public IReadOnlyDictionary<string, float> Grants { get; }
        public IReadOnlyDictionary<string, int> BasicAttackKills { get; }

        internal DungeonExperienceReward(Dictionary<string, float> grants, Dictionary<string, int> basicAttackKills)
        {
            Id = Guid.NewGuid().ToString("N");
            Grants = new ReadOnlyDictionary<string, float>(grants);
            BasicAttackKills = new ReadOnlyDictionary<string, int>(basicAttackKills);
        }
    }

    public sealed partial class DungeonDataContainer
    {
        private CharacterSheet _characters;
        private IAdventureProgressReader _characterProgress;
        private CombatStatCalculator _statCalculator;
        private IRandomSource _random;
        private readonly Dictionary<string, CombatantData> _expeditionUnits = new(StringComparer.Ordinal);
        private readonly List<string> _expeditionMembers = new();
        private readonly Queue<DungeonExperienceReward> _experienceRewards = new();
        private bool _previousDetailsOpen;
        private string _previousDetailsTitle;
        private string _previousDetailsBody;

        public bool ProgressSaveBlocked { get; private set; }
        public IReadOnlyList<string> ExpeditionMemberIds => _expeditionMembers.AsReadOnly();
        public DungeonExperienceReward PendingExperience => _experienceRewards.Count == 0 ? null : _experienceRewards.Peek();

        private void InitializeCharacters(CharacterSheet characters, IAdventureProgressReader progress, IRandomSource random)
        {
            _characters = characters;
            _characterProgress = progress ?? throw new ArgumentNullException(nameof(progress));
            _statCalculator = new CombatStatCalculator(characters.Combat);
            _random = random ?? new UnityRandomSource();
            SynchronizeParty(true);
        }

        // 저장된 파티/성장 변경만 반영한다. 탈퇴 후 재합류해도 탐험 중 HP는 보존한다.
        internal void SynchronizeParty(bool initializing = false)
        {
            if (_characters == null)
            {
                return;
            }
            var attack = _characters.Combat.GetAttack("player");
            _allies.Clear();
            foreach (string id in _characterProgress.PartyIds)
            {
                var definition = _characters.GetCharacter(id);
                var character = _characterProgress.GetCharacter(id);
                var stats = _skills == null ? _statCalculator.Calculate(character.Stats)
                    : CombatSkillPassives.CalculateStats(_statCalculator, character.Stats, _skills, character);
                if (!_expeditionUnits.TryGetValue(id, out var unit))
                {
                    unit = new CombatantData(id, definition.Name, stats, attack, definition.PortraitIndex,
                        isPlayerControlled: definition.IsPlayer);
                    _expeditionUnits.Add(id, unit);
                    _expeditionMembers.Add(id);
                }
                else
                {
                    unit.RefreshStats(stats, attack);
                }
                unit.ConfigureSkills(_skills, character);
                _allies.Add(unit);
            }
            if (!initializing)
            {
                SelectReadyAlly();
                if (InCombat && !AnyAlive(_allies))
                {
                    EndCombat(false);
                }
                Revision++;
            }
        }

        private CombatantData CreateRat(int index, bool boss)
        {
            var settings = _sheet.Settings;
            var baseline = _sheet.Dungeons[0];
            float health = (Dungeon.RatHp + (Floor - 1) * settings.FloorHpBonus) / (float)baseline.RatHp;
            float attack = (Dungeon.RatAttack + (Floor - 1) * settings.FloorAttackBonus) / (float)baseline.RatAttack;
            float speed = (Dungeon.RatSpeed + (Floor - 1) * settings.FloorSpeedBonus) / baseline.RatSpeed;
            if (boss)
            {
                health *= settings.BossHpMultiplier;
                attack *= settings.BossAttackMultiplier;
                speed *= settings.BossSpeedMultiplier;
            }
            var stats = _statCalculator.CalculateEnemy("grey_rat").WithMultipliers(health, attack, speed);
            var unit = new CombatantData("rat_" + index, _sheet.Text("enemy.rat"), stats,
                _characters.Combat.GetAttack("grey_rat"), -1, boss);
            unit.ConfigureEnemySkills(_skills);
            return unit;
        }

        private CombatAttackResult ApplyAttack(CombatantData attacker, CombatantData target)
        {
            var result = attacker.Stats == null || target.Stats == null
                ? new CombatAttackResult(CombatOutcome.Normal, attacker.Attack, 0)
                : CombatCalculator.ResolveAttack(attacker.Stats, target.Stats, _random,
                    CombatDamageType.Physical, attacker.AttackCoefficient);
            target.TakeDamage(result.Damage);
            attacker.Heal(result.LifeSteal);
            return result;
        }

        private string FormatAttackStatus(CombatantData attacker, CombatantData target, CombatAttackResult result, bool enemy)
        {
            string key = result.Outcome == CombatOutcome.Miss ? "battle.miss_format"
                : result.Outcome == CombatOutcome.Guard ? "battle.guard_format"
                : result.Outcome == CombatOutcome.Critical ? "battle.critical_format"
                : enemy ? "battle.damage_format" : "battle.attack_format";
            return string.Format(_sheet.Text(key), attacker.Name, target.Name,
                result.Damage.ToString("0.##", CultureInfo.InvariantCulture));
        }

        // 전투 시뮬레이션에서 보상 사실만 기록한다. MainFlow가 같은 프레임의 후속 처리에서 저장한다.
        private void QueueKillExperience(CombatantData killer = null, SkillDefinition skill = null)
        {
            if (_characters == null)
            {
                return;
            }
            var recipients = new List<CombatantData>();
            foreach (var ally in _allies)
            {
                // DS는 전투 영역에 남아 있는 쓰러진 파티원도 EXP 분배에 포함한다.
                recipients.Add(ally);
            }
            if (recipients.Count == 0)
            {
                return;
            }
            float share = _characters.Combat.GetEnemy("grey_rat").OnDeathExperience / recipients.Count;
            var grants = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var recipient in recipients)
            {
                grants.Add(recipient.Id, Math.Max(0, share * (1f + recipient.Stats.ExperienceGainBonus * 0.01f)));
            }
            var kills = new Dictionary<string, int>(StringComparer.Ordinal);
            if (killer != null && skill != null && skill.IsBasicAttack && killer.HasSkill("SKILL_SharpShooter"))
            {
                kills.Add(killer.Id, 1);
            }
            _experienceRewards.Enqueue(new DungeonExperienceReward(grants, kills));
        }

        internal void CompleteExperienceReward(string id)
        {
            if (PendingExperience != null && PendingExperience.Id == id)
            {
                _experienceRewards.Dequeue();
            }
        }

        internal void ShowProgressSaveFailure(string message)
        {
            if (!ProgressSaveBlocked)
            {
                _previousDetailsOpen = DetailsOpen;
                _previousDetailsTitle = DetailsTitle;
                _previousDetailsBody = DetailsBody;
            }
            ProgressSaveBlocked = true;
            ShowDetails(_characters.Text("status.save_failed"), message);
            Status = message;
            Revision++;
        }

        internal void ClearProgressSaveFailure()
        {
            if (ProgressSaveBlocked)
            {
                ProgressSaveBlocked = false;
                DetailsOpen = _previousDetailsOpen;
                DetailsTitle = _previousDetailsTitle;
                DetailsBody = _previousDetailsBody;
                Status = DetailsOpen ? DetailsTitle : _sheet.Text("battle.start");
                Revision++;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Refactor;
using Refactor.Main;
using Refactor.Main.Event;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEditor;
using UnityEngine;
using EventHandler = Refactor.Main.Event.EventHandler;
using Object = UnityEngine.Object;

namespace P2.Tests
{
    public sealed class SkillCombatTests
    {
        private DungeonContent _content;
        private DungeonSheet _dungeon;
        private LootSheet _loot;
        private ShopSheet _shops;
        private CharacterSheet _characters;
        private QuestSheet _quests;
        private SkillSheet _skills;
        private AdventureProgressStore _store;
        private DungeonDataContainer _data;
        private CombatTickFlow _tick;
        private InputStream _stream;
        private UIInputStream _input;
        private EventQueue _queue;
        private MainFlow _flow;
        private GameObject _host;
        private Func<bool, bool> _commit;
        private NormalRandom _random;
        private string _directory;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            DI.Init();
            _random = new NormalRandom();
            _content = AssetDatabase.LoadAssetAtPath<DungeonContent>("Assets/0_Project/3_Dungeon/3_Data/DungeonContent.asset");
            Assert.That(_content, Is.Not.Null);
            _dungeon = _content.Load();
            _loot = _content.LoadLoot();
            _shops = _content.LoadShop();
            _characters = _content.LoadCharacters();
            _quests = _content.LoadQuests();
            _skills = _content.LoadSkills();
            _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "SkillCombatTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "progress.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }
            DI.Init();
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "SkillCombatTests")) + Path.DirectorySeparatorChar;
            Assert.That(_directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void LearnedVerticalSlashDealsDsThreeHundredPercentAndConsumesEnergyAndAdjustedCooldownOnce()
        {
            CreateStore();
            Learn("SKILL_VerticalSlash");
            Learn("SKILL_FastHand");
            CreateRuntime();
            var hero = _data.Allies.Single();
            Assert.That(hero.BasicAttackId, Is.EqualTo("SKILL_BasicSwordAttack"));
            Assert.That(hero.AttackCoefficient, Is.EqualTo(1f));
            Assert.That(hero.Speed, Is.EqualTo(hero.Stats.AttackSpeed / 1f).Within(0.0001f), "검의 기본 주기는 DS 시작 0.8초 + 종료 0.2초입니다.");
            Ready();
            var rat = _data.Enemies[0];
            float beforeHp = rat.Hp;
            float beforeEnergy = hero.Energy;
            Use("SKILL_VerticalSlash", rat.Id);
            Assert.That(beforeHp - rat.Hp, Is.EqualTo(hero.Stats.PhysicalAttackPower * 3f).Within(0.001f));
            Assert.That(beforeEnergy - hero.Energy, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(hero.GetCooldown("SKILL_VerticalSlash"), Is.EqualTo(12f / (1f + hero.Stats.CooldownReduction * 0.01f)).Within(0.0001f));
            Assert.That(hero.GlobalCooldown, Is.EqualTo(_skills.GlobalCooldown / (1f + hero.Stats.CooldownReduction * 0.01f)).Within(0.0001f));
            float afterHp = rat.Hp;
            float afterEnergy = hero.Energy;
            Use("SKILL_VerticalSlash", rat.Id);
            Assert.That(rat.Hp, Is.EqualTo(afterHp));
            Assert.That(hero.Energy, Is.EqualTo(afterEnergy));
            Ready();
            Assert.That(_data.CanUseCombatSkill("SKILL_VerticalSlash"), Is.False);
            Assert.That(_data.CanUseCombatSkill(string.Empty), Is.True, "개별/글로벌 재사용 대기 중에도 준비된 기본 공격은 가능합니다.");
            beforeHp = rat.Hp;
            Request(UIInputType.DungeonAttack, rat.Id, _data.ReadyAllyId);
            Assert.That(beforeHp - rat.Hp, Is.EqualTo(hero.Stats.PhysicalAttackPower).Within(0.001f));
        }

        [Test]
        public void PermanentFastHandAndResolveUseDsPercentageAndFlatAdditionsAndRestoreFromLearnedIds()
        {
            CreateStore(4);
            Learn("SKILL_FastHand");
            Learn("SKILL_SwordmansResolve");
            var source = _store.Progress.GetCharacter("hero");
            var basic = new CombatStatCalculator(_characters.Combat).Calculate(source.Stats);
            CreateRuntime();
            var hero = _data.Allies.Single();
            Assert.That(hero.Stats.AttackSpeed, Is.EqualTo(basic.AttackSpeed * 1.12f).Within(0.0001f));
            Assert.That(hero.Stats.CooldownReduction, Is.EqualTo(basic.CooldownReduction + 12f).Within(0.0001f));
            Assert.That(hero.Stats.PhysicalAttackPower, Is.EqualTo(basic.PhysicalAttackPower));
            Assert.That(source.Stats.Agility, Is.EqualTo(_characters.GetCharacter("hero").BaseStats.Agility));
            var restored = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests, _skills);
            Assert.That(restored.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            var second = new DungeonDataContainer(_dungeon, 1, _loot, _characters, restored.Progress, _random, _quests, _skills);
            Assert.That(second.Allies.Single().Stats.AttackSpeed, Is.EqualTo(hero.Stats.AttackSpeed));
            Assert.That(second.Allies.Single().Stats.CooldownReduction, Is.EqualTo(hero.Stats.CooldownReduction));
        }

        [Test]
        public void LevelFourCounterPrerequisiteUnlocksPommelAndStunPausesWithPlayerWaitThenExpiresWithoutBacklog()
        {
            CreateStore(4, agilityGrowth: 100);
            Assert.That(_store.TryLearnSkill("hero", "SKILL_PommelStrike", "without_counter", _store.Progress.Revision), Is.EqualTo(SkillLearnResult.PrerequisiteRequired));
            Learn("SKILL_SwordCounter");
            Learn("SKILL_PommelStrike");
            CreateRuntime();
            Ready();
            var rat = _data.Enemies[0];
            Use("SKILL_PommelStrike", rat.Id);
            Assert.That(rat.StunRemaining, Is.EqualTo(1f).Within(0.0001f));
            float gauge = rat.Gauge;
            _tick.Update(0.2f);
            Assert.That(rat.StunRemaining, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(rat.Gauge, Is.EqualTo(gauge));
            Ready();
            Assert.That(rat.StunRemaining, Is.GreaterThan(0));
            float paused = rat.StunRemaining;
            float cooldown = _data.Allies.Single().GetCooldown("SKILL_PommelStrike");
            _tick.Update(100f);
            Assert.That(rat.StunRemaining, Is.EqualTo(paused));
            Assert.That(_data.Allies.Single().GetCooldown("SKILL_PommelStrike"), Is.EqualTo(cooldown));
            Request(UIInputType.DungeonAttack, _data.Enemies[1].Id, _data.ReadyAllyId);
            _tick.Update(paused);
            Assert.That(rat.StunRemaining, Is.Zero.Within(0.0001f));
            Assert.That(rat.Gauge, Is.EqualTo(gauge).Within(0.0001f));
            _tick.Update(0.05f);
            Assert.That(rat.Gauge, Is.GreaterThan(gauge));
            Assert.That(rat.Gauge - gauge, Is.EqualTo(rat.Speed * 0.05f).Within(0.0001f));
        }

        [Test]
        public void LastHitOfChainSlashProducesOneLootExperienceAndQuestFactAndFailedSaveCanRetryWithoutRepeatingThem()
        {
            ChangeRatHealth(120);
            CreateStore(4);
            Learn("SKILL_VerticalSlash");
            Learn("SKILL_ChainSlash");
            Assert.That(_store.TryAcceptQuest("kill_rats", "accept", _store.Progress.Revision), Is.EqualTo(QuestActionResult.Saved));
            CreateRuntime();
            Ready();
            float oneHit = _data.Allies.Single().Stats.PhysicalAttackPower;
            Assert.That(oneHit * 3f, Is.LessThan(_data.Enemies[0].Hp));
            Assert.That(oneHit * 4f, Is.GreaterThanOrEqualTo(_data.Enemies[0].Hp));
            Use("SKILL_ChainSlash", "rat_0");
            Assert.That(_data.Enemies[0].IsAlive, Is.False);
            Assert.That(_data.Loot["rat_tail"], Is.EqualTo(1));
            var reward = _data.PendingExperience;
            var fact = _data.PendingQuestFact;
            Assert.That(reward, Is.Not.Null);
            Assert.That(reward.Grants["hero"], Is.EqualTo(8));
            Assert.That(reward.BasicAttackKills, Is.Empty);
            Assert.That(fact.Type, Is.EqualTo(QuestFactType.KillMonster));
            string saved = File.ReadAllText(_path);
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(_commit(false), Is.False);
            Assert.That(_data.ProgressSaveBlocked, Is.True);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            float survivorHp = _data.Enemies[1].Hp;
            _tick.Update(100);
            Request(UIInputType.DungeonAttack, "rat_1", "hero");
            Assert.That(_data.Enemies[1].Hp, Is.EqualTo(survivorHp));
            Assert.That(_data.PendingExperience, Is.SameAs(reward));
            Directory.Delete(_path + ".tmp");
            Assert.That(_commit(true), Is.True);
            Assert.That(_data.PendingExperience, Is.Null);
            Assert.That(_data.PendingQuestFact, Is.Null);
            Assert.That(_store.Progress.GetCharacter("hero").Experience, Is.EqualTo(8));
            Assert.That(_store.Progress.Quest.ProgressCount, Is.EqualTo(1));
            Assert.That(_store.TryGrantExperience(reward.Id, reward.Grants, _random, reward.BasicAttackKills), Is.EqualTo(CharacterActionResult.AlreadyApplied));
            Assert.That(_store.TryApplyQuestFact(fact), Is.EqualTo(QuestActionResult.AlreadyApplied));
            Assert.That(_data.Loot["rat_tail"], Is.EqualTo(1));
        }

        [Test]
        public void CounterKillStopsBeforeTheNextReadyEnemyAndBeforeMoreCombatTimeUntilSaved()
        {
            ChangeRatHealth(50);
            CreateStore();
            Learn("SKILL_SwordCounter");
            CreateRuntime();
            Ready();
            var hero = _data.Allies.Single();
            var first = _data.Enemies[0];
            var second = _data.Enemies[1];
            Use("SKILL_SwordCounter", hero.Id);
            float cooldown = hero.GetCooldown("SKILL_SwordCounter");
            float untilEnemy = (1f - first.Gauge) / first.Speed;
            // 적 명중·플레이어 가드, 이어지는 반격 명중·비가드·비치명.
            _random.QueueRolls(0f, 0f, 0f, 99f, 99f);
            _tick.Update(100f);
            Assert.That(first.IsAlive, Is.False);
            Assert.That(_data.PendingExperience, Is.Not.Null);
            Assert.That(second.IsAlive, Is.True);
            Assert.That(second.Gauge, Is.EqualTo(1f).Within(0.0001f), "같은 경계의 두 번째 적은 저장 전에 행동하지 않습니다.");
            Assert.That(hero.Gauge, Is.EqualTo(hero.Speed * untilEnemy).Within(0.0001f));
            Assert.That(hero.GetCooldown("SKILL_SwordCounter"), Is.EqualTo(cooldown - untilEnemy).Within(0.0001f));
            Assert.That(_data.Loot["rat_tail"], Is.EqualTo(1));
            Assert.That(_store.Progress.GetCharacter("hero").Experience, Is.Zero);
            Assert.That(_commit(false), Is.True);
            Assert.That(_store.Progress.GetCharacter("hero").Experience, Is.EqualTo(8));
            Assert.That(second.Gauge, Is.EqualTo(1f));
        }

        [Test]
        public void LearnedSharpShooterRecordsOnlyTheBasicAttackKillAlongsideItsExperience()
        {
            ChangeRatHealth(20);
            CreateStore();
            Learn("SKILL_SharpShooter");
            CreateRuntime();
            Ready();
            Request(UIInputType.DungeonAttack, "rat_0", _data.ReadyAllyId);
            var reward = _data.PendingExperience;
            Assert.That(reward.BasicAttackKills["hero"], Is.EqualTo(1));
            Assert.That(_commit(false), Is.True);
            Assert.That(_store.Progress.GetCharacter("hero").BasicAttackKills, Is.EqualTo(1));
            Assert.That(_store.Progress.GetCharacter("hero").Experience, Is.EqualTo(8));
            Assert.That(_store.TryGrantExperience(reward.Id, reward.Grants, _random, reward.BasicAttackKills), Is.EqualTo(CharacterActionResult.AlreadyApplied));
        }

        private void CreateStore(int level = 1, float agilityGrowth = 0f)
        {
            _store = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests, _skills);
            Assert.That(_store.Load(), Is.EqualTo(AdventureLoadResult.Missing));
            if (level == 1 && agilityGrowth == 0f)
            {
                return;
            }
            Assert.That(_store.TryTalk("mira", "seed", _store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            int hero = Array.IndexOf(dto.CharacterIds, "hero");
            int skillHero = Array.IndexOf(dto.SkillCharacterIds, "hero");
            dto.CharacterLevels[hero] = level;
            dto.CharacterAgilityGrowth[hero] = agilityGrowth;
            dto.CharacterMainSkillPoints[skillHero] = _skills.InitialMainSkillPoints + (level - 1) * _skills.MainSkillPointsPerLevel;
            dto.CharacterSubSkillPoints[skillHero] = _skills.InitialSubSkillPoints + (level - 1) * _skills.SubSkillPointsPerLevel;
            AdventureSave.Save(_path, dto, _loot, _shops, _characters, _quests, _skills);
            Assert.That(_store.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
        }

        private void Learn(string id)
        {
            Assert.That(_store.TryLearnSkill("hero", id, "learn_" + id, _store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved), id);
        }

        private void CreateRuntime()
        {
            _data = new DungeonDataContainer(_dungeon, 1, _loot, _characters, _store.Progress, _random, _quests, _skills);
            DI.PubSub(_data);
            _tick = new CombatTickFlow(_data);
            _host = new GameObject("SkillCombatTests isolated MainFlow");
            _host.SetActive(false);
            _flow = _host.AddComponent<MainFlow>();
            Field("_sceneType", SceneType.Dungeon);
            Field("_characterSheet", _characters);
            Field("_questSheet", _quests);
            Field("_skillSheet", _skills);
            Field("_characterRandom", _random);
            Field("_adventureStore", _store);
            Field("_dungeonData", _data);
            _commit = (Func<bool, bool>)Delegate.CreateDelegate(typeof(Func<bool, bool>), _flow,
                typeof(MainFlow).GetMethod("CommitDungeonProgress", BindingFlags.Instance | BindingFlags.NonPublic));
            _stream = new InputStream();
            _input = new UIInputStream(_stream);
            _queue = new EventQueue(new EventHandler(new DungeonEventHandler(new InputFilter(() => false), () => { },
                beforeCloseDetails: () => _commit(true))));
            Request(UIInputType.DungeonMove, "guard", _data.Context);
            Assert.That(_data.InCombat, Is.True);
        }

        private void Ready()
        {
            _tick.Update(100);
            Assert.That(_data.IsAwaitingCommand, Is.True);
        }

        private void Use(string skill, string target)
        {
            string readyId = _data.ReadyAllyId;
            Request(UIInputType.DungeonSelectSkill, skill, readyId);
            Request(UIInputType.DungeonUseSkill, target, readyId);
        }

        private void Request(UIInputType type, string target = null, string context = null)
        {
            _input.AddInput(type, target, context);
            foreach (var request in new InputBinding(_stream).ConvertInputStreamToEventData())
            {
                _queue.AddEvent(request);
            }
            _stream.ClearInputs();
            _queue.Update();
        }

        private void ChangeRatHealth(int hp)
        {
            string[] lines = _content.Characters.CombatEnemies.text.Replace("\r", string.Empty).Split('\n');
            int column = Array.IndexOf(lines[0].Split(','), "MaxHealth");
            Assert.That(column, Is.GreaterThanOrEqualTo(0));
            for (int index = 1; index < lines.Length; index++)
            {
                string[] cells = lines[index].Split(',');
                if (cells[0] == "grey_rat")
                {
                    cells[column] = hp.ToString();
                    lines[index] = string.Join(",", cells);
                }
            }
            var combat = new CombatStatSheet(_content.Characters.CombatStats.text, string.Join("\n", lines), _content.Characters.CombatAttacks.text);
            _characters = new CharacterSheet(_content.Characters.Characters.text, _content.Characters.Settings.text,
                _content.Characters.Texts.text, _content.Town.Load(), _characters.Growth, combat);
        }

        private void Field(string name, object value) => typeof(MainFlow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_flow, value);

        private sealed class NormalRandom : IRandomSource
        {
            private int _draw;
            private readonly Queue<float> _queued = new();
            public void QueueRolls(params float[] values)
            {
                foreach (float value in values)
                {
                    _queued.Enqueue(value);
                }
            }
            public int NextInt(int minimum, int maximum) => minimum;
            public float NextFloat(float minimum, float maximum) => _queued.Count > 0 ? _queued.Dequeue() : _draw++ % 3 == 0 ? 0f : 99f;
        }
    }
}

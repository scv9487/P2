using System;
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
    public sealed class EquipmentCombatTests
    {
        private DungeonSheet _dungeon;
        private LootSheet _loot;
        private ShopSheet _shops;
        private CharacterSheet _characters;
        private QuestSheet _quests;
        private SkillSheet _skills;
        private EquipmentSheet _equipment;
        private AdventureProgressStore _store;
        private DungeonDataContainer _data;
        private CombatTickFlow _tick;
        private MainFlow _flow;
        private GameObject _host;
        private InputStream _stream;
        private UIInputStream _input;
        private EventQueue _queue;
        private string _directory;
        private string _path;
        private CombatantData Hero => _data.Allies.Single();

        [SetUp]
        public void SetUp()
        {
            DI.Init();
            var content = AssetDatabase.LoadAssetAtPath<DungeonContent>("Assets/0_Project/3_Dungeon/3_Data/DungeonContent.asset");
            Assert.That(content, Is.Not.Null);
            _dungeon = content.Load();
            _loot = content.LoadLoot();
            _shops = content.LoadShop();
            _characters = content.LoadCharacters();
            _quests = content.LoadQuests();
            _skills = content.LoadSkills();
            _equipment = content.LoadEquipment();
            _directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "P2-EquipmentCombatTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "progress.json");
            _store = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests, _skills, _equipment);
            Assert.That(_store.Load(), Is.EqualTo(AdventureLoadResult.Missing));
            Assert.That(_store.TryTalk("mira", "seed", _store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            dto.ItemIds = new[] { "weapon_sword", "weapon_greatsword", "health_potion" };
            dto.ItemCounts = new[] { 1, 1, 3 };
            AdventureSave.Save(_path, dto, _loot, _shops, _characters, _quests, _skills, _equipment);
            Assert.That(_store.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }
            DI.Init();
            string allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "P2-EquipmentCombatTests")) + Path.DirectorySeparatorChar;
            Assert.That(_directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void WeaponSwapChangesBasicAndActiveTreeAndWeaponPassiveWithoutLosingLearnedSkills()
        {
            Learn("SKILL_VerticalSlash");
            Learn("SKILL_TwoHandGrip");
            var source = _store.Progress.GetCharacter("hero");
            int mainPoints = source.MainSkillPoints;
            int subPoints = source.SubSkillPoints;
            CreateRuntime();
            Ready();
            float swordAttack = Hero.Stats.PhysicalAttackPower;
            Assert.That(_data.CanUseCombatSkill("SKILL_VerticalSlash"), Is.True);
            Equip("weapon_greatsword");
            Assert.That(Hero.BasicAttackId, Is.EqualTo("SKILL_BasicGreatswordAttack"));
            Assert.That(Hero.Gauge, Is.Zero);
            Assert.That(_flow.HeroMenu.Open, Is.False);
            Assert.That(Hero.Stats.PhysicalAttackPower, Is.EqualTo(swordAttack / 1.35f).Within(0.001f));
            Assert.That(Hero.Speed, Is.EqualTo(1f / CombatCalculator.AdjustCooldown(
                _skills.GetSkill(Hero.BasicAttackId).BaseCycle, Hero.Stats, true)).Within(0.0001f));
            Ready();
            Assert.That(_data.CanUseCombatSkill("SKILL_VerticalSlash"), Is.False);
            Assert.That(_data.CanUseCombatSkill(string.Empty), Is.True);
            Equip("weapon_sword");
            Assert.That(Hero.BasicAttackId, Is.EqualTo("SKILL_BasicSwordAttack"));
            Assert.That(Hero.Stats.PhysicalAttackPower, Is.EqualTo(swordAttack).Within(0.001f));
            Ready();
            Assert.That(_data.CanUseCombatSkill("SKILL_VerticalSlash"), Is.True);
            var current = _store.Progress.GetCharacter("hero");
            CollectionAssert.AreEquivalent(source.LearnedSkillIds, current.LearnedSkillIds);
            Assert.That(current.MainSkillPoints, Is.EqualTo(mainPoints));
            Assert.That(current.SubSkillPoints, Is.EqualTo(subPoints));
            Assert.That(_store.Progress.Items["weapon_sword"], Is.EqualTo(1));
            Assert.That(_store.Progress.Items["weapon_greatsword"], Is.EqualTo(1));
        }

        [Test]
        public void SuccessfulEquipPreservesInjuryEnergyAndActiveCooldownsAndFailurePreservesTheTurn()
        {
            Learn("SKILL_VerticalSlash");
            CreateRuntime();
            Ready();
            Request(UIInputType.DungeonSelectSkill, "SKILL_VerticalSlash", "hero");
            Request(UIInputType.DungeonUseSkill, "rat_0", "hero");
            Ready();
            Invoke(Hero, "TakeHealthDamage", 30f);
            Invoke(Hero, "RestoreEnergy", -5f);
            float hp = Hero.Hp;
            float energy = Hero.Energy;
            float cooldown = Hero.GetCooldown("SKILL_VerticalSlash");
            float globalCooldown = Hero.GlobalCooldown;
            Assert.That(cooldown, Is.GreaterThan(0));
            string context = SelectItem("weapon_greatsword");
            string saved = File.ReadAllText(_path);
            Directory.CreateDirectory(_path + ".tmp");
            Request(UIInputType.InventoryEquipItem, "weapon_greatsword", context);
            Assert.That(_store.Progress.GetCharacter("hero").EquippedItemId, Is.EqualTo("weapon_sword"));
            Assert.That(Hero.BasicAttackId, Is.EqualTo("SKILL_BasicSwordAttack"));
            Assert.That(Hero.Gauge, Is.EqualTo(1f));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Assert.That(_flow.HeroMenu.Open, Is.True);
            Directory.Delete(_path + ".tmp");
            Request(UIInputType.InventoryEquipItem, "weapon_greatsword", context);
            Assert.That(_store.Progress.GetCharacter("hero").EquippedItemId, Is.EqualTo("weapon_greatsword"));
            Assert.That(Hero.Hp, Is.EqualTo(hp));
            Assert.That(Hero.Energy, Is.EqualTo(energy));
            Assert.That(Hero.GetCooldown("SKILL_VerticalSlash"), Is.EqualTo(cooldown));
            Assert.That(Hero.GlobalCooldown, Is.EqualTo(globalCooldown));
            Assert.That(Hero.Gauge, Is.Zero);
        }

        [TestCase(25f)]
        [TestCase(150f)]
        public void PotionSaveFailureCanRetryAndDuplicateInputCannotHealOrConsumeTwice(float injury)
        {
            CreateRuntime();
            Ready();
            Invoke(Hero, "TakeHealthDamage", injury);
            float hp = Hero.Hp;
            float energy = Hero.Energy;
            string context = SelectItem("health_potion");
            string saved = File.ReadAllText(_path);
            Directory.CreateDirectory(_path + ".tmp");
            Request(UIInputType.InventoryUseItem, "health_potion", context);
            Assert.That(Hero.Hp, Is.EqualTo(hp));
            Assert.That(Hero.Gauge, Is.EqualTo(1f));
            Assert.That(_store.Progress.Items["health_potion"], Is.EqualTo(3));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Directory.Delete(_path + ".tmp");
            _input.AddInput(UIInputType.InventoryUseItem, "health_potion", context);
            _input.AddInput(UIInputType.InventoryUseItem, "health_potion", context);
            Flush();
            Assert.That(Hero.Hp, Is.EqualTo(Math.Min(Hero.MaxHp, hp + 100f)).Within(0.0001f));
            Assert.That(Hero.Energy, Is.EqualTo(energy));
            Assert.That(Hero.Gauge, Is.Zero);
            Assert.That(_flow.HeroMenu.Open, Is.False);
            Assert.That(_store.Progress.Items["health_potion"], Is.EqualTo(2));
            var restored = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests, _skills, _equipment);
            Assert.That(restored.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(restored.Progress.Items["health_potion"], Is.EqualTo(2));
            Assert.That(_store.TryConsumeItem("health_potion", context, _store.Progress.Revision), Is.EqualTo(ItemActionResult.AlreadyApplied));
        }

        [TestCase("full")]
        [TestCase("dead")]
        [TestCase("not_turn")]
        public void UnusablePotionDoesNotWriteConsumeOrSpendTheTurn(string reason)
        {
            CreateRuntime();
            if (reason != "not_turn")
            {
                Ready();
            }
            if (reason != "full")
            {
                Invoke(Hero, "TakeHealthDamage", reason == "dead" ? Hero.MaxHp : 25f);
            }
            float hp = Hero.Hp;
            float gauge = Hero.Gauge;
            string saved = File.ReadAllText(_path);
            string context = SelectItem("health_potion");
            Request(UIInputType.InventoryUseItem, "health_potion", context);
            Assert.That(Hero.Hp, Is.EqualTo(hp));
            Assert.That(Hero.Gauge, Is.EqualTo(gauge));
            Assert.That(_store.Progress.Items["health_potion"], Is.EqualTo(3));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Assert.That(_flow.HeroMenu.Open, Is.True);
            Assert.That(_flow.HeroMenu.Status, Is.Not.Empty);
        }

        [Test]
        public void ChangingSelectionRejectsOldItemRequestsAndOpenInventoryBlocksBackgroundAttack()
        {
            CreateRuntime();
            Ready();
            Invoke(Hero, "TakeHealthDamage", 25f);
            string stale = SelectItem("health_potion");
            Request(UIInputType.InventorySelectItem, "weapon_greatsword", stale);
            string current = _flow.HeroMenu.Context;
            Assert.That(current, Is.Not.EqualTo(stale));
            string saved = File.ReadAllText(_path);
            float hp = Hero.Hp;
            float enemyHp = _data.Enemies[0].Hp;
            Request(UIInputType.InventoryUseItem, "health_potion", stale);
            Request(UIInputType.InventoryUseItem, "health_potion", current);
            Request(UIInputType.InventoryEquipItem, "weapon_greatsword", stale);
            Request(UIInputType.DungeonAttack, "rat_0", "hero");
            Assert.That(Hero.Hp, Is.EqualTo(hp));
            Assert.That(Hero.Gauge, Is.EqualTo(1f));
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(enemyHp));
            Assert.That(_store.Progress.GetCharacter("hero").EquippedItemId, Is.EqualTo("weapon_sword"));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
        }

        private void CreateRuntime()
        {
            _data = new DungeonDataContainer(_dungeon, 1, _loot, _characters, _store.Progress, new NormalRandom(), _quests, _skills);
            DI.PubSub(_data);
            _tick = new CombatTickFlow(_data);
            _host = new GameObject("EquipmentCombatTests isolated MainFlow");
            _host.SetActive(false);
            _flow = _host.AddComponent<MainFlow>();
            Field("_sceneType", SceneType.Dungeon);
            Field("_characterSheet", _characters);
            Field("_lootSheet", _loot);
            Field("_skillSheet", _skills);
            Field("_equipmentSheet", _equipment);
            Field("_adventureStore", _store);
            Field("_dungeonData", _data);
            var handle = (Action<HeroMenuActionRequested>)Delegate.CreateDelegate(typeof(Action<HeroMenuActionRequested>), _flow,
                typeof(MainFlow).GetMethod("HandleHeroMenuAction", BindingFlags.Instance | BindingFlags.NonPublic));
            var filter = new InputFilter(() => false);
            _queue = new EventQueue(new EventHandler(new DungeonEventHandler(filter, () => { }, isHeroMenuOpen: () => _flow.HeroMenu.Open),
                heroMenu: new HeroMenuEventHandler(filter, handle)));
            _stream = new InputStream();
            _input = new UIInputStream(_stream);
            Request(UIInputType.DungeonMove, "guard", _data.Context);
            Assert.That(_data.InCombat, Is.True);
        }

        private void Learn(string skill)
        {
            Assert.That(_store.TryLearnSkill("hero", skill, "learn_" + skill, _store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
        }

        private void Ready()
        {
            _tick.Update(100f);
            Assert.That(_data.IsAwaitingCommand, Is.True);
        }

        private string SelectItem(string item)
        {
            Request(UIInputType.InventoryOpen, "hero", "hero");
            Assert.That(_flow.HeroMenu.Open, Is.True);
            Request(UIInputType.InventorySelectItem, item, _flow.HeroMenu.Context);
            Assert.That(_flow.HeroMenu.SelectedItemId, Is.EqualTo(item));
            return _flow.HeroMenu.Context;
        }

        private void Equip(string item)
        {
            Request(UIInputType.InventoryEquipItem, item, SelectItem(item));
        }

        private void Request(UIInputType type, string target = null, string context = null)
        {
            _input.AddInput(type, target, context);
            Flush();
        }

        private void Flush()
        {
            foreach (var request in new InputBinding(_stream).ConvertInputStreamToEventData())
            {
                _queue.AddEvent(request);
            }
            _stream.ClearInputs();
            _queue.Update();
        }

        private void Field(string name, object value)
        {
            typeof(MainFlow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_flow, value);
        }

        // Fixture setup only: an isolated unit is injured without requiring a particular enemy RNG draw.
        private static void Invoke(CombatantData unit, string method, float amount)
        {
            typeof(CombatantData).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(unit, new object[] { amount });
        }

        private sealed class NormalRandom : IRandomSource
        {
            private int _draw;
            public int NextInt(int minimum, int maximum) => minimum;
            public float NextFloat(float minimum, float maximum) => _draw++ % 3 == 0 ? 0f : 99f;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Refactor;
using Refactor.Main;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace P2.Tests
{
    // 실제 씬의 가방/학습/전투 버튼을 최상단 레이캐스트로 조작한다. 저장은 테스트 경로에 격리한다.
    public sealed class SkillsSceneTests
    {
        private AdventureSaveScope _save;
        private MainFlow _flow;
        private float _scale;
        private float _maximumDelta;

        [SetUp]
        public void SetUp()
        {
            _save = new AdventureSaveScope();
            _scale = Time.timeScale;
            _maximumDelta = Time.maximumDeltaTime;
            Time.timeScale = 4f;
            Time.maximumDeltaTime = 0.1f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _scale;
            Time.maximumDeltaTime = _maximumDelta;
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && (scene.name == "2_Town" || scene.name == "3_Dungeon"))
            {
                var empty = SceneManager.CreateScene("P2 Skills Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            _save?.Dispose();
        }

        [UnityTest]
        public IEnumerator BagAndClassSkillsPreserveOwnedItemsAndSpendTheCorrectPointPoolOnce()
        {
            File.WriteAllText(_save.SavePath,
                "{\"Format\":\"P2.Adventure\",\"Version\":1,\"MemberIds\":[\"hero\",\"mira\"],\"MemberGold\":[42,7],"
                + "\"ItemIds\":[\"rat_tail\",\"health_potion\"],\"ItemCounts\":[2,3],\"SettlementIds\":[]}");
            yield return Load(SceneType.Town);
            var job = _flow.SkillSheet.GetClassForCharacter("hero");
            yield return Click("InventoryOpenButton");
            Assert.That(_flow.HeroMenu.Mode, Is.EqualTo(HeroMenuMode.Inventory));
            Assert.That(TextNamed("BagGoldText").text, Does.Contain("42"));
            Assert.That(TextNamed("BagClassText").text, Does.Contain(job.Name));
            Assert.That(TextNamed("BagWeaponText").text, Does.Contain(_flow.SkillSheet.GetWeapon(job.WeaponId).Name));
            Assert.That(Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Count(item => item.name.StartsWith("BagItem_")),
                Is.EqualTo(_flow.AdventureProgress.Items.Count(item => item.Value > 0)));
            var behind = ButtonNamed("Location_tavern");
            Assert.That(TopButtonAt(behind), Is.Not.SameAs(behind));
            behind.onClick.Invoke();
            yield return null;
            Assert.That(_flow.TownData.LocationId, Is.Empty);
            AssertTextsFit();
            _flow.UIInput.AddInput(UIInputType.Back);
            yield return null;
            yield return null;
            Assert.That(_flow.HeroMenu.Open, Is.False);

            yield return Click("SkillsOpenButton");
            var skills = _flow.SkillSheet;
            CollectionAssert.AreEquivalent(skills.Skills.Where(skill => skill.TreeKind == SkillTreeKind.Sub || skill.TreeId == job.ActiveTreeId)
                .Select(skill => "Skill_" + skill.Id), Buttons().Where(button => button.name.StartsWith("Skill_")).Select(button => button.name));
            Assert.That(Hero.MainSkillPoints, Is.EqualTo(skills.InitialMainSkillPoints));
            Assert.That(Hero.SubSkillPoints, Is.EqualTo(skills.InitialSubSkillPoints));
            var active = skills.GetSkill("SKILL_VerticalSlash");
            yield return Click("Skill_" + active.Id);
            Assert.That(TextNamed("SkillDescriptionText").text, Is.EqualTo(active.Description));
            Assert.That(TextNamed("SkillDescriptionText").text, Does.Not.Contain("{0}"));
            yield return Click("SkillLearnButton", 2);
            Assert.That(Hero.HasSkill(active.Id), Is.True);
            Assert.That(Hero.MainSkillPoints, Is.EqualTo(skills.InitialMainSkillPoints - active.Cost));
            Assert.That(Hero.SubSkillPoints, Is.EqualTo(skills.InitialSubSkillPoints));
            Assert.That(ButtonNamed("SkillLearnButton").IsInteractable(), Is.False);

            var passive = skills.Skills.Where(skill => skill.TreeKind == SkillTreeKind.Sub && SkillLearningRules.CanLearn(skills, Hero, skill.Id))
                .OrderBy(skill => skill.TreeId, StringComparer.Ordinal).ThenBy(skill => skill.Order).ThenBy(skill => skill.Id, StringComparer.Ordinal).First();
            yield return Click("Skill_" + passive.Id);
            yield return Click("SkillLearnButton", 2);
            Assert.That(Hero.HasSkill(passive.Id), Is.True);
            Assert.That(Hero.MainSkillPoints, Is.EqualTo(skills.InitialMainSkillPoints - active.Cost));
            Assert.That(Hero.SubSkillPoints, Is.EqualTo(skills.InitialSubSkillPoints - passive.Cost));
            yield return Click("Skill_SKILL_SwordCounter");
            Assert.That(ButtonNamed("SkillLearnButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("SkillStatusText").text, Is.EqualTo(skills.Text("status.insufficient_points")));
            yield return Click("Skill_SKILL_ChainSlash");
            Assert.That(ButtonNamed("SkillLearnButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("SkillStatusText").text, Is.EqualTo(skills.Text("status.level_required")));
            AssertTextsFit();
            yield return Click("HeroMenuCloseButton");
            yield return Click("PartyCharacter_hero");
            Assert.That(TextNamed("CharacterClassText").text, Does.Contain(job.Name));
            Assert.That(TextNamed("CharacterWeaponText").text, Does.Contain(skills.GetWeapon(job.WeaponId).Name));
            Assert.That(TextNamed("CharacterLearnedSkillsText").text, Does.Contain(active.Name).And.Contain(passive.Name));
            AssertTextsFit();

            yield return Load(SceneType.Town);
            Assert.That(Hero.HasSkill(active.Id) && Hero.HasSkill(passive.Id), Is.True);
            Assert.That(Hero.MainSkillPoints, Is.EqualTo(skills.InitialMainSkillPoints - active.Cost));
            Assert.That(Hero.SubSkillPoints, Is.EqualTo(skills.InitialSubSkillPoints - passive.Cost));
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(42));
            Assert.That(_flow.AdventureProgress.Items["rat_tail"], Is.EqualTo(2));
            Assert.That(_flow.AdventureProgress.Items["health_potion"], Is.EqualTo(3));
            Assert.That(_flow.AdventureProgress.GetCharacter("mira").LearnedSkillIds, Is.Empty,
                "NPC는 초기화/불러오기에서 임의 습득하지 않고 실제 레벨업 때만 자동 습득합니다.");
            var saved = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_save.SavePath));
            Assert.That(saved.Version, Is.EqualTo(AdventureSave.CurrentVersion));
        }

        [UnityTest]
        public IEnumerator FailedSkillSaveCanRetryWithoutSpendingTwiceOrAcceptingAStaleSelection()
        {
            yield return Load(SceneType.Town);
            yield return Click("SkillsOpenButton");
            yield return Click("Skill_SKILL_VerticalSlash");
            string context = _flow.HeroMenu.Context;
            string blocker = _save.SavePath + ".tmp";
            Directory.CreateDirectory(blocker);
            int points = Hero.MainSkillPoints;
            yield return Click("SkillLearnButton");
            Assert.That(Hero.HasSkill("SKILL_VerticalSlash"), Is.False);
            Assert.That(Hero.MainSkillPoints, Is.EqualTo(points));
            Assert.That(TextNamed("SkillStatusText").text, Is.EqualTo(_flow.SkillSheet.Text("status.save_failed")));
            Directory.Delete(blocker);
            yield return Click("SkillLearnButton", 2);
            Assert.That(Hero.HasSkill("SKILL_VerticalSlash"), Is.True);
            Assert.That(Hero.MainSkillPoints, Is.EqualTo(points - _flow.SkillSheet.GetSkill("SKILL_VerticalSlash").Cost));
            yield return Click("Skill_SKILL_SwordCounter");
            _flow.UIInput.AddInput(UIInputType.SkillLearn, "SKILL_VerticalSlash", context);
            _flow.UIInput.AddInput(UIInputType.HeroMenuClose, null, context);
            yield return null;
            Assert.That(_flow.HeroMenu.Open, Is.True);
            Assert.That(_flow.HeroMenu.SelectedSkillId, Is.EqualTo("SKILL_SwordCounter"));
            Assert.That(Hero.LearnedSkillIds.Count(id => id == "SKILL_VerticalSlash"), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CorruptSaveStaysUntouchedAndLearningRemainsDisabled()
        {
            const string original = "{corrupt adventure save";
            File.WriteAllText(_save.SavePath, original);
            yield return Load(SceneType.Town);
            yield return Click("SkillsOpenButton");
            yield return Click("Skill_SKILL_VerticalSlash");
            Assert.That(ButtonNamed("SkillLearnButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("SkillStatusText").text, Is.EqualTo(_flow.SkillSheet.Text("status.load_failed")));
            Assert.That(File.ReadAllText(_save.SavePath), Is.EqualTo(original));
            yield return Click("HeroMenuCloseButton");
            Assert.That(_flow.HeroMenu.Open, Is.False);
        }

        [UnityTest]
        public IEnumerator DungeonMenusPauseCombatAndLearnedSkillsUseEnemyOrSelfTargets()
        {
            SeedLevelFourHero();
            yield return Load(SceneType.Town);
            yield return Click("SkillsOpenButton");
            yield return Click("Skill_SKILL_VerticalSlash");
            yield return Click("SkillLearnButton");
            yield return Click("Skill_SKILL_SwordCounter");
            yield return Click("SkillLearnButton");
            yield return Load(SceneType.Dungeon);
            yield return Click("Node_guard");
            yield return WaitForReady();
            AssertResourceRowsDoNotOverlap(ButtonNamed("Ally_hero"));
            yield return Click("InventoryOpenButton");
            float[] before = _flow.DungeonData.Allies.Concat(_flow.DungeonData.Enemies).Select(unit => unit.Gauge).ToArray();
            float[] health = _flow.DungeonData.Allies.Concat(_flow.DungeonData.Enemies).Select(unit => unit.Hp).ToArray();
            yield return new WaitForSecondsRealtime(0.1f);
            CollectionAssert.AreEqual(before, _flow.DungeonData.Allies.Concat(_flow.DungeonData.Enemies).Select(unit => unit.Gauge));
            CollectionAssert.AreEqual(health, _flow.DungeonData.Allies.Concat(_flow.DungeonData.Enemies).Select(unit => unit.Hp));
            yield return Click("HeroMenuCloseButton");
            var hero = _flow.DungeonData.Allies.Single(unit => unit.IsPlayerControlled);
            var target = _flow.DungeonData.Enemies.First(unit => unit.IsAlive);
            float energy = hero.Energy;
            float hp = target.Hp;
            yield return Click("CombatSkill_SKILL_VerticalSlash");
            Assert.That(_flow.DungeonData.SelectedCombatSkillId, Is.EqualTo("SKILL_VerticalSlash"));
            Assert.That(ButtonNamed("Enemy_" + target.Id).IsInteractable(), Is.True);
            Assert.That(ButtonNamed("Ally_hero").IsInteractable(), Is.False);
            yield return Click("Enemy_" + target.Id);
            Assert.That(target.Hp, Is.LessThan(hp));
            Assert.That(hero.Energy, Is.LessThan(energy));
            Assert.That(hero.GetCooldown("SKILL_VerticalSlash"), Is.GreaterThan(0));
            Assert.That(ButtonNamed("CombatSkill_SKILL_VerticalSlash").IsInteractable(), Is.False);
            AssertTextsFit();

            // 두 번째 새 탐험에서 자기 대상 스킬의 별도 확정 버튼을 검증한다.
            yield return Load(SceneType.Dungeon);
            yield return Click("Node_guard");
            yield return WaitForReady();
            hero = _flow.DungeonData.Allies.Single(unit => unit.IsPlayerControlled);
            energy = hero.Energy;
            yield return Click("CombatSkill_SKILL_SwordCounter");
            Assert.That(Buttons().Where(button => button.name.StartsWith("Enemy_")).All(button => !button.IsInteractable()), Is.True);
            Assert.That(ButtonNamed("BattleSkillUseButton").GetComponentInChildren<Text>().text, Is.EqualTo(_flow.SkillSheet.Text("battle.use")));
            yield return Click("BattleSkillUseButton");
            Assert.That(hero.Energy, Is.LessThan(energy));
            Assert.That(hero.GetCooldown("SKILL_SwordCounter"), Is.GreaterThan(0));
            Assert.That(hero.GlobalCooldown, Is.GreaterThan(0));
            yield return Click("Ally_hero");
            Assert.That(_flow.CharacterInteraction.Open, Is.True);
            Assert.That(TextNamed("CharacterLearnedSkillsText").text, Does.Contain(_flow.SkillSheet.GetSkill("SKILL_SwordCounter").Name));
            AssertTextsFit();
        }

        private CharacterProgress Hero => _flow.AdventureProgress.GetCharacter("hero");

        private void SeedLevelFourHero()
        {
            string[] ids = { "hero", "mira", "orin", "bran", "sera", "neri", "cass" };
            var dto = new AdventureSaveData
            {
                Format = "P2.Adventure", Version = 4,
                MemberIds = ids, MemberGold = new long[ids.Length], ItemIds = Array.Empty<string>(), ItemCounts = Array.Empty<int>(),
                SettlementIds = Array.Empty<string>(), MerchantIds = new[] { "market", "tavern" },
                StockMerchantIds = new[] { "market", "tavern" }, StockItemIds = new[] { "health_potion", "health_potion" }, StockCounts = new[] { 10, 10 },
                TradeIds = Array.Empty<string>(), CharacterIds = ids, CharacterLevels = ids.Select(id => id == "hero" ? 4 : 1).ToArray(),
                CharacterExperience = new float[ids.Length], CharacterAffinity = new int[ids.Length],
                CharacterStrengthGrowth = ids.Select(id => id == "hero" ? 150f : 0f).ToArray(),
                CharacterConstitutionGrowth = ids.Select(id => id == "hero" ? 90f : 0f).ToArray(),
                CharacterWillPowerGrowth = new float[ids.Length], CharacterIntelligenceGrowth = new float[ids.Length],
                CharacterAgilityGrowth = new float[ids.Length], CharacterPerceptionGrowth = new float[ids.Length],
                PartyIds = new[] { "hero" }, CharacterActionIds = Array.Empty<string>()
            };
            File.WriteAllText(_save.SavePath, JsonUtility.ToJson(dto));
        }

        private IEnumerator Load(SceneType expected)
        {
            yield return SceneManager.LoadSceneAsync(SceneLoader.SceneName(expected), LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var flow = Object.FindFirstObjectByType<MainFlow>();
                if (flow != null && flow.IsReady && flow.CurrentScene == expected)
                {
                    _flow = flow;
                    Assert.That(_flow.AdventureSavePath, Is.EqualTo(_save.SavePath));
                    if (expected == SceneType.Dungeon)
                    {
                        typeof(DungeonDataContainer).GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic)
                            .SetValue(_flow.DungeonData, new PredictableRandom());
                    }
                    yield return null;
                    yield return null;
                    Focus();
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("씬 초기화 시간 초과: " + expected);
        }

        private IEnumerator WaitForReady()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!_flow.DungeonData.IsAwaitingCommand && Time.realtimeSinceStartup < deadline)
            {
                Focus();
                yield return null;
            }
            Assert.That(_flow.DungeonData.IsAwaitingCommand, Is.True);
            yield return null;
            yield return null;
        }

        private IEnumerator Click(string name, int count = 1)
        {
            var button = ButtonNamed(name);
            float deadline = Time.realtimeSinceStartup + 4f;
            GameObject hit = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                Focus();
                hit = RaycastAt(button);
                if (button.IsInteractable() && hit != null && hit.GetComponentInParent<Button>() == button)
                {
                    break;
                }
                var scroll = button.GetComponentInParent<ScrollRect>();
                if (scroll != null)
                {
                    Vector2 target = ScreenCenter(button);
                    var corners = new Vector3[4];
                    scroll.viewport.GetWorldCorners(corners);
                    float direction = target.y < corners[0].y ? -1 : target.y > corners[1].y ? 1 : 0;
                    if (direction != 0)
                    {
                        ExecuteEvents.Execute(scroll.gameObject, new PointerEventData(EventSystem.current)
                        {
                            position = target,
                            scrollDelta = new Vector2(0, direction * 3)
                        }, ExecuteEvents.scrollHandler);
                    }
                }
                yield return null;
            }
            Assert.That(button.IsInteractable(), Is.True, name);
            Assert.That(hit != null && hit.GetComponentInParent<Button>() == button, Is.True, name + " 레이캐스트 차단");
            var pointer = new PointerEventData(EventSystem.current) { position = ScreenCenter(button), button = PointerEventData.InputButton.Left };
            for (int i = 0; i < count; i++)
            {
                ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerClickHandler);
            }
            yield return null;
            yield return null;
            Focus();
        }

        private void Focus()
        {
            if (_flow != null)
            {
                _flow.SendMessage("OnApplicationFocus", true);
            }
        }

        private static Button[] Buttons() => Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
        private static Button ButtonNamed(string name) => Buttons().Single(button => button.name == name);
        private static Text TextNamed(string name) => Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Single(text => text.name == name);
        private static Button TopButtonAt(Button button) => RaycastAt(button)?.GetComponentInParent<Button>();
        private static GameObject RaycastAt(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ScreenCenter(button) }, results);
            return results.Count == 0 ? null : results[0].gameObject;
        }
        private static Vector2 ScreenCenter(Button button)
        {
            var rect = (RectTransform)button.transform;
            var canvas = button.GetComponentInParent<Canvas>();
            return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                rect.TransformPoint(rect.rect.center));
        }
        private static void AssertTextsFit()
        {
            Canvas.ForceUpdateCanvases();
            var failures = new List<string>();
            foreach (var text in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (!string.IsNullOrWhiteSpace(text.text) && text.verticalOverflow != VerticalWrapMode.Overflow
                    && text.preferredHeight > text.rectTransform.rect.height + 2f)
                {
                    failures.Add(text.name + $" {text.preferredHeight:F1}>{text.rectTransform.rect.height:F1}: {text.text}");
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private static void AssertResourceRowsDoNotOverlap(Button card)
        {
            Canvas.ForceUpdateCanvases();
            var hp = new Vector3[4];
            var energy = new Vector3[4];
            var speed = new Vector3[4];
            ((RectTransform)card.transform.Find("HpBar")).GetWorldCorners(hp);
            ((RectTransform)card.transform.Find("EnergyText")).GetWorldCorners(energy);
            ((RectTransform)card.transform.Find("SpeedText")).GetWorldCorners(speed);
            Assert.That(energy[1].y, Is.LessThan(hp[0].y), "에너지 문구는 HP 막대 아래에 표시합니다.");
            Assert.That(speed[1].y, Is.LessThan(energy[0].y), "속도 문구는 에너지 아래에 표시합니다.");
        }

        private sealed class PredictableRandom : IRandomSource
        {
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat(float minInclusive, float maxInclusive) => (minInclusive + maxInclusive) / 2f;
        }
    }
}

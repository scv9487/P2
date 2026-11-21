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
    // 실제 씬의 가방 장착/포션/상점 버튼을 최상단 레이캐스트로 조작한다. 저장은 테스트 경로에 격리한다.
    public sealed class EquipmentSceneTests
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
                var empty = SceneManager.CreateScene("P2 Equipment Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            _save?.Dispose();
        }

        [UnityTest]
        public IEnumerator WeaponTradingReservesEquippedCopyAndSwitchingRefreshesClassTreesWithoutLosingSkills()
        {
            Seed();
            yield return Load(SceneType.Town);
            yield return Learn("SKILL_VerticalSlash");
            int points = Hero.MainSkillPoints;
            yield return Click("Location_market");
            yield return Click("Command_trade");
            Assert.That(_flow.Shop.MerchantItems.Count, Is.EqualTo(10));
            yield return Click("ShopBuy_weapon_spear");
            Assert.That(TextNamed("ShopSelectionText").text, Does.Contain(_flow.LootSheet.GetItem("weapon_spear").Name));
            yield return Click("ShopConfirmButton", 2);
            Assert.That(ItemCount("weapon_spear"), Is.EqualTo(1));
            Assert.That(_flow.AdventureProgress.GetMerchantStock("market")["weapon_spear"], Is.EqualTo(2));
            yield return Click("ShopSell_weapon_sword");
            Assert.That(_flow.Shop.AvailableQuantity, Is.EqualTo(1));
            Assert.That(ButtonNamed("ShopIncreaseButton").IsInteractable(), Is.False);
            yield return Click("ShopConfirmButton");
            Assert.That(ItemCount("weapon_sword"), Is.EqualTo(1));
            yield return Click("ShopSell_weapon_sword");
            Assert.That(_flow.Shop.AvailableQuantity, Is.Zero);
            Assert.That(ButtonNamed("ShopConfirmButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("ShopStatusText").text, Is.EqualTo(_flow.EquipmentSheet.Text("shop.reserved_notice")));
            AssertTextsFit();
            yield return Click("ShopCloseButton");
            yield return Click("LeaveLocationButton");
            yield return Equip("weapon_spear");
            Assert.That(Hero.EquippedItemId, Is.EqualTo("weapon_spear"));
            Assert.That(Hero.MainSkillPoints, Is.EqualTo(points));
            Assert.That(Hero.HasSkill("SKILL_VerticalSlash"), Is.True);
            yield return Click("SkillsOpenButton");
            var job = _flow.SkillSheet.GetClassForCharacter(Hero);
            Assert.That(job.Id, Is.EqualTo("Spear"));
            Assert.That(TextNamed("ActiveSkillsHeadingText").text, Does.Contain(job.Name));
            CollectionAssert.AreEquivalent(_flow.SkillSheet.Skills
                .Where(skill => skill.TreeKind == SkillTreeKind.Sub || skill.TreeId == job.ActiveTreeId)
                .Select(skill => "Skill_" + skill.Id), Buttons().Where(button => button.name.StartsWith("Skill_")).Select(button => button.name));
            yield return Click("Skill_SKILL_TwinThrust");
            yield return Click("SkillLearnButton");
            Assert.That(Hero.HasSkill("SKILL_TwinThrust"), Is.True);
            points = Hero.MainSkillPoints;
            yield return Click("HeroMenuCloseButton");
            yield return Equip("weapon_bow");
            yield return Click("SkillsOpenButton");
            Assert.That(TextNamed("ActiveSkillsEmptyText").text, Is.EqualTo(_flow.SkillSheet.Text("skill.active_empty")));
            Assert.That(Buttons().Any(button => button.name == "Skill_SKILL_VerticalSlash" || button.name == "Skill_SKILL_TwinThrust"), Is.False);
            AssertTextsFit();
            yield return Click("HeroMenuCloseButton");
            yield return Click("PartyCharacter_hero");
            Assert.That(TextNamed("CharacterClassText").text, Does.Contain(_flow.SkillSheet.GetClassForCharacter(Hero).Name));
            Assert.That(TextNamed("CharacterLearnedSkillsText").text,
                Does.Contain(_flow.SkillSheet.GetSkill("SKILL_VerticalSlash").Name).And.Contain(_flow.SkillSheet.GetSkill("SKILL_TwinThrust").Name));
            AssertTextsFit();
            yield return Load(SceneType.Town);
            Assert.That(Hero.EquippedItemId, Is.EqualTo("weapon_bow"));
            Assert.That(Hero.MainSkillPoints, Is.EqualTo(points));
            Assert.That(Hero.HasSkill("SKILL_VerticalSlash") && Hero.HasSkill("SKILL_TwinThrust"), Is.True);
            Assert.That(ItemCount("weapon_sword"), Is.EqualTo(1));
            Assert.That(ItemCount("weapon_spear"), Is.EqualTo(1));
            Assert.That(_flow.AdventureProgress.GetMerchantStock("market")["weapon_spear"], Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator EquipmentSaveFailureRetriesOnceAndFullHealthPotionDoesNotConsume()
        {
            Seed();
            yield return Load(SceneType.Town);
            yield return Click("InventoryOpenButton");
            yield return Click("BagItem_weapon_bow");
            string oldContext = _flow.HeroMenu.Context;
            string original = File.ReadAllText(_save.SavePath);
            string blocker = _save.SavePath + ".tmp";
            Directory.CreateDirectory(blocker);
            yield return Click("BagEquipButton");
            Assert.That(Hero.EquippedItemId, Is.EqualTo("weapon_sword"));
            Assert.That(File.ReadAllText(_save.SavePath), Is.EqualTo(original));
            Assert.That(TextNamed("BagStatusText").text, Is.EqualTo(_flow.EquipmentSheet.Text("status.save_failed")));
            Directory.Delete(blocker);
            yield return Click("BagEquipButton", 2);
            Assert.That(Hero.EquippedItemId, Is.EqualTo("weapon_bow"));
            Assert.That(ItemCount("weapon_sword"), Is.EqualTo(2));
            Assert.That(ItemCount("weapon_bow"), Is.EqualTo(1));
            Assert.That(JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_save.SavePath)).ItemActionIds.Length, Is.EqualTo(1));
            Assert.That(TextNamed("BagClassText").text, Does.Contain(_flow.SkillSheet.GetClassForCharacter(Hero).Name));
            Assert.That(ButtonNamed("BagEquipButton").IsInteractable(), Is.False);
            yield return Click("BagItem_health_potion");
            Assert.That(ButtonNamed("BagUseButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("BagStatusText").text, Is.EqualTo(_flow.EquipmentSheet.Text("status.full_health")));
            _flow.UIInput.AddInput(UIInputType.InventoryEquipItem, "weapon_sword", oldContext);
            _flow.UIInput.AddInput(UIInputType.InventoryUseItem, "health_potion", _flow.HeroMenu.Context);
            yield return null;
            yield return null;
            Assert.That(ItemCount("health_potion"), Is.EqualTo(3));
            Assert.That(Hero.EquippedItemId, Is.EqualTo("weapon_bow"));
            AssertTextsFit();
        }

        [UnityTest]
        public IEnumerator CombatItemsRequirePlayerTurnSaveBeforeHealingAndSpendOneAction()
        {
            Seed();
            yield return Load(SceneType.Town);
            yield return Learn("SKILL_VerticalSlash");
            yield return Load(SceneType.Dungeon);
            yield return Click("Node_guard");
            yield return Click("InventoryOpenButton");
            yield return Click("BagItem_weapon_bow");
            Assert.That(_flow.HeroMenu.InCombat, Is.True);
            Assert.That(_flow.HeroMenu.CanActWithItems, Is.False);
            Assert.That(ButtonNamed("BagEquipButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("BagStatusText").text, Is.EqualTo(_flow.EquipmentSheet.Text("status.turn_required")));
            yield return Click("HeroMenuCloseButton");
            yield return WaitForReady();
            Assert.That(ButtonNamed("CombatSkill_SKILL_VerticalSlash"), Is.Not.Null);
            yield return Click("InventoryOpenButton");
            yield return Click("BagItem_health_potion");
            Assert.That(ButtonNamed("BagUseButton").IsInteractable(), Is.False);
            Assert.That(ItemCount("health_potion"), Is.EqualTo(3));
            yield return Click("BagItem_weapon_bow");
            var hero = _flow.DungeonData.Allies.Single(unit => unit.IsPlayerControlled);
            float hp = hero.Hp;
            yield return Click("BagEquipButton", 2);
            Assert.That(_flow.HeroMenu.Open, Is.False);
            Assert.That(Hero.EquippedItemId, Is.EqualTo("weapon_bow"));
            Assert.That(hero.Hp, Is.EqualTo(hp));
            Assert.That(hero.Gauge, Is.LessThan(1));
            Assert.That(Buttons().Any(button => button.name == "CombatSkill_SKILL_VerticalSlash"), Is.False);
            yield return WaitForReady();
            Assert.That(hero.Hp, Is.LessThan(hero.MaxHp), "첫 장비 행동 뒤 적의 실제 자동 공격으로 부상을 입습니다.");
            yield return Click("InventoryOpenButton");
            yield return Click("BagItem_health_potion");
            hp = hero.Hp;
            float gauge = hero.Gauge;
            string before = File.ReadAllText(_save.SavePath);
            string blocker = _save.SavePath + ".tmp";
            Directory.CreateDirectory(blocker);
            yield return Click("BagUseButton");
            Assert.That(_flow.HeroMenu.Open, Is.True);
            Assert.That(ItemCount("health_potion"), Is.EqualTo(3));
            Assert.That(hero.Hp, Is.EqualTo(hp));
            Assert.That(hero.Gauge, Is.EqualTo(gauge));
            Assert.That(File.ReadAllText(_save.SavePath), Is.EqualTo(before));
            Assert.That(TextNamed("BagStatusText").text, Is.EqualTo(_flow.EquipmentSheet.Text("status.save_failed")));
            AssertTextsFit();
            Directory.Delete(blocker);
            yield return Click("BagUseButton", 2);
            Assert.That(_flow.HeroMenu.Open, Is.False);
            Assert.That(ItemCount("health_potion"), Is.EqualTo(2));
            Assert.That(hero.Hp, Is.EqualTo(Mathf.Min(hero.MaxHp, hp + _flow.EquipmentSheet.GetConsumable("health_potion").Amount)).Within(0.001f));
            Assert.That(hero.Gauge, Is.LessThan(1));
            Assert.That(Hero.HasSkill("SKILL_VerticalSlash"), Is.True);
            yield return Load(SceneType.Town);
            Assert.That(ItemCount("health_potion"), Is.EqualTo(2));
            Assert.That(Hero.EquippedItemId, Is.EqualTo("weapon_bow"));
            yield return Click("InventoryOpenButton");
            yield return Click("BagItem_health_potion");
            Assert.That(_flow.HeroMenu.PlayerHealth, Is.EqualTo(_flow.HeroMenu.PlayerMaxHealth));
            Assert.That(ButtonNamed("BagUseButton").IsInteractable(), Is.False);
        }

        private CharacterProgress Hero => _flow.AdventureProgress.GetCharacter("hero");
        private int ItemCount(string id) => _flow.AdventureProgress.Items.TryGetValue(id, out int count) ? count : 0;

        private IEnumerator Learn(string id)
        {
            yield return Click("SkillsOpenButton");
            yield return Click("Skill_" + id);
            yield return Click("SkillLearnButton");
            yield return Click("HeroMenuCloseButton");
        }

        private IEnumerator Equip(string id)
        {
            yield return Click("InventoryOpenButton");
            yield return Click("BagItem_" + id);
            yield return Click("BagEquipButton");
            Assert.That(Hero.EquippedItemId, Is.EqualTo(id));
            AssertTextsFit();
            yield return Click("HeroMenuCloseButton");
        }

        private void Seed()
        {
            string[] ids = { "hero", "mira", "orin", "bran", "sera", "neri", "cass" };
            var dto = new AdventureSaveData
            {
                Format = "P2.Adventure", Version = 4,
                MemberIds = ids, MemberGold = ids.Select(id => id == "hero" ? 1000L : 0L).ToArray(),
                ItemIds = new[] { "weapon_sword", "weapon_bow", "health_potion" }, ItemCounts = new[] { 2, 1, 3 },
                SettlementIds = Array.Empty<string>(), CharacterIds = ids,
                CharacterLevels = ids.Select(id => id == "hero" ? 4 : 1).ToArray(),
                CharacterExperience = new float[ids.Length], CharacterAffinity = new int[ids.Length],
                CharacterStrengthGrowth = ids.Select(id => id == "hero" ? 150f : 0f).ToArray(),
                CharacterConstitutionGrowth = ids.Select(id => id == "hero" ? 90f : 0f).ToArray(),
                CharacterWillPowerGrowth = new float[ids.Length], CharacterIntelligenceGrowth = new float[ids.Length],
                CharacterAgilityGrowth = new float[ids.Length], CharacterPerceptionGrowth = new float[ids.Length],
                PartyIds = new[] { "hero" }
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

        private sealed class PredictableRandom : IRandomSource
        {
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat(float minInclusive, float maxInclusive) => (minInclusive + maxInclusive) / 2f;
        }
    }
}

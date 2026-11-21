using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Refactor.Util;
using UnityEngine;

namespace P2.Tests
{
    public sealed class EquipmentSheetTests
    {
        private TownSheet _town;
        private DungeonSheet _dungeon;
        private LootSheet _loot;
        private SkillSheet _skills;
        private CharacterSheet _characters;

        [SetUp]
        public void SetUp()
        {
            _town = new TownSheet(Read("TownLocationTable.csv"), Read("TownCommandTable.csv"), Read("TownCharacterTable.csv"), Read("TownTextTable.csv"));
            _dungeon = new DungeonSheet(Read("DungeonDefinitionTable.csv"), Read("DungeonNodeTable.csv"), Read("DungeonPartyTable.csv"),
                Read("DungeonSettingTable.csv"), Read("DungeonTextTable.csv"), _town);
            _loot = LoadItems(Read("LootItemTable.csv"));
            _skills = new SkillSheet(Read("SkillDefinitionTable.csv"), Read("SkillClassTable.csv"), Read("CharacterClassTable.csv"),
                Read("SkillWeaponTable.csv"), Read("SkillSettingTable.csv"), Read("SkillTextTable.csv"));
            _characters = new CharacterSheet(Read("CharacterDefinitionTable.csv"), Read("CharacterSettingTable.csv"), Read("CharacterTextTable.csv"), _town,
                new CharacterGrowthSheet(Read("CharacterGrowthTable.csv"), Read("CharacterLevelTable.csv"), Read("CharacterGrowthSettingTable.csv")),
                new CombatStatSheet(Read("CombatStatTable.csv"), Read("CombatEnemyTable.csv"), Read("CombatAttackTable.csv")));
        }

        [Test]
        public void AuthoredCatalogCoversNineWeaponTypesAndBothShopsWithoutChangingPotionPrice()
        {
            var equipment = Load();
            var shops = new ShopSheet(Read("ShopMerchantTable.csv"), Read("ShopStockTable.csv"), Read("ShopTextTable.csv"), _loot, _town);
            Assert.That(equipment.Weapons.Count, Is.EqualTo(9));
            CollectionAssert.AreEquivalent(_skills.Weapons.Select(weapon => weapon.Id), equipment.Weapons.Select(weapon => weapon.WeaponId));
            foreach (var merchant in shops.Merchants)
            {
                foreach (var weapon in equipment.Weapons)
                {
                    Assert.That(merchant.InitialStock[weapon.ItemId], Is.EqualTo(3), merchant.Id + "/" + weapon.ItemId);
                    Assert.That(weapon.Name, Is.EqualTo(_loot.GetItem(weapon.ItemId).Name));
                }
                Assert.That(merchant.InitialStock["health_potion"], Is.EqualTo(10));
            }
            var potion = equipment.GetConsumable("health_potion");
            Assert.That(potion.Effect, Is.EqualTo(ConsumableEffect.RestoreHealth));
            Assert.That(potion.Amount, Is.EqualTo(100));
            Assert.That(potion.Price, Is.EqualTo(20));
            Assert.That(equipment.GetWeapon("weapon_sword").Price, Is.EqualTo(50));
            Assert.That(equipment.GetStartingWeaponId(equipment.PlayerId), Is.EqualTo("weapon_sword"));
            foreach (var character in _characters.Characters)
            {
                Assert.That(equipment.GetStartingWeapon(character.Id).WeaponId,
                    Is.EqualTo(_skills.GetClassForCharacter(character.Id).WeaponId));
            }
        }

        [Test]
        public void AnotherTradeItemCanUseTheSameWeaponTypeAndBecomeTheStarter()
        {
            var items = LoadItems(Read("LootItemTable.csv") + "practice_sword,연습용 검,55\n");
            var equipment = Load(Read("EquipmentDefinitionTable.csv") + "practice_sword,Equipment_Sword\n",
                starters: Read("CharacterEquipmentTable.csv").Replace("hero,weapon_sword", "hero,practice_sword"), items: items);
            Assert.That(equipment.GetWeapon("practice_sword").WeaponId, Is.EqualTo(equipment.GetWeapon("weapon_sword").WeaponId));
            Assert.That(equipment.GetStartingWeapon("hero").ItemId, Is.EqualTo("practice_sword"));
            Assert.That(equipment.GetStartingWeapon("hero").Name, Is.EqualTo("연습용 검"));
            Assert.That(equipment.GetStartingWeapon("hero").Price, Is.EqualTo(55));
        }

        [Test]
        public void MissingItemOrWeaponReferencesAndDuplicateMappingsFailAtLoad()
        {
            string csv = Read("EquipmentDefinitionTable.csv");
            Assert.Throws<KeyNotFoundException>(() => Load(csv.Replace("weapon_sword,Equipment_Sword", "missing_item,Equipment_Sword")));
            Assert.Throws<KeyNotFoundException>(() => Load(csv.Replace("weapon_sword,Equipment_Sword", "weapon_sword,Equipment_Missing")));
            Assert.Throws<FormatException>(() => Load(csv + "weapon_sword,Equipment_Sword\n"));
        }

        [Test]
        public void EveryCharacterNeedsExactlyOneStarterMatchingItsInitialClass()
        {
            string csv = Read("CharacterEquipmentTable.csv");
            Assert.Throws<FormatException>(() => Load(starters: csv.Replace("mira,weapon_blunt\n", string.Empty)));
            Assert.Throws<FormatException>(() => Load(starters: csv + "hero,weapon_sword\n"));
            Assert.Throws<FormatException>(() => Load(starters: csv.Replace("hero,weapon_sword", "hero,weapon_spear")));
            Assert.Throws<KeyNotFoundException>(() => Load(starters: csv.Replace("hero,weapon_sword", "missing_person,weapon_sword")));
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("1000001")]
        public void InvalidHealingAmountsAreRejectedBeforeAnyItemCanBeUsed(string value)
        {
            Assert.Throws<FormatException>(() => Load(consumables: "ItemId,Effect,Amount\nhealth_potion,RestoreHealth," + value + "\n"));
        }

        [Test]
        public void ConsumableTypeAndEquipmentIdentityCannotBeAmbiguous()
        {
            Assert.Throws<FormatException>(() => Load(consumables: "ItemId,Effect,Amount\nhealth_potion,Damage,100\n"));
            Assert.Throws<FormatException>(() => Load(consumables: "ItemId,Effect,Amount\nweapon_sword,RestoreHealth,100\n"));
            Assert.Throws<FormatException>(() => Load(consumables: Read("ConsumableDefinitionTable.csv") + "health_potion,RestoreHealth,50\n"));
            var sheet = Load();
            Assert.That(sheet.TryGetWeapon("health_potion", out _), Is.False);
            Assert.That(sheet.TryGetConsumable("rat_tail", out _), Is.False);
            Assert.That(sheet.TryGetConsumable(null, out _), Is.False);
        }

        private EquipmentSheet Load(string weapons = null, string consumables = null, string starters = null, LootSheet items = null)
        {
            return new EquipmentSheet(weapons ?? Read("EquipmentDefinitionTable.csv"), consumables ?? Read("ConsumableDefinitionTable.csv"),
                starters ?? Read("CharacterEquipmentTable.csv"), Read("EquipmentTextTable.csv"), items ?? _loot, _skills, _characters);
        }

        private LootSheet LoadItems(string items)
        {
            return new LootSheet(items, Read("LootDropTable.csv"), Read("LootShareTable.csv"), Read("LootSettingTable.csv"), Read("LootTextTable.csv"), _dungeon);
        }

        private static string Read(string file)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "1_InternalTools/Util/DataSheetLoader/Table", file)).Replace("\r\n", "\n");
        }
    }
}

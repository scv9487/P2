using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Refactor.Main;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using F = Refactor.UI.AdventureUIFactory;

namespace Refactor.UI
{
    public sealed partial class HeroMenuView
    {
        private SkillSheet _sheet;
        private LootSheet _loot;
        private CharacterSheet _characters;
        private TownContent _portraits;
        private UIInputStream _input;
        private string _context;
        private string _selectedId;
        private string _renderedSkillId;
        private string _renderedClassId;
        private bool _wasOpen;
        private readonly List<GameObject> _generated = new();
        private readonly List<SkillRow> _skillRows = new();
        private readonly List<GameObject> _skillGenerated = new();
        private readonly List<ItemRow> _itemRows = new();

        private sealed class SkillRow
        {
            public string Id;
            public Button Button;
            public Text State;
        }

        private sealed class ItemRow
        {
            public string Id;
            public RectTransform Root;
            public Button Button;
            public Text Quantity;
            public Text Equipped;
        }

        public void Initialize(SkillSheet sheet, LootSheet loot, CharacterSheet characters,
            TownContent portraits, UIInputStream input)
        {
            UnbindInputs();
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _loot = loot ?? throw new ArgumentNullException(nameof(loot));
            _characters = characters ?? throw new ArgumentNullException(nameof(characters));
            _portraits = portraits != null ? portraits : throw new ArgumentNullException(nameof(portraits));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _bagTitle.text = sheet.Text("bag.title");
            _bagItemsHeading.text = sheet.Text("bag.items");
            _bagEmpty.text = sheet.Text("bag.empty");
            _bagNote.text = sheet.Text("bag.note");
            _skillsTitle.text = sheet.Text("skill.title");
            _passiveHeading.text = sheet.Text("skill.passives");
            _bagClose.GetComponentInChildren<Text>().text = sheet.Text("menu.close");
            _skillsClose.GetComponentInChildren<Text>().text = sheet.Text("menu.close");
            Bind(_bagClose, () => _input.AddInput(UIInputType.HeroMenuClose, null, _context));
            Bind(_skillsClose, () => _input.AddInput(UIInputType.HeroMenuClose, _selectedId, _context));
            Bind(_learn, () => _input.AddInput(UIInputType.SkillLearn, _selectedId, _context));
            Bind(_bagEquip, () => _input.AddInput(UIInputType.InventoryEquipItem, _selectedItemId, _context));
            Bind(_bagUse, () => _input.AddInput(UIInputType.InventoryUseItem, _selectedItemId, _context));
            BuildItems();
        }

        private void BuildItems()
        {
            foreach (var item in _loot.Items)
            {
                var button = F.Button("BagItem_" + item.Id, _bagItems, _font, 8, 0, 550, 64);
                var row = (RectTransform)button.transform;
                var name = button.GetComponentInChildren<Text>();
                name.name = "BagItemNameText";
                name.rectTransform.anchoredPosition = new Vector2(16, 0);
                name.rectTransform.sizeDelta = new Vector2(322, 35);
                name.fontSize = 18;
                name.alignment = TextAnchor.MiddleLeft;
                name.text = item.Name;
                var quantity = F.Text("BagQuantityText", row, _font, 354, 0, 180, 35, 17, F.PaleGold);
                quantity.alignment = TextAnchor.MiddleRight;
                var price = F.Text("BagPriceText", row, _font, 16, 35, 280, 26, 14, F.Muted);
                price.text = Format("bag.price_format", item.Price);
                var equipped = F.Text("BagEquippedText", row, _font, 310, 35, 224, 26, 14, F.PaleGold);
                equipped.alignment = TextAnchor.MiddleRight;
                string id = item.Id;
                Bind(button, () => _input.AddInput(UIInputType.InventorySelectItem, id, _context));
                _itemRows.Add(new ItemRow { Id = item.Id, Root = row, Button = button, Quantity = quantity, Equipped = equipped });
                _generated.Add(row.gameObject);
            }
        }

        private void BuildSkillLists(SkillClassDefinition job)
        {
            ClearSkillLists();
            _renderedClassId = job.Id;
            int generatedStart = _generated.Count;
            _activeHeading.text = Format("skill.active_tree_format", job.Name);
            var active = _sheet.Skills.Where(skill => skill.TreeKind == SkillTreeKind.Main && skill.TreeId == job.ActiveTreeId)
                .OrderBy(skill => skill.Order).ThenBy(skill => skill.Id, StringComparer.Ordinal).ToArray();
            var passive = _sheet.Skills.Where(skill => skill.TreeKind == SkillTreeKind.Sub)
                .OrderBy(skill => skill.TreeId, StringComparer.Ordinal).ThenBy(skill => skill.Order)
                .ThenBy(skill => skill.Id, StringComparer.Ordinal).ToArray();
            var positions = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < active.Length; i++)
            {
                positions.Add(active[i].Id, 8 + i * 104 + 43);
            }
            foreach (var skill in active)
            {
                foreach (string prerequisite in skill.PrerequisiteIds)
                {
                    if (positions.TryGetValue(prerequisite, out float from))
                    {
                        float to = positions[skill.Id];
                        var line = F.Rect("SkillLink_" + prerequisite + "_" + skill.Id, _activeList, 10, Mathf.Min(from, to), 2, Mathf.Abs(to - from));
                        F.Image(line, F.Gold);
                        _generated.Add(line.gameObject);
                    }
                }
            }
            for (int i = 0; i < active.Length; i++)
            {
                CreateSkillRow(active[i], _activeList, 20, 8 + i * 104, 262, 86);
                var connector = F.Rect("NodeConnector", _activeList, 10, positions[active[i].Id], 10, 2);
                F.Image(connector, F.Gold);
                _generated.Add(connector.gameObject);
            }
            for (int i = 0; i < passive.Length; i++)
            {
                CreateSkillRow(passive[i], _passiveList, 6, 8 + i * 90, 282, 82);
            }
            if (active.Length == 0)
            {
                var empty = F.Text("ActiveSkillsEmptyText", _activeList, _font, 16, 16, 262, 86, 16, F.Muted);
                empty.text = _sheet.Text("skill.active_empty");
                _generated.Add(empty.gameObject);
            }
            if (passive.Length == 0)
            {
                var empty = F.Text("PassiveSkillsEmptyText", _passiveList, _font, 16, 16, 262, 86, 16, F.Muted);
                empty.text = _sheet.Text("skill.passive_empty");
                _generated.Add(empty.gameObject);
            }
            _activeList.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(348, active.Length * 104 + 8));
            _passiveList.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(348, passive.Length * 90 + 8));
            _skillGenerated.AddRange(_generated.GetRange(generatedStart, _generated.Count - generatedStart));
            _activeList.anchoredPosition = Vector2.zero;
            _passiveList.anchoredPosition = Vector2.zero;
        }

        private void CreateSkillRow(SkillDefinition skill, RectTransform parent, float x, float y, float width, float height)
        {
            var button = F.Button("Skill_" + skill.Id, parent, _font, x, y, width, height);
            var name = button.GetComponentInChildren<Text>();
            name.name = "SkillNameText";
            name.rectTransform.anchoredPosition = new Vector2(12, -4);
            name.rectTransform.sizeDelta = new Vector2(width - 24, 37);
            name.alignment = TextAnchor.MiddleLeft;
            name.fontSize = 17;
            name.text = skill.Name;
            F.Text("SkillCostText", button.transform, _font, 12, 43, width - 24, 20, 12, F.Muted).text =
                Format("skill.list_level_format", skill.RequiredLevel) + " · "
                + Format(skill.TreeKind == SkillTreeKind.Main ? "skill.main_cost_format" : "skill.sub_cost_format", skill.Cost);
            var state = F.Text("SkillStateText", button.transform, _font, 12, 64, width - 24, 18, 11, F.PaleGold);
            string id = skill.Id;
            Bind(button, () => _input.AddInput(UIInputType.SkillSelect, id, _context));
            _skillRows.Add(new SkillRow { Id = id, Button = button, State = state });
            _generated.Add(button.gameObject);
        }

        public void Render(IAdventureProgressReader progress, IHeroMenuReader menu, bool locked, bool canWrite)
        {
            bool open = menu != null && menu.Open;
            gameObject.SetActive(open);
            if (!open)
            {
                if (_wasOpen)
                {
                    EventSystem.current?.SetSelectedGameObject(null);
                }
                _wasOpen = false;
                return;
            }
            transform.SetAsLastSibling();
            _context = menu.Context;
            _selectedId = menu.SelectedSkillId;
            _controls.interactable = !locked;
            _controls.blocksRaycasts = true;
            bool inventory = menu.Mode == HeroMenuMode.Inventory;
            _inventoryPanel.gameObject.SetActive(inventory);
            _skillsPanel.gameObject.SetActive(!inventory);
            if (inventory)
            {
                RenderInventory(progress, menu, locked, canWrite);
            }
            else
            {
                RenderSkills(progress, menu, locked, canWrite);
            }
            _bagClose.interactable = !locked;
            _skillsClose.interactable = !locked;
            if (!_wasOpen)
            {
                EventSystem.current?.SetSelectedGameObject((inventory ? _bagClose : _skillsClose).gameObject);
            }
            _wasOpen = true;
        }

        private void RenderInventory(IAdventureProgressReader progress, IHeroMenuReader menu, bool locked, bool canWrite)
        {
            var hero = _characters.GetCharacter(_characters.PlayerId);
            var character = progress.GetCharacter(hero.Id);
            var job = _sheet.GetClassForCharacter(character);
            TownView.SetArtwork(_bagPortrait, _portraits.Portraits[hero.PortraitIndex]);
            _bagOwner.text = Format("bag.owner_format", hero.Name);
            _bagClass.text = Format("profile.class_format", job.Name);
            _bagWeapon.text = Format("profile.weapon_format", _sheet.GetWeapon(job.WeaponId).Name);
            _bagGold.text = Format("bag.gold_format", progress.PlayerGold);
            _selectedItemId = menu.SelectedItemId;
            int visible = 0;
            foreach (var row in _itemRows)
            {
                int quantity = progress.Items.TryGetValue(row.Id, out int value) ? value : 0;
                row.Root.gameObject.SetActive(quantity > 0);
                if (quantity > 0)
                {
                    row.Root.anchoredPosition = new Vector2(8, -8 - visible * 72);
                    row.Quantity.text = Format("bag.quantity_format", quantity);
                    row.Button.interactable = !locked;
                    row.Button.GetComponent<Image>().color = row.Id == _selectedItemId
                        ? new Color(0.32f, 0.26f, 0.17f) : new Color(0.12f, 0.13f, 0.135f);
                    row.Equipped.text = _equipment != null && row.Id == character.EquippedItemId
                        ? _equipment.Text("bag.equipped") : string.Empty;
                    visible++;
                }
            }
            _bagEmpty.gameObject.SetActive(visible == 0);
            _bagItems.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(318, visible * 72 + 16));
            RenderItemDetails(progress, menu, locked, canWrite);
        }

        private void RenderSkills(IAdventureProgressReader progress, IHeroMenuReader menu, bool locked, bool canWrite)
        {
            var hero = progress.GetCharacter(_characters.PlayerId);
            var job = _sheet.GetClassForCharacter(hero);
            if (_renderedClassId != job.Id)
            {
                BuildSkillLists(job);
            }
            _skillsIdentity.text = _characters.GetCharacter(hero.Id).Name + " · " + job.Name;
            _points.text = Format("skill.points_format", hero.MainSkillPoints, hero.SubSkillPoints);
            foreach (var row in _skillRows)
            {
                var failure = SkillLearningRules.GetLearnFailure(_sheet, hero, row.Id);
                row.State.text = RowState(failure);
                row.Button.interactable = !locked;
                row.Button.GetComponent<Image>().color = row.Id == _selectedId
                    ? new Color(0.32f, 0.26f, 0.17f) : hero.HasSkill(row.Id)
                        ? new Color(0.12f, 0.20f, 0.18f) : new Color(0.12f, 0.13f, 0.135f);
            }
            var selected = string.IsNullOrEmpty(_selectedId) ? null : _sheet.GetSkill(_selectedId);
            if (_renderedSkillId != _selectedId)
            {
                _renderedSkillId = _selectedId;
                _skillDetailsContent.anchoredPosition = Vector2.zero;
            }
            _skillTitle.text = selected == null ? _sheet.Text("skill.choose") : selected.Name;
            _skillDescription.text = selected?.Description ?? string.Empty;
            _skillRequirements.text = string.Empty;
            _skillExecution.text = string.Empty;
            if (selected != null)
            {
                string prerequisites = selected.PrerequisiteIds.Count == 0 ? _sheet.Text("skill.prerequisite_none")
                    : Format("skill.prerequisite_format", string.Join(_sheet.Text("profile.skill_separator"),
                        selected.PrerequisiteIds.Select(id => _sheet.GetSkill(id).Name)));
                _skillRequirements.text = Format("skill.required_level_format", selected.RequiredLevel) + "\n"
                    + Format(selected.TreeKind == SkillTreeKind.Main ? "skill.main_cost_format" : "skill.sub_cost_format", selected.Cost)
                    + "\n" + prerequisites;
                if (selected.Type == SkillType.Active)
                {
                    _skillExecution.text = Format("skill.target_format", TargetName(selected.TargetType))
                        + "\n" + Format("skill.energy_format", selected.EnergyCost)
                        + "\n" + Format("skill.cooldown_format", selected.Cooldown);
                }
            }
            float descriptionHeight = Mathf.Max(22, _skillDescription.preferredHeight + 4);
            _skillDescription.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, descriptionHeight);
            float requirementHeight = Mathf.Max(22, _skillRequirements.preferredHeight + 4);
            _skillRequirements.rectTransform.anchoredPosition = new Vector2(8, -descriptionHeight - 26);
            _skillRequirements.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, requirementHeight);
            float executionHeight = Mathf.Max(22, _skillExecution.preferredHeight + 4);
            _skillExecution.rectTransform.anchoredPosition = new Vector2(8, -descriptionHeight - requirementHeight - 44);
            _skillExecution.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, executionHeight);
            _skillDetailsContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(304, descriptionHeight + requirementHeight + executionHeight + 58));
            var reason = selected == null ? SkillLearnFailure.InvalidSkill : SkillLearningRules.GetLearnFailure(_sheet, hero, selected.Id);
            _learn.interactable = !locked && canWrite && selected != null && reason == SkillLearnFailure.None;
            _learn.GetComponentInChildren<Text>().text = _sheet.Text(reason == SkillLearnFailure.AlreadyLearned ? "skill.learned" : "skill.learn");
            _status.text = !string.IsNullOrEmpty(menu.Status) ? menu.Status
                : !canWrite ? _sheet.Text("status.load_failed") : selected == null ? _sheet.Text("skill.choose_hint")
                : reason == SkillLearnFailure.None ? _sheet.Text("skill.available") : FailureText(reason);
            _status.color = !canWrite || menu.Status == _sheet.Text("status.save_failed") ? F.Error : F.Muted;
        }

        private string FailureText(SkillLearnFailure failure)
        {
            return _sheet.Text(failure switch
            {
                SkillLearnFailure.AlreadyLearned => "status.already_learned",
                SkillLearnFailure.WrongTree => "status.wrong_tree",
                SkillLearnFailure.LevelRequired => "status.level_required",
                SkillLearnFailure.PrerequisiteRequired => "status.prerequisite_required",
                SkillLearnFailure.InsufficientPoints => "status.insufficient_points",
                _ => "status.invalid_skill"
            });
        }

        private string RowState(SkillLearnFailure failure)
        {
            return _sheet.Text(failure switch
            {
                SkillLearnFailure.None => "skill.available",
                SkillLearnFailure.AlreadyLearned => "skill.learned",
                SkillLearnFailure.WrongTree => "skill.state_wrong_tree",
                SkillLearnFailure.LevelRequired => "skill.state_level",
                SkillLearnFailure.PrerequisiteRequired => "skill.state_prerequisite",
                SkillLearnFailure.InsufficientPoints => "skill.state_points",
                _ => "status.invalid_skill"
            });
        }

        private string TargetName(SkillTargetType target)
        {
            return _sheet.Text(target switch
            {
                SkillTargetType.SingleEnemy => "skill.target_single_enemy",
                SkillTargetType.Self => "skill.target_self",
                SkillTargetType.SingleAlly => "skill.target_single_ally",
                SkillTargetType.AllEnemies => "skill.target_all_enemies",
                SkillTargetType.AllAllies => "skill.target_all_allies",
                SkillTargetType.AllUnits => "skill.target_all_units",
                _ => "skill.target_none"
            });
        }

        private string Format(string key, params object[] values) => string.Format(CultureInfo.InvariantCulture, _sheet.Text(key), values);

        private void Bind(Button button, UnityAction action)
        {
            button.onClick.AddListener(action);
            _bindings.Add((button, action));
        }

        public void UnbindInputs()
        {
            foreach (var binding in _bindings)
            {
                if (binding.Button != null)
                {
                    binding.Button.onClick.RemoveListener(binding.Action);
                }
            }
            _bindings.Clear();
            foreach (var item in _generated)
            {
                if (item != null)
                {
                    item.SetActive(false);
                    Destroy(item);
                }
            }
            _generated.Clear();
            _skillGenerated.Clear();
            _skillRows.Clear();
            _itemRows.Clear();
            _input = null;
            _context = null;
            _selectedId = null;
            _renderedSkillId = null;
            _renderedClassId = null;
            _selectedItemId = null;
            _wasOpen = false;
            if (this != null)
            {
                gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            UnbindInputs();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Refactor.Main;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using UnityEngine.UI;
using F = Refactor.UI.AdventureUIFactory;

namespace Refactor.UI
{
    public sealed partial class DungeonView
    {
        private SkillSheet _battleSkills;
        private IDungeonDataReader _battleState;
        private RectTransform _battleSkillContent;
        private GameObject _battleSkillRoot;
        private Text _battleSkillsEmpty;
        private Button _battleSkillUse;
        private readonly Dictionary<string, CombatSkillButton> _combatSkillButtons = new(StringComparer.Ordinal);

        private sealed class CombatSkillButton
        {
            public Button Button;
            public Text Resource;
        }

        private void ConfigureBattleSkills(SkillSheet sheet)
        {
            _battleSkills = sheet;
            if (_battleSkillRoot == null)
            {
                var parent = AttackButton.transform.parent;
                _battleSkillContent = F.ScrollContent("BattleSkillChoices", parent, 240, 356, 784, 50);
                _battleSkillRoot = _battleSkillContent.parent.parent.gameObject;
                var scroll = _battleSkillRoot.GetComponent<ScrollRect>();
                scroll.horizontal = true;
                scroll.vertical = false;
                _battleSkillsEmpty = F.Text("BattleSkillsEmptyText", _battleSkillContent, DungeonNameText.font,
                    16, 0, 752, 50, 15, F.Muted);
                _battleSkillUse = F.Button("BattleSkillUseButton", parent, DungeonNameText.font, 1040, 356, 176, 50, true);
            }
            _battleSkillsEmpty.text = sheet.Text("battle.no_skills");
            Bind(_battleSkillUse, ConfirmGroupOrCancelTarget);
            ((RectTransform)AttackButton.transform).anchoredPosition = new Vector2(64, -356);
            ((RectTransform)AttackButton.transform).sizeDelta = new Vector2(160, 50);
            var attackLabel = AttackButton.GetComponentInChildren<Text>();
            attackLabel.fontSize = 17;
            attackLabel.rectTransform.anchoredPosition = new Vector2(8, 0);
            attackLabel.rectTransform.sizeDelta = new Vector2(144, 50);
            ((RectTransform)AttackButton.transform.Find("Line")).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 160);
            BattleReadyText.rectTransform.anchoredPosition = new Vector2(360, -684);
            BattleReadyText.rectTransform.sizeDelta = new Vector2(856, 35);
            BattleReadyText.fontSize = 20;
            BattleHintText.rectTransform.anchoredPosition = new Vector2(360, -722);
            BattleHintText.rectTransform.sizeDelta = new Vector2(856, 48);
            BattleHintText.fontSize = 15;
        }

        private void SelectBasicAttack()
        {
            if (_battleState != null && _battleState.Skills != null)
            {
                _input.AddInput(UIInputType.DungeonSelectSkill, string.Empty, _readyAllyId);
            }
            _choosingTarget = true;
        }

        private void SelectCombatSkill(string id)
        {
            _input.AddInput(UIInputType.DungeonSelectSkill, id, _readyAllyId);
            _choosingTarget = true;
        }

        private void ConfirmGroupOrCancelTarget()
        {
            if (RequiresUseButton())
            {
                _input.AddInput(UIInputType.DungeonUseSkill, _readyAllyId, _readyAllyId);
            }
            _choosingTarget = false;
        }

        private void OnCombatUnitPressed(string targetId, bool enemy)
        {
            if (_choosingTarget)
            {
                _input.AddInput(_battleState?.Skills != null ? UIInputType.DungeonUseSkill : UIInputType.DungeonAttack,
                    targetId, _readyAllyId);
                _choosingTarget = false;
            }
            else if (!enemy)
            {
                _input.AddInput(UIInputType.PartyCharacterOpen, targetId, "party");
            }
        }

        private bool CanSelectUnit(CombatantData unit, bool enemy)
        {
            if (_choosingTarget)
            {
                return _battleState?.Skills != null
                    ? !RequiresUseButton() && _battleState.CanTargetCombatSkill(unit.Id)
                    : enemy && unit.IsAlive;
            }
            return !enemy && _characterView != null;
        }

        private bool RequiresUseButton()
        {
            if (_battleState?.Skills == null || string.IsNullOrEmpty(_battleState.SelectedCombatSkillId))
            {
                return false;
            }
            var target = _battleState.Skills.GetSkill(_battleState.SelectedCombatSkillId).TargetType;
            return target == SkillTargetType.Self || target == SkillTargetType.AllEnemies
                || target == SkillTargetType.AllAllies || target == SkillTargetType.AllUnits;
        }

        private void RenderBattleSkills(IDungeonDataReader state, bool locked)
        {
            if (_battleSkillRoot == null)
            {
                return;
            }
            bool available = state.Skills != null && state.InCombat;
            _battleSkillRoot.SetActive(available);
            _battleSkillUse.gameObject.SetActive(available && _choosingTarget);
            if (!available)
            {
                return;
            }
            CombatantData hero = null;
            foreach (var ally in state.Allies)
            {
                if (ally.IsPlayerControlled)
                {
                    hero = ally;
                    break;
                }
            }
            if (hero == null)
            {
                return;
            }
            var remaining = new HashSet<string>(_combatSkillButtons.Keys, StringComparer.Ordinal);
            int index = 0;
            foreach (var definition in state.Skills.Skills)
            {
                if (definition.Type != SkillType.Active || !hero.HasSkill(definition.Id)
                    || hero.CharacterClass == null || definition.TreeId != hero.CharacterClass.ActiveTreeId)
                {
                    continue;
                }
                if (!_combatSkillButtons.TryGetValue(definition.Id, out var row))
                {
                    var button = F.Button("CombatSkill_" + definition.Id, _battleSkillContent, DungeonNameText.font, 0, 0, 148, 50);
                    var name = button.GetComponentInChildren<Text>();
                    name.name = "CombatSkillNameText";
                    name.rectTransform.anchoredPosition = new Vector2(5, 0);
                    name.rectTransform.sizeDelta = new Vector2(138, 28);
                    name.fontSize = 15;
                    name.text = definition.Name;
                    var resource = F.Text("CombatSkillResourceText", button.transform, DungeonNameText.font, 5, 29, 138, 20, 11, F.Muted);
                    resource.alignment = TextAnchor.MiddleCenter;
                    string id = definition.Id;
                    button.onClick.AddListener(() => SelectCombatSkill(id));
                    row = new CombatSkillButton { Button = button, Resource = resource };
                    _combatSkillButtons.Add(id, row);
                }
                remaining.Remove(definition.Id);
                ((RectTransform)row.Button.transform).anchoredPosition = new Vector2(index * 156, 0);
                row.Button.interactable = !locked && state.CanUseCombatSkill(definition.Id);
                row.Button.GetComponent<Image>().color = _choosingTarget && state.SelectedCombatSkillId == definition.Id
                    ? new Color(0.34f, 0.28f, 0.16f) : new Color(0.12f, 0.13f, 0.135f);
                float cooldown = hero.GetCooldown(definition.Id);
                row.Resource.text = SkillFormat(cooldown > 0 ? "battle.cooldown_format" : "battle.skill_cost_format",
                    cooldown > 0 ? cooldown : definition.EnergyCost);
                index++;
            }
            foreach (string id in remaining)
            {
                DestroyButton(_combatSkillButtons[id].Button);
                _combatSkillButtons.Remove(id);
            }
            _battleSkillsEmpty.gameObject.SetActive(index == 0);
            _battleSkillContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(784, index * 156 - 8));
            AttackButton.interactable = !locked && state.CanUseCombatSkill(string.Empty);
            AttackButton.GetComponent<Image>().color = _choosingTarget && string.IsNullOrEmpty(state.SelectedCombatSkillId)
                ? new Color(0.34f, 0.28f, 0.16f) : new Color(0.12f, 0.13f, 0.135f);
            bool group = RequiresUseButton();
            _battleSkillUse.GetComponentInChildren<Text>().text = _battleSkills.Text(group ? "battle.use" : "battle.cancel");
            _battleSkillUse.interactable = !locked && (!group || state.CanTargetCombatSkill(_readyAllyId));
            if (_choosingTarget && !string.IsNullOrEmpty(state.SelectedCombatSkillId))
            {
                var skill = state.Skills.GetSkill(state.SelectedCombatSkillId);
                BattleHintText.text = skill.TargetType == SkillTargetType.Self
                    ? SkillFormat("battle.confirm_self_format", skill.Name)
                    : SkillFormat(group ? "battle.confirm_group_format" : "battle.select_target_format", skill.Name, CombatTargetName(skill.TargetType));
            }
        }

        private void RenderUnitResources(UnitCard card, CombatantData unit)
        {
            card.Energy.gameObject.SetActive(_battleSkills != null && unit.MaxEnergy > 0);
            card.Effects.gameObject.SetActive(_battleSkills != null);
            if (_battleSkills == null)
            {
                return;
            }
            card.Energy.text = SkillFormat("battle.energy_format", unit.Energy, unit.MaxEnergy);
            var effects = new List<string>();
            if (unit.StunRemaining > 0)
            {
                effects.Add(SkillFormat("battle.stun_format", unit.StunRemaining));
            }
            if (unit.Barrier > 0)
            {
                effects.Add(SkillFormat("battle.barrier_format", unit.Barrier));
            }
            if (unit.GlobalCooldown > 0)
            {
                effects.Add(SkillFormat("battle.global_cooldown_format", unit.GlobalCooldown));
            }
            card.Effects.text = string.Join(_battleSkills.Text("profile.skill_separator"), effects);
        }

        private string CombatTargetName(SkillTargetType type)
        {
            return _battleSkills.Text(type switch
            {
                SkillTargetType.SingleEnemy => "skill.target_single_enemy",
                SkillTargetType.SingleAlly => "skill.target_single_ally",
                SkillTargetType.Self => "skill.target_self",
                SkillTargetType.AllEnemies => "skill.target_all_enemies",
                SkillTargetType.AllAllies => "skill.target_all_allies",
                SkillTargetType.AllUnits => "skill.target_all_units",
                _ => "skill.target_none"
            });
        }

        private string SkillFormat(string key, params object[] values)
            => string.Format(CultureInfo.InvariantCulture, _battleSkills.Text(key), values);

        private void ClearBattleSkills()
        {
            foreach (var row in _combatSkillButtons.Values)
            {
                DestroyButton(row.Button);
            }
            _combatSkillButtons.Clear();
            _battleState = null;
            _battleSkills = null;
        }
    }
}

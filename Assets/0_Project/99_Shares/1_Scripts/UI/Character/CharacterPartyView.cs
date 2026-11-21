using System;
using System.Collections.Generic;
using System.Globalization;
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
    // 인물/파티의 조회 전용 표시. 친밀도, 성장, 영입과 추방은 요청 처리 계층이 소유한다.
    public sealed class CharacterPartyView : MonoBehaviour
    {
        private Font _font;
        private CharacterSheet _sheet;
        private TownSheet _town;
        private TownContent _portraits;
        private UIInputStream _input;
        private CharacterGrowthCalculator _growth;
        private CombatStatCalculator _combat;
        private SkillSheet _skills;
        private RectTransform _partyFrame;
        private CanvasGroup _partyControls;
        private Text _partyHeading;
        private RectTransform _modal;
        private CanvasGroup _modalControls;
        private Image _portrait;
        private Text _name;
        private Text _role;
        private Text _identity;
        private Text _progress;
        private Text _affinity;
        private Text _status;
        private Text _classInfo;
        private Text _weaponInfo;
        private Text _pointInfo;
        private Text _learnedHeading;
        private Text _learnedSkills;
        private float _baseStatsHeight;
        private RectTransform _statsViewport;
        private RectTransform _statsContent;
        private RectTransform _dialogue;
        private Text _greeting;
        private Text _speech;
        private Button _talk;
        private Button _recruit;
        private Button _dismiss;
        private Button _close;
        private Button _inventoryButton;
        private Button _skillsButton;
        private string _selectedId;
        private string _context;
        private bool _wasOpen;
        private readonly List<(Text Text, string Key)> _labels = new();
        private readonly List<(Button Button, UnityAction Action)> _bindings = new();
        private readonly Dictionary<string, PartyCard> _cards = new(StringComparer.Ordinal);
        private readonly List<RectTransform> _emptySlots = new();
        private readonly Dictionary<MajorStatType, Text> _majorValues = new();
        private readonly Dictionary<CombatStatType, Text> _combatValues = new();

        private sealed class PartyCard
        {
            public Button Button;
            public Image Portrait;
            public Text Name;
            public Text Summary;
        }

        public static CharacterPartyView Create(Transform canvas, Font font)
        {
            var root = F.Stretch("CharacterPartyView", canvas);
            var view = root.gameObject.AddComponent<CharacterPartyView>();
            view._font = font != null ? font : throw new ArgumentNullException(nameof(font));
            view.Build();
            return view;
        }

        private void Build()
        {
            _partyFrame = F.Center("PartyFrame", transform, 1280, 800);
            var bar = F.Rect("PartyBar", _partyFrame, 64, 680, 1152, 108);
            F.Image(bar, new Color(0.025f, 0.035f, 0.04f, 0.97f));
            F.Image(F.Rect("TopRule", bar, 0, 0, 1152, 1), F.Gold);
            _partyControls = bar.gameObject.AddComponent<CanvasGroup>();
            _partyHeading = F.Text("PartyCountText", bar, _font, 12, 1, 1128, 27, 15, F.PaleGold);

            _modal = F.Stretch("CharacterInteractionRoot", transform);
            F.Image(_modal, new Color(0.01f, 0.015f, 0.02f, 0.92f), true);
            _modalControls = _modal.gameObject.AddComponent<CanvasGroup>();
            var panel = F.Center("CharacterInteractionPanel", _modal, 1160, 640);
            panel.anchoredPosition = new Vector2(0, 55);
            F.Image(panel, F.Panel, true);
            F.Image(F.Rect("TopRule", panel, 0, 0, 1160, 1), F.Gold);
            var portrait = F.Rect("PortraitClip", panel, 24, 24, 160, 184);
            F.Image(portrait, Color.white);
            portrait.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var artwork = F.Stretch("CharacterPortrait", portrait);
            _portrait = F.Image(artwork, Color.white);
            artwork.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            _name = F.Text("CharacterNameText", panel, _font, 208, 17, 650, 59, 30, F.PaleGold);
            _role = F.Text("CharacterRoleText", panel, _font, 208, 76, 650, 32, 18, F.White);
            _identity = F.Text("CharacterIdentityText", panel, _font, 208, 113, 650, 32, 17, F.White);
            _progress = F.Text("CharacterProgressText", panel, _font, 208, 150, 650, 32, 18, F.PaleGold);
            _affinity = F.Text("CharacterAffinityText", panel, _font, 208, 187, 650, 32, 17, F.White);
            var training = F.Rect("CharacterTrainingPanel", panel, 872, 24, 264, 184);
            F.Image(training, new Color(0.035f, 0.047f, 0.05f));
            _classInfo = F.Text("CharacterClassText", training, _font, 16, 12, 232, 40, 19, F.PaleGold);
            _weaponInfo = F.Text("CharacterWeaponText", training, _font, 16, 64, 232, 42, 17, F.White);
            _pointInfo = F.Text("CharacterSkillPointsText", training, _font, 16, 119, 232, 53, 15, F.Muted);

            _dialogue = F.Rect("CharacterDialoguePanel", panel, 24, 230, 820, 126);
            F.Image(_dialogue, new Color(0.035f, 0.047f, 0.05f));
            _greeting = F.Text("CharacterGreetingText", _dialogue, _font, 16, 7, 788, 37, 17, F.PaleGold);
            _speech = F.Text("CharacterDialogueText", _dialogue, _font, 16, 47, 788, 72, 16, F.White);
            _speech.alignment = TextAnchor.UpperLeft;
            _statsContent = F.ScrollContent("CharacterStats", panel, 24, 230, 820, 338);
            _statsViewport = (RectTransform)_statsContent.parent.parent;
            AddScrollbar(_statsViewport, _statsContent);
            var actions = F.Rect("CharacterActions", panel, 872, 230, 264, 338);
            F.Image(actions, new Color(0.035f, 0.047f, 0.05f));
            Label("CharacterActionsHeading", actions, "profile.actions", 16, 8, 232, 34, 18);
            _talk = F.Button("CharacterTalkButton", actions, _font, 16, 62, 232, 49, true);
            _recruit = F.Button("CharacterRecruitButton", actions, _font, 16, 126, 232, 49);
            _dismiss = F.Button("CharacterDismissButton", actions, _font, 16, 126, 232, 49);
            _close = F.Button("CharacterCloseButton", actions, _font, 16, 273, 232, 49);
            _status = F.Text("CharacterStatusText", panel, _font, 24, 580, 1112, 48, 15, F.Muted);
            _modal.gameObject.SetActive(false);
            // 상호작용 중에도 현재 파티를 보여 주되 클릭은 CanvasGroup으로 차단한다.
            _partyFrame.SetAsLastSibling();
        }

        private void Label(string name, Transform parent, string key, float x, float y, float width, float height, int fontSize)
        {
            _labels.Add((F.Text(name, parent, _font, x, y, width, height, fontSize, F.PaleGold), key));
        }

        private static void AddScrollbar(RectTransform viewport, RectTransform content)
        {
            var track = F.Rect("Scrollbar", viewport, 810, 0, 10, viewport.rect.height);
            track.anchorMin = new Vector2(0, 0);
            track.anchorMax = new Vector2(0, 1);
            track.offsetMin = new Vector2(810, 4);
            track.offsetMax = new Vector2(820, -4);
            F.Image(track, new Color(0.12f, 0.13f, 0.135f));
            var handle = F.Stretch("Handle", track);
            var graphic = F.Image(handle, F.Gold, true);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = graphic;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.verticalScrollbar = scrollbar;
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 800);
        }

        public void Initialize(CharacterSheet sheet, TownSheet town, TownContent portraits, UIInputStream input)
        {
            UnbindInputs();
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _town = town ?? throw new ArgumentNullException(nameof(town));
            _portraits = portraits != null ? portraits : throw new ArgumentNullException(nameof(portraits));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _growth = new CharacterGrowthCalculator(sheet.Growth);
            _combat = new CombatStatCalculator(sheet.Combat);
            foreach (var label in _labels)
            {
                label.Text.text = sheet.Text(label.Key);
            }
            SetButtonLabel(_talk, "action.talk");
            SetButtonLabel(_recruit, "action.recruit");
            SetButtonLabel(_dismiss, "action.dismiss");
            SetButtonLabel(_close, "action.close");
            Bind(_talk, () => _input.AddInput(UIInputType.CharacterTalk, _selectedId, _context));
            Bind(_recruit, () => _input.AddInput(UIInputType.CharacterRecruit, _selectedId, _context));
            Bind(_dismiss, () => _input.AddInput(UIInputType.CharacterDismiss, _selectedId, _context));
            Bind(_close, () => _input.AddInput(UIInputType.CharacterClose, _selectedId, _context));
            BuildStats();
            gameObject.SetActive(true);
        }

        private void BuildStats()
        {
            if (_majorValues.Count > 0)
            {
                return;
            }
            var heading = F.Text("MajorStatsHeading", _statsContent, _font, 12, 6, 776, 31, 17, F.PaleGold);
            heading.text = _sheet.Text("stats.major");
            var keys = new[] { "stats.strength", "stats.constitution", "stats.will_power", "stats.intelligence", "stats.agility", "stats.perception" };
            for (int i = 0; i < keys.Length; i++)
            {
                var type = (MajorStatType)i;
                _majorValues.Add(type, StatCell("Major_" + type, _sheet.Text(keys[i]), i % 3, 41 + i / 3 * 34));
            }
            var combatHeading = F.Text("CombatStatsHeading", _statsContent, _font, 12, 117, 776, 31, 17, F.PaleGold);
            combatHeading.text = _sheet.Text("stats.combat");
            var types = (CombatStatType[])Enum.GetValues(typeof(CombatStatType));
            for (int i = 0; i < types.Length; i++)
            {
                var type = types[i];
                _combatValues.Add(type, StatCell("Combat_" + type, _sheet.Combat.Get(type).Name, i % 3, 152 + i / 3 * 34));
            }
            _baseStatsHeight = 164 + Mathf.Ceil(types.Length / 3f) * 34;
            _learnedHeading = F.Text("CharacterLearnedHeadingText", _statsContent, _font, 12, _baseStatsHeight + 6, 776, 31, 17, F.PaleGold);
            _learnedSkills = F.Text("CharacterLearnedSkillsText", _statsContent, _font, 12, _baseStatsHeight + 44, 776, 40, 15, F.White);
            _learnedSkills.alignment = TextAnchor.UpperLeft;
            _statsContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _baseStatsHeight);
        }

        private Text StatCell(string name, string label, int column, float y)
        {
            var cell = F.Rect(name, _statsContent, 12 + column * 264, y, 248, 32);
            F.Text("Label", cell, _font, 0, 0, 146, 32, 13, F.Muted).text = label;
            var value = F.Text("Value", cell, _font, 146, 0, 102, 32, 14, F.White);
            value.alignment = TextAnchor.MiddleRight;
            return value;
        }

        public void Render(IAdventureProgressReader progress, ICharacterInteractionReader interaction, bool locked,
            bool canWrite, bool partyBlocked, IDungeonDataReader dungeon = null)
        {
            bool battle = dungeon != null && (dungeon.InCombat || dungeon.IsDefeated || dungeon.IsCleared);
            _partyFrame.gameObject.SetActive(true);
            RenderParty(progress, dungeon, locked || partyBlocked || interaction.Open, battle && !interaction.Open);
            _modal.gameObject.SetActive(interaction.Open);
            if (!interaction.Open)
            {
                if (_wasOpen)
                {
                    EventSystem.current?.SetSelectedGameObject(null);
                }
                _wasOpen = false;
                return;
            }
            transform.SetAsLastSibling();
            var definition = _sheet.GetCharacter(interaction.CharacterId);
            var character = progress.GetCharacter(interaction.CharacterId);
            bool member = progress.IsInParty(definition.Id);
            bool full = progress.PartyIds.Count >= _sheet.MaxPartySize;
            bool hero = definition.IsPlayer;
            bool changed = _selectedId != definition.Id || !_wasOpen;
            _selectedId = definition.Id;
            _context = interaction.Context;
            _modalControls.interactable = !locked;
            _modalControls.blocksRaycasts = true;
            TownView.SetArtwork(_portrait, _portraits.Portraits[definition.PortraitIndex]);
            _name.text = hero ? Format("party.player_name_format", definition.Name) : definition.Name;
            _role.text = definition.Role;
            _identity.text = Format("profile.identity_format", definition.Gender, definition.Age);
            _progress.text = Format("profile.progress_format", character.Level, character.Experience, _growth.RequiredExperience(character.Level));
            _affinity.text = hero ? _sheet.Text("profile.self") : Format("profile.affinity_format", character.Affinity);
            _talk.interactable = !locked && canWrite && !hero;
            _recruit.gameObject.SetActive(!member);
            _recruit.interactable = !locked && canWrite && !hero && !full;
            _dismiss.gameObject.SetActive(member);
            _dismiss.interactable = !locked && canWrite && !hero;
            _close.interactable = !locked;
            foreach (var entry in _majorValues)
            {
                entry.Value.text = Format("stats.value_format", character.Stats[entry.Key]);
            }
            var combat = _skills == null ? _combat.Calculate(character.Stats)
                : CombatSkillPassives.CalculateStats(_combat, character.Stats, _skills, character);
            if (dungeon != null)
            {
                foreach (var unit in dungeon.Allies)
                {
                    if (unit.Id == definition.Id && unit.EffectiveStats != null)
                    {
                        combat = unit.EffectiveStats;
                        break;
                    }
                }
            }
            RenderSkillProfile(character);
            foreach (var entry in _combatValues)
            {
                entry.Value.text = CombatValue(entry.Key, combat[entry.Key]);
            }
            bool dialogue = interaction.DialogueOpen && !hero;
            _dialogue.gameObject.SetActive(dialogue);
            _statsViewport.anchoredPosition = new Vector2(24, dialogue ? -368 : -230);
            _statsViewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, dialogue ? 200 : 338);
            if (dialogue)
            {
                var npc = _town.GetCharacter(definition.Id);
                _greeting.text = npc.Greeting;
                _speech.text = npc.Dialogue;
            }
            _status.text = !string.IsNullOrEmpty(interaction.Status) ? interaction.Status
                : !canWrite ? _sheet.Text("status.load_failed") : hero ? _sheet.Text("status.hero")
                : !member && full ? _sheet.Text("status.party_full")
                : _sheet.Text(member ? "status.party_hint" : "status.recruit_hint");
            _status.color = !canWrite || interaction.Status == _sheet.Text("status.save_failed")
                || interaction.Status == _sheet.Text("status.recruit_failed") ? F.Error : F.Muted;
            if (changed)
            {
                _statsContent.anchoredPosition = Vector2.zero;
                EventSystem.current?.SetSelectedGameObject(_close.gameObject);
            }
            _wasOpen = true;
        }

        public void ConfigureSkills(SkillSheet skills)
        {
            _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        }

        private void RenderSkillProfile(CharacterProgress character)
        {
            _classInfo.transform.parent.gameObject.SetActive(_skills != null);
            _learnedHeading.gameObject.SetActive(_skills != null);
            _learnedSkills.gameObject.SetActive(_skills != null);
            if (_skills == null)
            {
                return;
            }
            var job = _skills.GetClassForCharacter(character);
            _classInfo.text = string.Format(CultureInfo.InvariantCulture, _skills.Text("profile.class_format"), job.Name);
            _weaponInfo.text = string.Format(CultureInfo.InvariantCulture, _skills.Text("profile.weapon_format"), _skills.GetWeapon(job.WeaponId).Name);
            _pointInfo.text = string.Format(CultureInfo.InvariantCulture, _skills.Text("profile.points_format"), character.MainSkillPoints, character.SubSkillPoints);
            _learnedHeading.text = _skills.Text("profile.learned_skills");
            var names = new List<string>();
            foreach (string id in character.LearnedSkillIds)
            {
                names.Add(_skills.GetSkill(id).Name);
            }
            _learnedSkills.text = names.Count == 0 ? _skills.Text("profile.learned_empty")
                : string.Join(_skills.Text("profile.skill_separator"), names);
            float height = Mathf.Max(32, _learnedSkills.preferredHeight + 8);
            _learnedSkills.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            _statsContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _baseStatsHeight + 52 + height);
        }

        private void RenderParty(IAdventureProgressReader progress, IDungeonDataReader dungeon, bool blocked, bool compact)
        {
            _partyControls.interactable = !blocked;
            _partyControls.blocksRaycasts = !blocked;
            float barWidth = compact ? 276f : 1152f;
            ((RectTransform)_partyControls.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, barWidth);
            ((RectTransform)_partyControls.transform.Find("TopRule")).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, barWidth);
            _partyHeading.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, barWidth - 24);
            _partyHeading.text = Format("party.count_format", progress.PartyIds.Count, _sheet.MaxPartySize);
            var remaining = new HashSet<string>(_cards.Keys, StringComparer.Ordinal);
            float width = (1152f - 16f * (_sheet.MaxPartySize - 1)) / _sheet.MaxPartySize;
            for (int i = 0; i < _sheet.MaxPartySize; i++)
            {
                if (i >= _emptySlots.Count)
                {
                    var slot = F.Rect("PartyEmpty_" + i, _partyControls.transform, i * (width + 16), 32, width, 72);
                    F.Image(slot, new Color(0.06f, 0.075f, 0.08f));
                    F.Text("Text", slot, _font, 16, 0, width - 32, 72, 15, F.Muted).text = _sheet.Text("party.empty");
                    _emptySlots.Add(slot);
                }
                _emptySlots[i].gameObject.SetActive(!compact && i >= progress.PartyIds.Count);
                if (i >= progress.PartyIds.Count)
                {
                    continue;
                }
                string id = progress.PartyIds[i];
                var definition = _sheet.GetCharacter(id);
                var character = progress.GetCharacter(id);
                remaining.Remove(id);
                if (!_cards.TryGetValue(id, out var card))
                {
                    card = CreatePartyCard(id, width);
                    _cards.Add(id, card);
                }
                ((RectTransform)card.Button.transform).anchoredPosition = new Vector2(i * (width + 16), -32);
                card.Button.gameObject.SetActive(!compact || definition.IsPlayer);
                card.Button.interactable = !blocked;
                card.Name.text = definition.IsPlayer ? Format("party.player_name_format", definition.Name) : definition.Name;
                card.Summary.text = Format("party.member_format", character.Level, character.Affinity);
                if (dungeon != null)
                {
                    foreach (var ally in dungeon.Allies)
                    {
                        if (ally.Id == id)
                        {
                            card.Summary.text = Format("party.dungeon_member_format", character.Level, ally.Hp, ally.MaxHp);
                            break;
                        }
                    }
                }
                TownView.SetArtwork(card.Portrait, _portraits.Portraits[definition.PortraitIndex]);
            }
            if (_inventoryButton != null)
            {
                bool configured = _skills != null;
                _inventoryButton.gameObject.SetActive(configured);
                _skillsButton.gameObject.SetActive(configured);
                _inventoryButton.interactable = !blocked;
                _skillsButton.interactable = !blocked;
                if (configured)
                {
                    _inventoryButton.GetComponentInChildren<Text>().text = _skills.Text("menu.inventory");
                    _skillsButton.GetComponentInChildren<Text>().text = _skills.Text("menu.skills");
                }
            }
            foreach (string id in remaining)
            {
                DestroyCard(_cards[id]);
                _cards.Remove(id);
            }
        }

        private PartyCard CreatePartyCard(string id, float width)
        {
            var button = F.Button("PartyCharacter_" + id, _partyControls.transform, _font, 0, 32, width, 72);
            var name = button.GetComponentInChildren<Text>();
            name.name = "PartyNameText";
            name.rectTransform.anchoredPosition = new Vector2(78, -1);
            name.rectTransform.sizeDelta = new Vector2(width - 90, 35);
            name.fontSize = 18;
            name.alignment = TextAnchor.MiddleLeft;
            var clip = F.Rect("PortraitClip", button.transform, 5, 5, 62, 62);
            F.Image(clip, Color.white);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = F.Image(F.Stretch("Portrait", clip), Color.white);
            portrait.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            var summary = F.Text("PartySummaryText", button.transform, _font, 78, 36, width - 90, 30, 13, F.Muted);
            button.onClick.AddListener(() => _input.AddInput(UIInputType.PartyCharacterOpen, id, "party"));
            if (_sheet.GetCharacter(id).IsPlayer)
            {
                name.rectTransform.sizeDelta = new Vector2(width - 90, 26);
                name.fontSize = 17;
                summary.rectTransform.anchoredPosition = new Vector2(78, -26);
                summary.rectTransform.sizeDelta = new Vector2(width - 90, 18);
                summary.fontSize = 11;
                float actionWidth = (width - 98) * 0.5f;
                _inventoryButton = F.Button("InventoryOpenButton", button.transform, _font, 78, 46, actionWidth, 26);
                _skillsButton = F.Button("SkillsOpenButton", button.transform, _font, 86 + actionWidth, 46, actionWidth, 26);
                _inventoryButton.GetComponentInChildren<Text>().fontSize = 13;
                _skillsButton.GetComponentInChildren<Text>().fontSize = 13;
                Bind(_inventoryButton, () => _input.AddInput(UIInputType.InventoryOpen, id, "hero"));
                Bind(_skillsButton, () => _input.AddInput(UIInputType.SkillsOpen, id, "hero"));
            }
            return new PartyCard { Button = button, Name = name, Summary = summary, Portrait = portrait };
        }

        private string CombatValue(CombatStatType type, float value)
        {
            switch (type)
            {
                case CombatStatType.MaxHealth:
                case CombatStatType.PhysicalAttackPower:
                case CombatStatType.MagicAttackPower:
                case CombatStatType.AttackSpeed:
                case CombatStatType.MovementSpeed:
                    return Format("stats.value_format", value);
                case CombatStatType.CooldownReduction:
                case CombatStatType.PhysicalResistance:
                case CombatStatType.MagicResistance:
                    return Format("stats.diminishing_format", value, (1f - 1f / (1f + value / 100f)) * 100f);
                case CombatStatType.FinalDamageMultiplier:
                    return Format("stats.multiplier_format", value);
                default:
                    return Format("stats.percent_format", value);
            }
        }

        private string Format(string key, params object[] values) => string.Format(CultureInfo.InvariantCulture, _sheet.Text(key), values);

        private void SetButtonLabel(Button button, string key) => button.GetComponentInChildren<Text>().text = _sheet.Text(key);

        private void Bind(Button button, UnityAction action)
        {
            button.onClick.AddListener(action);
            _bindings.Add((button, action));
        }

        private static void DestroyCard(PartyCard card)
        {
            if (card.Button != null)
            {
                card.Button.onClick.RemoveAllListeners();
                card.Button.gameObject.SetActive(false);
                Destroy(card.Button.gameObject);
            }
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
            foreach (var card in _cards.Values)
            {
                DestroyCard(card);
            }
            _cards.Clear();
            _input = null;
            _selectedId = null;
            _context = null;
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

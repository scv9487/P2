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
    // 정의/진척/선택을 조회해 표시한다. 수주, 납부, 보상 지급과 저장은 요청 처리 계층이 담당한다.
    public sealed class QuestBoardView : MonoBehaviour
    {
        private Font _font;
        private QuestSheet _sheet;
        private UIInputStream _input;
        private CanvasGroup _controls;
        private RectTransform _cardsRoot;
        private Text _title;
        private Text _active;
        private Text _selectedTitle;
        private Text _description;
        private Text _kind;
        private Text _progress;
        private Text _reward;
        private Text _delivery;
        private Text _status;
        private Button _accept;
        private Button _complete;
        private Button _close;
        private string _context;
        private string _selectedId;
        private bool _wasOpen;
        private readonly List<QuestCard> _cards = new();
        private readonly List<(Button Button, UnityAction Action)> _bindings = new();

        private sealed class QuestCard
        {
            public string Id;
            public Button Button;
            public Text State;
            public Text Progress;
        }

        public static QuestBoardView Create(Transform canvas, Font font)
        {
            var root = F.Stretch("QuestBoardRoot", canvas);
            F.Image(root, new Color(0.01f, 0.015f, 0.02f, 0.92f), true);
            var view = root.gameObject.AddComponent<QuestBoardView>();
            view._font = font != null ? font : throw new ArgumentNullException(nameof(font));
            view.Build();
            root.gameObject.SetActive(false);
            return view;
        }

        private void Build()
        {
            _controls = gameObject.AddComponent<CanvasGroup>();
            var panel = F.Center("QuestBoardPanel", transform, 1160, 640);
            panel.anchoredPosition = new Vector2(0, 55);
            F.Image(panel, F.Panel, true);
            F.Image(F.Rect("TopRule", panel, 0, 0, 1160, 1), F.Gold);
            _title = F.Text("QuestBoardTitleText", panel, _font, 24, 14, 1112, 56, 29, F.PaleGold);
            _active = F.Text("QuestActiveText", panel, _font, 24, 77, 1112, 40, 16, F.Muted);
            _cardsRoot = F.Rect("QuestCards", panel, 24, 140, 512, 372);
            var detail = F.Rect("QuestDetailPanel", panel, 560, 140, 576, 372);
            F.Image(detail, new Color(0.035f, 0.047f, 0.05f));
            _selectedTitle = F.Text("QuestTitleText", detail, _font, 20, 10, 536, 50, 25, F.PaleGold);
            _description = F.Text("QuestDescriptionText", detail, _font, 20, 68, 536, 88, 17, F.White);
            _description.alignment = TextAnchor.UpperLeft;
            _kind = F.Text("QuestKindText", detail, _font, 20, 170, 536, 30, 15, F.Muted);
            _progress = F.Text("QuestProgressText", detail, _font, 20, 208, 536, 43, 23, F.PaleGold);
            _reward = F.Text("QuestRewardText", detail, _font, 20, 261, 536, 38, 20, F.PaleGold);
            _delivery = F.Text("QuestDeliveryText", detail, _font, 20, 309, 536, 52, 14, F.White);
            _status = F.Text("QuestStatusText", panel, _font, 24, 526, 1112, 56, 15, F.Muted);
            _close = F.Button("QuestCloseButton", panel, _font, 24, 594, 176, 36);
            _accept = F.Button("QuestAcceptButton", panel, _font, 664, 594, 216, 36, true);
            _complete = F.Button("QuestCompleteButton", panel, _font, 904, 594, 232, 36, true);
        }

        public void Initialize(QuestSheet sheet, UIInputStream input)
        {
            UnbindInputs();
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _title.text = sheet.Text("board.title");
            _accept.GetComponentInChildren<Text>().text = sheet.Text("board.accept");
            _close.GetComponentInChildren<Text>().text = sheet.Text("board.close");
            Bind(_accept, () => _input.AddInput(UIInputType.QuestAccept, _selectedId, _context));
            Bind(_complete, () => _input.AddInput(UIInputType.QuestComplete, _selectedId, _context));
            Bind(_close, () => _input.AddInput(UIInputType.QuestClose, _selectedId, _context));
            for (int i = 0; i < sheet.Quests.Count; i++)
            {
                var quest = sheet.Quests[i];
                var button = F.Button("QuestCard_" + quest.Id, _cardsRoot, _font, i % 2 * 264, i / 2 * 194, 248, 178);
                var name = button.GetComponentInChildren<Text>();
                name.name = "NameText";
                name.rectTransform.anchoredPosition = new Vector2(16, -9);
                name.rectTransform.sizeDelta = new Vector2(216, 62);
                name.alignment = TextAnchor.MiddleLeft;
                name.fontSize = 20;
                name.text = quest.Name;
                F.Text("KindText", button.transform, _font, 16, 78, 216, 28, 14, F.Muted).text = KindText(quest.Kind);
                var state = F.Text("StateText", button.transform, _font, 16, 113, 216, 28, 15, F.PaleGold);
                var progress = F.Text("ProgressText", button.transform, _font, 16, 145, 216, 26, 13, F.White);
                string id = quest.Id;
                button.onClick.AddListener(() => _input.AddInput(UIInputType.QuestSelect, id, _context));
                _cards.Add(new QuestCard { Id = id, Button = button, State = state, Progress = progress });
            }
        }

        public void Render(IAdventureProgressReader progress, IQuestBoardReader board, bool locked, bool canWrite)
        {
            bool visible = board != null && board.Open;
            gameObject.SetActive(visible);
            if (!visible)
            {
                if (_wasOpen)
                {
                    EventSystem.current?.SetSelectedGameObject(null);
                }
                _wasOpen = false;
                return;
            }
            transform.SetAsLastSibling();
            _context = board.Context;
            _selectedId = board.SelectedQuestId;
            _controls.interactable = !locked;
            _controls.blocksRaycasts = true;
            string active = progress.Quest.ActiveQuestId;
            _active.text = string.IsNullOrEmpty(active) ? _sheet.Text("board.no_active")
                : Format("board.active_format", _sheet.GetQuest(active).Name);
            foreach (var card in _cards)
            {
                var definition = _sheet.GetQuest(card.Id);
                bool completed = progress.Quest.IsCompleted(card.Id);
                bool isActive = active == card.Id;
                string stateKey = completed ? "board.state_completed" : isActive
                    ? QuestRules.CanReport(definition, progress) ? "board.state_reportable" : "board.state_active"
                    : string.IsNullOrEmpty(active) ? "board.state_available" : "board.state_other_active";
                card.State.text = _sheet.Text(stateKey);
                card.Progress.text = Format("board.card_progress_format", QuestRules.GetProgress(definition, progress), definition.RequiredCount);
                card.Button.interactable = !locked;
                card.Button.GetComponent<Image>().color = card.Id == _selectedId
                    ? new Color(0.32f, 0.26f, 0.17f) : new Color(0.12f, 0.13f, 0.135f);
            }
            var selected = string.IsNullOrEmpty(_selectedId) ? null : _sheet.GetQuest(_selectedId);
            RenderDetails(selected, progress);
            _accept.interactable = !locked && canWrite && selected != null && QuestRules.CanAccept(selected, progress);
            _complete.interactable = !locked && canWrite && selected != null && QuestRules.CanReport(selected, progress);
            _complete.GetComponentInChildren<Text>().text = _sheet.Text(selected != null && selected.Kind == QuestKind.DeliverItems
                ? "board.deliver_complete" : "board.complete");
            _close.interactable = !locked;
            RenderStatus(selected, progress, board.Status, canWrite);
            if (!_wasOpen)
            {
                EventSystem.current?.SetSelectedGameObject(_close.gameObject);
            }
            _wasOpen = true;
        }

        private void RenderDetails(QuestDefinition quest, IAdventureProgressReader progress)
        {
            _selectedTitle.text = quest == null ? _sheet.Text("board.choose") : quest.Name;
            _description.text = quest?.Description ?? string.Empty;
            _kind.text = quest == null ? string.Empty : KindText(quest.Kind);
            _progress.text = quest == null ? string.Empty
                : Format("board.progress_format", QuestRules.GetProgress(quest, progress), quest.RequiredCount);
            _reward.text = quest == null ? string.Empty : Format("board.reward_format", quest.RewardGold);
            _delivery.text = string.Empty;
            if (quest != null && quest.Kind == QuestKind.DeliverItems)
            {
                int owned = progress.Items.TryGetValue(quest.TargetId, out int count) ? count : 0;
                _delivery.text = Format("board.delivery_format", quest.TargetName, quest.RequiredCount, owned)
                    + "\n" + _sheet.Text("board.delivery_notice");
            }
        }

        private void RenderStatus(QuestDefinition selected, IAdventureProgressReader progress, string status, bool canWrite)
        {
            _status.color = !canWrite || status == _sheet.Text("status.save_failed") ? F.Error : F.Muted;
            if (!string.IsNullOrEmpty(status))
            {
                _status.text = status;
            }
            else if (!canWrite)
            {
                _status.text = _sheet.Text("status.load_failed");
            }
            else if (selected == null)
            {
                _status.text = _sheet.Text("board.choose_hint");
            }
            else if (progress.Quest.IsCompleted(selected.Id))
            {
                _status.text = _sheet.Text("board.completed_hint");
            }
            else if (QuestRules.CanReport(selected, progress))
            {
                _status.text = _sheet.Text("board.report_hint");
            }
            else if (selected.Id == progress.Quest.ActiveQuestId)
            {
                _status.text = selected.Kind == QuestKind.DeliverItems ? Format("board.delivery_hint_format", selected.TargetName)
                    : _sheet.Text("board.progress_hint");
            }
            else if (QuestRules.CanAccept(selected, progress))
            {
                _status.text = selected.Kind == QuestKind.DeliverItems ? Format("board.delivery_hint_format", selected.TargetName)
                    : _sheet.Text("board.accept_hint");
            }
            else
            {
                _status.text = _sheet.Text("status.already_active");
            }
        }

        private string KindText(QuestKind kind)
        {
            return _sheet.Text(kind switch
            {
                QuestKind.ClearDungeon => "board.kind_clear_dungeon",
                QuestKind.DeliverItems => "board.kind_deliver_items",
                QuestKind.RescueAdventurers => "board.kind_rescue_adventurers",
                QuestKind.KillMonster => "board.kind_kill_monster",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
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
            foreach (var card in _cards)
            {
                if (card.Button != null)
                {
                    card.Button.onClick.RemoveAllListeners();
                    card.Button.gameObject.SetActive(false);
                    Destroy(card.Button.gameObject);
                }
            }
            _cards.Clear();
            _input = null;
            _context = null;
            _selectedId = null;
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

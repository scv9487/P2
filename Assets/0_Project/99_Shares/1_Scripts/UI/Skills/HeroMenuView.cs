using System;
using System.Collections.Generic;
using Refactor.Main;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using F = Refactor.UI.AdventureUIFactory;

namespace Refactor.UI
{
    // 가방과 기술창은 저장된 원천 데이터의 조회와 입력 의도 표시만 담당한다.
    public sealed partial class HeroMenuView : MonoBehaviour
    {
        private Font _font;
        private CanvasGroup _controls;
        private RectTransform _inventoryPanel;
        private RectTransform _skillsPanel;
        private Image _bagPortrait;
        private Text _bagTitle;
        private Text _bagOwner;
        private Text _bagClass;
        private Text _bagWeapon;
        private Text _bagGold;
        private Text _bagItemsHeading;
        private Text _bagEmpty;
        private Text _bagNote;
        private RectTransform _bagItems;
        private Text _skillsTitle;
        private Text _skillsIdentity;
        private Text _points;
        private Text _activeHeading;
        private Text _passiveHeading;
        private RectTransform _activeList;
        private RectTransform _passiveList;
        private Text _skillTitle;
        private Text _skillDescription;
        private Text _skillRequirements;
        private Text _skillExecution;
        private RectTransform _skillDetailsContent;
        private Text _status;
        private Button _bagClose;
        private Button _skillsClose;
        private Button _learn;
        private readonly List<(Button Button, UnityAction Action)> _bindings = new();

        public static HeroMenuView Create(Transform canvas, Font font)
        {
            var root = F.Stretch("HeroMenuRoot", canvas);
            F.Image(root, new Color(0.01f, 0.015f, 0.02f, 0.94f), true);
            var view = root.gameObject.AddComponent<HeroMenuView>();
            view._font = font != null ? font : throw new ArgumentNullException(nameof(font));
            view.Build();
            root.gameObject.SetActive(false);
            return view;
        }

        private void Build()
        {
            _controls = gameObject.AddComponent<CanvasGroup>();
            BuildInventory();
            BuildSkills();
        }

        private void BuildInventory()
        {
            _inventoryPanel = F.Center("BagPanel", transform, 1080, 620);
            _inventoryPanel.anchoredPosition = new Vector2(0, 55);
            F.Image(_inventoryPanel, F.Panel, true);
            F.Image(F.Rect("TopRule", _inventoryPanel, 0, 0, 1080, 1), F.Gold);
            var portrait = F.Rect("PortraitClip", _inventoryPanel, 24, 24, 88, 112);
            F.Image(portrait, Color.white);
            portrait.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _bagPortrait = F.Image(F.Stretch("BagPortrait", portrait), Color.white);
            _bagPortrait.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            _bagTitle = F.Text("BagTitleText", _inventoryPanel, _font, 132, 17, 510, 50, 28, F.PaleGold);
            _bagOwner = F.Text("BagOwnerText", _inventoryPanel, _font, 132, 70, 510, 28, 17, F.White);
            _bagClass = F.Text("BagClassText", _inventoryPanel, _font, 132, 105, 250, 28, 15, F.Muted);
            _bagWeapon = F.Text("BagWeaponText", _inventoryPanel, _font, 382, 105, 260, 28, 15, F.Muted);
            _bagGold = F.Text("BagGoldText", _inventoryPanel, _font, 670, 35, 386, 66, 24, F.PaleGold);
            _bagGold.alignment = TextAnchor.MiddleRight;
            _bagItemsHeading = F.Text("BagItemsHeadingText", _inventoryPanel, _font, 24, 158, 584, 34, 19, F.PaleGold);
            _bagItems = Scroll("BagItems", _inventoryPanel, 24, 200, 584, 318);
            _bagEmpty = F.Text("BagEmptyText", _bagItems, _font, 16, 16, 534, 60, 18, F.Muted);
            _bagNote = F.Text("BagStatusText", _inventoryPanel, _font, 24, 528, 1032, 37, 14, F.Muted);
            _bagClose = F.Button("HeroMenuCloseButton", _inventoryPanel, _font, 824, 570, 232, 38);
            BuildItemDetails();
        }

        private void BuildSkills()
        {
            _skillsPanel = F.Center("SkillsPanel", transform, 1160, 640);
            _skillsPanel.anchoredPosition = new Vector2(0, 55);
            F.Image(_skillsPanel, F.Panel, true);
            F.Image(F.Rect("TopRule", _skillsPanel, 0, 0, 1160, 1), F.Gold);
            _skillsTitle = F.Text("SkillsTitleText", _skillsPanel, _font, 24, 14, 1112, 50, 29, F.PaleGold);
            _skillsIdentity = F.Text("SkillsIdentityText", _skillsPanel, _font, 24, 76, 682, 34, 17, F.White);
            _points = F.Text("SkillPointsText", _skillsPanel, _font, 720, 76, 416, 34, 17, F.PaleGold);
            _points.alignment = TextAnchor.MiddleRight;
            _activeHeading = F.Text("ActiveSkillsHeadingText", _skillsPanel, _font, 24, 130, 310, 34, 18, F.PaleGold);
            _passiveHeading = F.Text("PassiveSkillsHeadingText", _skillsPanel, _font, 358, 130, 310, 34, 18, F.PaleGold);
            _activeList = Scroll("ActiveSkillTree", _skillsPanel, 24, 174, 310, 348);
            _passiveList = Scroll("PassiveSkillList", _skillsPanel, 358, 174, 310, 348);
            var detail = F.Rect("SkillDetailPanel", _skillsPanel, 692, 130, 444, 392);
            F.Image(detail, new Color(0.035f, 0.047f, 0.05f));
            _skillTitle = F.Text("SkillTitleText", detail, _font, 20, 8, 404, 58, 24, F.PaleGold);
            _skillDetailsContent = Scroll("SkillDetails", detail, 12, 76, 420, 304);
            _skillDescription = F.Text("SkillDescriptionText", _skillDetailsContent, _font, 8, 8, 388, 141, 16, F.White);
            _skillDescription.alignment = TextAnchor.UpperLeft;
            _skillRequirements = F.Text("SkillRequirementsText", _skillDetailsContent, _font, 8, 166, 388, 76, 14, F.PaleGold);
            _skillExecution = F.Text("SkillExecutionText", _skillDetailsContent, _font, 8, 252, 388, 66, 13, F.Muted);
            _status = F.Text("SkillStatusText", _skillsPanel, _font, 24, 534, 1112, 45, 15, F.Muted);
            _skillsClose = F.Button("HeroMenuCloseButton", _skillsPanel, _font, 24, 588, 176, 40);
            _learn = F.Button("SkillLearnButton", _skillsPanel, _font, 904, 588, 232, 40, true);
        }

        private static RectTransform Scroll(string name, Transform parent, float x, float y, float width, float height)
        {
            var content = F.ScrollContent(name, parent, x, y, width, height);
            var viewport = (RectTransform)content.parent.parent;
            var track = F.Rect("Scrollbar", viewport, width - 10, 4, 8, height - 8);
            F.Image(track, new Color(0.12f, 0.13f, 0.135f));
            var handle = F.Stretch("Handle", track);
            var graphic = F.Image(handle, F.Gold, true);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = graphic;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            viewport.GetComponent<ScrollRect>().verticalScrollbar = scrollbar;
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width - 16);
            return content;
        }
    }
}

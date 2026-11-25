using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GachaTrainers
{
    public sealed class BattleSceneView : MonoBehaviour
    {
        [SerializeField] private BattleActorView _playerActor;
        [SerializeField] private BattleActorView _enemyActor;
        [SerializeField] private SpriteRenderer _projectileTemplate;
        [SerializeField] private Text _playerName;
        [SerializeField] private Text _enemyName;
        [SerializeField] private Text _playerHealth;
        [SerializeField] private Text _enemyHealth;
        [SerializeField] private Text _playerPassiveText;
        [SerializeField] private Text _enemyPassiveText;
        [SerializeField] private Image _playerHealthFill;
        [SerializeField] private Image _enemyHealthFill;
        [SerializeField] private Text[] _playerRosterTexts;
        [SerializeField] private Text[] _enemyRosterTexts;
        [SerializeField] private Image[] _playerRosterPanels;
        [SerializeField] private Image[] _enemyRosterPanels;
        [SerializeField] private Button[] _playerRosterButtons;
        [SerializeField] private Text _phaseText;
        [SerializeField] private Text _timerText;
        [SerializeField] private Text _statusText;
        [SerializeField] private CanvasGroup _battleContent;
        [SerializeField] private Button _attackButton;
        [SerializeField] private Button _specialButton;
        [SerializeField] private Button _dodgeButton;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Button _controlModeButton;
        [SerializeField] private Text _controlModeLabel;
        [SerializeField] private Button _pauseControlButton;
        [SerializeField] private Text _pauseControlLabel;
        [SerializeField] private Text _controlsText;
        [SerializeField] private Text _attackLabel;
        [SerializeField] private Text _specialLabel;
        [SerializeField] private Text _dodgeLabel;
        [SerializeField] private GameObject _overlay;
        [SerializeField] private GameObject _pausePanel;
        [SerializeField] private GameObject _resultPanel;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _leaveButton;
        [SerializeField] private Button _finishButton;
        [SerializeField] private Text _resultTitle;
        [SerializeField] private Text _resultDescription;
        [SerializeField] private Text _finishLabel;
        [SerializeField] private SpriteRenderer _aimMarker;
        private readonly List<(Button button, UnityAction action)> _bindings = new();
        private readonly Dictionary<int, SpriteRenderer> _projectileViews = new();
        private readonly Stack<SpriteRenderer> _projectilePool = new();
        private readonly HashSet<int> _liveProjectileIds = new();
        private readonly List<int> _expiredProjectileIds = new();
        private static readonly Color Mint = new(0.37f, 0.93f, 0.76f);
        private static readonly Color Violet = new(0.73f, 0.63f, 1f);
        private static readonly Color Muted = new(0.57f, 0.66f, 0.73f);

        public void Bind(Action<GachaAction, int> enqueue)
        {
            Unbind();
            Bind(_attackButton, () => enqueue(GachaAction.BattleAttack, 0));
            Bind(_specialButton, () => enqueue(GachaAction.BattleSpecial, 0));
            Bind(_dodgeButton, () => enqueue(GachaAction.BattleDodge, 0));
            Bind(_pauseButton, () => enqueue(GachaAction.BattlePause, 0));
            Bind(_resumeButton, () => enqueue(GachaAction.ResumeBattle, 0));
            Bind(_leaveButton, () => enqueue(GachaAction.LeaveBattle, 0));
            Bind(_finishButton, () => enqueue(GachaAction.FinishBattle, 0));
            Bind(_controlModeButton, () => enqueue(GachaAction.ToggleBattleControl, 0));
            Bind(_pauseControlButton, () => enqueue(GachaAction.ToggleBattleControl, 0));
            for (int index = 0; index < _playerRosterButtons.Length; index++)
            {
                int slot = index;
                Bind(_playerRosterButtons[index], () => enqueue(GachaAction.SwitchBattleCreature, slot));
            }
        }

        private void Bind(Button button, UnityAction action)
        {
            button.onClick.AddListener(action);
            _bindings.Add((button, action));
        }

        public void Unbind()
        {
            foreach (var binding in _bindings)
            {
                if (binding.button != null)
                {
                    binding.button.onClick.RemoveListener(binding.action);
                }
            }
            _bindings.Clear();
        }

        public void Render(IReadOnlyRunData run, IReadOnlyBattleData battle)
        {
            if (battle == null || battle.Player == null || battle.Enemy == null)
            {
                return;
            }
            _playerActor.Render(battle.Player, battle.Phase, battle.PhaseTime, battle.ElapsedTime);
            _enemyActor.Render(battle.Enemy, battle.Phase, battle.PhaseTime, battle.ElapsedTime);
            RenderProjectiles(battle.Projectiles);
            _playerName.text = battle.Player.Name + "  R" + battle.Player.Form.Rank;
            _enemyName.text = battle.Enemy.Name + "  R" + battle.Enemy.Form.Rank;
            _playerHealth.text = Mathf.CeilToInt(battle.Player.Health) + " / " + battle.Player.MaxHealth;
            _enemyHealth.text = Mathf.CeilToInt(battle.Enemy.Health) + " / " + battle.Enemy.MaxHealth;
            _playerHealthFill.fillAmount = battle.Player.Health / battle.Player.MaxHealth;
            _enemyHealthFill.fillAmount = battle.Enemy.Health / battle.Enemy.MaxHealth;
            RenderPassive(battle.Player, _playerPassiveText);
            RenderPassive(battle.Enemy, _enemyPassiveText);
            RenderRoster(battle.PlayerTeam, battle.Player, _playerRosterTexts, _playerRosterPanels, true);
            RenderRoster(battle.EnemyTeam, battle.Enemy, _enemyRosterTexts, _enemyRosterPanels, false);
            int seconds = Mathf.FloorToInt(battle.ElapsedTime);
            _timerText.text = "STAGE " + battle.StageNumber + "/" + GachaStageSheet.StageCount + "  ·  "
                + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            _phaseText.text = battle.Paused ? "PAUSED" : battle.Phase switch
            {
                BattlePhase.Summoning => "소환 중",
                BattlePhase.Fighting => "실시간 대전",
                BattlePhase.Victory => "VICTORY",
                BattlePhase.Defeat => "DEFEAT",
                _ => "DRAW"
            };
            _statusText.text = battle.Status;
            bool ended = battle.Phase == BattlePhase.Victory || battle.Phase == BattlePhase.Defeat || battle.Phase == BattlePhase.Draw;
            bool automatic = battle.ControlMode == BattleControlMode.Auto;
            bool canFight = !battle.Paused && battle.Phase == BattlePhase.Fighting && battle.Player.Alive;
            _battleContent.interactable = !battle.Paused && !ended;
            _battleContent.blocksRaycasts = !battle.Paused && !ended;
            _attackButton.interactable = canFight && !automatic && battle.Player.BasicCooldown <= 0f;
            float skillCost = battle.Player.Balance.Number("Battle.SkillEnergyCost");
            _specialButton.interactable = canFight && !automatic && battle.Player.SpecialCooldown <= 0f && battle.Player.Energy >= skillCost;
            _dodgeButton.interactable = canFight && !automatic && battle.Player.DodgeCooldown <= 0f;
            _controlModeButton.interactable = !ended;
            _pauseControlButton.interactable = !ended;
            _controlModeLabel.text = automatic ? "자동 전투  ·  TAB" : "직접 조작  ·  TAB";
            _pauseControlLabel.text = automatic ? "자동 전투 중  ·  직접 조작으로 전환" : "직접 조작 중  ·  자동 전투로 전환";
            _controlModeButton.GetComponent<Image>().color = automatic ? new Color(0.29f, 0.23f, 0.40f) : new Color(0.15f, 0.30f, 0.32f);
            _controlsText.text = automatic
                ? "AI가 이동 · 공격 · 스킬 · 회피     |     1 · 2 · 3 크리쳐 교체     |     TAB 직접 조작     |     ESC 일시정지"
                : "WASD 이동 · 마우스 조준 · 좌클릭 공격 · 우클릭 스킬 (기력 " + skillCost.ToString("0.##") + ") · SPACE 회피 · 1 / 2 / 3 교체 · TAB 자동";
            for (int index = 0; index < _playerRosterButtons.Length; index++)
            {
                _playerRosterButtons[index].interactable = canFight && index < battle.PlayerTeam.Count
                    && battle.PlayerTeam[index].Alive && index != battle.ActivePlayerIndex;
            }
            var moves = BattleCatalog.Get(battle.Player.Kind, battle.Player.Balance);
            _attackLabel.text = CooldownLabel("좌클릭  ·  " + moves.BasicName, battle.Player.BasicCooldown);
            _specialLabel.text = CooldownLabel("우클릭  ·  " + moves.SpecialName, battle.Player.SpecialCooldown);
            _specialLabel.text += " · 기력 " + Mathf.FloorToInt(battle.Player.Energy) + "/" + battle.Player.Stats.MaxEnergy;
            if (_aimMarker != null)
            {
                _aimMarker.enabled = !automatic && !battle.Paused && !ended && battle.AimPoint.HasValue;
                if (battle.AimPoint.HasValue)
                {
                    _aimMarker.transform.position = battle.AimPoint.Value;
                }
            }
            _dodgeLabel.text = CooldownLabel("SPACE  ·  회피", battle.Player.DodgeCooldown);
            _overlay.SetActive(battle.Paused || ended);
            _pausePanel.SetActive(battle.Paused && !ended);
            _resultPanel.SetActive(ended);
            if (ended)
            {
                bool victory = battle.Phase == BattlePhase.Victory;
                bool completed = victory && battle.StageNumber == GachaStageSheet.StageCount;
                _resultTitle.text = completed ? "10개 스테이지 완료!" : victory ? "스테이지 " + battle.StageNumber + " 승리!"
                    : battle.Phase == BattlePhase.Draw ? "무승부" : "이번 모험은 여기까지";
                _resultTitle.color = victory ? Mint : Violet;
                _resultDescription.text = victory
                    ? completed ? "마지막 상대까지 모두 쓰러뜨렸습니다. 모험 성공!\n상금 " + battle.Player.Balance.VictoryGold(battle.StageNumber) + " G 획득 · 새 모험은 스테이지 1부터 시작합니다."
                        : "상대 크리쳐 " + battle.EnemyTeam.Count + "마리를 모두 쓰러뜨렸습니다.\n상금 " + battle.Player.Balance.VictoryGold(battle.StageNumber) + " G를 받고 스테이지 " + (battle.StageNumber + 1) + "을 준비하세요."
                    : "새로운 동료와 수정으로 다시 도전해 보세요.\n타이틀로 돌아가면 새 모험을 시작할 수 있습니다.";
                _finishLabel.text = completed ? "모험 완료 · 타이틀로   →" : victory ? "상금 받고 가챠샵으로   →" : "타이틀로 돌아가기";
            }
        }

        private static string CooldownLabel(string label, float cooldown)
        {
            return label + "\n" + (cooldown > 0.05f ? cooldown.ToString("0.0") + "초" : "준비 완료");
        }

        private static void RenderPassive(BattleFighter fighter, Text label)
        {
            string text = "수정 없음";
            label.color = Muted;
            if (fighter.EquippedCrystal.HasValue)
            {
                var crystal = GachaCatalog.GetCrystal(fighter.EquippedCrystal.Value, fighter.Balance);
                text = "[" + GachaCatalog.GetRarityName(crystal.Rarity) + "] " + crystal.Name;
                label.color = GachaSceneView.CrystalColor(crystal.Kind);
            }
            if (fighter.ShieldHealth > 0f)
            {
                text += "   ·   보호막 " + Mathf.CeilToInt(fighter.ShieldHealth);
            }
            if (fighter.BurnTime > 0f)
            {
                text += "   ·   연소 " + fighter.BurnTime.ToString("0.0") + "초";
            }
            if (fighter.SlowTime > 0f)
            {
                text += "   ·   감속 " + fighter.SlowTime.ToString("0.0") + "초";
            }
            if (fighter.IronWallTime > 0f)
            {
                text += " · 철벽 " + fighter.IronWallTime.ToString("0.0") + "초";
            }
            if (fighter.HitFlashTime > 0f)
            {
                text += " · " + fighter.LastHitResult;
            }
            label.text = text;
        }

        private static void RenderRoster(IReadOnlyList<BattleFighter> team, BattleFighter active, Text[] labels, Image[] panels, bool player)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                if (i >= team.Count)
                {
                    labels[i].text = "빈 슬롯";
                    labels[i].color = Muted;
                    panels[i].color = new Color(0.08f, 0.12f, 0.19f);
                    continue;
                }
                var fighter = team[i];
                bool current = ReferenceEquals(fighter, active);
                labels[i].text = (player ? "[" + (i + 1) + "] " : "") + fighter.Name + " R" + fighter.Form.Rank + "\n"
                    + (fighter.Alive ? (current ? "전투 중  ·  " : "대기  ·  ") + Mathf.CeilToInt(fighter.Health) + " HP" : "전투 불능");
                labels[i].color = fighter.Alive ? Color.white : Muted;
                panels[i].color = current && fighter.Alive
                    ? (player ? new Color(0.16f, 0.32f, 0.33f) : new Color(0.29f, 0.23f, 0.40f))
                    : new Color(0.10f, 0.15f, 0.23f);
            }
        }

        private void RenderProjectiles(IReadOnlyList<BattleProjectile> projectiles)
        {
            _liveProjectileIds.Clear();
            foreach (var projectile in projectiles)
            {
                _liveProjectileIds.Add(projectile.Id);
                if (!_projectileViews.TryGetValue(projectile.Id, out var visual))
                {
                    visual = _projectilePool.Count > 0 ? _projectilePool.Pop() : Instantiate(_projectileTemplate, _projectileTemplate.transform.parent);
                    visual.gameObject.SetActive(true);
                    _projectileViews.Add(projectile.Id, visual);
                }
                visual.transform.position = new Vector3(projectile.Position.x, projectile.Position.y, 0f);
                visual.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(projectile.Direction.y, projectile.Direction.x) * Mathf.Rad2Deg);
                float diameter = Mathf.Max(projectile.Radius * 2f, 0.16f);
                float lengthScale = projectile.Kind switch
                {
                    CreatureKind.VoltRabbit => 2.4f,
                    CreatureKind.FrostOwl => 2.7f,
                    CreatureKind.TideOtter => 1.1f,
                    CreatureKind.StoneBoar => 0.85f,
                    _ => 1.4f
                };
                visual.transform.localScale = new Vector3(diameter * lengthScale, diameter, 1f);
                visual.color = GachaSceneView.CreatureColor(projectile.Kind);
            }
            _expiredProjectileIds.Clear();
            foreach (var pair in _projectileViews)
            {
                if (!_liveProjectileIds.Contains(pair.Key))
                {
                    pair.Value.gameObject.SetActive(false);
                    _projectilePool.Push(pair.Value);
                    _expiredProjectileIds.Add(pair.Key);
                }
            }
            foreach (int id in _expiredProjectileIds)
            {
                _projectileViews.Remove(id);
            }
        }
    }
}

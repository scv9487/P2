using System;
using System.Collections.Generic;
using UnityEngine;

namespace GachaTrainers
{
    public enum BattleControlMode
    {
        Manual,
        Auto
    }

    public enum BattlePhase
    {
        Summoning,
        Fighting,
        Victory,
        Defeat,
        Draw
    }

    public interface IReadOnlyBattleData
    {
        int StageNumber { get; }
        IReadOnlyList<BattleFighter> PlayerTeam { get; }
        IReadOnlyList<BattleFighter> EnemyTeam { get; }
        BattleFighter Player { get; }
        BattleFighter Enemy { get; }
        IReadOnlyList<BattleProjectile> Projectiles { get; }
        BattlePhase Phase { get; }
        BattleControlMode ControlMode { get; }
        int ActivePlayerIndex { get; }
        Vector2? AimPoint { get; }
        bool Paused { get; }
        float PhaseTime { get; }
        float ElapsedTime { get; }
        string Status { get; }
        int Revision { get; }
    }

    public sealed class BattleFighter
    {
        public GachaBalance Balance { get; }
        public CreatureKind Kind { get; }
        public CreatureForm Form { get; }
        public string Name => Form.Name;
        public CombatStats Stats { get; }
        public float Energy { get; private set; }
        public float IronWallTime { get; private set; }
        public string LastHitResult { get; private set; } = string.Empty;
        public int MaxHealth { get; }
        public float Health { get; private set; }
        public int Attack { get; }
        public float AttackSpeed { get; }
        public Vector2 Position { get; private set; }
        public Vector2 Facing { get; private set; }
        public float BasicCooldown { get; private set; }
        public float SpecialCooldown { get; private set; }
        public float DodgeCooldown { get; private set; }
        public float InvulnerableTime { get; private set; }
        public float ShieldTime { get; private set; }
        public float HitFlashTime { get; private set; }
        public CrystalKind? EquippedCrystal { get; }
        public float ShieldHealth { get; private set; }
        public float BarrierTime { get; private set; }
        public float BurnTime => (float)_burnTimeRemaining;
        public float SlowTime { get; private set; }
        public float MovementSpeedMultiplier => SlowTime > 0f ? Balance.Number("Battle.SlowMoveRatio") : 1f;
        public float PassiveCooldown { get; private set; }
        public int BasicHitProgress { get; private set; }
        public CreatureAnimationState AnimationState { get; private set; }
        public float AnimationTime { get; private set; }
        public bool Alive => Health > 0f;
        internal Vector2 DashDirection { get; private set; }
        internal float DashTime { get; private set; }
        private bool _isMoving;
        private float _attackAnimationRemaining;
        private float _skillAnimationRemaining;
        private float _dodgeAnimationRemaining;
        private float _hitAnimationRemaining;
        private double _burnTimeRemaining;
        private double _burnTickElapsed;

        public string PassiveStatus => EquippedCrystal switch
        {
            CrystalKind.Ember => "불씨 " + BasicHitProgress + "/" + Balance.Integer("Crystal.Ember.HitCount") + " 명중",
            CrystalKind.Protection => ShieldHealth > 0f ? "보호막 " + Mathf.CeilToInt(ShieldHealth) + " · " + BarrierTime.ToString("0.0") + "초"
                : PassiveCooldown > 0f ? "보호 대기 " + PassiveCooldown.ToString("0.0") + "초" : "보호 · 출전 시 발동",
            CrystalKind.Reflection => "직접 피해 반사 " + (Balance.Number("Crystal.Reflection.Ratio") * 100f).ToString("0.##") + "%",
            CrystalKind.DeathThroes => "발악 피해 +" + Mathf.RoundToInt(MissingHealthRatio * Balance.Number("Crystal.DeathThroes.MaxBonus") * 100f) + "%",
            CrystalKind.Sink => "침몰 " + BasicHitProgress + "/" + Balance.Integer("Crystal.Sink.HitCount") + " 명중",
            CrystalKind.Capital => "사형 · 체력 " + (Balance.Number("Crystal.Capital.Threshold") * 100f).ToString("0.##") + "% 이하 처형",
            _ => EquippedCrystal.HasValue ? GachaCatalog.GetCrystal(EquippedCrystal.Value, Balance).Description : "수정 없음"
        };

        internal float MissingHealthRatio => Mathf.Clamp01(1f - Health / MaxHealth);
        internal float DirectDamageMultiplier => EquippedCrystal == CrystalKind.DeathThroes ? 1f + MissingHealthRatio * Balance.Number("Crystal.DeathThroes.MaxBonus") : 1f;

        internal BattleFighter(PartyCreature creature, CombatStats stats = null)
        {
            Balance = creature.Balance;
            Kind = creature.Kind;
            Form = creature.Form;
            Stats = stats ?? creature.Stats;
            Energy = Stats.MaxEnergy;
            MaxHealth = Stats.MaxHealth;
            Health = MaxHealth;
            Attack = Mathf.RoundToInt(Stats.PhysicalAttackPower);
            AttackSpeed = Stats.AttackSpeed;
            EquippedCrystal = creature.EquippedCrystal;
        }

        internal void Place(Vector2 position, Vector2 facing)
        {
            Position = position;
            Facing = facing;
            DashTime = 0f;
            InvulnerableTime = 0f;
            ResetAnimation();
        }

        internal void Bench()
        {
            DashTime = 0f;
            InvulnerableTime = 0f;
            HitFlashTime = 0f;
            ResetAnimation();
        }

        private void ResetAnimation()
        {
            _isMoving = false;
            _attackAnimationRemaining = 0f;
            _skillAnimationRemaining = 0f;
            _dodgeAnimationRemaining = 0f;
            _hitAnimationRemaining = 0f;
            AnimationState = CreatureAnimationState.Idle;
            AnimationTime = 0f;
        }

        internal void Face(Vector2 target)
        {
            Vector2 direction = target - Position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                Facing = direction.normalized;
            }
        }

        internal void Move(Vector2 delta)
        {
            Position = new Vector2(Mathf.Clamp(Position.x + delta.x, BattleCatalog.MinX, BattleCatalog.MaxX),
                Mathf.Clamp(Position.y + delta.y, BattleCatalog.MinY, BattleCatalog.MaxY));
        }

        internal void TickTimers(float step, bool active = true)
        {
            BasicCooldown = Mathf.Max(0f, BasicCooldown - step);
            SpecialCooldown = Mathf.Max(0f, SpecialCooldown - step);
            DodgeCooldown = Mathf.Max(0f, DodgeCooldown - step);
            InvulnerableTime = Mathf.Max(0f, InvulnerableTime - step);
            ShieldTime = Mathf.Max(0f, ShieldTime - step);
            HitFlashTime = Mathf.Max(0f, HitFlashTime - step);
            DashTime = Mathf.Max(0f, DashTime - step);
            PassiveCooldown = Mathf.Max(0f, PassiveCooldown - step);
            BarrierTime = Mathf.Max(0f, BarrierTime - step);
            SlowTime = Mathf.Max(0f, SlowTime - step);
            IronWallTime = Mathf.Max(0f, IronWallTime - step);
            if (active)
            {
                Heal(MaxHealth * Stats.HealthPercentageRegeneration * 0.01f * step);
                Energy = Mathf.Min(Stats.MaxEnergy, Energy + Stats.EnergyRegeneration * step);
            }
            if (BarrierTime <= 0f)
            {
                ShieldHealth = 0f;
            }
            if (!active)
            {
                ResetAnimation();
                return;
            }
            _attackAnimationRemaining = Mathf.Max(0f, _attackAnimationRemaining - step);
            _skillAnimationRemaining = Mathf.Max(0f, _skillAnimationRemaining - step);
            _dodgeAnimationRemaining = Mathf.Max(0f, _dodgeAnimationRemaining - step);
            _hitAnimationRemaining = Mathf.Max(0f, _hitAnimationRemaining - step);
            if (CreatureAnimationCatalog.Loops(AnimationState))
            {
                AnimationTime = (AnimationTime + step) % CreatureAnimationCatalog.Duration(AnimationState);
            }
            RefreshAnimation();
        }

        internal void BeginBasic()
        {
            BasicCooldown = 1f / AttackSpeed;
            _attackAnimationRemaining = CreatureAnimationCatalog.Duration(CreatureAnimationState.Attack);
            RefreshAnimation();
        }

        internal void BeginSpecial()
        {
            SpecialCooldown = BattleCatalog.Get(Kind, Balance).SpecialCooldown / Mathf.Max(0.1f, 1f + Stats.CooldownReduction * 0.01f);
            Energy = Mathf.Max(0f, Energy - Balance.Number("Battle.SkillEnergyCost"));
            _skillAnimationRemaining = CreatureAnimationCatalog.Duration(CreatureAnimationState.Skill);
            RefreshAnimation();
        }

        internal void BeginDodge(Vector2 direction, bool consumeCooldown)
        {
            DashDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Facing;
            DashTime = Balance.Number("Battle.DashSeconds");
            InvulnerableTime = Balance.Number("Battle.DodgeInvulnerability");
            if (consumeCooldown)
            {
                DodgeCooldown = Balance.Number("Battle.DodgeCooldown");
            }
            _dodgeAnimationRemaining = CreatureAnimationCatalog.Duration(CreatureAnimationState.Dodge);
            RefreshAnimation();
        }

        internal void BeginShield()
        {
            ShieldTime = Balance.Creature(CreatureKind.MossTurtle, "ShieldSeconds");
        }

        internal void ApplySlow()
        {
            if (Alive)
            {
                SlowTime = Balance.Number("Battle.SlowSeconds");
            }
        }

        internal void Heal(float amount)
        {
            if (Alive && float.IsFinite(amount) && amount > 0f)
            {
                Health = Mathf.Min(MaxHealth, Health + amount);
            }
        }

        internal float ReceiveDamage(float damage, bool ignoreInvulnerability = false)
        {
            if (!Alive || (!ignoreInvulnerability && InvulnerableTime > 0f) || !float.IsFinite(damage) || damage <= 0f)
            {
                return 0f;
            }
            damage *= (ShieldTime > 0f ? 1f - Balance.Creature(CreatureKind.MossTurtle, "ShieldReduction") : 1f)
                * (IronWallTime > 0f ? 1f - Balance.Number("Crystal.Elasticity.Reduction") : 1f);
            float absorbed = Mathf.Min(ShieldHealth, damage);
            ShieldHealth -= absorbed;
            float previousHealth = Health;
            Health = Mathf.Max(0f, Health - (damage - absorbed));
            HitFlashTime = 0.16f;
            _hitAnimationRemaining = CreatureAnimationCatalog.Duration(CreatureAnimationState.Hit);
            RefreshAnimation();
            return previousHealth - Health;
        }

        internal void RecordHit(string result)
        {
            LastHitResult = result;
        }

        internal void ActivateElasticity()
        {
            Heal((MaxHealth - Health) * Balance.Number("Crystal.Elasticity.HealRatio"));
            IronWallTime = Balance.Number("Crystal.Elasticity.Seconds");
        }

        internal bool RegisterBasicHit()
        {
            if (EquippedCrystal != CrystalKind.Ember && EquippedCrystal != CrystalKind.Sink)
            {
                return false;
            }
            BasicHitProgress = (BasicHitProgress + 1) % Balance.Integer(EquippedCrystal == CrystalKind.Ember ? "Crystal.Ember.HitCount" : "Crystal.Sink.HitCount");
            return BasicHitProgress == 0;
        }

        internal void ApplyBurn()
        {
            if (Alive)
            {
                if (_burnTimeRemaining <= 0d)
                {
                    _burnTickElapsed = 0d;
                }
                _burnTimeRemaining = Balance.Number("Battle.BurnSeconds");
            }
        }

        internal void TickBurn(float step, bool active)
        {
            // A shared double-precision elapsed budget keeps expiration from discarding the fourth tick.
            double elapsed = Math.Min(_burnTimeRemaining, step);
            _burnTimeRemaining = Math.Max(0d, _burnTimeRemaining - elapsed);
            _burnTickElapsed += elapsed;
            while (_burnTickElapsed + 0.000000001d >= Balance.Number("Battle.BurnInterval"))
            {
                _burnTickElapsed -= Balance.Number("Battle.BurnInterval");
                if (active)
                {
                    // Indirect damage cannot recursively trigger crystals, and dodge does not cleanse it.
                    ReceiveDamage(Balance.Number("Battle.BurnDamage"), true);
                }
            }
        }

        internal void TryActivateEntryPassive()
        {
            if (EquippedCrystal != CrystalKind.Protection || !Alive || PassiveCooldown > 0f)
            {
                return;
            }
            float barrier = (MaxHealth - Health) * Balance.Number("Crystal.Protection.ShieldRatio");
            if (barrier <= 0f)
            {
                return;
            }
            ShieldHealth = barrier;
            BarrierTime = Balance.Number("Crystal.Protection.Seconds");
            PassiveCooldown = Balance.Number("Crystal.Protection.Cooldown");
        }

        internal void Execute()
        {
            Health = 0f;
        }

        internal void UpdateMovementAnimation(bool moving)
        {
            _isMoving = moving;
            RefreshAnimation();
        }

        private void RefreshAnimation()
        {
            // Visual timers never lock movement, delay attacks, or change combat cooldowns.
            // A skill takes precedence over its own dodge (the rabbit's lightning dash).
            CreatureAnimationState next;
            float remaining;
            if (_hitAnimationRemaining > 0f)
            {
                next = CreatureAnimationState.Hit;
                remaining = _hitAnimationRemaining;
            }
            else if (_skillAnimationRemaining > 0f)
            {
                next = CreatureAnimationState.Skill;
                remaining = _skillAnimationRemaining;
            }
            else if (_dodgeAnimationRemaining > 0f)
            {
                next = CreatureAnimationState.Dodge;
                remaining = _dodgeAnimationRemaining;
            }
            else if (_attackAnimationRemaining > 0f)
            {
                next = CreatureAnimationState.Attack;
                remaining = _attackAnimationRemaining;
            }
            else
            {
                next = _isMoving ? CreatureAnimationState.Move : CreatureAnimationState.Idle;
                remaining = 0f;
            }

            if (CreatureAnimationCatalog.Loops(next))
            {
                if (next != AnimationState)
                {
                    AnimationTime = 0f;
                }
            }
            else
            {
                AnimationTime = CreatureAnimationCatalog.Duration(next) - remaining;
            }
            AnimationState = next;
        }
    }

    public sealed class BattleProjectile
    {
        public int Id { get; }
        public bool IsPlayer { get; }
        public Vector2 Position { get; private set; }
        public Vector2 Direction { get; }
        public float Radius { get; }
        public CreatureKind Kind { get; }
        internal float Speed { get; }
        internal float Damage { get; }
        internal float DistanceLeft { get; private set; }
        internal BattleFighter Source { get; }
        internal bool IsBasic { get; }

        internal BattleProjectile(int id, bool isPlayer, CreatureKind kind, Vector2 position,
            Vector2 direction, float speed, float damage, float radius, float range, BattleFighter source, bool isBasic)
        {
            Id = id;
            IsPlayer = isPlayer;
            Kind = kind;
            Position = position;
            Direction = direction.normalized;
            Speed = speed;
            Damage = damage;
            Radius = radius;
            DistanceLeft = range;
            Source = source;
            IsBasic = isBasic;
        }

        internal void Advance(float step)
        {
            float distance = Mathf.Min(DistanceLeft, Speed * step);
            Position += Direction * distance;
            DistanceLeft -= distance;
        }
    }

    // Owns combat state; simulation never depends on transforms, frame rendering or the scene.
    public sealed class BattleDataContainer : IReadOnlyBattleData
    {
        private const double FixedStep = 1.0 / 120.0;
        private readonly GachaBalance _balance;
        private readonly List<BattleFighter> _playerTeam = new();
        private readonly List<BattleFighter> _enemyTeam = new();
        private readonly List<BattleProjectile> _projectiles = new();
        private readonly List<(BattleFighter target, float damage)> _reflectedHits = new();
        private Vector2 _movement;
        private Vector2? _aimPoint;
        private readonly Func<float> _rollPercent;
        private bool _basicAttack;
        private int _playerIndex;
        private int _enemyIndex;
        private int _nextProjectileId;
        private double _accumulator;
        private double _elapsed;

        public int StageNumber { get; }
        public IReadOnlyList<BattleFighter> PlayerTeam { get; }
        public IReadOnlyList<BattleFighter> EnemyTeam { get; }
        public BattleFighter Player => _playerTeam[_playerIndex];
        public BattleFighter Enemy => _enemyTeam[_enemyIndex];
        public IReadOnlyList<BattleProjectile> Projectiles { get; }
        public BattlePhase Phase { get; private set; } = BattlePhase.Summoning;
        public BattleControlMode ControlMode { get; private set; }
        public int ActivePlayerIndex => _playerIndex;
        public Vector2? AimPoint => _aimPoint;
        public bool Paused { get; private set; }
        public float PhaseTime { get; private set; }
        public float ElapsedTime => (float)_elapsed;
        public string Status { get; private set; } = "양쪽 크리쳐를 소환합니다.";
        public int Revision { get; private set; }

        public BattleDataContainer(IReadOnlyList<PartyCreature> playerParty, bool autoPlayer = false, Func<float> rollPercent = null)
            : this(playerParty, new StageDefinition(1, 1f, 1f, 1f, new List<CreatureKind>(BattleCatalog.TestRoster)), autoPlayer, rollPercent)
        {
            // Neutral training fixture for isolated combat simulations. Scene gameplay always supplies the CSV stage.
        }

        public BattleDataContainer(IReadOnlyList<PartyCreature> playerParty, StageDefinition stage,
            bool autoPlayer = false, Func<float> rollPercent = null)
        {
            if (stage == null)
            {
                throw new ArgumentNullException(nameof(stage));
            }
            StageNumber = stage.Number;
            if (playerParty == null)
            {
                throw new ArgumentNullException(nameof(playerParty));
            }
            if (playerParty.Count == 0 || playerParty.Count > GachaCatalog.MaxPartySize)
            {
                throw new ArgumentException("전투 파티는 1~3마리여야 합니다.", nameof(playerParty));
            }
            _balance = playerParty[0]?.Balance ?? throw new ArgumentException("첫 크리쳐가 비어 있습니다.", nameof(playerParty));
            PhaseTime = _balance.Number("Battle.SummonSeconds");
            foreach (var creature in playerParty)
            {
                if (creature == null)
                {
                    throw new ArgumentException("비어 있는 크리쳐는 출전할 수 없습니다.", nameof(playerParty));
                }
                if (!ReferenceEquals(creature.Balance, _balance))
                {
                    throw new ArgumentException("출전 크리쳐의 밸런스 설정이 서로 다릅니다.", nameof(playerParty));
                }
                _playerTeam.Add(new BattleFighter(creature));
            }
            foreach (var kind in stage.Enemies)
            {
                var enemy = new PartyCreature(kind, _balance);
                _enemyTeam.Add(new BattleFighter(enemy, enemy.Stats.WithStageMultipliers(stage)));
            }
            var random = new System.Random();
            _rollPercent = rollPercent ?? (() => (float)random.NextDouble() * 100f);
            PlayerTeam = _playerTeam.AsReadOnly();
            EnemyTeam = _enemyTeam.AsReadOnly();
            Projectiles = _projectiles.AsReadOnly();
            ControlMode = autoPlayer ? BattleControlMode.Auto : BattleControlMode.Manual;
            PlaceActiveFighters();
        }

        public void SetControlMode(BattleControlMode mode)
        {
            if (mode != BattleControlMode.Manual && mode != BattleControlMode.Auto)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (IsFinished || ControlMode == mode)
            {
                return;
            }
            ControlMode = mode;
            ClearInput();
            Status = mode == BattleControlMode.Auto ? "자동 전투 · 크리쳐가 스스로 싸웁니다." : "직접 조작 · 이동하고 공격할 수 있습니다.";
            Revision++;
        }

        public bool TrySwitchPlayer(int index)
        {
            if (!CanAct || index < 0 || index >= _playerTeam.Count || index == _playerIndex || !_playerTeam[index].Alive)
            {
                return false;
            }
            Vector2 position = Player.Position;
            Vector2 facing = Player.Facing;
            Player.Bench();
            _playerIndex = index;
            Player.Place(position, facing);
            if (ControlMode == BattleControlMode.Auto)
            {
                Player.Face(Enemy.Position);
            }
            Player.TryActivateEntryPassive();
            ClearInput();
            Status = Player.Name + " 출전 · 체력과 재사용 시간은 유지됩니다.";
            Revision++;
            return true;
        }

        public void SetMovement(Vector2 direction)
        {
            _movement = ControlMode == BattleControlMode.Manual && float.IsFinite(direction.x) && float.IsFinite(direction.y)
                ? Vector2.ClampMagnitude(direction, 1f) : Vector2.zero;
        }

        public void SetAim(Vector2 point)
        {
            if (ControlMode == BattleControlMode.Manual && !Paused && float.IsFinite(point.x) && float.IsFinite(point.y))
            {
                _aimPoint = point;
                Player.Face(point);
            }
        }

        private void FaceForAttack(BattleFighter fighter, BattleFighter opponent, bool isPlayer)
        {
            if (!isPlayer || ControlMode == BattleControlMode.Auto)
            {
                fighter.Face(opponent.Position);
            }
            else if (_aimPoint.HasValue)
            {
                fighter.Face(_aimPoint.Value);
            }
        }

        public void SetBasicAttack(bool held)
        {
            _basicAttack = ControlMode == BattleControlMode.Manual && held;
        }

        public bool TryBasicAttack()
        {
            if (!CanManualAct || Player.BasicCooldown > 0f)
            {
                return false;
            }
            Basic(Player, Enemy, true);
            Revision++;
            return true;
        }

        public bool TrySpecial()
        {
            if (!CanManualAct || Player.SpecialCooldown > 0f || Player.Energy < _balance.Number("Battle.SkillEnergyCost"))
            {
                return false;
            }
            Special(Player, Enemy, true, _movement);
            Revision++;
            return true;
        }

        public bool TryDodge()
        {
            if (!CanManualAct || Player.DodgeCooldown > 0f)
            {
                return false;
            }
            Player.BeginDodge(_movement.sqrMagnitude > 0.01f ? _movement : -Player.Facing, true);
            Revision++;
            return true;
        }

        public void SetPaused(bool paused)
        {
            if (Paused != paused)
            {
                Paused = paused;
                ClearInput();
                _accumulator = 0;
                Revision++;
            }
        }

        public void ClearInput()
        {
            _movement = Vector2.zero;
            _basicAttack = false;
            _aimPoint = null;
        }

        public void Tick(float deltaTime)
        {
            if (Paused || IsFinished || !float.IsFinite(deltaTime) || deltaTime <= 0f)
            {
                return;
            }
            _accumulator += Math.Min(deltaTime, 0.25f);
            while (_accumulator + 0.00000001 >= FixedStep && !IsFinished)
            {
                _accumulator -= FixedStep;
                Step((float)FixedStep);
            }
        }

        private bool CanAct => !Paused && Phase == BattlePhase.Fighting && Player.Alive && Enemy.Alive;
        private bool CanManualAct => CanAct && ControlMode == BattleControlMode.Manual;
        private bool IsFinished => Phase == BattlePhase.Victory || Phase == BattlePhase.Defeat || Phase == BattlePhase.Draw;

        private void Step(float step)
        {
            _elapsed += FixedStep;
            Revision++;
            if (Phase == BattlePhase.Summoning)
            {
                PhaseTime = Mathf.Max(0f, PhaseTime - step);
                if (PhaseTime <= 0.0001f)
                {
                    Phase = BattlePhase.Fighting;
                    PhaseTime = 0f;
                    Status = "실시간 전투 시작!";
                }
                return;
            }

            Vector2 previousPlayerPosition = Player.Position;
            Vector2 previousEnemyPosition = Enemy.Position;
            foreach (var fighter in _playerTeam)
            {
                if (fighter.Alive)
                {
                    fighter.TickTimers(step, ReferenceEquals(fighter, Player));
                }
            }
            foreach (var fighter in _enemyTeam)
            {
                if (fighter.Alive)
                {
                    fighter.TickTimers(step, ReferenceEquals(fighter, Enemy));
                }
            }
            FaceForAttack(Player, Enemy, true);
            Enemy.Face(Player.Position);
            if (ControlMode == BattleControlMode.Auto)
            {
                ControlAI(Player, Enemy, true, step);
            }
            else
            {
                MoveFighter(Player, _movement, step);
                FaceForAttack(Player, Enemy, true);
                if (_basicAttack && Player.BasicCooldown <= 0f)
                {
                    Basic(Player, Enemy, true);
                }
            }
            ControlAI(Enemy, Player, false, step);
            SeparateFighters();
            UpdateProjectiles(step);
            foreach (var fighter in _playerTeam)
            {
                fighter.TickBurn(step, ReferenceEquals(fighter, Player));
            }
            foreach (var fighter in _enemyTeam)
            {
                fighter.TickBurn(step, ReferenceEquals(fighter, Enemy));
            }
            Player.UpdateMovementAnimation((Player.Position - previousPlayerPosition).sqrMagnitude > 0.00000001f);
            Enemy.UpdateMovementAnimation((Enemy.Position - previousEnemyPosition).sqrMagnitude > 0.00000001f);
            ResolveKnockouts();
        }

        private void ControlAI(BattleFighter fighter, BattleFighter opponent, bool isPlayer, float step)
        {
            var definition = BattleCatalog.Get(fighter.Kind, fighter.Balance);
            Vector2 toward = opponent.Position - fighter.Position;
            float distance = toward.magnitude;
            Vector2 direction = distance > 0.001f ? toward / distance : fighter.Facing;
            float preferred = definition.BasicRange * _balance.Number("AI.PreferredRangeRatio");
            float approach = distance > preferred ? 1f : distance < preferred * _balance.Number("AI.RetreatRangeRatio") ? -_balance.Number("AI.RetreatSpeed") : 0f;
            float strafe = Mathf.Sin(ElapsedTime * _balance.Number("AI.StrafeFrequency") + (isPlayer ? 0f : 2f)) * _balance.Number("AI.StrafeStrength");
            Vector2 movement = direction * approach + new Vector2(-direction.y, direction.x) * strafe;

            if (fighter.DodgeCooldown <= 0f && HasIncomingThreat(fighter, isPlayer))
            {
                Vector2 evasive = new(-direction.y, direction.x);
                if (fighter.Position.y > 1.4f)
                {
                    evasive.y = -Mathf.Abs(evasive.y);
                }
                else if (fighter.Position.y < -2.5f)
                {
                    evasive.y = Mathf.Abs(evasive.y);
                }
                fighter.BeginDodge(evasive, true);
            }
            MoveFighter(fighter, movement, step);
            if (distance <= definition.BasicRange && fighter.BasicCooldown <= 0f)
            {
                Basic(fighter, opponent, isPlayer);
            }
            bool needsHealing = fighter.Kind == CreatureKind.TideOtter && fighter.Health / fighter.MaxHealth <= _balance.Number("AI.HealThreshold");
            if ((distance <= definition.BasicRange * _balance.Number("AI.SkillRangeRatio") || needsHealing) && fighter.SpecialCooldown <= 0f && fighter.Energy >= _balance.Number("Battle.SkillEnergyCost"))
            {
                Special(fighter, opponent, isPlayer, movement);
            }
        }

        private bool HasIncomingThreat(BattleFighter fighter, bool isPlayer)
        {
            foreach (var projectile in _projectiles)
            {
                if (projectile.IsPlayer == isPlayer)
                {
                    continue;
                }
                Vector2 offset = fighter.Position - projectile.Position;
                if (offset.sqrMagnitude < _balance.Number("AI.ThreatDistanceSquared") && Vector2.Dot(offset, projectile.Direction) > 0f)
                {
                    return true;
                }
            }
            return false;
        }

        private static void MoveFighter(BattleFighter fighter, Vector2 movement, float step)
        {
            Vector2 velocity = fighter.DashTime > 0f
                ? fighter.DashDirection * fighter.Balance.Number("Battle.DashSpeed")
                : Vector2.ClampMagnitude(movement, 1f) * fighter.Stats.MovementSpeed * fighter.MovementSpeedMultiplier;
            fighter.Move(velocity * step);
        }

        private void SeparateFighters()
        {
            Vector2 delta = Enemy.Position - Player.Position;
            float distance = delta.magnitude;
            float separation = _balance.Number("Battle.FighterRadius") * 2f;
            if (distance < separation)
            {
                Vector2 push = (distance > 0.001f ? delta / distance : Vector2.right) * ((separation - distance) * 0.5f);
                Player.Move(-push);
                Enemy.Move(push);
            }
        }

        private void Basic(BattleFighter fighter, BattleFighter opponent, bool isPlayer)
        {
            FaceForAttack(fighter, opponent, isPlayer);
            fighter.BeginBasic();
            SpawnProjectile(fighter, isPlayer, fighter.Facing, fighter.Stats.PhysicalAttackPower + (fighter.EquippedCrystal == CrystalKind.Giant ? fighter.MaxHealth * _balance.Number("Crystal.Giant.HealthRatio") : 0f),
                _balance.Creature(fighter.Kind, "BasicRadius"), 1f, 1f, true);
        }

        private void Special(BattleFighter fighter, BattleFighter opponent, bool isPlayer, Vector2 movement)
        {
            FaceForAttack(fighter, opponent, isPlayer);
            fighter.BeginSpecial();
            float power = fighter.Stats.MagicAttackPower + (fighter.EquippedCrystal == CrystalKind.Divine
                ? fighter.MaxHealth * _balance.Number("Crystal.Divine.HealthRatio") : 0f);
            if (fighter.Kind == CreatureKind.MossTurtle)
            {
                fighter.BeginShield();
            }
            else if (fighter.Kind == CreatureKind.VoltRabbit)
            {
                fighter.BeginDodge(movement.sqrMagnitude > 0.01f ? movement : -fighter.Facing, false);
            }
            else if (fighter.Kind == CreatureKind.TideOtter)
            {
                fighter.Heal(fighter.MaxHealth * _balance.Creature(fighter.Kind, "HealRatio"));
            }
            float damage = power * _balance.Creature(fighter.Kind, "SkillDamage");
            float radius = _balance.Creature(fighter.Kind, "SkillRadius");
            float speed = _balance.Creature(fighter.Kind, "SkillSpeedScale");
            float range = _balance.Creature(fighter.Kind, "SkillRangeScale");
            if (fighter.Kind == CreatureKind.EmberFox)
            {
                foreach (float angle in _balance.Values("Creature.EmberFox.SkillAngles"))
                {
                    SpawnProjectile(fighter, isPlayer, Rotate(fighter.Facing, angle), damage, radius, speed, range);
                }
            }
            else
            {
                SpawnProjectile(fighter, isPlayer, fighter.Facing, damage, radius, speed, range);
            }
            Status = (isPlayer ? "아군 " : "상대 ") + fighter.Name + " · " + BattleCatalog.Get(fighter.Kind, fighter.Balance).SpecialName;
        }

        private void SpawnProjectile(BattleFighter fighter, bool isPlayer, Vector2 direction, float damage,
            float radius, float speedScale, float rangeScale, bool isBasic = false)
        {
            var definition = BattleCatalog.Get(fighter.Kind, fighter.Balance);
            _projectiles.Add(new BattleProjectile(++_nextProjectileId, isPlayer, fighter.Kind,
                fighter.Position + direction * _balance.Number("Battle.ProjectileSpawnOffset"), direction, definition.ProjectileSpeed * speedScale,
                damage * fighter.DirectDamageMultiplier, radius, definition.BasicRange * rangeScale, fighter, isBasic));
        }

        private void UpdateProjectiles(float step)
        {
            _reflectedHits.Clear();
            for (int index = _projectiles.Count - 1; index >= 0; index--)
            {
                var projectile = _projectiles[index];
                Vector2 before = projectile.Position;
                projectile.Advance(step);
                var target = projectile.IsPlayer ? Enemy : Player;
                if (SegmentHits(before, projectile.Position, target.Position, projectile.Radius + _balance.Number("Battle.FighterRadius")))
                {
                    ApplyDirectHit(projectile, target);
                    _projectiles.RemoveAt(index);
                }
                else if (projectile.DistanceLeft <= 0f || projectile.Position.x < BattleCatalog.MinX - 1f
                    || projectile.Position.x > BattleCatalog.MaxX + 1f || projectile.Position.y < BattleCatalog.MinY - 1f
                    || projectile.Position.y > BattleCatalog.MaxY + 1f)
                {
                    _projectiles.RemoveAt(index);
                }
            }
            // Resolve every direct impact before reflections and knockouts. A shot already in flight
            // still belongs to its original creature even if that creature was switched or defeated.
            foreach (var hit in _reflectedHits)
            {
                hit.target.ReceiveDamage(hit.damage, true);
                if (!ReferenceEquals(hit.target, Player) && !ReferenceEquals(hit.target, Enemy))
                {
                    hit.target.Bench();
                }
            }
        }

        private bool Chance(float percent)
        {
            return percent >= 100f || (percent > 0f && _rollPercent() < percent);
        }

        // DS: hit-minus-dodge, then guard, then critical; resistance = 100 / (100 + resistance).
        private void ApplyDirectHit(BattleProjectile projectile, BattleFighter target)
        {
            if (!target.Alive)
            {
                return;
            }
            BattleFighter source = projectile.Source;
            if (target.InvulnerableTime > 0f || !Chance(source.Stats.HitChance - target.Stats.DodgeChance))
            {
                target.RecordHit("회피");
                if (target.EquippedCrystal == CrystalKind.Jester)
                {
                    source.ApplySlow();
                }
                return;
            }
            bool guarded = Chance(target.Stats.GuardChance);
            bool critical = !guarded && Chance(source.Stats.CriticalChance);
            bool thirdHit = projectile.IsBasic && source.RegisterBasicHit();
            float damage = projectile.Damage;
            if (thirdHit && source.EquippedCrystal == CrystalKind.Sink)
            {
                damage *= 1f + target.MissingHealthRatio * _balance.Number("Crystal.Sink.MaxBonus");
            }
            float typeBonus = projectile.IsBasic ? source.Stats.PhysicalDamageDealtBonus : source.Stats.MagicDamageDealtBonus;
            float resistance = projectile.IsBasic ? target.Stats.PhysicalResistance : target.Stats.MagicResistance;
            damage *= Mathf.Max(0.01f, 1f + (source.Stats.DamageDealtBonus + typeBonus + target.Stats.DamageTakenBonus) * 0.01f);
            damage *= _balance.Number("Battle.ResistanceScale") / (_balance.Number("Battle.ResistanceScale") + Mathf.Max(0f, resistance));
            damage *= guarded ? 1f - Mathf.Clamp(target.Stats.GuardDamageReduction, 0f, _balance.Number("Battle.GuardReductionCap")) * 0.01f
                : critical ? 1f + source.Stats.CriticalDamageBonus * 0.01f : 1f;
            target.RecordHit(guarded ? "가드" : critical ? "치명타" : "명중");
            float healthLost = target.ReceiveDamage(damage);
            source.Heal(healthLost * source.Stats.LifeSteal * 0.01f);
            if (healthLost > 0f && target.EquippedCrystal == CrystalKind.Reflection)
            {
                _reflectedHits.Add((source, healthLost * _balance.Number("Crystal.Reflection.Ratio")));
            }
            if ((thirdHit && source.EquippedCrystal == CrystalKind.Ember)
                || (critical && source.EquippedCrystal == CrystalKind.Flint))
            {
                target.ApplyBurn();
            }
            if (guarded && target.EquippedCrystal == CrystalKind.Elasticity && target.Alive)
            {
                target.ActivateElasticity();
            }
            if (projectile.IsBasic && healthLost > 0f && source.EquippedCrystal == CrystalKind.MagicSmite)
            {
                // Secondary damage has no new hit/crit roll and cannot recursively trigger passives.
                target.ReceiveDamage(healthLost * _balance.Number("Crystal.MagicSmite.Ratio") * _balance.Number("Battle.ResistanceScale") / (_balance.Number("Battle.ResistanceScale") + target.Stats.MagicResistance), true);
            }
            if (healthLost > 0f && source.EquippedCrystal == CrystalKind.Capital && target.Alive && target.Health / target.MaxHealth <= _balance.Number("Crystal.Capital.Threshold"))
            {
                target.Execute();
            }
            if (!projectile.IsBasic && target.Alive)
            {
                if (projectile.Kind == CreatureKind.FrostOwl)
                {
                    target.ApplySlow();
                }
                else if (projectile.Kind == CreatureKind.StoneBoar)
                {
                    target.Move(projectile.Direction * _balance.Creature(CreatureKind.StoneBoar, "PushDistance"));
                }
            }
        }

        private void ResolveKnockouts()
        {
            if (Player.Alive && Enemy.Alive)
            {
                return;
            }
            int nextPlayer = FindLiving(_playerTeam);
            int nextEnemy = FindLiving(_enemyTeam);
            _projectiles.Clear();
            if (nextPlayer < 0 || nextEnemy < 0)
            {
                Phase = nextPlayer < 0 ? (nextEnemy < 0 ? BattlePhase.Draw : BattlePhase.Defeat) : BattlePhase.Victory;
                Status = Phase == BattlePhase.Victory ? "승리! 상대 팀을 모두 쓰러뜨렸습니다."
                    : Phase == BattlePhase.Defeat ? "패배 · 모든 아군 크리쳐가 쓰러졌습니다." : "무승부 · 양쪽 마지막 크리쳐가 함께 쓰러졌습니다.";
                ClearInput();
                return;
            }
            bool playerEntering = !Player.Alive;
            bool enemyEntering = !Enemy.Alive;
            if (playerEntering)
            {
                _playerIndex = nextPlayer;
            }
            if (enemyEntering)
            {
                _enemyIndex = nextEnemy;
            }
            Phase = BattlePhase.Summoning;
            PhaseTime = _balance.Number("Battle.SummonSeconds");
            PlaceActiveFighters(playerEntering, enemyEntering);
            Status = Player.Name + " vs " + Enemy.Name + " · 다음 크리쳐 소환";
        }

        private void PlaceActiveFighters(bool playerEntering = true, bool enemyEntering = true)
        {
            Player.Place(new Vector2(-4.7f, -0.6f), Vector2.right);
            Enemy.Place(new Vector2(4.7f, -0.6f), Vector2.left);
            if (playerEntering)
            {
                Player.TryActivateEntryPassive();
            }
            if (enemyEntering)
            {
                Enemy.TryActivateEntryPassive();
            }
        }

        private static int FindLiving(List<BattleFighter> team)
        {
            for (int index = 0; index < team.Count; index++)
            {
                if (team[index].Alive)
                {
                    return index;
                }
            }
            return -1;
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            float sine = Mathf.Sin(angle);
            float cosine = Mathf.Cos(angle);
            return new Vector2(value.x * cosine - value.y * sine, value.x * sine + value.y * cosine);
        }

        private static bool SegmentHits(Vector2 from, Vector2 to, Vector2 target, float radius)
        {
            Vector2 segment = to - from;
            float lengthSquared = segment.sqrMagnitude;
            float along = lengthSquared <= 0.000001f ? 0f : Mathf.Clamp01(Vector2.Dot(target - from, segment) / lengthSquared);
            return (from + segment * along - target).sqrMagnitude <= radius * radius;
        }
    }
}

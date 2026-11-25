using UnityEngine;

namespace GachaTrainers
{
    // One active actor per side; all movement and combat timing come from the data reader.
    public sealed class BattleActorView : MonoBehaviour
    {
        [SerializeField] private Transform _body;
        [SerializeField] private SpriteRenderer _creatureSprite;
        [SerializeField] private CreatureAnimationLibrary _animations;
        [SerializeField] private SpriteRenderer _groundRing;
        [SerializeField] private SpriteRenderer _shield;
        [SerializeField] private SpriteRenderer _shadow;
        [SerializeField] private bool _isPlayer;
        [SerializeField] private EvolutionAdornmentView _evolution;
        private CreatureFacing _facing = CreatureFacing.East;

        public void Render(BattleFighter fighter, BattlePhase phase, float phaseTime, float elapsedTime)
        {
            if (fighter == null)
            {
                return;
            }
            transform.position = new Vector3(fighter.Position.x, fighter.Position.y, 0f);
            bool summoning = phase == BattlePhase.Summoning;
            float summonScale = summoning ? Mathf.Clamp01(1f - phaseTime / Mathf.Max(0.0001f, fighter.Balance.Number("Battle.SummonSeconds"))) : 1f;
            summonScale = Mathf.Max(0.12f, summonScale);
            float growth = 1f + (fighter.Form.Rank - 1) * 0.13f;
            _body.localScale = new Vector3(summonScale * growth, summonScale * growth, 1f);
            _body.localPosition = Vector3.zero;
            _facing = CreatureFacingUtility.FromDirection(fighter.Facing, _facing);
            int frame = CreatureAnimationCatalog.Frame(fighter.AnimationState, fighter.AnimationTime);
            _creatureSprite.sprite = _animations.Get(fighter.Kind, _facing, fighter.AnimationState, frame);
            _creatureSprite.enabled = fighter.Alive;
            _creatureSprite.flipX = false;
            _creatureSprite.flipY = false;
            float flash = Mathf.Clamp01(fighter.HitFlashTime * 6f);
            float alpha = fighter.InvulnerableTime > 0f ? 0.56f + Mathf.Abs(Mathf.Sin(elapsedTime * 32f)) * 0.44f : 1f;
            // Full-color sprites use a red hit tint; white multiplication would be invisible.
            Color baseTint = fighter.BurnTime > 0f ? new Color(1f, 0.82f, 0.58f) : fighter.Form.Tint;
            if (fighter.SlowTime > 0f)
            {
                baseTint = Color.Lerp(baseTint, new Color(0.56f, 0.83f, 1f), 0.55f);
            }
            Color tint = Color.Lerp(baseTint, new Color(1f, 0.42f, 0.42f), flash);
            tint.a = alpha;
            _creatureSprite.color = tint;
            _evolution?.Render(fighter, tint, elapsedTime);
            _groundRing.enabled = fighter.Alive;
            _shadow.enabled = fighter.Alive;
            _shield.enabled = fighter.Alive && (fighter.ShieldTime > 0f || fighter.ShieldHealth > 0f);
            _shield.color = new Color(0.42f, 0.82f, 1f, 0.17f + 0.06f * Mathf.Sin(elapsedTime * 7f));
            float ringScale = summoning ? 1.5f + Mathf.Sin(elapsedTime * 18f) * 0.3f : 1f;
            _groundRing.transform.localScale = new Vector3(1.55f * ringScale, 0.55f * ringScale, 1f);
            _groundRing.color = _isPlayer
                ? new Color(0.37f, 0.93f, 0.76f, summoning ? 0.7f : 0.25f)
                : new Color(0.75f, 0.63f, 1f, summoning ? 0.7f : 0.25f);
        }
    }
}

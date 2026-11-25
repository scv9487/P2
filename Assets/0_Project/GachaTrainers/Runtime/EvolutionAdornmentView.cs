using UnityEngine;

namespace GachaTrainers
{
    // Code-native silhouette accents follow the existing eight-direction animation pose.
    public sealed class EvolutionAdornmentView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _crest;
        [SerializeField] private SpriteRenderer _left;
        [SerializeField] private SpriteRenderer _right;
        [SerializeField] private SpriteRenderer _halo;

        public void Render(BattleFighter fighter, Color tint, float elapsed)
        {
            int node = fighter.Form.Node;
            bool visible = fighter.Alive && node != 0;
            bool assault = node == 1 || node == 3 || node == 4;
            _crest.enabled = visible;
            _left.enabled = visible;
            _right.enabled = visible;
            _halo.enabled = visible && fighter.Form.Rank == 3;
            float sway = Mathf.Sin(fighter.AnimationTime * 9f) * 0.04f;
            float side = fighter.Facing.x * 0.12f;
            _crest.transform.localPosition = new Vector3(side, 0.76f + sway, 0f);
            _crest.transform.localRotation = Quaternion.Euler(0f, 0f, assault ? -fighter.Facing.x * 20f : 45f);
            _crest.transform.localScale = assault ? new Vector3(0.16f, 0.44f, 1f) : new Vector3(0.28f, 0.28f, 1f);
            _left.transform.localPosition = new Vector3(-0.43f, 0.29f + sway, 0f);
            _right.transform.localPosition = new Vector3(0.43f, 0.29f - sway, 0f);
            _left.transform.localRotation = Quaternion.Euler(0f, 0f, assault ? 30f : 0f);
            _right.transform.localRotation = Quaternion.Euler(0f, 0f, assault ? -30f : 0f);
            _left.transform.localScale = _right.transform.localScale = assault
                ? new Vector3(0.17f, 0.48f, 1f) : new Vector3(0.36f, 0.42f, 1f);
            _crest.color = _left.color = _right.color = tint;
            _halo.transform.localPosition = new Vector3(0f, 1f + sway, 0f);
            _halo.transform.localScale = new Vector3(0.9f, 0.16f, 1f);
            _halo.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(elapsed * 2f) * 5f);
            _halo.color = new Color(tint.r, tint.g, tint.b, tint.a * 0.65f);
        }
    }
}

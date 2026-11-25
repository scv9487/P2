using UnityEngine;

namespace GachaTrainers
{
    public enum ShopModal
    {
        None,
        Machine,
        ReturnTitle,
        Collection,
        Evolution
    }

    public interface IShopDataReader
    {
        Vector2 Position { get; }
        ShopModal Modal { get; }
        GachaMachineKind ActiveMachine { get; }
        GachaMachineKind? NearestMachine { get; }
        bool IsMoving { get; }
        int CollectionPage { get; }
        int PoolPage { get; }
    }

    public sealed class ShopDataContainer : IShopDataReader
    {
        public static readonly Vector2 CreatureInteractionPoint = new(-5.5f, 0.8f);
        public static readonly Vector2 CrystalInteractionPoint = new(0.2f, 0.8f);
        private static readonly Rect[] Obstacles =
        {
            new(-6.9f, 1.3f, 2.8f, 2.6f),
            new(-1.2f, 1.3f, 2.8f, 2.6f)
        };
        public static float MoveSpeed => GachaBalance.Default.Number("Shop.MoveSpeed");
        public static float InteractionRange => GachaBalance.Default.Number("Shop.InteractionRange");
        private readonly GachaBalance _balance;

        public ShopDataContainer(GachaBalance balance = null)
        {
            _balance = balance ?? GachaBalance.Default;
        }

        public Vector2 Position { get; private set; } = new(-2.7f, -1.6f);
        public ShopModal Modal { get; private set; }
        public GachaMachineKind ActiveMachine { get; private set; }
        public bool IsMoving { get; private set; }
        public int CollectionPage { get; private set; }
        public int PoolPage { get; private set; }
        public GachaMachineKind? NearestMachine
        {
            get
            {
                float creatureDistance = Vector2.Distance(Position, CreatureInteractionPoint);
                float crystalDistance = Vector2.Distance(Position, CrystalInteractionPoint);
                if (Mathf.Min(creatureDistance, crystalDistance) > _balance.Number("Shop.InteractionRange"))
                {
                    return null;
                }
                return creatureDistance <= crystalDistance ? GachaMachineKind.Creature : GachaMachineKind.Crystal;
            }
        }

        public void SetMoving(bool moving)
        {
            IsMoving = moving;
        }

        public void Move(Vector2 direction, float deltaTime)
        {
            IsMoving = false;
            if (Modal != ShopModal.None || !float.IsFinite(direction.x) || !float.IsFinite(direction.y)
                || !float.IsFinite(deltaTime) || deltaTime <= 0f)
            {
                return;
            }
            var delta = Vector2.ClampMagnitude(direction, 1f) * (_balance.Number("Shop.MoveSpeed") * Mathf.Min(deltaTime, 0.05f));
            Vector2 previous = Position;
            var horizontal = new Vector2(Mathf.Clamp(Position.x + delta.x, -8.7f, 4f), Position.y);
            if (!IsBlocked(horizontal))
            {
                Position = horizontal;
            }
            var vertical = new Vector2(Position.x, Mathf.Clamp(Position.y + delta.y, -3.6f, 3.5f));
            if (!IsBlocked(vertical))
            {
                Position = vertical;
            }
            IsMoving = (Position - previous).sqrMagnitude > 0.000001f;
        }

        public bool OpenNearestMachine()
        {
            var nearest = NearestMachine;
            if (Modal != ShopModal.None || !nearest.HasValue)
            {
                return false;
            }
            ActiveMachine = nearest.Value;
            PoolPage = 0;
            Modal = ShopModal.Machine;
            IsMoving = false;
            return true;
        }

        public void OpenReturnTitle()
        {
            if (Modal == ShopModal.None)
            {
                Modal = ShopModal.ReturnTitle;
                IsMoving = false;
            }
        }

        public void OpenCollection()
        {
            Modal = ShopModal.Collection;
            IsMoving = false;
        }

        public void OpenEvolution()
        {
            Modal = ShopModal.Evolution;
            IsMoving = false;
        }

        public void ChangeCollectionPage(int delta, int count)
        {
            CollectionPage = Mathf.Clamp(CollectionPage + delta, 0, Mathf.Max(0, (count - 1) / 8));
        }

        public void ChangePoolPage()
        {
            PoolPage = ActiveMachine == GachaMachineKind.Crystal ? 1 - PoolPage : 0;
        }

        public void CloseModal()
        {
            Modal = ShopModal.None;
        }

        private static bool IsBlocked(Vector2 position)
        {
            foreach (var obstacle in Obstacles)
            {
                if (obstacle.Contains(position))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

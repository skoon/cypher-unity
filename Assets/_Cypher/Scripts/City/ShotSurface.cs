using Cypher.Core;
using UnityEngine;

namespace Cypher.City
{
    /// <summary>Marks a collider for the guns: what a shot that strikes it has hit (a prop, a person, a vehicle). A collider without one is the world.</summary>
    [DisallowMultipleComponent]
    public sealed class ShotSurface : MonoBehaviour
    {
        [SerializeField] ShotTarget target = ShotTarget.World;

        public ShotTarget Target
        {
            get => target;
            set => target = value;
        }

        /// <summary>What a collider is to a shot.</summary>
        public static ShotTarget Of(Collider collider)
        {
            if (collider == null) return ShotTarget.World;
            ShotSurface s = collider.GetComponentInParent<ShotSurface>();
            return s != null ? s.Target : ShotTarget.World;
        }
    }
}

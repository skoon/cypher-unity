using UnityEngine;

namespace Cypher.City
{
    /// <summary>On an enemy's hit volume (with a <see cref="ShotSurface"/> saying <c>Enemy</c>): which enemy it is, and whether it is the head.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyHitbox : MonoBehaviour
    {
        /// <summary>The enemy's id (<c>Enemy.Id</c>), or -1 while the volume is parked.</summary>
        public int Enemy { get; set; } = -1;

        public bool Head { get; set; }

        /// <summary>The hitbox a collider belongs to, or null.</summary>
        public static EnemyHitbox Of(Collider collider) => collider == null ? null : collider.GetComponent<EnemyHitbox>();
    }
}

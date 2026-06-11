using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Demo
{
    // Authors a CrossingPortal from two child marker transforms (entrance/exit).
    // If a marker is unassigned, this GameObject's own position is used.
    public class CrossingPortalAuthoring : MonoBehaviour
    {
        public Transform Entrance;
        public Transform Exit;
        public float     Width = 2f;

        class Baker : Baker<CrossingPortalAuthoring>
        {
            public override void Bake(CrossingPortalAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.None);
                float3 entrance = a.Entrance != null ? (float3)a.Entrance.position : (float3)a.transform.position;
                float3 exit     = a.Exit     != null ? (float3)a.Exit.position     : (float3)a.transform.position;
                AddComponent(e, new CrossingPortal
                {
                    Entrance = entrance,
                    Exit     = exit,
                    Width    = a.Width,
                });
            }
        }

        void OnDrawGizmosSelected()
        {
            Vector3 en = Entrance != null ? Entrance.position : transform.position;
            Vector3 ex = Exit     != null ? Exit.position     : transform.position;
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(en, 0.4f);
            Gizmos.DrawSphere(ex, 0.4f);
            Gizmos.DrawLine(en, ex);
        }
    }
}

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Demo
{
    // Bakes the CrowdConfig singleton (CrowdScene subscene). Defaults match
    // the world layout in docs/superpowers/plans/2026-06-11-crowd-sandbox.md.
    public class CrowdConfigAuthoring : MonoBehaviour
    {
        [Tooltip("CrowdSoldier prefab — must have CrowdSoldierAuthoring.")]
        public GameObject SoldierPrefab;

        [Header("Armies")]
        public int     Army0Count = 750;
        public int     Army1Count = 750;
        public Vector3 Army0SpawnCenter = new Vector3(-30f, 0f, 0f);
        public Vector3 Army1SpawnCenter = new Vector3( 30f, 0f, 0f);
        public Vector2 SpawnHalfExtents = new Vector2(12f, 30f);
        public Vector3 Army0Goal = new Vector3( 30f, 0f, 0f);
        public Vector3 Army1Goal = new Vector3(-30f, 0f, 0f);

        [Header("Movement")]
        [Tooltip("Grid pitch at spawn; keep above the capsule diameter.")]
        public float SpawnSpacing  = 1.2f;
        public float MoveSpeed     = 2.5f;
        [Tooltip("Soldiers stop within this distance of their goal. Generous: hundreds share one goal point.")]
        public float ArrivalRadius = 6f;

        [Header("Team colors (RGBA, linear)")]
        public Color Army0Color = new Color(1f, 0.1f, 0.1f, 1f);
        public Color Army1Color = new Color(0.1f, 0.4f, 1f, 1f);

        class Baker : Baker<CrowdConfigAuthoring>
        {
            public override void Bake(CrowdConfigAuthoring a)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new CrowdConfig
                {
                    SoldierPrefab = a.SoldierPrefab != null
                        ? GetEntity(a.SoldierPrefab, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                    Army0Count       = a.Army0Count,
                    Army1Count       = a.Army1Count,
                    Army0SpawnCenter = a.Army0SpawnCenter,
                    Army1SpawnCenter = a.Army1SpawnCenter,
                    SpawnHalfExtents = new float2(a.SpawnHalfExtents.x, a.SpawnHalfExtents.y),
                    Army0Goal        = a.Army0Goal,
                    Army1Goal        = a.Army1Goal,
                    SpawnSpacing     = a.SpawnSpacing,
                    MoveSpeed        = a.MoveSpeed,
                    ArrivalRadius    = a.ArrivalRadius,
                    Army0Color       = new float4(a.Army0Color.r, a.Army0Color.g, a.Army0Color.b, a.Army0Color.a),
                    Army1Color       = new float4(a.Army1Color.r, a.Army1Color.g, a.Army1Color.b, a.Army1Color.a),
                });
            }
        }
    }
}

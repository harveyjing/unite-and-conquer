using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Demo
{
    // Singleton baked from BattleConfigAuthoring. Drives every battle system.
    public struct BattleConfig : IComponentData
    {
        public Entity SoldierPrefab;
        public int    CountPerSide;
        public float3 RedCenter;
        public float3 BlueCenter;
        public float  Spacing;
        public float  SearchRadius;
        public float  MoveSpeed;
        public float  AttackRange;
        public float  Dps;
        public float  MaxHealth;
        public int    TargetRefreshIntervalTicks;
        public float4 RedColor;
        public float4 BlueColor;
    }

    public class BattleConfigAuthoring : MonoBehaviour
    {
        [Tooltip("Soldier prefab — must have a GhostAuthoringComponent + SoldierAuthoring.")]
        public GameObject SoldierPrefab;

        [Header("Army size")]
        [Tooltip("Soldiers per team. Start at 10 for verification; scale to 10000 at the end.")]
        public int CountPerSide = 10;
        public float Spacing = 1.5f;

        [Header("Spawn centers")]
        public Vector3 RedCenter  = new Vector3(-20f, 0f, 0f);
        public Vector3 BlueCenter = new Vector3( 20f, 0f, 0f);

        [Header("Combat tuning")]
        public float SearchRadius = 50f;
        public float MoveSpeed    = 2f;
        public float AttackRange  = 0.8f;
        public float Dps          = 25f;
        public float MaxHealth    = 50f;
        public int   TargetRefreshIntervalTicks = 5;

        [Header("Team colors (RGBA, linear)")]
        public Color RedColor  = new Color(1f, 0.1f, 0.1f, 1f);
        public Color BlueColor = new Color(0.1f, 0.4f, 1f, 1f);

        class Baker : Baker<BattleConfigAuthoring>
        {
            public override void Bake(BattleConfigAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new BattleConfig
                {
                    SoldierPrefab = GetEntity(authoring.SoldierPrefab, TransformUsageFlags.Dynamic),
                    CountPerSide  = authoring.CountPerSide,
                    RedCenter     = authoring.RedCenter,
                    BlueCenter    = authoring.BlueCenter,
                    Spacing       = authoring.Spacing,
                    SearchRadius  = authoring.SearchRadius,
                    MoveSpeed     = authoring.MoveSpeed,
                    AttackRange   = authoring.AttackRange,
                    Dps           = authoring.Dps,
                    MaxHealth     = authoring.MaxHealth,
                    TargetRefreshIntervalTicks = authoring.TargetRefreshIntervalTicks,
                    RedColor      = new float4(authoring.RedColor.r,  authoring.RedColor.g,  authoring.RedColor.b,  authoring.RedColor.a),
                    BlueColor     = new float4(authoring.BlueColor.r, authoring.BlueColor.g, authoring.BlueColor.b, authoring.BlueColor.a),
                });
            }
        }
    }
}

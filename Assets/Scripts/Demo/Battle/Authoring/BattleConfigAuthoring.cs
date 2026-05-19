using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Demo
{
    // Singleton baked from BattleConfigAuthoring. Drives every battle system.
    public struct BattleConfig : IComponentData
    {
        public Entity SoldierPrefab;

        // Derived from squad shape: SquadsPerTeam * SquadRows * SquadCols.
        // Kept on the component for HUD / diagnostics.
        public int CountPerSide;

        // Squad shape.
        public int   SquadsPerTeam;
        public int   SquadRows;
        public int   SquadCols;
        public float SquadSpacing;

        // Squad behavior.
        public float SquadAdvanceSpeed;
        public float SquadRotationSpeed;
        public float ContactMargin;
        public int   CompactionIntervalTicks;

        // Spawn centers.
        public float3 RedCenter;
        public float3 BlueCenter;

        // Combat / soldier tuning.
        public float SoldierStepSpeed;
        public float AttackRange;
        public float Dps;
        public float MaxHealth;
        public int   TargetRefreshIntervalTicks;

        // Kept until Task 11 deletes TargetingSystem (its only consumer).
        // SquadTargetingSystem does not use the physics broadphase.
        public float SearchRadius;

        // Visuals.
        public float4 RedColor;
        public float4 BlueColor;
    }

    public class BattleConfigAuthoring : MonoBehaviour
    {
        [Tooltip("Soldier prefab — must have a GhostAuthoringComponent + SoldierAuthoring.")]
        public GameObject SoldierPrefab;

        [Header("Squad shape")]
        public int   SquadsPerTeam = 2;
        public int   SquadRows     = 5;
        public int   SquadCols     = 10;
        [FormerlySerializedAs("Spacing")]
        public float SquadSpacing  = 1.5f;

        [Header("Squad behavior")]
        public float SquadAdvanceSpeed       = 2f;
        public float SquadRotationSpeed      = 2f;   // rad/s
        public float ContactMargin           = 0.1f;
        public int   CompactionIntervalTicks = 10;

        [Header("Spawn centers")]
        public Vector3 RedCenter  = new Vector3(-20f, 0f, 0f);
        public Vector3 BlueCenter = new Vector3( 20f, 0f, 0f);

        [Header("Combat tuning")]
        [FormerlySerializedAs("MoveSpeed")]
        public float SoldierStepSpeed = 2f;
        public float AttackRange      = 0.8f;
        public float Dps              = 25f;
        public float MaxHealth        = 50f;
        public int   TargetRefreshIntervalTicks = 5;

        // Removed once TargetingSystem is deleted in Task 11.
        public float SearchRadius = 200f;

        [Header("Team colors (RGBA, linear)")]
        public Color RedColor  = new Color(1f, 0.1f, 0.1f, 1f);
        public Color BlueColor = new Color(0.1f, 0.4f, 1f, 1f);

        class Baker : Baker<BattleConfigAuthoring>
        {
            public override void Bake(BattleConfigAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                int countPerSide = authoring.SquadsPerTeam
                                   * authoring.SquadRows
                                   * authoring.SquadCols;

                AddComponent(entity, new BattleConfig
                {
                    SoldierPrefab = GetEntity(authoring.SoldierPrefab, TransformUsageFlags.Dynamic),
                    CountPerSide  = countPerSide,

                    SquadsPerTeam = authoring.SquadsPerTeam,
                    SquadRows     = authoring.SquadRows,
                    SquadCols     = authoring.SquadCols,
                    SquadSpacing  = authoring.SquadSpacing,

                    SquadAdvanceSpeed       = authoring.SquadAdvanceSpeed,
                    SquadRotationSpeed      = authoring.SquadRotationSpeed,
                    ContactMargin           = authoring.ContactMargin,
                    CompactionIntervalTicks = authoring.CompactionIntervalTicks,

                    RedCenter  = authoring.RedCenter,
                    BlueCenter = authoring.BlueCenter,

                    SoldierStepSpeed           = authoring.SoldierStepSpeed,
                    AttackRange                = authoring.AttackRange,
                    Dps                        = authoring.Dps,
                    MaxHealth                  = authoring.MaxHealth,
                    TargetRefreshIntervalTicks = authoring.TargetRefreshIntervalTicks,
                    SearchRadius               = authoring.SearchRadius,

                    RedColor  = new float4(authoring.RedColor.r,  authoring.RedColor.g,  authoring.RedColor.b,  authoring.RedColor.a),
                    BlueColor = new float4(authoring.BlueColor.r, authoring.BlueColor.g, authoring.BlueColor.b, authoring.BlueColor.a),
                });
            }
        }
    }
}

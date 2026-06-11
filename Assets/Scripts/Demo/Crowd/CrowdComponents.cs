using Unity.Entities;
using Unity.Mathematics;

namespace Demo
{
    // One fully individual soldier in the crowd sandbox. No squad, no slot:
    // the goal is stamped at spawn and steering re-derives everything else
    // from position each tick.
    public struct CrowdSoldier : IComponentData
    {
        public int    Team;
        public float3 Goal;
    }

    // Singleton baked from CrowdConfigAuthoring (CrowdScene subscene only).
    // Its presence is also the gate that lets crowd systems run at all.
    public struct CrowdConfig : IComponentData
    {
        public Entity SoldierPrefab;

        public int    Army0Count;
        public int    Army1Count;
        public float3 Army0SpawnCenter;
        public float3 Army1SpawnCenter;
        public float2 SpawnHalfExtents; // XZ half-size of each spawn rectangle
        public float3 Army0Goal;
        public float3 Army1Goal;
        public float  SpawnSpacing;     // grid pitch; keep > capsule diameter
                                        // so soldiers never spawn interpenetrating

        public float  MoveSpeed;
        public float  ArrivalRadius;

        public float4 Army0Color;
        public float4 Army1Color;
    }
}

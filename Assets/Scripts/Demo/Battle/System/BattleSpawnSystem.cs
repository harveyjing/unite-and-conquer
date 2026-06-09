using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Demo
{
    // One-shot. Creates 2 * SquadsPerTeam Squad entities, lays squads in
    // a line per team perpendicular to the red<->blue axis, bulk-spawns
    // soldiers, and wires SquadMembership + SquadMember buffers.
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct BattleSpawnSystem : ISystem
    {
        const float InterSquadGap = 2f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            var em     = state.EntityManager;

            int squadsPerTeam   = config.SquadsPerTeam;
            int rows            = config.SquadRows;
            int cols            = config.SquadCols;
            int soldiersPerSquad = rows * cols;
            int countPerSide    = squadsPerTeam * soldiersPerSquad;
            float spacing       = config.SquadSpacing;
            float squadStrideZ  = cols * spacing + InterSquadGap;

            var squadArch = em.CreateArchetype(
                typeof(Squad),
                typeof(SquadTarget),
                typeof(SquadMember),
                typeof(SquadNav),
                typeof(SquadMoveGoal),
                typeof(LocalTransform),
                typeof(LocalToWorld));

            var redSquads  = em.CreateEntity(squadArch, squadsPerTeam, Allocator.TempJob);
            var blueSquads = em.CreateEntity(squadArch, squadsPerTeam, Allocator.TempJob);

            quaternion redFacing  = quaternion.LookRotationSafe(new float3( 1, 0, 0), math.up());
            quaternion blueFacing = quaternion.LookRotationSafe(new float3(-1, 0, 0), math.up());

            for (int i = 0; i < squadsPerTeam; i++)
            {
                float offsetZ = (i - (squadsPerTeam - 1) * 0.5f) * squadStrideZ;

                var redPos = (float3)config.RedCenter + new float3(0, 0, offsetZ);
                em.SetComponentData(redSquads[i], new Squad
                {
                    Team = 0, Rows = rows, Cols = cols, Spacing = spacing,
                });
                em.SetComponentData(redSquads[i], new SquadTarget { Value = Entity.Null });
                em.SetComponentData(redSquads[i], LocalTransform.FromPositionRotation(redPos, redFacing));
                em.SetComponentData(redSquads[i], new SquadNav { State = NavState.Pursue });
                em.SetComponentData(redSquads[i], new SquadMoveGoal { Position = redPos, Engage = 0 });
                var redBuf = em.GetBuffer<SquadMember>(redSquads[i]);
                redBuf.ResizeUninitialized(soldiersPerSquad);
                for (int s = 0; s < soldiersPerSquad; s++)
                    redBuf[s] = new SquadMember { Value = Entity.Null };

                var bluePos = (float3)config.BlueCenter + new float3(0, 0, offsetZ);
                em.SetComponentData(blueSquads[i], new Squad
                {
                    Team = 1, Rows = rows, Cols = cols, Spacing = spacing,
                });
                em.SetComponentData(blueSquads[i], new SquadTarget { Value = Entity.Null });
                em.SetComponentData(blueSquads[i], LocalTransform.FromPositionRotation(bluePos, blueFacing));
                em.SetComponentData(blueSquads[i], new SquadNav { State = NavState.Pursue });
                em.SetComponentData(blueSquads[i], new SquadMoveGoal { Position = bluePos, Engage = 0 });
                var blueBuf = em.GetBuffer<SquadMember>(blueSquads[i]);
                blueBuf.ResizeUninitialized(soldiersPerSquad);
                for (int s = 0; s < soldiersPerSquad; s++)
                    blueBuf[s] = new SquadMember { Value = Entity.Null };
            }

            var reds  = em.Instantiate(config.SoldierPrefab, countPerSide, Allocator.TempJob);
            var blues = em.Instantiate(config.SoldierPrefab, countPerSide, Allocator.TempJob);

            var redAnchorPos  = new NativeArray<float3>(squadsPerTeam, Allocator.TempJob);
            var redAnchorRot  = new NativeArray<quaternion>(squadsPerTeam, Allocator.TempJob);
            var blueAnchorPos = new NativeArray<float3>(squadsPerTeam, Allocator.TempJob);
            var blueAnchorRot = new NativeArray<quaternion>(squadsPerTeam, Allocator.TempJob);
            for (int i = 0; i < squadsPerTeam; i++)
            {
                var rt = em.GetComponentData<LocalTransform>(redSquads[i]);
                redAnchorPos[i] = rt.Position;
                redAnchorRot[i] = rt.Rotation;
                var bt = em.GetComponentData<LocalTransform>(blueSquads[i]);
                blueAnchorPos[i] = bt.Position;
                blueAnchorRot[i] = bt.Rotation;
            }

            var xformLookup      = SystemAPI.GetComponentLookup<LocalTransform>(false);
            var teamLookup       = SystemAPI.GetComponentLookup<Team>(false);
            var healthLookup     = SystemAPI.GetComponentLookup<Health>(false);
            var attackLookup     = SystemAPI.GetComponentLookup<AttackStats>(false);
            var colorLookup      = SystemAPI.GetComponentLookup<SoldierColor>(false);
            var membershipLookup = SystemAPI.GetComponentLookup<SquadMembership>(false);

            state.Dependency = new InitSoldierJob
            {
                Entities         = reds,
                SquadEntities    = redSquads,
                SquadAnchorPos   = redAnchorPos,
                SquadAnchorRot   = redAnchorRot,
                Rows             = rows,
                Cols             = cols,
                Spacing          = spacing,
                SoldiersPerSquad = soldiersPerSquad,
                TeamValue        = 0,
                TeamColor        = config.RedColor,
                MaxHealth        = config.MaxHealth,
                AttackRange      = config.AttackRange,
                Dps              = config.Dps,
                XformLookup      = xformLookup,
                TeamLookup       = teamLookup,
                HealthLookup     = healthLookup,
                AttackLookup     = attackLookup,
                ColorLookup      = colorLookup,
                MembershipLookup = membershipLookup,
            }.Schedule(reds.Length, 64, state.Dependency);

            state.Dependency = new InitSoldierJob
            {
                Entities         = blues,
                SquadEntities    = blueSquads,
                SquadAnchorPos   = blueAnchorPos,
                SquadAnchorRot   = blueAnchorRot,
                Rows             = rows,
                Cols             = cols,
                Spacing          = spacing,
                SoldiersPerSquad = soldiersPerSquad,
                TeamValue        = 1,
                TeamColor        = config.BlueColor,
                MaxHealth        = config.MaxHealth,
                AttackRange      = config.AttackRange,
                Dps              = config.Dps,
                XformLookup      = xformLookup,
                TeamLookup       = teamLookup,
                HealthLookup     = healthLookup,
                AttackLookup     = attackLookup,
                ColorLookup      = colorLookup,
                MembershipLookup = membershipLookup,
            }.Schedule(blues.Length, 64, state.Dependency);

            state.Dependency.Complete();

            for (int i = 0; i < reds.Length; i++)
            {
                int squadIndex = i / soldiersPerSquad;
                int slot       = i % soldiersPerSquad;
                var buf        = em.GetBuffer<SquadMember>(redSquads[squadIndex]);
                buf[slot]      = new SquadMember { Value = reds[i] };
            }
            for (int i = 0; i < blues.Length; i++)
            {
                int squadIndex = i / soldiersPerSquad;
                int slot       = i % soldiersPerSquad;
                var buf        = em.GetBuffer<SquadMember>(blueSquads[squadIndex]);
                buf[slot]      = new SquadMember { Value = blues[i] };
            }

            reds.Dispose();
            blues.Dispose();
            redSquads.Dispose();
            blueSquads.Dispose();
            redAnchorPos.Dispose();
            redAnchorRot.Dispose();
            blueAnchorPos.Dispose();
            blueAnchorRot.Dispose();

            Debug.Log($"BattleSpawnSystem: spawned {squadsPerTeam} red + {squadsPerTeam} blue squads, {countPerSide} soldiers per side.");
            state.Enabled = false;
        }

        [BurstCompile]
        struct InitSoldierJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity>     Entities;
            [ReadOnly] public NativeArray<Entity>     SquadEntities;
            [ReadOnly] public NativeArray<float3>     SquadAnchorPos;
            [ReadOnly] public NativeArray<quaternion> SquadAnchorRot;

            public int    Rows;
            public int    Cols;
            public float  Spacing;
            public int    SoldiersPerSquad;
            public int    TeamValue;
            public float4 TeamColor;
            public float  MaxHealth;
            public float  AttackRange;
            public float  Dps;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform>  XformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Team>            TeamLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Health>          HealthLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<AttackStats>     AttackLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<SoldierColor>    ColorLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<SquadMembership> MembershipLookup;

            public void Execute(int i)
            {
                int squadIndex = i / SoldiersPerSquad;
                int slot       = i % SoldiersPerSquad;

                var local = SquadGeometry.SlotLocalOffset(slot, Rows, Cols, Spacing);
                var world = SquadAnchorPos[squadIndex] + math.mul(SquadAnchorRot[squadIndex], local);

                var e = Entities[i];
                XformLookup[e]      = LocalTransform.FromPositionRotation(world, SquadAnchorRot[squadIndex]);
                TeamLookup[e]       = new Team { Value = TeamValue };
                HealthLookup[e]     = new Health { Current = MaxHealth, Max = MaxHealth };
                AttackLookup[e]     = new AttackStats { Range = AttackRange, Dps = Dps };
                ColorLookup[e]      = new SoldierColor { Value = TeamColor };
                MembershipLookup[e] = new SquadMembership
                {
                    Squad     = SquadEntities[squadIndex],
                    SlotIndex = slot,
                };
            }
        }
    }
}

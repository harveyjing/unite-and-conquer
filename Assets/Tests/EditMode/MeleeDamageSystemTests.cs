using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class MeleeDamageSystemTests : EcsTestsBase
    {
        // Two engaged squads face each other, so they are mirrored 180 degrees:
        // self column c stands physically opposite enemy column (Cols-1-c), NOT
        // enemy column c. The attacker must damage the enemy that is actually in
        // front of it, not the one sharing its slot index.
        [Test]
        public void FrontRankSoldier_DamagesPhysicallyOpposedEnemy_NotSameColumnIndex()
        {
            CreateBattleConfig(cols: 2, rows: 1);

            var selfSquad  = CreateSquad(0, 1, 2, 1.5f, float3.zero, quaternion.identity);
            var enemySquad = CreateSquad(1, 1, 2, 1.5f, float3.zero, quaternion.identity);

            // Attacker sits in self column 0 at the origin.
            var attacker = CreateSoldier(selfSquad, slot: 0, pos: float3.zero);
            Manager.SetComponentData(selfSquad, new SquadTarget { Value = enemySquad });

            // Enemy buffer: column 0 (same index as attacker) parked far away;
            // column 1 (the mirrored, physically-opposed column) right in front.
            var enemySameIndex = CreateSoldier(enemySquad, slot: 0, pos: new float3(0f, 0f, 5f));
            var enemyOpposed   = CreateSoldier(enemySquad, slot: 1, pos: new float3(0.5f, 0f, 0f));

            var buf = Manager.GetBuffer<SquadMember>(enemySquad);
            buf.Add(new SquadMember { Value = enemySameIndex }); // slot 0
            buf.Add(new SquadMember { Value = enemyOpposed });   // slot 1

            SetTime(0.0, 0.1f);
            CreateAndUpdateSystem<MeleeDamageSystem>();

            float opposedHp   = Manager.GetComponentData<Health>(enemyOpposed).Current;
            float sameIndexHp  = Manager.GetComponentData<Health>(enemySameIndex).Current;

            Assert.Less(opposedHp, 50f,
                "physically-opposed enemy (mirrored column) should take damage");
            Assert.AreEqual(50f, sameIndexHp, 1e-4f,
                "same-slot-index enemy is not in front of the attacker and must be untouched");
        }

        // Regression for the attrition "freeze": after compaction the survivors
        // are left-packed into the lowest slots, so the enemy buffer can be
        // shorter than Cols and its columns no longer line up with physical
        // position. Column-index pairing then points past the buffer (or at the
        // wrong soldier) and combat stalls. Targeting must be by proximity: the
        // attacker damages the nearest live enemy within Range regardless of slot.
        [Test]
        public void FrontRankSoldier_DamagesNearestEnemy_WhenEnemyRowIsPartialRemnant()
        {
            CreateBattleConfig(cols: 4, rows: 1);

            var selfSquad  = CreateSquad(0, 1, 4, 1.5f, float3.zero, quaternion.identity);
            var enemySquad = CreateSquad(1, 1, 4, 1.5f, float3.zero, quaternion.identity);

            // Attacker in self column 0. With Cols=4 the old mirror pairing wants
            // enemy column 3 (Cols-1-0), but the remnant buffer only has 2 slots.
            var attacker = CreateSoldier(selfSquad, slot: 0, pos: float3.zero);
            Manager.SetComponentData(selfSquad, new SquadTarget { Value = enemySquad });

            // Enemy remnant: only 2 survivors, both in front of the attacker and
            // within Range. Neither sits in column 3, so index pairing finds none.
            var nearEnemy = CreateSoldier(enemySquad, slot: 0, pos: new float3(0.3f, 0f, 0f));
            var farEnemy  = CreateSoldier(enemySquad, slot: 1, pos: new float3(0.6f, 0f, 0f));

            var buf = Manager.GetBuffer<SquadMember>(enemySquad);
            buf.Add(new SquadMember { Value = nearEnemy }); // slot 0
            buf.Add(new SquadMember { Value = farEnemy });  // slot 1

            SetTime(0.0, 0.1f);
            CreateAndUpdateSystem<MeleeDamageSystem>();

            float nearHp = Manager.GetComponentData<Health>(nearEnemy).Current;
            float farHp  = Manager.GetComponentData<Health>(farEnemy).Current;

            Assert.Less(nearHp, 50f,
                "nearest enemy in the partial remnant should take damage (no freeze)");
            Assert.AreEqual(50f, farHp, 1e-4f,
                "an attacker hits only its single nearest target");
        }
    }
}

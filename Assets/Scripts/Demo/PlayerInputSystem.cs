using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine.InputSystem;


[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial struct PlayerInputSystem : ISystem
{

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
    }

    public void OnUpdate(ref SystemState state)
    {

        foreach(var playerInput in SystemAPI.Query<RefRW<PlayerInput>>().WithAll<GhostOwnerIsLocal>())
        {
            playerInput.ValueRW = default;
            if (Input.GetKey("left"))
                playerInput.ValueRW.Horizontal -= 1;
            if (Input.GetKey("right"))
                playerInput.ValueRW.Horizontal += 1;
            if (Input.GetKey("down"))
                playerInput.ValueRW.Vertical -= 1;
            if (Input.GetKey("up"))
                playerInput.ValueRW.Vertical += 1;
            return;
        }

    }
}

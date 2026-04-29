using Unity.Entities;
using Unity.NetCode;
using UnityEngine.InputSystem;

namespace Demo
{

    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial struct PlayerInputSystem : ISystem
    {

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
        }

        public void OnUpdate(ref SystemState state)
        {

            foreach (var playerInput in SystemAPI.Query<RefRW<PlayerInput>>().WithAll<GhostOwnerIsLocal>())
            {
                playerInput.ValueRW = default;
                var kb = Keyboard.current;
                if (kb == null) return;
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed)
                    playerInput.ValueRW.Horizontal -= 1;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed)
                    playerInput.ValueRW.Horizontal += 1;
                if (kb.downArrowKey.isPressed || kb.sKey.isPressed)
                    playerInput.ValueRW.Vertical -= 1;
                if (kb.upArrowKey.isPressed || kb.wKey.isPressed)
                    playerInput.ValueRW.Vertical += 1;
                return;
            }

        }
    }
}

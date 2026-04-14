using Unity.Entities;
using Unity.Mathematics;

public struct PlayerInputData : IComponentData
{
    // Normalized, camera-relative move direction in world XZ.
    // x = world X axis contribution, y = world Z axis contribution.
    public float2 MoveAxis;
}

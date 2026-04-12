using Unity.Entities;
using Unity.Mathematics;

public struct PlayerMovementData : IComponentData
{
    public float MoveSpeed;     // units/sec at full input
    public float Acceleration;  // velocity lerp rate (higher = snappier)
    public float2 Velocity;     // current smoothed XZ velocity (mutable, starts at zero)
}

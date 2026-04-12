using Unity.Entities;
using Unity.Mathematics;

public struct CameraTargetData : IComponentData
{
    public float3 Position; // world position of the player this frame
}

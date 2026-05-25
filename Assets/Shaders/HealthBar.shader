Shader "Demo/HealthBar"
{
    Properties
    {
        _Health01 ("Health (0..1)", Range(0,1)) = 1.0
    }
    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HealthBar"
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float _Health01;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float, _Health01)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Health01 UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Health01)
            #endif

            static const float BarWidth  = 0.8;
            static const float BarHeight = 0.1;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // Camera-facing billboard. The quad's object-space vertices are
                // assumed in [-0.5, 0.5]^2 (Unity built-in Quad). We expand
                // around the bar's world-space origin using camera-right/up
                // extracted from the view matrix.
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp    = UNITY_MATRIX_V[1].xyz;

                float3 offset = camRight * (IN.positionOS.x * BarWidth)
                              + camUp    * (IN.positionOS.y * BarHeight);
                float3 worldPos = originWS + offset;

                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float h      = saturate(_Health01);
                float filled = step(IN.uv.x, h);

                // Gradient: red -> yellow at h=0.5 -> green at h=1.0
                float3 redToYellow   = lerp(float3(1, 0, 0), float3(1, 1, 0), saturate(h * 2.0));
                float3 fillCol       = lerp(redToYellow,   float3(0, 1, 0), saturate(h * 2.0 - 1.0));
                float3 bgCol         = float3(0.08, 0.08, 0.08);

                float3 col = lerp(bgCol, fillCol, filled);
                float  a   = lerp(0.55,  1.0,     filled);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

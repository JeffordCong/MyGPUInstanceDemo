Shader "Unlit/SimpleIndirectShader"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 与C#脚本中匹配的数据结构
            struct InstanceData
            {
                float4 position;
                float4 color;
            };


            StructuredBuffer<InstanceData> _VisibleInstancesData;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                // UNITY_VERTEX_INPUT_INSTANCE_ID 是必须的，它提供了unity_InstanceID
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR0;
            };

            Varyings vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input); // 初始化实例ID

                // 使用实例ID作为索引，从Buffer中读取当前实例的数据
                InstanceData data = _VisibleInstancesData[instanceID];

                // 将实例的位置应用到模型的顶点上
                float3 worldPos = input.positionOS.xyz + data.position.xyz;

                // 转换到裁剪空间
                o.positionHCS = TransformWorldToHClip(worldPos);

                // ==================== 步骤 3.2: 在顶点着色器中传递颜色 ====================
                o.color = data.color; // 将从Buffer中读到的颜色
                // ===================================================================

                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return input.color; // 直接使用传递过来的颜色
            }
            ENDHLSL
        }
    }
}
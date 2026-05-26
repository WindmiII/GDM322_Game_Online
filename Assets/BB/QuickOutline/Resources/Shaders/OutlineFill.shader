//
//  OutlineFill.shader
//  QuickOutline
//
//  Created by Chris Nolet on 2/21/18.
//  Copyright © 2018 Chris Nolet. All rights reserved.
//

Shader "Custom/Outline Fill" {
 Properties
    {
        _OutlineColor("Outline Color", Color) = (1,1,0,1)
        _Thickness("Thickness", Float) = 1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"

    float _Thickness;
    float4 _OutlineColor;

    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Name "OutlinePass"
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                worldPos += normalize(worldPos) * _Thickness;
                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }

            float4 Frag() : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}

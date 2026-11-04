Shader "WildMagic/LayeredDamageCutout"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BurnColor ("Burn Edge", Color) = (0.08, 0.015, 0.008, 1)
        _BurnWidth ("Burn Width", Range(0, 0.1)) = 0.025
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
        _RequireReveal ("Require Damage Reveal", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define MAX_DAMAGE_POINTS 32

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _BurnColor;
                half _BurnWidth;
                half _Smoothness;
                float4 _DamagePoints[MAX_DAMAGE_POINTS];
                int _DamageCount;
                float4 _RevealPoints[MAX_DAMAGE_POINTS];
                int _RevealCount;
                half _RequireReveal;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_RequireReveal > 0.5h)
                {
                    float revealDistance = -1000.0;
                    [loop]
                    for (int revealIndex = 0; revealIndex < _RevealCount; revealIndex++)
                    {
                        float insideReveal = _RevealPoints[revealIndex].w
                            - distance(input.positionOS, _RevealPoints[revealIndex].xyz);
                        revealDistance = max(revealDistance, insideReveal);
                    }
                    clip(revealDistance);
                }

                float nearestWound = 1000.0;
                [loop]
                for (int index = 0; index < _DamageCount; index++)
                {
                    float distanceToDamage = distance(input.positionOS, _DamagePoints[index].xyz);
                    float signedDistance = distanceToDamage - _DamagePoints[index].w;
                    nearestWound = min(nearestWound, signedDistance);
                }

                clip(nearestWound);

                half edgeBlend = _DamageCount > 0
                    ? 1.0h - saturate(nearestWound / max(_BurnWidth, 0.0001h))
                    : 0.0h;
                half3 albedo = lerp(_BaseColor.rgb, _BurnColor.rgb, edgeBlend);

                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = ambient + mainLight.color * diffuse * mainLight.shadowAttenuation;

                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDirection = SafeNormalize(mainLight.direction + viewDirection);
                half specular = pow(saturate(dot(normalWS, halfDirection)), lerp(8.0h, 64.0h, _Smoothness));
                half3 color = albedo * lighting + specular * mainLight.color * _Smoothness;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_DAMAGE_POINTS 32

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _BurnColor;
                half _BurnWidth;
                half _Smoothness;
                float4 _DamagePoints[MAX_DAMAGE_POINTS];
                int _DamageCount;
                float4 _RevealPoints[MAX_DAMAGE_POINTS];
                int _RevealCount;
                half _RequireReveal;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                if (_RequireReveal > 0.5h)
                {
                    float revealDistance = -1000.0;
                    [loop]
                    for (int revealIndex = 0; revealIndex < _RevealCount; revealIndex++)
                    {
                        float insideReveal = _RevealPoints[revealIndex].w
                            - distance(input.positionOS, _RevealPoints[revealIndex].xyz);
                        revealDistance = max(revealDistance, insideReveal);
                    }
                    clip(revealDistance);
                }

                float nearestWound = 1000.0;
                [loop]
                for (int index = 0; index < _DamageCount; index++)
                {
                    float distanceToDamage = distance(input.positionOS, _DamagePoints[index].xyz);
                    nearestWound = min(nearestWound, distanceToDamage - _DamagePoints[index].w);
                }
                clip(nearestWound);
                return 0;
            }
            ENDHLSL
        }
    }
}

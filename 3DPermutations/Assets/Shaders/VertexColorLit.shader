Shader "Custom/VertexColorLit"
{
    Properties
    {
        _Smoothness ("Smoothness", Range(0,1)) = 0.2
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _AmbientStrength ("Ambient Strength", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float _Smoothness;
                float _Metallic;
                float _AmbientStrength;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.color = input.color;
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Normalize inputs
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                
                // Get main light
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                
                // Base color from vertex
                float3 albedo = input.color.rgb;
                
                // Metallic workflow: interpolate between dielectric and metallic
                float3 specularColor = lerp(float3(0.04, 0.04, 0.04), albedo, _Metallic);
                albedo = lerp(albedo, float3(0, 0, 0), _Metallic);
                
                // Diffuse lighting
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = albedo * mainLight.color * NdotL * mainLight.shadowAttenuation;
                
                // Specular (Blinn-Phong for simplicity)
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfDir));
                float specularPower = exp2(10 * _Smoothness + 1); // Convert smoothness to power
                float3 specular = specularColor * pow(NdotH, specularPower) * mainLight.color * mainLight.shadowAttenuation;
                
                // Ambient lighting
                float3 ambient = albedo * SampleSH(normalWS) * _AmbientStrength;
                
                // Ambient specular (simple reflection)
                float3 reflectVector = reflect(-viewDirWS, normalWS);
                float3 ambientSpecular = specularColor * SampleSH(reflectVector) * _Smoothness * 0.5;
                
                // Combine everything
                float3 finalColor = diffuse + specular + ambient + ambientSpecular;
                
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
        
        // Shadow caster pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };
            
            float3 _LightDirection;
            
            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }
            
            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
Shader "Suimono2/surface_scale"
{
    Properties
    {
        _shallowColor ("Shallow Color", Color) = (0.208, 0.373, 0.441, 0.78)
        _depthColor ("Depth Color", Color) = (0.16, 0.281, 0.382, 1)
        _BlendColor ("Blend Color", Color) = (0.36, 1, 0.867, 0.22)
        _FoamColor ("Foam Color", Color) = (0.912, 0.874, 0.791, 1)
        _NormalTexS ("Surface Normal", 2D) = "bump" {}
        _NormalTexD ("Detail Normal", 2D) = "bump" {}
        _HeightTex ("Height Texture", 2D) = "gray" {}
        _FoamTex ("Foam Texture", 2D) = "white" {}
        _WaveTex ("Wave Texture", 2D) = "white" {}
        _overallBrightness ("Overall Brightness", Float) = 1
        _overallTransparency ("Overall Transparency", Range(0, 1)) = 0.82
        _heightScale ("Wave Height", Float) = 0.65
        _lgWaveHeight ("Large Wave Height", Float) = 0.05
        _NormalStrength ("Normal Strength", Float) = 1
        _foamSpeed ("Foam Speed", Float) = 0.05
        _enableFoam ("Enable Foam", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SuimonoURPScale"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalTexS); SAMPLER(sampler_NormalTexS);
            TEXTURE2D(_NormalTexD); SAMPLER(sampler_NormalTexD);
            TEXTURE2D(_HeightTex); SAMPLER(sampler_HeightTex);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_WaveTex); SAMPLER(sampler_WaveTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _shallowColor;
                float4 _depthColor;
                float4 _BlendColor;
                float4 _FoamColor;
                float4 _NormalTexS_ST;
                float4 _NormalTexD_ST;
                float4 _HeightTex_ST;
                float4 _FoamTex_ST;
                float4 _WaveTex_ST;
                float _overallBrightness;
                float _overallTransparency;
                float _heightScale;
                float _lgWaveHeight;
                float _NormalStrength;
                float _foamSpeed;
                float _enableFoam;
                float4 _suimono_Dir;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            float2 FlowDirection()
            {
                float2 dir = float2(_suimono_Dir.x, _suimono_Dir.z);
                return dot(dir, dir) > 0.0001 ? normalize(dir) : float2(0.0, 1.0);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float2 dir = FlowDirection();
                float t = _suimono_Dir.w;
                float heightSample = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, TRANSFORM_TEX(input.uv, _HeightTex) + dir * t, 0).r - 0.5;
                float3 displaced = input.positionOS.xyz;
                displaced.y += heightSample * _heightScale * 0.25 + sin((input.positionOS.x + input.positionOS.z) * 0.02 + t * 6.28318) * _lgWaveHeight;

                VertexPositionInputs posInputs = GetVertexPositionInputs(displaced);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 dir = FlowDirection();
                float t = _suimono_Dir.w;
                float3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalTexS, sampler_NormalTexS, TRANSFORM_TEX(input.uv, _NormalTexS) + dir * t), _NormalStrength);
                float3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalTexD, sampler_NormalTexD, TRANSFORM_TEX(input.uv, _NormalTexD) - dir.yx * t * 0.45), _NormalStrength * 0.35);
                float3 normalWS = normalize(input.normalWS + float3(nA.x + nB.x, 0.0, nA.y + nB.y));

                Light mainLight = GetMainLight();
                float ndl = saturate(dot(normalWS, mainLight.direction));
                float fresnel = pow(1.0 - saturate(dot(normalWS, normalize(GetWorldSpaceViewDir(input.positionWS)))), 3.0);
                float4 color = lerp(_depthColor, _shallowColor, saturate(nA.z * 0.8 + nB.z * 0.2));
                color.rgb = lerp(color.rgb, _BlendColor.rgb, saturate(_BlendColor.a));

                float surfaceTravel = t * max(_foamSpeed * 10.0, 0.8);
                float2 surfaceUv = input.positionWS.xz * 0.035;
                float foamA = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, surfaceUv + dir * surfaceTravel).r;
                float foamB = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, surfaceUv * 0.63 + float2(-dir.y, dir.x) * surfaceTravel * 0.55).r;
                float waveLayer = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, surfaceUv * 0.75 - dir * surfaceTravel * 0.35).r;
                float foam = saturate(max(foamA, foamB * 0.85));
                color.rgb = saturate(color.rgb + (waveLayer - 0.5) * 0.12);
                color.rgb = lerp(color.rgb, _FoamColor.rgb, saturate((foam - 0.62) * 2.9) * saturate(_enableFoam));
                color.rgb = saturate(color.rgb * (0.72 + ndl * 0.28 + fresnel * 0.18) * max(_overallBrightness, 0.0));
                color.a = saturate(0.5 + _overallTransparency * 0.35 + fresnel * 0.1);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}

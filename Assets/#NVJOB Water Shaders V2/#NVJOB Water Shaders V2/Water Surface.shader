// Copyright (c) 2016 Unity Technologies. MIT license - license_unity.txt
// #NVJOB Water Shaders. MIT license - license_nvjob.txt
// #NVJOB Water Shaders v2.0 - https://nvjob.github.io/unity/nvjob-water-shaders-v2
// #NVJOB Nicholas Veselov - https://nvjob.github.io
// Support this asset - https://nvjob.github.io/donate


Shader "#NVJOB/Water Shaders V2/Water Surface" {


//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


Properties{
//----------------------------------------------

[HideInInspector]_AlbedoTex1("Albedo Texture 1", 2D) = "white" {}
[HideInInspector][HDR]_AlbedoColor("Albedo Color", Color) = (0.15,0.161,0.16,1)
[HideInInspector][HDR]_Color("Color", Color) = (1,1,1,1)
[HideInInspector][NoScaleOffset]_AlbedoTex2("Albedo Texture 2", 2D) = "gray" {}
[HideInInspector]_Albedo2Tiling("Albedo 2 Tiling", float) = 1
[HideInInspector]_Albedo2Flow("Albedo 2 Flow", float) = 1
[HideInInspector]_AlbedoIntensity("Brightness", Range(0.1, 5)) = 1
[HideInInspector]_AlbedoContrast("Contrast", Range(-0.5, 3)) = 1
[HideInInspector]_Glossiness("Glossiness", Range(0,1)) = 0.5
[HideInInspector]_Metallic("Metallic", Range(-1,2)) = 0.0
[HideInInspector]_SoftFactor("Soft Factor", Range(0.0001, 1)) = 0.5
[HideInInspector]_NormalMap1("Normal Map 1", 2D) = "bump" {}
[HideInInspector]_NormalMap1Strength("Normal Map 1 Strength", Range(0.001, 10)) = 1
[HideInInspector][NoScaleOffset]_NormalMap2("Normal Map 2", 2D) = "bump" {}
[HideInInspector]_NormalMap2Tiling("Normal Map 2 Tiling", float) = 1.2
[HideInInspector]_NormalMap2Strength("Normal Map 2 Strength", Range(0.001, 10)) = 1
[HideInInspector]_NormalMap2Flow("Normal Map 2 Flow", float) = 0.5
[HideInInspector]_MicrowaveScale("Micro Waves Scale", Range(0.5, 10)) = 1
[HideInInspector]_MicrowaveStrength("Micro Waves Strength", Range(0.001, 1.5)) = 0.5
[HideInInspector]_ParallaxAmount("Parallax Amount", float) = 0.1
[HideInInspector]_ParallaxFlow("Parallax Flow", float) = 40
[HideInInspector]_ParallaxNormal2Offset("Parallax Normal Map 2 Offset", float) = 1
[HideInInspector]_ParallaxNoiseGain("Parallax Noise Gain", Range(0.0 , 1.0)) = 0.3
[HideInInspector]_ParallaxNoiseAmplitude("Parallax Noise Amplitude", Range(0.0 , 5.0)) = 3
[HideInInspector]_ParallaxNoiseFrequency("Parallax Noise Frequency", Range(0.0 , 6.0)) = 1
[HideInInspector]_ParallaxNoiseScale("Parallax Noise Scale", Float) = 1
[HideInInspector]_ParallaxNoiseLacunarity("Parallax Noise Lacunarity", Range(1 , 6)) = 4
[HideInInspector][HDR]_MirrorColor("Mirror Reflection Color", Color) = (1,1,1,0.5)
[HideInInspector]_MirrorDepthColor("Mirror Reflection Depth Color", Color) = (0,0,0,0.5)
[HideInInspector]_MirrorStrength("Reflection Strength", Range(0, 5)) = 1
[HideInInspector]_MirrorSaturation("Reflection Saturation", Range(0, 5)) = 1
[HideInInspector]_MirrorContrast("Reflection Contrast", Range(0, 5)) = 1
[HideInInspector]_MirrorFPOW("Mirror FPOW", Float) = 5.0
[HideInInspector]_MirrorR0("Mirror R0", Float) = 0.01
[HideInInspector]_MirrorWavePow("Reflections Wave Strength", Float) = 1
[HideInInspector]_MirrorWaveScale("Reflections Wave Scale", Float) = 1
[HideInInspector]_MirrorWaveFlow("Reflections Wave Flow", Float) = 5
[HideInInspector]_MirrorReflectionTex("_MirrorReflectionTex", 2D) = "gray" {}
[HideInInspector][HDR]_FoamColor("Foam Color", Color) = (1, 1, 1, 1)
[HideInInspector]_FoamFlow("Foam Flow", Float) = 10
[HideInInspector]_FoamGain("Foam Gain", Float) = 0.6
[HideInInspector]_FoamAmplitude("Foam Amplitude", Float) = 15
[HideInInspector]_FoamFrequency("Foam Frequency", Float) = 4
[HideInInspector]_FoamScale("Foam Scale", Float) = 0.1
[HideInInspector]_FoamLacunarity("Foam Lacunarity", Float) = 5
[HideInInspector]_FoamSoft("Foam Soft", Vector) = (0.25, 0.6, 1, 0)

//----------------------------------------------
}



//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////



SubShader{
///////////////////////////////////////////////////////////////////////////////////////////////////////////////

Tags{ "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+800" "IgnoreProjector" = "True" "RenderType" = "Transparent" "ForceNoShadowCasting" = "True" }
LOD 200
Cull Off
ZWrite On

Pass{
Name "UniversalForward"
Tags{ "LightMode" = "UniversalForward" }

Blend SrcAlpha OneMinusSrcAlpha

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.0

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_AlbedoTex1);
SAMPLER(sampler_AlbedoTex1);
TEXTURE2D(_NormalMap1);
SAMPLER(sampler_NormalMap1);

CBUFFER_START(UnityPerMaterial)
float4 _AlbedoTex1_ST;
float4 _NormalMap1_ST;
half4 _AlbedoColor;
half4 _Color;
half4 _FoamColor;
half _AlbedoIntensity;
half _NormalMap1Strength;
half _Glossiness;
CBUFFER_END

float4 _NvWatersMovement;

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 tangentWS : TEXCOORD2;
    float3 bitangentWS : TEXCOORD3;
};

Varyings vert(Attributes input)
{
    Varyings output;
    VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = positionInputs.positionCS;
    output.uv = input.uv;
    output.normalWS = normalInputs.normalWS;
    output.tangentWS = normalInputs.tangentWS;
    output.bitangentWS = normalInputs.bitangentWS;
    return output;
}

half4 frag(Varyings input) : SV_Target
{
    float2 albedoUv = TRANSFORM_TEX(input.uv, _AlbedoTex1) + _NvWatersMovement.xy;
    float2 normalUv = TRANSFORM_TEX(input.uv, _NormalMap1) + _NvWatersMovement.zw;

    half4 albedo = SAMPLE_TEXTURE2D(_AlbedoTex1, sampler_AlbedoTex1, albedoUv) * _AlbedoColor * _Color;
    half3 tangentNormal = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap1, sampler_NormalMap1, normalUv), _NormalMap1Strength);
    half3 normalWS = normalize(TransformTangentToWorld(tangentNormal, half3x3(input.tangentWS, input.bitangentWS, input.normalWS)));

    Light mainLight = GetMainLight();
    half ndotl = saturate(dot(normalWS, mainLight.direction));
    half3 ambient = SampleSH(normalWS) * albedo.rgb;
    half3 lit = ambient + albedo.rgb * mainLight.color * (0.25h + ndotl * 0.9h);
    half3 specular = pow(saturate(ndotl), 32.0h) * _Glossiness * mainLight.color;
    half3 foamTint = _FoamColor.rgb * 0.025h;

    return half4(saturate(lit * max(_AlbedoIntensity, 0.1h) + specular + foamTint), albedo.a);
}
ENDHLSL
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////
}



//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////



SubShader{
///////////////////////////////////////////////////////////////////////////////////////////////////////////////

Tags{ "Queue" = "Geometry+800" "IgnoreProjector" = "True" "RenderType" = "Transparent" "ForceNoShadowCasting" = "True" }
LOD 200
Cull Off
ZWrite On

CGPROGRAM
#pragma shader_feature_local EFFECT_ALBEDO2
#pragma shader_feature_local EFFECT_NORMALMAP2
#pragma shader_feature_local EFFECT_MICROWAVE
#pragma shader_feature_local EFFECT_PARALLAX
#pragma shader_feature_local EFFECT_MIRROR
#pragma shader_feature_local EFFECT_FOAM
#pragma surface surf Standard alpha:fade vertex:vert noshadowmask noshadow
#pragma target 3.0

//----------------------------------------------

#include "NvWaters.cginc"

//----------------------------------------------

void surf(Input IN, inout SurfaceOutputStandard o) {

#ifdef EFFECT_PARALLAX
float2 offset = OffsetParallax(IN);
IN.uv_AlbedoTex1 -= offset;
IN.uv_NormalMap1 += offset;
float2 uvn = IN.uv_NormalMap1;
uvn.xy += float2(_NvWatersMovement.z, _NvWatersMovement.w);
#ifdef EFFECT_NORMALMAP2
float2 uvnd = IN.uv_NormalMap1 + (offset * _ParallaxNormal2Offset);
uvnd.xy += float2(_NvWatersMovement.z, _NvWatersMovement.w) * _NormalMap2Flow;
#endif
#else
float2 uvn = IN.uv_NormalMap1;
uvn.xy += float2(_NvWatersMovement.z, _NvWatersMovement.w);
#ifdef EFFECT_NORMALMAP2
float2 uvnd = IN.uv_NormalMap1;
uvnd.xy += float2(_NvWatersMovement.z, _NvWatersMovement.w) * _NormalMap2Flow;
#endif
#endif

float2 uv = IN.uv_AlbedoTex1;
uv.xy += float2(_NvWatersMovement.x, _NvWatersMovement.y);
#ifdef EFFECT_ALBEDO2
float2 uvd = IN.uv_AlbedoTex1;
uvd.xy += float2(_NvWatersMovement.x, _NvWatersMovement.y) * _Albedo2Flow;
#endif

float4 tex = tex2D(_AlbedoTex1, uv) * _AlbedoColor;
#ifdef EFFECT_ALBEDO2
tex *= tex2D(_AlbedoTex2, uvd * _Albedo2Tiling);
#endif
tex *= _AlbedoIntensity;
float3 albedo = ((tex - 0.5) * _AlbedoContrast + 0.5).rgb;

float3 normal = UnpackNormal(tex2D(_NormalMap1, uvn)) * _NormalMap1Strength;
#ifdef EFFECT_NORMALMAP2
normal += UnpackNormal(tex2D(_NormalMap2, uvnd * _NormalMap2Tiling)) * _NormalMap2Strength;
#ifdef EFFECT_MICROWAVE
normal -= UnpackNormal(tex2D(_NormalMap2, (uv + uvnd) * 2 * _MicrowaveScale)) * _MicrowaveStrength;
normal = normalize(normal / 3);
#else
normal = normalize(normal / 2);
#endif
#endif

#ifdef EFFECT_MIRROR
o.Emission = (o.Emission + MirrorReflection(IN, normal)) * 0.6;
#endif

#ifdef EFFECT_FOAM
albedo = FoamFactor(IN, albedo, uvn);
#endif

o.Normal = normal;
o.Metallic = _Metallic;
o.Smoothness = _Glossiness;
o.Albedo.rgb = albedo;
o.Alpha = SoftFactor(IN);

}

//----------------------------------------------

ENDCG

///////////////////////////////////////////////////////////////////////////////////////////////////////////////
}


FallBack "Legacy Shaders/Reflective/Bumped Diffuse"
CustomEditor "NVWaterMaterials"


//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
}

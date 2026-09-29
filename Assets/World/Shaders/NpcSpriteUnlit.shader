// Unlit, alpha-blended, double-sided quad shader for NPC billboard sprites.
// Written as its own tiny shader (like BiomeBlend.shader) rather than
// configuring a stock URP/Unlit material for transparency at runtime via
// its Surface Type properties/keywords — that inspector-driven setup is
// easy to get subtly wrong through code (wrong keyword combination silently
// renders opaque), whereas this shader's blend state is just declared
// directly in the pass, guaranteed correct regardless of URP version.
Shader "Custom/NpcSpriteUnlit"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Color;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color;
                // Discard the chroma-keyed-out background outright rather than
                // just alpha-blending it, so it never draws even a faint edge
                // fringe over whatever's behind the sprite.
                clip(col.a - 0.05);
                return col;
            }
            ENDHLSL
        }
    }
}

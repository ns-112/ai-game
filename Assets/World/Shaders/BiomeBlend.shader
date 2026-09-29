// Blends up to 3 tiling ground textures across the terrain using the
// per-vertex biome weights TerrainChunk writes into vertex colors
// (R = biome 0, G = biome 1, B = biome 2 — matching TerrainSettings.biomes order).
// Note: this shader does not cast or receive shadows yet (no ShadowCaster
// pass / shadow sampling) — flag it if you want that added later.
Shader "Custom/BiomeBlend"
{
    Properties
    {
        _Texture0 ("Biome 0 Texture", 2D) = "white" {}
        _Texture1 ("Biome 1 Texture", 2D) = "white" {}
        _Texture2 ("Biome 2 Texture", 2D) = "white" {}
        _Tiling ("World Units Per Tile", Float) = 25

        [Header(Peak Highlight)]
        _PeakColor ("Peak Highlight Color", Color) = (1, 1, 1, 1)
        _PeakHeightStart ("Height Where Highlight Starts", Float) = 30
        _PeakHeightEnd ("Height Where Highlight Is Full Strength", Float) = 45
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Texture0); SAMPLER(sampler_Texture0);
            TEXTURE2D(_Texture1); SAMPLER(sampler_Texture1);
            TEXTURE2D(_Texture2); SAMPLER(sampler_Texture2);
            float _Tiling;

            half4 _PeakColor;
            float _PeakHeightStart;
            float _PeakHeightEnd;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
                float3 normalWS    : TEXCOORD1;
                float fogCoord     : TEXCOORD2;
                float worldHeight  : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = positions.positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                // World-space UV instead of the incoming per-chunk 0..1 uv:
                // that uv resets to 0 at every chunk boundary, so tiling it
                // made the same ground texture repeat in perfect lockstep at
                // every chunk edge — the "obviously repeating pattern" seen
                // in-game. Deriving uv from world position instead makes the
                // tiling continuous across chunks (still seamless within a
                // texture that tiles, just no longer synchronized to chunk
                // size), which breaks up the visible repetition.
                OUT.uv = positions.positionWS.xz / _Tiling;
                OUT.color = IN.color;
                OUT.fogCoord = ComputeFogFactor(OUT.positionHCS.z);
                OUT.worldHeight = positions.positionWS.y;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 tex0 = SAMPLE_TEXTURE2D(_Texture0, sampler_Texture0, IN.uv).rgb;
                half3 tex1 = SAMPLE_TEXTURE2D(_Texture1, sampler_Texture1, IN.uv).rgb;
                half3 tex2 = SAMPLE_TEXTURE2D(_Texture2, sampler_Texture2, IN.uv).rgb;

                half3 albedo = tex0 * IN.color.r + tex1 * IN.color.g + tex2 * IN.color.b;

                // Highlight actual high ground (peaks/ridgelines) instead of
                // outlining the whole terrain mesh — a silhouette-outline
                // technique (extruding along vertex normals) doesn't work on
                // a huge, mostly-flat mesh like terrain: normals point
                // almost straight up everywhere, so extruding along them
                // just raises a second copy of the whole ground surface
                // instead of drawing a thin edge line. Blending by world
                // height instead only affects genuinely elevated terrain,
                // leaving the rest of the ground fully visible.
                half peakBlend = smoothstep(_PeakHeightStart, _PeakHeightEnd, IN.worldHeight);
                albedo = lerp(albedo, _PeakColor.rgb, peakBlend * _PeakColor.a);

                float3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = mainLight.color * NdotL + SampleSH(normalWS) * 0.5;
                half3 color = albedo * lighting;
                color = MixFog(color, IN.fogCoord);

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}

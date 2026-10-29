// The main light's shadow map in 16 x 16 texel blocks, each block's nearest and farthest depth (StationScreenGiFeature's
// ShadowRangePass). StationLighting.hlsl skips its soft filters where every texel they could touch lies on one side of
// the receiver: there the filter's answer is already known, wholly lit or wholly in shadow.
Shader "Hidden/DeathStar/Station Shadow Range"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "Shadow range"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Range
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            Texture2D<float> _StationShadowSource;
            float2 Range(Varyings input) : SV_Target
            {
                uint w, h; _StationShadowSource.GetDimensions(w, h);
                int2 base = (int2)input.positionCS.xy * 16, last = int2(w, h) - 1;
                float lo = 1e9, hi = -1e9;
                [loop] for (int y = 0; y < 16; y++)
                [unroll] for (int x = 0; x < 16; x++)
                {
                    float d = _StationShadowSource.Load(int3(min(base + int2(x, y), last), 0));
                    lo = min(lo, d); hi = max(hi, d);
                }
                return float2(lo, hi);
            }
            ENDHLSL
        }
    }
}

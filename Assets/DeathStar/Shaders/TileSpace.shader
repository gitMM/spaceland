// Backdrop for the tile gallery: near-black space, a faint galactic band, stars that stay about a pixel wide, and a
// planet lit by the same sun as the station, with a thin atmosphere at its limb.
Shader "DeathStar/Space Sky"
{
    Properties
    {
        _Zenith("Zenith", Color) = (0.010, 0.013, 0.022, 1)
        _Nadir("Nadir", Color) = (0.004, 0.005, 0.008, 1)
        _Band("Galactic band", Color) = (0.035, 0.04, 0.07, 1)
        _Stars("Star brightness", Float) = 1.6
        _SunDir("Direction to the sun", Vector) = (0, 1, 0, 0)
        _PlanetDir("Direction to the planet", Vector) = (0, -1, 0, 0)
        _PlanetSize("Planet angular radius (sine)", Float) = 0.4
        _PlanetColor("Planet colour", Color) = (0.3, 0.35, 0.4, 1)
        _Atmosphere("Atmosphere", Color) = (0.25, 0.5, 1.1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "TileCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith;
                half4 _Nadir;
                half4 _Band;
                half _Stars;
                float4 _SunDir;
                float4 _PlanetDir;
                float _PlanetSize;
                half4 _PlanetColor;
                half4 _Atmosphere;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
            };

            Varyings Vert(float4 positionOS : POSITION)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(positionOS.xyz);
                o.dir = positionOS.xyz;
                return o;
            }

            float StarLayer(float3 d, float scale, float density)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                if (TileHash3(cell) > density) return 0;
                float3 jitter = float3(TileHash3(cell + 17.1), TileHash3(cell + 41.3), TileHash3(cell + 73.9)) * 0.6 + 0.2;
                float3 off = frac(p) - jitter;
                float dist = length(off - d * dot(off, d));
                float r = max(length(fwidth(p)) * 0.7, 0.03);
                float mag = pow(TileHash3(cell + 5.5), 6) * 3 + 0.2;
                return mag * exp(-dist * dist / (r * r)) * min(1, 0.0016 / (r * r));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half3 col = lerp(_Nadir.rgb, _Zenith.rgb, saturate(d.y * 0.5 + 0.5));
                float n = (TileNoise(d.xy * 4.1 + 2) + TileNoise(d.yz * 4.3 + 5) + TileNoise(d.zx * 3.9 + 9)) / 3;
                float n2 = (TileNoise(d.xy * 13.1) + TileNoise(d.yz * 12.7 + 3) + TileNoise(d.zx * 13.3 + 1)) / 3;
                float band = exp(-pow(dot(d, normalize(float3(0.35, 0.82, 0.45))) * 3.2, 2));
                col += _Band.rgb * band * saturate(n * 1.4 - 0.25) * (0.6 + 0.8 * n2);
                float stars = StarLayer(d, 160, 0.06) + StarLayer(d, 420, 0.05 + band * 0.08) * 0.6;
                col += stars * _Stars * lerp(half3(1, 0.92, 0.82), half3(0.8, 0.9, 1.1), TileHash(floor(d.xy * 300)));

                float3 P = normalize(_PlanetDir.xyz), S = normalize(_SunDir.xyz);
                float sinR = _PlanetSize, cosR = sqrt(1 - sinR * sinR);
                float c = dot(d, P);
                float3 o = d - P * c;
                float aa = fwidth(c) * 1.5;
                float disc = smoothstep(cosR - aa, cosR + aa, c);
                if (c > cosR - aa)
                {
                    float r = saturate(length(o) / sinR);
                    float3 n = normalize(o / sinR - P * sqrt(saturate(1 - r * r)));
                    float ndl = dot(n, S);
                    float day = smoothstep(-0.08, 0.35, ndl);
                    float bands = TileNoise(float2(dot(n, float3(0.3, 0.9, 0.2)) * 9, dot(n, float3(0.8, -0.2, 0.5)) * 3))
                                * 0.6 + TileNoise(float2(n.x, n.z) * 14 + n.y * 5) * 0.4;
                    half3 surface = _PlanetColor.rgb * (0.004 + day * (0.55 + 0.6 * bands));
                    surface += _Atmosphere.rgb * pow(r, 5) * saturate(ndl + 0.3) * 0.5;
                    col = lerp(col, surface, disc);
                }
                // Thin glow just outside the limb, on the sunward side.
                float beyond = max(0, acos(clamp(c, -1, 1)) - asin(sinR));
                float3 limbDir = normalize(o + 1e-5);
                col += _Atmosphere.rgb * exp(-beyond * 60) * saturate(dot(limbDir, S) + 0.25) * 0.35 * (1 - disc);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

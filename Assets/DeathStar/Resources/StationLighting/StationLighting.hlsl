// Adapted from C:/Transhuman/Button, 2026-10-28.
#ifndef STATION_LIGHTING_INCLUDED
#define STATION_LIGHTING_INCLUDED
// URP strips its own AO keyword when the built-in SSAO feature is absent.
// Keep SSILVB's material variant independent, while reusing URP's AO accessors.
#if defined(_STATION_SCREEN_OCCLUSION)
    #define _SCREEN_SPACE_OCCLUSION 1
#endif
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"


// One presentation contract for station surfaces and light sources.
// Uncontrolled study scenes retain URP defaults; no material keywords are needed.
float _StationLightingControlled;
TEXTURE2D_X(_StationLocalLightTexture);
float _StationLocalLightActive, _StationLocalLampScreenGi;
half3 StationLocalIrradiance(float2 uv)
{
    #if defined(_SURFACE_TYPE_TRANSPARENT)
        return 0;
    #else
        if(_StationLocalLightActive<.5)return 0;
        uint2 pixel=(uint2)clamp(uv*_ScaledScreenParams.xy,0,_ScaledScreenParams.xy-1);
        return LOAD_TEXTURE2D_X(_StationLocalLightTexture,pixel).rgb;
    #endif
}
float4 _StationDirectLighting; // main, additional, received shadows, filter width
float4 _StationScreenOcclusion; // Station SSILVB AO amount, direct-light AO amount
float4 StationDirectSettings()
{
    return _StationLightingControlled > .5 ? _StationDirectLighting : float4(1,1,1,1);
}
// Sun contact shadows, traced through this camera's depth by StationScreenGiFeature. x is 1 only while the
// texture belongs to the camera being drawn.
TEXTURE2D_X(_StationContactShadowTexture);
float4 _StationContactShadowParams;
// screenUV: the pixel's normalized screen position (InputData.normalizedScreenSpaceUV), which matches how the
// full-screen pass wrote the texture.
// A surface's own shadow on the sun beyond the shadow map (the relief trace's plates); set before shading.
static half StationSurfaceShadow = 1;
half StationContactShadow(float2 screenUV)
{
    #if defined(_SURFACE_TYPE_TRANSPARENT)
        return 1;
    #else
        if (_StationContactShadowParams.x < .5 || StationDirectSettings().z <= 0) return 1;
        uint2 pixel = (uint2)clamp(screenUV * _ScaledScreenParams.xy, 0, _ScaledScreenParams.xy - 1);
        return LOAD_TEXTURE2D_X(_StationContactShadowTexture, pixel).r;
    #endif
}
// Integral of a unit-height triangular kernel, with support [-radius,+radius].
// Integrating over real texel cells keeps subtexel motion continuous.
float StationShadowTentIntegral(float x, float radius)
{
    float left = clamp(x + radius, 0, radius);
    float right = clamp(radius - x, 0, radius);
    return x < 0 ? left * left / (2 * radius) : radius - right * right / (2 * radius);
}
float StationShadowTexelWeight(float offset, float radius)
{
    return StationShadowTentIntegral(offset + .5, radius) - StationShadowTentIntegral(offset - .5, radius);
}
// XY: receiver-plane depth gradient; Z: depth units to metres along the light
// ray; W: one shadow texel in metres at this receiver (also for point lights).
float4 StationShadowReceiverPlane(float4x4 worldToShadow,float3 positionWS,half3 normalWS,
    half3 lightDirection,float2 texelSize,bool perspective)
{
    float3 n = normalize(normalWS);
    float3 tangent = normalize(cross(abs(n.y)<.95 ? float3(0,1,0) : float3(1,0,0),n));
    float3 bitangent = cross(n,tangent);
    float4 p = mul(worldToShadow,float4(positionWS,1));
    float4 tx = mul(worldToShadow,float4(tangent,0));
    float4 ty = mul(worldToShadow,float4(bitangent,0));
    float3 dx = tx.xyz, dy = ty.xyz;
    if(perspective) {
        dx = (tx.xyz - p.xyz / p.w * tx.w) / p.w;
        dy = (ty.xyz - p.xyz / p.w * ty.w) / p.w;
    }
    float determinant = dx.x * dy.y - dx.y * dy.x;
    float2 gradient = abs(determinant)>1e-12 ?
        float2(dx.z*dy.y-dy.z*dx.y,dy.z*dx.x-dx.z*dy.x)/determinant : 0;
    float3 u = worldToShadow[0].xyz, v = worldToShadow[1].xyz, z = worldToShadow[2].xyz;
    if (perspective) {
        float3 w = worldToShadow[3].xyz;
        u = (u-p.x/p.w*w)/p.w;
        v = (v-p.y/p.w*w)/p.w;
        z = (z-p.z/p.w*w)/p.w;
    }
    float depthToWorld = rcp(max(abs(dot(z,lightDirection)),1e-8));
    float texelWorld = max(texelSize.x/max(length(u),1e-8),texelSize.y/max(length(v),1e-8));
    return float4(gradient,depthToWorld,texelWorld);
}
bool StationUseWideShadowFilter(half4 parameters)
{
    // A bounce's diffuse sources (StationScreenGiSource) take the original filter: the screen GI spreads their light
    // far wider than the wide filter's penumbra, so its hundred-odd taps would be lost there.
    #if defined(STATION_GI_SOURCE)
        return false;
    #elif defined(_SHADOWS_SOFT_LOW) || defined(_SHADOWS_SOFT_MEDIUM) || defined(_SHADOWS_SOFT_HIGH)
        return StationDirectSettings().w > 1;
    #elif defined(_SHADOWS_SOFT)
        return StationDirectSettings().w > 1 && parameters.y > SOFT_SHADOW_QUALITY_OFF;
    #else
        return false;
    #endif
}
// The wide filter's radius, in shadow map texels.
float StationWideShadowRadius(ShadowSamplingData data)
{
    float baseRadius = 3.5;
    #if defined(_SHADOWS_SOFT_LOW)
        baseRadius = 1;
    #elif defined(_SHADOWS_SOFT_MEDIUM)
        baseRadius = 2.5;
    #elif !defined(_SHADOWS_SOFT_HIGH)
        baseRadius = data.softShadowQuality == SOFT_SHADOW_QUALITY_LOW ? 1 :
            (data.softShadowQuality == SOFT_SHADOW_QUALITY_MEDIUM ? 2.5 : 3.5);
    #endif
    return baseRadius * clamp(StationDirectSettings().w,1,3);
}
// The main light's shadow map in 16 x 16 texel blocks, each block's least and greatest depth (StationShadowRange.shader,
// drawn by StationScreenGiFeature); x of the parameters is 1 while it belongs to the shadow map being sampled.
Texture2D<float2> _StationShadowRange;
float4 _StationShadowRangeParams;
// Whether every texel within reach texels of shadow coordinate uv compares alike against receiver depths from lo to hi:
// 1 all lit, 0 all in shadow, -1 either way or no range to go by. Where they agree, every filter over them gives that
// answer without its taps.
half StationShadowRangeTest(float2 uv, float reach, float lo, float hi, float4 shadowmapSize)
{
    if (_StationShadowRangeParams.x < .5) return -1;
    float2 texel = uv * shadowmapSize.zw;
    int2 last = (int2)shadowmapSize.zw - 1;
    int2 b0 = clamp((int2)floor(texel - reach), 0, last) >> 4, b1 = clamp((int2)floor(texel + reach), 0, last) >> 4;
    float least = 1e9, most = -1e9;
    [loop] for (int y = b0.y; y <= b1.y; y++)
    [loop] for (int x = b0.x; x <= b1.x; x++)
    {
        float2 range = _StationShadowRange.Load(int3(x, y, 0));
        least = min(least, range.x); most = max(most, range.y);
    }
    // (a margin of two 16-bit depth steps, in case the hardware rounds the receiver's depth to the shadow map's)
    #if UNITY_REVERSED_Z
        // lit where the receiver's depth is at least the texel's
        if (lo - 3e-5 >= most) return 1;
        if (hi + 3e-5 < least) return 0;
    #else
        if (hi + 3e-5 <= least) return 1;
        if (lo - 3e-5 > most) return 0;
    #endif
    return -1;
}
real StationSampleShadowmap(TEXTURE2D_SHADOW_PARAM(shadowMap, shadowSampler), float4 coord,
    ShadowSamplingData data, half4 parameters, bool perspective, float4 receiverPlane)
{
    // The original filter anchors contact shadows. Its result already includes
    // the light's strength, so blend only after both filters apply that strength.
    real contactShadow = SampleShadowmap(TEXTURE2D_SHADOW_ARGS(shadowMap, shadowSampler),coord,data,parameters,perspective);
    if (perspective) coord.xyz /= coord.w;
    if (BEYOND_SHADOW_FAR(coord)) return 1;
    float radius = StationWideShadowRadius(data);
    float2 depthGradient = receiverPlane.xy;
    float nearestBlocker = 1e20;
    // Search the original kernel's core for nearby blockers, using raw depth.
    // Receiver-plane correction excludes the receiver itself from this search.
    [unroll] for (int by = -1; by <= 1; by++) {
        [unroll] for (int bx = -1; bx <= 1; bx++) {
            // A point fetch returns the texel centre's depth, not the depth at
            // the requested fractional UV. Evaluate the receiver there too.
            float2 uv = (floor(coord.xy*data.shadowmapSize.zw)+float2(bx,by)+.5)*data.shadowmapSize.xy;
            float2 offset = uv-coord.xy;
            float blockerDepth = SAMPLE_TEXTURE2D_LOD(shadowMap,sampler_PointClamp,uv,0).r;
            float receiverDepth = coord.z+dot(depthGradient,offset);
            #if UNITY_REVERSED_Z
                float gap = (blockerDepth-receiverDepth)*receiverPlane.z;
            #else
                float gap = (receiverDepth-blockerDepth)*receiverPlane.z;
            #endif
            if(gap>0) nearestBlocker = min(nearestBlocker,gap);
        }
    }
    // A contact blocker closer than this footprint cannot support the wide
    // penumbra. This also accounts for grazing receivers and cascade texel size.
    float footprint = radius*(receiverPlane.w + dot(abs(depthGradient),data.shadowmapSize.xy)*receiverPlane.z);
    float widen = smoothstep(footprint,2*footprint,nearestBlocker);
    if(widen<=0) return contactShadow;
    float2 texel = coord.xy * data.shadowmapSize.zw - .5;
    int reach = (int)ceil(radius + .5);
    float2 first = floor(texel) - reach;
    int pairs = reach + 1; // Includes every texel touched by the kernel.
    float visibility = 0, weightSum = 0;
    [loop] for (int y = 0; y < pairs; y++) {
        float cellY = first.y + 2*y;
        float ay = StationShadowTexelWeight(cellY-texel.y,radius);
        float by = StationShadowTexelWeight(cellY+1-texel.y,radius);
        float wy = ay+by;
        float v = (cellY+.5+by/max(wy,1e-6))*data.shadowmapSize.y;
        [loop] for (int x = 0; x < pairs; x++) {
            float cellX = first.x + 2*x;
            float ax = StationShadowTexelWeight(cellX-texel.x,radius);
            float bx = StationShadowTexelWeight(cellX+1-texel.x,radius);
            float wx = ax+bx;
            float u = (cellX+.5+bx/max(wx,1e-6))*data.shadowmapSize.x;
            float weight = wx*wy;
            if(weight>0) {
                float2 uv = float2(u,v);
                // Compare against the actual receiving plane at each tap.
                float depth = coord.z + dot(depthGradient,uv-coord.xy);
                // Hardware PCF compares four neighbouring depths to one value.
                // Cover their receiver-plane slope as well as the tap centre;
                // otherwise a lit sloping face shadows itself in a texel grid.
                float slopeBias = dot(abs(depthGradient),data.shadowmapSize.xy);
                #if UNITY_REVERSED_Z
                    depth += slopeBias;
                #else
                    depth -= slopeBias;
                #endif
                visibility += weight*SAMPLE_TEXTURE2D_SHADOW(shadowMap,shadowSampler,float3(uv,depth));
                weightSum += weight;
            }
        }
    }
    visibility /= max(weightSum,1e-6);
    return lerp(contactShadow,LerpWhiteTo(visibility,parameters.x),widen);
}
half StationMainRealtimeShadow(float4 coord, half4 parameters, ShadowSamplingData data,float3 positionWS,half3 normalWS)
{
    #if !defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        return 1;
    #elif defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
        return SampleScreenSpaceShadowmap(coord);
    #else
        if (BEYOND_SHADOW_FAR(coord)) return 1;
        // Hard shadows and the original URP width need no receiver-plane math. (URP's widest filter, 7 x 7 texels and
        // their bilinear neighbours, stays within 6 texels.)
        if (!StationUseWideShadowFilter(parameters))
        {
            // (only URP's medium and high filters, 9 and 16 taps, cost more than the test)
            #if defined(_SHADOWS_SOFT_MEDIUM) || defined(_SHADOWS_SOFT_HIGH)
            bool tested = true;
            #elif defined(_SHADOWS_SOFT) && !defined(_SHADOWS_SOFT_LOW)
            bool tested = parameters.y > SOFT_SHADOW_QUALITY_LOW && data.softShadowQuality > SOFT_SHADOW_QUALITY_LOW;
            #else
            bool tested = false;
            #endif
            half known = tested ? StationShadowRangeTest(coord.xy,6,coord.z,coord.z,data.shadowmapSize) : -1;
            if (known >= 0) return LerpWhiteTo(known,parameters.x);
            return SampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture,sampler_LinearClampCompare),coord,data,parameters,false);
        }
        #ifdef _MAIN_LIGHT_SHADOWS_CASCADE
        int cascade = ComputeCascadeIndex(positionWS);
        #else
        int cascade = 0;
        #endif
        float4 receiverPlane = StationShadowReceiverPlane(_MainLightWorldToShadow[cascade],positionWS,normalWS,
            GetMainLight().direction,data.shadowmapSize.xy,false);
        {
            // The wide filter's taps, their bilinear neighbours and the original filter inside them; the receiving
            // plane's depths over that reach, slope bias included.
            float reach = max(ceil(StationWideShadowRadius(data) + .5) + 2, 6);
            float spread = dot(abs(receiverPlane.xy),data.shadowmapSize.xy) * (reach + 2);
            half known = StationShadowRangeTest(coord.xy,reach,coord.z - spread,coord.z + spread,data.shadowmapSize);
            if (known >= 0) return LerpWhiteTo(known,parameters.x);
        }
        return StationSampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture,sampler_LinearClampCompare),coord,data,parameters,false,receiverPlane);
    #endif
}
half StationAdditionalRealtimeShadow(int index,float3 positionWS,half3 direction,half4 parameters,ShadowSamplingData data,half3 normalWS)
{
    #if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
        int slice = parameters.w;
        if (slice < 0) return 1;
        if (parameters.z) slice += CubeMapFaceID(-direction);
        #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
            float4x4 worldToShadow = _AdditionalLightsWorldToShadow_SSBO[slice];
        #else
            float4x4 worldToShadow = _AdditionalLightsWorldToShadow[slice];
        #endif
        float4 coord = mul(worldToShadow,float4(positionWS,1));
        if (!StationUseWideShadowFilter(parameters))
            return SampleShadowmap(TEXTURE2D_ARGS(_AdditionalLightsShadowmapTexture,sampler_LinearClampCompare),coord,data,parameters,true);
        float4 receiverPlane = StationShadowReceiverPlane(worldToShadow,positionWS,normalWS,direction,data.shadowmapSize.xy,true);
        return StationSampleShadowmap(TEXTURE2D_ARGS(_AdditionalLightsShadowmapTexture,sampler_LinearClampCompare),coord,data,parameters,true,receiverPlane);
    #else
        return 1;
    #endif
}
AmbientOcclusionFactor StationAmbientOcclusion(float2 uv, half surfaceOcclusion)
{
    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(uv);
    if (_StationLightingControlled > .5) {
        ao.indirectAmbientOcclusion = lerp(1,ao.indirectAmbientOcclusion,_StationScreenOcclusion.x);
        ao.directAmbientOcclusion = lerp(1,ao.indirectAmbientOcclusion,_StationScreenOcclusion.y);
    }
    ao.indirectAmbientOcclusion = min(ao.indirectAmbientOcclusion,surfaceOcclusion);
    return ao;
}
half StationMainShadow(float4 shadowCoord, float3 positionWS, half4 shadowMask, half4 occlusionProbeChannels,half3 normalWS)
{
    half4 shadowParams = GetMainLightShadowParams();
    half realtimeShadow = StationMainRealtimeShadow(shadowCoord, shadowParams, GetMainLightShadowSamplingData(),positionWS,normalWS);

    #ifdef CALCULATE_BAKED_SHADOWS
        half bakedShadow = BakedShadow(shadowMask, occlusionProbeChannels, shadowParams);
    #else
        half bakedShadow = half(1.0);
    #endif

    #ifdef MAIN_LIGHT_CALCULATE_SHADOWS
        half shadowFade = GetMainLightShadowFade(positionWS);
    #else
        half shadowFade = half(1.0);
    #endif

    return lerp(1, MixRealtimeAndBakedShadows(realtimeShadow, bakedShadow, shadowFade), StationDirectSettings().z);
}

half StationAdditionalShadow(int lightIndex, float3 positionWS, half3 lightDirection, half4 shadowMask, half4 occlusionProbeChannels,half3 normalWS)
{
    half4 shadowParams = GetAdditionalLightShadowParams(lightIndex);
    ShadowSamplingData samplingData = GetAdditionalLightShadowSamplingData(lightIndex);
    half realtimeShadow = StationAdditionalRealtimeShadow(lightIndex, positionWS, lightDirection, shadowParams, samplingData,normalWS);

    #ifdef CALCULATE_BAKED_SHADOWS
        // This fading of the baked shadow using the light's shadow strength parameter needs
        // to be guarded against the Real-Time Shadow keyword as _AdditionalShadowParams is
        // only included in URP Shaders and updated when real time additional shadows are enabled.
        #ifndef ADDITIONAL_LIGHT_CALCULATE_SHADOWS
            shadowParams.x = half(1.0);
        #endif
        half bakedShadow = BakedShadow(shadowMask, occlusionProbeChannels, shadowParams);
    #else
        half bakedShadow = half(1.0);
    #endif

    #ifdef ADDITIONAL_LIGHT_CALCULATE_SHADOWS
        half shadowFade = GetAdditionalLightShadowFade(positionWS);
    #else
        half shadowFade = half(1.0);
    #endif

    return lerp(1, MixRealtimeAndBakedShadows(realtimeShadow, bakedShadow, shadowFade), StationDirectSettings().z);
}


Light StationMainLightAtReceiver(Light light, float4 shadowCoord, float3 positionWS, half4 shadowMask,half3 normalWS)
{
    light.color *= StationDirectSettings().x;
    light.shadowAttenuation = StationDirectSettings().z <= 0 ? 1 : StationMainShadow(shadowCoord,positionWS,shadowMask,_MainLightOcclusionProbes,normalWS);
    #if defined(_LIGHT_COOKIES)
    light.color *= SampleMainLightCookie(positionWS);
    #endif
    return light;
}
Light StationMainLight(float4 shadowCoord, float3 positionWS, half4 shadowMask,half3 normalWS)
{
    return StationMainLightAtReceiver(GetMainLight(),shadowCoord,positionWS,shadowMask,normalWS);
}
Light StationMainLight(InputData inputData, half4 shadowMask, AmbientOcclusionFactor ao)
{
    Light light = StationMainLight(inputData.shadowCoord,inputData.positionWS,shadowMask,inputData.tangentToWorld[2]);
    light.shadowAttenuation *= StationContactShadow(inputData.normalizedScreenSpaceUV) * StationSurfaceShadow;
    light.color *= ao.directAmbientOcclusion;
    return light;
}
Light StationMainLight(InputData inputData)
{
    return StationMainLight(inputData,CalculateShadowMask(inputData),StationAmbientOcclusion(inputData.normalizedScreenSpaceUV,1));
}
int StationAdditionalLightIndex(uint i)
{
    #if USE_CLUSTER_LIGHT_LOOP
    return i;
    #else
    return GetPerObjectLightIndex(i);
    #endif
}
// Apply receiver-dependent visibility to an already fetched light. Transmission
// offsets only the receiver; its direction and range stay at the visible surface.
Light StationAdditionalLightAtReceiver(int index, Light light, float3 positionWS, half4 shadowMask, half3 normalWS)
{
    light.color *= StationDirectSettings().y;
    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    half4 channels = _AdditionalLightsBuffer[index].occlusionProbeChannels;
    #else
    half4 channels = _AdditionalLightsOcclusionProbes[index];
    #endif
    light.shadowAttenuation = StationDirectSettings().z <= 0 ? 1 : StationAdditionalShadow(index,positionWS,light.direction,shadowMask,channels,normalWS);
    #if defined(_LIGHT_COOKIES)
    light.color *= SampleAdditionalLightCookie(index,positionWS);
    #endif
    return light;
}
Light StationAdditionalLight(uint i, float3 positionWS, half4 shadowMask,half3 normalWS)
{
    int index = StationAdditionalLightIndex(i);
    return StationAdditionalLightAtReceiver(index,GetAdditionalPerObjectLight(index,positionWS),positionWS,shadowMask,normalWS);
}
Light StationAdditionalLight(uint i, InputData inputData, half4 shadowMask, AmbientOcclusionFactor ao)
{
    Light light = StationAdditionalLight(i,inputData.positionWS,shadowMask,inputData.tangentToWorld[2]);
    light.color *= ao.directAmbientOcclusion;
    return light;
}
// URP 17 PBR composition with the shared light/occlusion accessors. Retains
// URP BRDFs, Forward+ loops, cookies, rendering layers and shadow distance fades.
#if defined(STATION_CLOTH_TRANSMISSION)
#include "StationClothScattering.hlsl"

// Share the light fetch with reflection; only the displaced shadow/cookie
// receiver needs a second visibility evaluation for transmission.
Light StationAdditionalLightWithTransmission(uint i, InputData inputData, half4 shadowMask,
    AmbientOcclusionFactor ao, StationClothTransmissionData cloth, inout half3 transmitted)
{
    int index = StationAdditionalLightIndex(i);
    Light source = GetAdditionalPerObjectLight(index,inputData.positionWS);
    transmitted += StationClothAdditionalTransmission(index,source,inputData,shadowMask,cloth);
    Light light = StationAdditionalLightAtReceiver(index,source,inputData.positionWS,shadowMask,inputData.tangentToWorld[2]);
    light.color *= ao.directAmbientOcclusion;
    return light;
}
#endif
#if defined(STATION_WOVEN_CLOTH)
#include "StationFabricLighting.hlsl"
#define StationMaterialBRDF(brdf,coat,light,n,v,mask,off) StationFabricBRDF(brdf,coat,light,n,v,mask,off,fabric)
#else
#define StationMaterialBRDF(brdf,coat,light,n,v,mask,off) LightingPhysicallyBased(brdf,coat,light,n,v,mask,off)
#endif
half4 StationFragmentPBR(InputData inputData, SurfaceData surfaceData
    #if defined(STATION_WOVEN_CLOTH)
    , StationFabricLightingData fabric
    #endif
    #if defined(STATION_CLOTH_TRANSMISSION)
    , StationClothTransmissionData cloth
    #endif
    #if defined(STATION_FELT_SHEEN)
    , half feltSheen
    #endif
)
{
    #if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
    #else
    bool specularHighlightsOff = false;
    #endif
    BRDFData brdfData;

    // NOTE: can modify "surfaceData"...
    InitializeBRDFData(surfaceData, brdfData);

    #if defined(DEBUG_DISPLAY)
    half4 debugColor;

    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
    {
        return debugColor;
    }
    #endif

    // Clear-coat calculation...
    BRDFData brdfDataClearCoat = CreateClearCoatBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = StationAmbientOcclusion(inputData.normalizedScreenSpaceUV, surfaceData.occlusion);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    #if defined(STATION_CLOTH_TRANSMISSION)
    Light mainSource = GetMainLight();
    Light mainLight = StationMainLightAtReceiver(mainSource,inputData.shadowCoord,inputData.positionWS,shadowMask,inputData.tangentToWorld[2]);
    mainLight.color *= aoFactor.directAmbientOcclusion;
    half3 transmitted = StationClothMainTransmission(mainSource,inputData,shadowMask,cloth);
    #else
    Light mainLight = StationMainLight(inputData, shadowMask, aoFactor);
    #endif

    #if defined(STATION_FELT_SHEEN)
    half3 feltSheenColor = 0;
    [branch] if (feltSheen > 0) {
        half fuzz = pow(1-saturate(dot(inputData.normalWS,inputData.viewDirectionWS)),2.5) * feltSheen;
        // Use the same unmixed ambient and shadowed main light as before,
        // without another shadow, cookie or screen-occlusion lookup.
        half3 irradiance = inputData.bakedGI * aoFactor.indirectAmbientOcclusion + mainLight.color *
            (mainLight.shadowAttenuation * mainLight.distanceAttenuation * saturate(dot(inputData.normalWS,mainLight.direction)));
        feltSheenColor = surfaceData.albedo * fuzz * irradiance;
    }
    #endif

    // NOTE: We don't apply AO to the GI here because it's done in the lighting calculation below...
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);

    lightingData.giColor = GlobalIllumination(brdfData, brdfDataClearCoat, surfaceData.clearCoatMask,
                                              inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
                                              inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = StationMaterialBRDF(brdfData, brdfDataClearCoat,
                                                              mainLight,
                                                              inputData.normalWS, inputData.viewDirectionWS,
                                                              surfaceData.clearCoatMask, specularHighlightsOff);
    }

    #if defined(_ADDITIONAL_LIGHTS) || (defined(_ADDITIONAL_LIGHTS_VERTEX) && defined(STATION_WOVEN_CLOTH))
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

        #if defined(STATION_CLOTH_TRANSMISSION)
        Light light = StationAdditionalLightWithTransmission(lightIndex,inputData,shadowMask,aoFactor,cloth,transmitted);
        #else
        Light light = StationAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        #endif

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += StationMaterialBRDF(brdfData, brdfDataClearCoat, light,
                                                                          inputData.normalWS, inputData.viewDirectionWS,
                                                                          surfaceData.clearCoatMask, specularHighlightsOff);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        #if defined(STATION_CLOTH_TRANSMISSION)
        Light light = StationAdditionalLightWithTransmission(lightIndex,inputData,shadowMask,aoFactor,cloth,transmitted);
        #else
        Light light = StationAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        #endif

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += StationMaterialBRDF(brdfData, brdfDataClearCoat, light,
                                                                          inputData.normalWS, inputData.viewDirectionWS,
                                                                          surfaceData.clearCoatMask, specularHighlightsOff);
        }
    LIGHT_LOOP_END
    #endif

    #if defined(_ADDITIONAL_LIGHTS_VERTEX) && !defined(STATION_WOVEN_CLOTH)
    lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse * StationDirectSettings().y * aoFactor.directAmbientOcclusion;
        #if defined(STATION_CLOTH_TRANSMISSION)
        // Vertex Lambert lighting cannot carry backlight. Keep only transmission
        // per pixel in this quality mode, without changing the reflection path.
        if (any(cloth.color) && StationDirectSettings().y > 0) {
            uint pixelLightCount = GetAdditionalLightsCount();
            LIGHT_LOOP_BEGIN(pixelLightCount)
                int index = StationAdditionalLightIndex(lightIndex);
                transmitted += StationClothAdditionalTransmission(index,GetAdditionalPerObjectLight(index,inputData.positionWS),inputData,shadowMask,cloth);
            LIGHT_LOOP_END
        }
        #endif
    #endif

    #if defined(STATION_CLOTH_TRANSMISSION)
    lightingData.emissionColor += cloth.color * transmitted * aoFactor.directAmbientOcclusion;
    #endif

    lightingData.additionalLightsColor += brdfData.diffuse * StationLocalIrradiance(inputData.normalizedScreenSpaceUV);
    half4 color = CalculateFinalColor(lightingData, surfaceData.alpha);
#if REAL_IS_HALF
    // Clamp any half.inf+ to HALF_MAX
    color = min(color, HALF_MAX);
#endif
    #if defined(STATION_FELT_SHEEN)
    color.rgb += feltSheenColor;
    #endif
    return color;
}
#undef StationMaterialBRDF
#endif

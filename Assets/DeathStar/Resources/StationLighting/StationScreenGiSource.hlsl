// Adapted from C:/Transhuman/Button, 2026-10-28.
#ifndef STATION_SCREEN_GI_SOURCE_INCLUDED
#define STATION_SCREEN_GI_SOURCE_INCLUDED
// Transport directly lit diffuse radiance and material emission, without
// highlights, tone mapping or the previous screen GI result.
struct StationGiSourceOutput {
    float4 radiance : SV_Target0;
    float4 receiver : SV_Target1;
};
// The same geometric-normal gather used by the forward materials, without
// ambient/probe fallback or any direct/specular/emissive lighting.
StationGiSourceOutput StationGiSource(InputData inputData, SurfaceData surfaceData, float bounceStrength, float receiveStrength)
{
    BRDFData brdf;
    InitializeBRDFData(surfaceData,brdf);
    // The trace measures visibility for this bounce. Applying ambient occlusion
    // again at source and receiver incorrectly kills light at the contacts we trace.
    AmbientOcclusionFactor ao;
    ao.directAmbientOcclusion=1;ao.indirectAmbientOcclusion=1;
    half4 shadowMask=CalculateShadowMask(inputData);
    Light main=StationMainLight(inputData,shadowMask,ao);
    uint layers=GetMeshRenderingLayer();
    float3 direct=0;
    #ifdef _LIGHT_LAYERS
    if(IsMatchingLightLayer(main.layerMask,layers))
    #endif
        direct=main.color*(main.distanceAttenuation*main.shadowAttenuation*saturate(dot(inputData.normalWS,main.direction)));
    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount=GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for(uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++) {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light=StationAdditionalLight(lightIndex,inputData,shadowMask,ao);
        #ifdef _LIGHT_LAYERS
        if(IsMatchingLightLayer(light.layerMask,layers))
        #endif
            direct+=light.color*(light.distanceAttenuation*light.shadowAttenuation*saturate(dot(inputData.normalWS,light.direction)));
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light=StationAdditionalLight(lightIndex,inputData,shadowMask,ao);
        #ifdef _LIGHT_LAYERS
        if(IsMatchingLightLayer(light.layerMask,layers))
        #endif
            direct+=light.color*(light.distanceAttenuation*light.shadowAttenuation*saturate(dot(inputData.normalWS,light.direction)));
    LIGHT_LOOP_END
    #endif
    #if defined(_ADDITIONAL_LIGHTS_VERTEX)
        direct+=inputData.vertexLighting*StationDirectSettings().y*ao.directAmbientOcclusion;
    #endif
    StationGiSourceOutput result;
    // Emission is already outgoing radiance: do not multiply it by the lamp's
    // dark base reflectance or divide it by PI like reflected irradiance.
    result.radiance=float4((max(0,brdf.diffuse*direct)/PI+max(0,surfaceData.emission))*bounceStrength,1);
    result.receiver=float4(brdf.diffuse*receiveStrength,1);
    return result;
}
#endif

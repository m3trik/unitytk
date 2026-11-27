// Baked Lit's reroute: the Lightmaps feature's URP door puts it in the forward pass of the project's own URP Lit
// (BakedLitImporter, which makes Baked Lit of it on import), after the pass's material input and before
// LitForwardPass.hlsl, where it reroutes the pass's one call to UniversalFragmentPBR.
#ifndef UNITYTK_BAKED_LIT_INCLUDED
#define UNITYTK_BAKED_LIT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// A surface a lightmap lights keeps its environment's reflection (URP adds no environment diffuse to a lightmapped
// surface), at its reflection probe's own level (a delivery's probe: its intensity is the record's envMapIntensity).
// Its material reflecting by its bake (_UNITYTK_REFLECT_BY_BAKE, set by the import where the record states
// reflectionNormalization lightmapLuma), the reflection is scaled per texel by the bake's luma over the light the room
// it reflects gives it about its normal -- its probe's blurriest mip toward the normal, sampled as URP samples the
// probe for this draw (its blending, its box, the Forward+ atlas, its level) -- clamped to 1, so a shadow reflects next
// to nothing and a lit surface up to its probe. URP's occlusion scales its indirect diffuse and specular alike, so the
// bake is divided by the same factor: the diffuse stays the bake, as stored. Nothing here is the material's own, so
// its UnityPerMaterial buffer is Lit's and the SRP Batcher draws it; each renderer reads the probe Unity gives it, so
// two deliveries in one scene each draw their own.
half4 UnitytkBakedFragmentPBR(InputData inputData, SurfaceData surfaceData)
{
#if defined(LIGHTMAP_ON) && defined(_UNITYTK_REFLECT_BY_BAKE)
    const half3 luma = half3(0.2126h, 0.7152h, 0.0722h);
    half bake = dot(inputData.bakedGI, luma);
    half3 room = GlossyEnvironmentReflection(half3(inputData.normalWS), inputData.positionWS, 1.0h, 1.0h,
                                             inputData.normalizedScreenSpaceUV);
    half environment = dot(room, luma);
    half factor = max(environment > 1e-4h ? saturate(bake / environment) : 1.0h, 1e-3h);
    surfaceData.occlusion *= factor;
    inputData.bakedGI /= factor;
#endif
    return UniversalFragmentPBR(inputData, surfaceData);
}

#define UniversalFragmentPBR UnitytkBakedFragmentPBR

#endif

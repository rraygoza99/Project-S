#ifndef SPELLCASTER_FOG_INCLUDED
#define SPELLCASTER_FOG_INCLUDED

// Set globally from Materials.SetFog(). Done by hand (not Unity fog) so build-time fog stripping can't remove it.
float4 _SC_FogColor;
float4 _SC_FogParams; // x = start, y = end

float SC_FogFactor(float3 wp)
{
    float d = distance(wp, _WorldSpaceCameraPos);
    float f = saturate((d - _SC_FogParams.x) / max(0.001, _SC_FogParams.y - _SC_FogParams.x));
    return floor(f * 8.0) / 8.0; // banded, DOOM-style light diminishing
}

float3 SC_ApplyFog(float3 rgb, float3 wp)
{
    return lerp(rgb, _SC_FogColor.rgb, SC_FogFactor(wp));
}

#endif

#ifndef CYPHER_CITY_NIGHT_FILL_INCLUDED
#define CYPHER_CITY_NIGHT_FILL_INCLUDED

// The prototype's night rig has two unshadowed directional "light pollution" fills and a street
// uplight. URP only has one directional light, so they are evaluated here as plain Lambert terms.
// Set by Cypher.Rendering.NightLighting. xyz: unit vector toward the light; rgb: linear radiance
// in Unity's convention (intensity / pi already applied). Unset globals read as zero: no fill.
// Shared by every shader that is lit like the city (CityLit, the pedestrians).
float4 _CypherFillDir0;
float4 _CypherFillDir1;
float4 _CypherFillDir2;
float4 _CypherFillColor0;
float4 _CypherFillColor1;
float4 _CypherFillColor2;

half3 CityFillLight(half3 normalWS)
{
    return _CypherFillColor0.rgb * saturate(dot(normalWS, _CypherFillDir0.xyz))
         + _CypherFillColor1.rgb * saturate(dot(normalWS, _CypherFillDir1.xyz))
         + _CypherFillColor2.rgb * saturate(dot(normalWS, _CypherFillDir2.xyz));
}

#endif

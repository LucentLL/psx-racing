// THE FLICKER FINDER's flat ID (Editor/CityFlickerFinder.cs, city-popin
// 2026-10-28): what a renderer that does not draw with PSX/Lit wears for the
// ID renders - its draw's ID colour, alpha its distance (2 km = 1), with the
// depth state of the material it stands in for (decals keep their offset and
// their ZWrite Off), so the fights the finder sees are the game's. Editor
// tool only: nothing in the game references it.
Shader "Hidden/PSX/FlickerId"
{
    Properties
    {
        _PSXIdCol ("ID", Color) = (0, 0, 0, 1)
        _IdOffF ("Offset factor", Float) = 0
        _IdOffU ("Offset units", Float) = 0
        _IdZWrite ("ZWrite", Float) = 1
        _IdCull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite [_IdZWrite]
            ZTest LEqual
            Offset [_IdOffF], [_IdOffU]
            Cull [_IdCull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _PSXIdCol;
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; };
            v2f vert (float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.wpos = mul(unity_ObjectToWorld, vertex).xyz;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(_PSXIdCol.rgb, saturate(length(i.wpos - _WorldSpaceCameraPos) / 2000.0));
            }
            ENDCG
        }
    }
}

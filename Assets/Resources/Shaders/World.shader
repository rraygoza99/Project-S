// Level geometry: unlit texture with world-space UVs, fake per-face shading and banded distance fog.
Shader "Spellcaster/World"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Tiling ("World units per texture", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "SpellcasterFog.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _Tiling;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float shade : TEXCOORD1;
                float3 wp : TEXCOORD2;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = UnityObjectToWorldNormal(v.normal);
                float3 an = abs(n);
                float2 uv = an.y > 0.5 ? wp.xz : (an.x > an.z ? wp.zy : wp.xy);
                o.uv = uv / _Tiling;
                o.shade = an.y > 0.5 ? (n.y > 0 ? 1.0 : 0.55) : (an.x > an.z ? 0.7 : 0.85);
                o.wp = wp;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.rgb *= i.shade;
                c.rgb = SC_ApplyFog(c.rgb, i.wp);
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
}

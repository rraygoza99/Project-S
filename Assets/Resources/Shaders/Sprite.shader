// Enemy sprites: alpha-cutout, tint, white hurt flash, dithered fade-out, fog.
Shader "Spellcaster/Sprite"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint (a = dither fade)", Color) = (1,1,1,1)
        _Flash ("Flash", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "SpellcasterFog.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST; // per-frame sprite-sheet cell (negative x scale = mirrored)
            fixed4 _Color;
            float _Flash;

            static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wp : TEXCOORD1;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy * _MainTex_ST.xy + _MainTex_ST.zw;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                clip(t.a - 0.5);
                int x = (int)fmod(i.pos.x, 4.0);
                int y = (int)fmod(i.pos.y, 4.0);
                clip(_Color.a - (bayer[y * 4 + x] + 0.5) / 16.0);
                float3 rgb = t.rgb * _Color.rgb;
                rgb = lerp(rgb, float3(1,1,1), _Flash);
                rgb = SC_ApplyFog(rgb, i.wp);
                return fixed4(rgb, 1);
            }
            ENDCG
        }
    }
}

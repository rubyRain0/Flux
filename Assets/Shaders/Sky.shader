// Вертикальный градиент неба для Flux. Цвет у горизонта совпадает с цветом
// тумана, поэтому дальний конец жёлоба растворяется в фоне на крутых спусках.
Shader "Flux/Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.035, 0.050, 0.100, 1)
        _HorizonColor ("Horizon", Color) = (0.020, 0.028, 0.060, 1)
        _BottomColor ("Bottom", Color) = (0.008, 0.012, 0.030, 1)
        _GlowColor ("Horizon Glow", Color) = (0.10, 0.04, 0.20, 1)
        _GlowSharp ("Glow Sharpness", Range(1, 16)) = 7
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TopColor;
            fixed4 _HorizonColor;
            fixed4 _BottomColor;
            fixed4 _GlowColor;
            float _GlowSharp;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float y = normalize(i.dir).y;

                fixed3 c = (y > 0.0)
                    ? lerp(_HorizonColor.rgb, _TopColor.rgb, saturate(y * 1.7))
                    : lerp(_HorizonColor.rgb, _BottomColor.rgb, saturate(-y * 2.4));

                // Мягкая полоса цвета, лежащая на горизонте.
                float band = pow(saturate(1.0 - abs(y) * 7.0), _GlowSharp);
                c += _GlowColor.rgb * band;

                return fixed4(c, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}

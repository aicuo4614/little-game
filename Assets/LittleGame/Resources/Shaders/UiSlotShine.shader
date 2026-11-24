Shader "LittleGame/UI/SlotShine"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ShineColor ("Shine Color", Color) = (1, 0.94, 0.62, 1)
        _ShinePos ("Shine Position", Range(-0.5, 1.5)) = -0.5
        _ShineWidth ("Shine Width", Range(0.05, 0.6)) = 0.24
        _FlashIntensity ("Flash Intensity", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _ShineColor;
            float _ShinePos;
            float _ShineWidth;
            float _FlashIntensity;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.texcoord) * i.color;
                float diag = (i.texcoord.x * 0.72 + i.texcoord.y * 0.55);
                float band = saturate(1.0 - abs(diag - _ShinePos) / max(0.01, _ShineWidth));
                band = band * band * (3.0 - 2.0 * band);
                col.rgb += _ShineColor.rgb * (band * 0.95 + _FlashIntensity * 0.65) * col.a;
                return col;
            }
            ENDCG
        }
    }
}

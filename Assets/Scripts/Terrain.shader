Shader "Custom/Terrain" {
    Properties {
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        const static int maxColorCount = 8;

        int baseColorCount;
        float3 baseColors[maxColorCount];
        float baseStartHeights[maxColorCount];
        
        float minHeight;
        float maxHeight;

        sampler2D _MainTex;

        struct Input {
          float3 worldPos;
        };

        float inverseLerp(float a, float b, float value) {
          return saturate((value - a) / (b - a));
        }

        void surf (Input IN, inout SurfaceOutputStandard o) {
          float heightPercent = inverseLerp(minHeight, maxHeight, IN.worldPos.y);
          for (int i = 0; i < baseColorCount; i++) {
            float drawStrength = saturate(sign(heightPercent - baseStartHeights[i]));
            o.Albedo = o.Albedo * (1 - drawStrength) + baseColors[i] * drawStrength;
          }
        }
        ENDCG
    }
    FallBack "Diffuse"
}

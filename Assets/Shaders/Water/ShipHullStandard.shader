Shader "Hull/ShipHullStandard"
{
    Properties
    {
        _Color ("Base Color", Color) = (0.3, 0.35, 0.4, 1)
        _BelowWaterColor ("Below Water Color", Color) = (0.15, 0.2, 0.25, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0.7
        _Smoothness ("Smoothness", Range(0, 1)) = 0.4
        _Roughness ("Roughness", Range(0, 1)) = 0.6
        _Waterline ("Waterline Height", Float) = 0.0
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1.0
        _RustTex ("Rust Texture", 2D) = "white" {}
        _RustAmount ("Rust Amount", Range(0, 1)) = 0.2
        _FoamAccumulation ("Foam Accumulation", 2D) = "black" {}
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamIntensity ("Foam Intensity", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        LOD 300

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            float4 _Color;
            float4 _BelowWaterColor;
            float _Metallic;
            float _Smoothness;
            float _Roughness;
            float _Waterline;
            sampler2D _NormalMap;
            float _NormalStrength;
            sampler2D _RustTex;
            float _RustAmount;
            sampler2D _FoamAccumulation;
            float4 _FoamColor;
            float _FoamIntensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldTangent : TEXCOORD3;
                float3 worldBitangent : TEXCOORD4;
                float3 viewDir : TEXCOORD5;
                SHADOW_COORDS(6)
            };

            float3 FresnelSchlick(float cosTheta, float3 F0)
            {
                return F0 + (1.0 - F0) * pow(max(0.0, 1.0 - cosTheta), 5.0);
            }

            float DistributionGGX(float3 N, float3 H, float roughness)
            {
                float a = roughness * roughness;
                float a2 = a * a;
                float NdotH = max(dot(N, H), 0.0);
                float NdotH2 = NdotH * NdotH;

                float num = a2;
                float denom = (NdotH2 * (a2 - 1.0) + 1.0);
                denom = 3.14159265 * denom * denom;

                return num / max(denom, 0.0001);
            }

            float GeometrySchlickGGX(float NdotV, float roughness)
            {
                float r = (roughness + 1.0);
                float k = (r * r) / 8.0;
                float num = NdotV;
                float denom = NdotV * (1.0 - k) + k;
                return num / max(denom, 0.0001);
            }

            float GeometrySmith(float3 N, float3 V, float3 L, float roughness)
            {
                float NdotV = max(dot(N, V), 0.0);
                float NdotL = max(dot(N, L), 0.0);
                float ggx2 = GeometrySchlickGGX(NdotV, roughness);
                float ggx1 = GeometrySchlickGGX(NdotL, roughness);
                return ggx1 * ggx2;
            }

            v2f vert(appdata v)
            {
                v2f o;

                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = mul(UNITY_MATRIX_VP, float4(o.worldPos, 1.0));
                o.uv = v.uv;

                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = normalize(mul(unity_ObjectToWorld, float4(v.tangent.xyz, 0.0)).xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * v.tangent.w;

                o.viewDir = normalize(_WorldSpaceCameraPos - o.worldPos);

                TRANSFER_SHADOW(o);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normalTex = UnpackNormal(tex2D(_NormalMap, i.uv));
                normalTex.xy *= _NormalStrength;

                float3 T = normalize(i.worldTangent);
                float3 B = normalize(i.worldBitangent);
                float3 N = normalize(i.worldNormal);
                float3x3 TBN = float3x3(T, B, N);
                float3 normalWS = normalize(mul(TBN, normalTex));

                float3 V = normalize(i.viewDir);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 H = normalize(V + L);

                float isBelowWater = step(i.worldPos.y, _Waterline);
                float3 baseColor = lerp(_Color.rgb, _BelowWaterColor.rgb, isBelowWater);

                float rustFactor = tex2D(_RustTex, i.uv * 2.0).r * _RustAmount * (1.0 - isBelowWater * 0.5f);
                float3 rustColor = float3(0.6, 0.35, 0.15);
                baseColor = lerp(baseColor, rustColor, rustFactor);

                float foam = tex2D(_FoamAccumulation, i.uv).r * _FoamIntensity;
                baseColor = lerp(baseColor, _FoamColor.rgb, foam);

                float metallic = _Metallic * (1.0 - isBelowWater * 0.3f) * (1.0 - rustFactor * 0.8f);
                float roughness = lerp(_Smoothness, _Roughness, isBelowWater * 0.5f + rustFactor * 0.5f);

                float3 F0 = lerp(float3(0.04, 0.04, 0.04), baseColor, metallic);

                float NdotL = max(dot(normalWS, L), 0.0);
                float NdotV = max(dot(normalWS, V), 0.0);
                float NdotH = max(dot(normalWS, H), 0.0);

                float D = DistributionGGX(normalWS, H, roughness);
                float G = GeometrySmith(normalWS, V, L, roughness);
                float3 F = FresnelSchlick(NdotV, F0);

                float3 kS = F;
                float3 kD = (1.0 - kS) * (1.0 - metallic);

                float3 numerator = D * G * F;
                float denominator = 4.0 * NdotV * NdotL + 0.0001;
                float3 specular = numerator / denominator;

                float3 ambient = ShadeSH9(float4(normalWS, 1.0)) * baseColor;
                float3 diffuse = kD * baseColor / 3.14159265;

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

                float3 Lo = (diffuse + specular) * _LightColor0.rgb * NdotL * atten;

                float3 finalColor = ambient + Lo;

                float waterLineTransition = smoothstep(_Waterline - 0.05f, _Waterline + 0.05f, i.worldPos.y);
                float3 wetShine = float3(0.1f, 0.15f, 0.2f) * (1.0 - waterLineTransition) * NdotL * 0.5f;
                finalColor += wetShine;

                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Standard"
}

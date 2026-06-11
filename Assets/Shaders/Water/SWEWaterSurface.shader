Shader "Water/SWEWaterSurface"
{
    Properties
    {
        _HeightField ("Height Field", 2D) = "black" {}
        _NormalField ("Normal Field", 2D) = "blue" {}
        _VelocityField ("Velocity Field", 2D) = "black" {}
        _WaterColor ("Water Color", Color) = (0.05, 0.3, 0.5, 0.9)
        _DeepColor ("Deep Water Color", Color) = (0.02, 0.1, 0.2, 1.0)
        _ShallowColor ("Shallow Water Color", Color) = (0.2, 0.5, 0.6, 0.9)
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _Shininess ("Shininess", Range(10, 500)) = 200
        _WaveHeight ("Wave Height Scale", Range(0, 10)) = 1.0
        _Tiling ("Tiling", Float) = 1.0
        _FoamThreshold ("Foam Threshold", Range(0, 1)) = 0.3
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.5
        _RefractionStrength ("Refraction Strength", Range(0, 1)) = 0.8
        _WaterSize ("Water Size", Float) = 200
        _GridResolution ("Grid Resolution", Int) = 512
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _HeightField;
            sampler2D _NormalField;
            sampler2D _VelocityField;
            sampler2D _CameraDepthTexture;
            sampler2D _RefractionTex;

            float4 _WaterColor;
            float4 _DeepColor;
            float4 _ShallowColor;
            float4 _SpecularColor;
            float4 _FoamColor;
            float _Shininess;
            float _WaveHeight;
            float _Tiling;
            float _FoamThreshold;
            float _ReflectionStrength;
            float _RefractionStrength;
            float _WaterSize;
            int _GridResolution;
            float _Time;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float3 viewDir : TEXCOORD4;
                SHADOW_COORDS(5)
            };

            float SampleHeight(float2 uv)
            {
                return tex2Dlod(_HeightField, float4(uv, 0, 0)).r;
            }

            float3 SampleNormal(float2 uv)
            {
                float4 n = tex2Dlod(_NormalField, float4(uv, 0, 0));
                return normalize(n.xyz * 2.0 - 1.0);
            }

            v2f vert(appdata v)
            {
                v2f o;

                float2 uv = v.uv * _Tiling;

                float h = SampleHeight(uv);
                float3 displacedPos = v.vertex.xyz;
                displacedPos.y += h * _WaveHeight;

                float2 dx = float2(1.0 / _GridResolution, 0.0);
                float2 dy = float2(0.0, 1.0 / _GridResolution);

                float hL = SampleHeight(uv - dx);
                float hR = SampleHeight(uv + dx);
                float hD = SampleHeight(uv - dy);
                float hU = SampleHeight(uv + dy);

                float cellSize = _WaterSize / _GridResolution;
                float3 tangentX = normalize(float3(2.0 * cellSize, (hR - hL) * _WaveHeight, 0.0));
                float3 tangentZ = normalize(float3(0.0, (hU - hD) * _WaveHeight, 2.0 * cellSize));
                float3 bitangent = normalize(cross(tangentX, tangentZ));

                o.worldNormal = normalize(mul(unity_ObjectToWorld, float4(bitangent, 0.0)).xyz);

                o.worldPos = mul(unity_ObjectToWorld, float4(displacedPos, 1.0)).xyz;
                o.pos = mul(UNITY_MATRIX_VP, float4(o.worldPos, 1.0));
                o.uv = uv;
                o.screenPos = ComputeScreenPos(o.pos);
                o.viewDir = normalize(_WorldSpaceCameraPos - o.worldPos);

                TRANSFER_SHADOW(o);

                return o;
            }

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

                return num / denom;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normalFromTex = SampleNormal(i.uv);
                float3 N = normalize(i.worldNormal * 0.3f + normalFromTex * 0.7f);

                float height = SampleHeight(i.uv);
                float2 velocity = tex2D(_VelocityField, i.uv).xy;
                float speed = length(velocity);

                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 V = i.viewDir;
                float3 H = normalize(V + L);

                float NdotL = max(dot(N, L), 0.0);
                float NdotV = max(dot(N, V), 0.0);
                float NdotH = max(dot(N, H), 0.0);

                float depth = _WaterSize * 0.5f - height * _WaveHeight;
                float depthFactor = smoothstep(0.0, 5.0, depth);
                float3 waterCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthFactor);
                waterCol = lerp(waterCol, _WaterColor.rgb, 0.5f);

                float slope = tex2D(_NormalField, i.uv).w;
                float foamFactor = smoothstep(_FoamThreshold, _FoamThreshold + 0.1f, slope);
                foamFactor += smoothstep(0.5f, 1.0f, speed * 0.5f);
                foamFactor = saturate(foamFactor);
                waterCol = lerp(waterCol, _FoamColor.rgb, foamFactor * 0.6f);

                float3 F0 = float3(0.02, 0.02, 0.02);
                float3 Fresnel = FresnelSchlick(NdotV, F0);

                float roughness = lerp(0.05f, 0.2f, slope * 0.5f);
                float D = DistributionGGX(N, H, roughness);
                float specular = D * pow(NdotH, _Shininess);

                float3 specularColor = _SpecularColor.rgb * specular * NdotL * Fresnel;

                float3 diffuse = waterCol * NdotL * _LightColor0.rgb;

                float4 screenUV = i.screenPos / i.screenPos.w;
                float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV.xy));
                float waterDepth = LinearEyeDepth(i.screenPos.z / i.screenPos.w);
                float depthDiff = abs(sceneDepth - waterDepth);
                float transparency = smoothstep(0.0f, 2.0f, depthDiff);

                float3 ambient = ShadeSH9(float4(N, 1.0)) * waterCol;

                float3 finalColor = ambient + diffuse + specularColor;
                float alpha = lerp(0.4f, 0.95f, transparency * depthFactor);
                alpha = lerp(alpha, 1.0f, foamFactor * 0.5f);

                float reflectionMask = Fresnel * _ReflectionStrength;

                float highlight = pow(max(0.0, dot(reflect(-L, N), V)), 512) * 2.0;
                finalColor += _LightColor0.rgb * highlight;

                float causticPattern = sin(i.worldPos.x * 5.0 + _Time * 2.0) * sin(i.worldPos.z * 5.0 + _Time * 1.5) * 0.5 + 0.5;
                causticPattern *= smoothstep(-2.0f, 0.0f, height);
                finalColor += causticPattern * 0.1f * waterCol;

                return fixed4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Transparent/Diffuse"
}

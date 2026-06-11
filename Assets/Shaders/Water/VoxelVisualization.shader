Shader "Water/VoxelVisualization"
{
    Properties
    {
        _VoxelTexture ("Voxel 3D Texture", 3D) = "black" {}
        _VoxelSize ("Voxel Size", Vector) = (0.5, 0.5, 0.5, 0)
        _GridSize ("Grid Size", Vector) = (64, 64, 64, 0)
        _GridMin ("Grid Min", Vector) = (0, 0, 0, 0)
        _AlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.1
        _SubmergedColor ("Submerged Color", Color) = (0.2, 0.5, 0.9, 0.8)
        _DryColor ("Dry Color", Color) = (0.8, 0.6, 0.3, 0.9)
        _WireframeColor ("Wireframe Color", Color) = (1, 1, 1, 0.3)
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
        ZWrite On
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma require 3d

            #include "UnityCG.cginc"

            sampler3D _VoxelTexture;
            float4 _VoxelTexture_TexelSize;
            float3 _VoxelSize;
            float3 _GridSize;
            float3 _GridMin;
            float _AlphaCutoff;
            float4 _SubmergedColor;
            float4 _DryColor;
            float4 _WireframeColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;

                float3 worldPos = _GridMin + v.uv * _GridSize * _VoxelSize;
                o.worldPos = worldPos;
                o.uv = v.uv;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 uv = i.uv;

                float4 voxel = tex3D(_VoxelTexture, uv);

                if (voxel.a < _AlphaCutoff)
                {
                    discard;
                }

                float3 localInCell = frac(uv * _GridSize);
                float3 distToEdge = min(localInCell, 1.0 - localInCell);
                float edge = min(min(distToEdge.x, distToEdge.y), distToEdge.z);
                float edgeFactor = smoothstep(0.0f, 0.08f, edge);

                float3 normal = voxel.rgb * 2.0 - 1.0;
                float submerged = voxel.a;

                float3 color = lerp(_DryColor.rgb, _SubmergedColor.rgb, submerged);

                float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                float NdotL = max(dot(normal, lightDir), 0.0);
                color *= 0.5f + 0.7f * NdotL;

                color = lerp(_WireframeColor.rgb, color, edgeFactor);

                float alpha = lerp(_WireframeColor.a, lerp(_DryColor.a, _SubmergedColor.a, submerged), edgeFactor);

                return fixed4(color, alpha);
            }
            ENDCG
        }

        Pass
        {
            Name "RAYMARCH"
            CGPROGRAM
            #pragma vertex vertRaymarch
            #pragma fragment fragRaymarch
            #pragma target 5.0
            #pragma require 3d

            #include "UnityCG.cginc"

            sampler3D _VoxelTexture;
            float3 _VoxelSize;
            float3 _GridSize;
            float3 _GridMin;
            float _AlphaCutoff;
            float4 _SubmergedColor;
            float4 _DryColor;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 rayOrigin : TEXCOORD1;
                float3 rayDir : TEXCOORD2;
            };

            v2f vertRaymarch(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.rayOrigin = _WorldSpaceCameraPos;
                o.rayDir = normalize(o.worldPos - _WorldSpaceCameraPos);
                return o;
            }

            float IntersectBox(float3 ro, float3 rd, float3 bmin, float3 bmax)
            {
                float3 invRd = 1.0 / rd;
                float3 t1 = (bmin - ro) * invRd;
                float3 t2 = (bmax - ro) * invRd;
                float3 tmin = min(t1, t2);
                float3 tmax = max(t1, t2);
                float tnear = max(max(tmin.x, tmin.y), tmin.z);
                float tfar = min(min(tmax.x, tmax.y), tmax.z);
                if (tfar < max(0.0, tnear)) return -1.0;
                return max(0.0, tnear);
            }

            fixed4 fragRaymarch(v2f i) : SV_Target
            {
                float3 bmin = _GridMin;
                float3 bmax = _GridMin + _GridSize * _VoxelSize;

                float tEnter = IntersectBox(i.rayOrigin, i.rayDir, bmin, bmax);
                if (tEnter < 0.0) discard;

                float3 startPos = i.rayOrigin + i.rayDir * tEnter;
                float3 stepSize = _VoxelSize * 0.5f;
                float stepLen = length(stepSize);

                float3 pos = startPos;
                float4 accumulatedColor = float4(0, 0, 0, 0);

                const int MAX_STEPS = 512;

                for (int step = 0; step < MAX_STEPS; step++)
                {
                    float3 boxPos = pos - bmin;
                    float3 uv = boxPos / (_GridSize * _VoxelSize);

                    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1 || uv.z < 0 || uv.z > 1)
                        break;

                    float4 voxel = tex3D(_VoxelTexture, uv);

                    if (voxel.a > _AlphaCutoff)
                    {
                        float3 normal = voxel.rgb * 2.0 - 1.0;
                        float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                        float NdotL = max(dot(normal, lightDir), 0.0);

                        float3 voxelColor = lerp(_DryColor.rgb, _SubmergedColor.rgb, voxel.a);
                        voxelColor *= 0.5f + 0.7f * NdotL;

                        float alpha = voxel.a * 0.6f;
                        accumulatedColor.rgb += (1.0 - accumulatedColor.a) * alpha * voxelColor;
                        accumulatedColor.a += (1.0 - accumulatedColor.a) * alpha;

                        if (accumulatedColor.a > 0.95f)
                            break;
                    }

                    pos += i.rayDir * stepLen;
                }

                if (accumulatedColor.a < 0.01f)
                    discard;

                return fixed4(accumulatedColor.rgb, accumulatedColor.a);
            }
            ENDCG
        }
    }
}

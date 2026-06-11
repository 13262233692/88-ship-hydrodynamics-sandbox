using UnityEngine;
using ShipHydrodynamics.Water;

namespace ShipHydrodynamics.Water
{
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class WaterSurfaceRenderer : MonoBehaviour
    {
        [Header("References")]
        public SWEWaterSimulator WaterSimulator;

        [Header("Rendering")]
        public Material WaterMaterial;
        public int MeshResolution = 128;
        public float SurfaceSize = 200f;
        public float WaveHeightScale = 1.0f;

        [Header("Water Colors")]
        public Color DeepColor = new Color(0.02f, 0.1f, 0.2f, 1f);
        public Color ShallowColor = new Color(0.2f, 0.5f, 0.6f, 0.9f);
        public Color FoamColor = Color.white;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _waterMesh;

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            GenerateWaterMesh();
            SetupMaterial();
        }

        private void GenerateWaterMesh()
        {
            _waterMesh = new Mesh
            {
                name = "WaterSurfaceMesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };

            int vertsPerLine = MeshResolution + 1;
            Vector3[] vertices = new Vector3[vertsPerLine * vertsPerLine];
            Vector2[] uvs = new Vector2[vertsPerLine * vertsPerLine];
            int[] triangles = new int[MeshResolution * MeshResolution * 6];

            float halfSize = SurfaceSize * 0.5f;
            float stepSize = SurfaceSize / MeshResolution;

            for (int y = 0; y < vertsPerLine; y++)
            {
                for (int x = 0; x < vertsPerLine; x++)
                {
                    int idx = y * vertsPerLine + x;
                    vertices[idx] = new Vector3(
                        -halfSize + x * stepSize,
                        0f,
                        -halfSize + y * stepSize
                    );
                    uvs[idx] = new Vector2(
                        (float)x / MeshResolution,
                        (float)y / MeshResolution
                    );
                }
            }

            int triIndex = 0;
            for (int y = 0; y < MeshResolution; y++)
            {
                for (int x = 0; x < MeshResolution; x++)
                {
                    int a = y * vertsPerLine + x;
                    int b = a + 1;
                    int c = a + vertsPerLine;
                    int d = c + 1;

                    triangles[triIndex++] = a;
                    triangles[triIndex++] = c;
                    triangles[triIndex++] = b;
                    triangles[triIndex++] = b;
                    triangles[triIndex++] = c;
                    triangles[triIndex++] = d;
                }
            }

            _waterMesh.vertices = vertices;
            _waterMesh.uv = uvs;
            _waterMesh.triangles = triangles;
            _waterMesh.RecalculateNormals();
            _waterMesh.RecalculateBounds();

            _meshFilter.sharedMesh = _waterMesh;
        }

        private void SetupMaterial()
        {
            if (WaterMaterial == null)
            {
                Shader waterShader = Shader.Find("Water/SWEWaterSurface");
                if (waterShader != null)
                {
                    WaterMaterial = new Material(waterShader);
                }
                else
                {
                    Debug.LogError("WaterSurfaceRenderer: Could not find Water/SWEWaterSurface shader!");
                    return;
                }
            }

            UpdateMaterialProperties();
            _meshRenderer.sharedMaterial = WaterMaterial;
        }

        private void Update()
        {
            if (WaterSimulator == null) return;

            UpdateMaterialProperties();
        }

        private void UpdateMaterialProperties()
        {
            if (WaterMaterial == null || WaterSimulator == null) return;

            WaterMaterial.SetTexture("_HeightField", WaterSimulator.HeightField);
            WaterMaterial.SetTexture("_NormalField", WaterSimulator.NormalField);
            WaterMaterial.SetTexture("_VelocityField", WaterSimulator.VelocityField);

            WaterMaterial.SetColor("_DeepColor", DeepColor);
            WaterMaterial.SetColor("_ShallowColor", ShallowColor);
            WaterMaterial.SetColor("_FoamColor", FoamColor);

            WaterMaterial.SetFloat("_WaveHeight", WaveHeightScale);
            WaterMaterial.SetFloat("_WaterSize", SurfaceSize);
            WaterMaterial.SetInt("_GridResolution", WaterSimulator.Settings.GridResolution);
            WaterMaterial.SetFloat("_Tiling", 1f);
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
            {
                Gizmos.color = new Color(0f, 0.5f, 1f, 0.3f);
                Gizmos.DrawWireCube(transform.position, new Vector3(SurfaceSize, 0.1f, SurfaceSize));
            }
        }
    }
}

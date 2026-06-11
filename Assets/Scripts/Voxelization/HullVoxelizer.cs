using System;
using System.Collections.Generic;
using UnityEngine;
using ShipHydrodynamics.Core;

namespace ShipHydrodynamics.Voxelization
{
    [RequireComponent(typeof(MeshFilter))]
    public class HullVoxelizer : MonoBehaviour
    {
        [Header("Compute Shader")]
        public ComputeShader VoxelizationShader;

        [Header("Settings")]
        public VoxelGridSettings GridSettings = new VoxelGridSettings();
        public float WaterlineY = 0f;
        public bool ShowVoxelDebug = true;

        [Header("Output")]
        public HydrostaticData LastHydrostaticData;

        private MeshFilter _meshFilter;
        private Mesh _mesh;
        private ComputeBuffer _triangleBuffer;
        private ComputeBuffer _voxelGridBuffer;
        private ComputeBuffer _reductionBuffer;
        private RenderTexture _voxelTexture3D;

        private Triangle[] _triangles;
        private VoxelData[] _voxelDataCPU;
        private ReductionResult[] _reductionResultsCPU;

        private int _kernelVoxelize;
        private int _kernelClear;
        private int _kernelReduceVolume;
        private int _kernelReduceFinal;
        private int _kernelBuildTexture;

        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private float _lastVoxelizationTime;
        private bool _needsUpdate = true;

        public event Action<HydrostaticData> OnHydrostaticDataUpdated;

        public HydrostaticData CurrentHydrostaticData => LastHydrostaticData;
        public RenderTexture VoxelTexture => _voxelTexture3D;

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            Initialize();
        }

        private void Initialize()
        {
            ExtractTriangles();
            CreateComputeBuffers();
            InitializeKernels();
            CreateVoxelTexture();
            UpdateBounds();
        }

        private void ExtractTriangles()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            if (_meshFilter == null || _meshFilter.sharedMesh == null)
            {
                Debug.LogError("HullVoxelizer: No mesh found on this GameObject!");
                return;
            }

            _mesh = _meshFilter.sharedMesh;
            Vector3[] vertices = _mesh.vertices;
            int[] triangles = _mesh.triangles;

            _triangles = new Triangle[triangles.Length / 3];

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v0 = transform.TransformPoint(vertices[triangles[i]]);
                Vector3 v1 = transform.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 v2 = transform.TransformPoint(vertices[triangles[i + 2]]);
                _triangles[i / 3] = new Triangle(v0, v1, v2);
            }

            Debug.Log($"HullVoxelizer: Extracted {_triangles.Length} triangles from hull mesh");
        }

        public void RefreshTriangles()
        {
            ExtractTriangles();
            if (_triangleBuffer != null)
            {
                _triangleBuffer.Release();
            }
            _triangleBuffer = new ComputeBuffer(_triangles.Length, 48);
            _triangleBuffer.SetData(_triangles);
            _needsUpdate = true;
        }

        private void CreateComputeBuffers()
        {
            int totalVoxels = GridSettings.TotalVoxels;

            if (_triangleBuffer != null) _triangleBuffer.Release();
            if (_voxelGridBuffer != null) _voxelGridBuffer.Release();
            if (_reductionBuffer != null) _reductionBuffer.Release();

            _triangleBuffer = new ComputeBuffer(_triangles.Length, 48);
            _triangleBuffer.SetData(_triangles);

            _voxelGridBuffer = new ComputeBuffer(totalVoxels, 36);
            _voxelDataCPU = new VoxelData[totalVoxels];

            int groupSizeX = Mathf.CeilToInt(GridSettings.GridSize.x / 8f);
            int groupSizeY = Mathf.CeilToInt(GridSettings.GridSize.y / 8f);
            int groupSizeZ = Mathf.CeilToInt(GridSettings.GridSize.z / 8f);
            int totalGroups = groupSizeX * groupSizeY * groupSizeZ;

            _reductionBuffer = new ComputeBuffer(Mathf.Max(totalGroups, 1), 36);
            _reductionResultsCPU = new ReductionResult[Mathf.Max(totalGroups, 1)];
        }

        private void InitializeKernels()
        {
            if (VoxelizationShader == null)
            {
                Debug.LogError("HullVoxelizer: Compute Shader is not assigned!");
                return;
            }

            _kernelVoxelize = VoxelizationShader.FindKernel("VoxelizeHull");
            _kernelClear = VoxelizationShader.FindKernel("ClearVoxelGrid");
            _kernelReduceVolume = VoxelizationShader.FindKernel("ReduceVolume");
            _kernelReduceFinal = VoxelizationShader.FindKernel("ReduceArea");
            _kernelBuildTexture = VoxelizationShader.FindKernel("BuildVoxelTexture");
        }

        private void CreateVoxelTexture()
        {
            if (_voxelTexture3D != null)
            {
                _voxelTexture3D.Release();
            }

            _voxelTexture3D = new RenderTexture(
                GridSettings.GridSize.x,
                GridSettings.GridSize.y,
                0,
                RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear
            );
            _voxelTexture3D.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            _voxelTexture3D.volumeDepth = GridSettings.GridSize.z;
            _voxelTexture3D.enableRandomWrite = true;
            _voxelTexture3D.useMipMap = false;
            _voxelTexture3D.Create();
        }

        private void UpdateBounds()
        {
            if (VoxelizationShader == null) return;

            Vector3 gridMin = GridSettings.GetGridMin(transform.position);
            Vector3 gridMax = GridSettings.GetGridMax(transform.position);
            Vector3 cellSize = new Vector3(GridSettings.CellSize, GridSettings.CellSize, GridSettings.CellSize);

            VoxelizationShader.SetInt("_GridSizeX", GridSettings.GridSize.x);
            VoxelizationShader.SetInt("_GridSizeY", GridSettings.GridSize.y);
            VoxelizationShader.SetInt("_GridSizeZ", GridSettings.GridSize.z);
            VoxelizationShader.SetFloats("_GridMin", gridMin.x, gridMin.y, gridMin.z);
            VoxelizationShader.SetFloats("_GridMax", gridMax.x, gridMax.y, gridMax.z);
            VoxelizationShader.SetFloats("_GridCellSize", cellSize.x, cellSize.y, cellSize.z);
            VoxelizationShader.SetFloat("_WaterlineY", WaterlineY);
            VoxelizationShader.SetInt("_TriangleCount", _triangles.Length);
        }

        public void RunVoxelization()
        {
            if (VoxelizationShader == null || _triangleBuffer == null) return;

            UpdateBounds();

            SetBufferToKernel(_kernelClear);
            SetBufferToKernel(_kernelVoxelize);
            SetBufferToKernel(_kernelReduceVolume);
            SetBufferToKernel(_kernelBuildTexture);

            VoxelizationShader.SetTexture(_kernelBuildTexture, "_VoxelTexture", _voxelTexture3D);

            int groupsX = Mathf.CeilToInt(GridSettings.GridSize.x / 8f);
            int groupsY = Mathf.CeilToInt(GridSettings.GridSize.y / 8f);
            int groupsZ = Mathf.CeilToInt(GridSettings.GridSize.z / 8f);

            VoxelizationShader.Dispatch(_kernelClear, groupsX, groupsY, groupsZ);
            VoxelizationShader.Dispatch(_kernelVoxelize, groupsX, groupsY, groupsZ);
            VoxelizationShader.Dispatch(_kernelReduceVolume, groupsX, groupsY, groupsZ);
            VoxelizationShader.Dispatch(_kernelReduceFinal, 1, 1, 1);
            VoxelizationShader.Dispatch(_kernelBuildTexture, groupsX, groupsY, groupsZ);

            _reductionBuffer.GetData(_reductionResultsCPU);
            ProcessReductionResults(_reductionResultsCPU[0]);

            _lastPosition = transform.position;
            _lastRotation = transform.rotation;
        }

        private void SetBufferToKernel(int kernel)
        {
            VoxelizationShader.SetBuffer(kernel, "_VoxelGrid", _voxelGridBuffer);
            VoxelizationShader.SetBuffer(kernel, "_ReductionBuffer", _reductionBuffer);
            VoxelizationShader.SetBuffer(kernel, "_HullTriangles", _triangleBuffer);
        }

        private void ProcessReductionResults(ReductionResult result)
        {
            const float waterDensity = 1025f;
            const float gravity = 9.81f;

            LastHydrostaticData = new HydrostaticData
            {
                DisplacedVolume = result.displacedVolume,
                WettedSurfaceArea = result.wettedSurfaceArea,
                WaterplaneArea = result.waterplaneArea,
                CenterOfBuoyancy = result.centerOfBuoyancy,
                BuoyantForce = waterDensity * gravity * result.displacedVolume,
                MetacentricHeightGM = CalculateMetacentricHeight(result),
                WaterlineY = WaterlineY,
                SubmergedVoxelCount = result.voxelCount
            };

            OnHydrostaticDataUpdated?.Invoke(LastHydrostaticData);
        }

        private float CalculateMetacentricHeight(ReductionResult result)
        {
            if (result.displacedVolume < 0.001f) return 0f;

            float waterplaneInertiaIxx = 0f;
            float waterplaneInertiaIyy = 0f;

            if (result.waterplaneArea > 0.001f)
            {
                float beam = GridSettings.GridSize.z * GridSettings.CellSize;
                float length = GridSettings.GridSize.x * GridSettings.CellSize;

                waterplaneInertiaIxx = length * beam * beam * beam / 12f;
                waterplaneInertiaIyy = beam * length * length * length / 12f;
            }

            float GMT = waterplaneInertiaIxx / result.displacedVolume;
            float GML = waterplaneInertiaIyy / result.displacedVolume;

            LastHydrostaticData.TransverseMetacentricHeightGMT = GMT;
            LastHydrostaticData.LongitudinalMetacentricHeightGML = GML;

            return GMT;
        }

        private void Update()
        {
            if (!GridSettings.AutoUpdate) return;

            if (Time.time - _lastVoxelizationTime >= GridSettings.UpdateInterval)
            {
                bool positionChanged = Vector3.Distance(_lastPosition, transform.position) > 0.001f;
                bool rotationChanged = Quaternion.Angle(_lastRotation, transform.rotation) > 0.1f;

                if (positionChanged || rotationChanged || _needsUpdate)
                {
                    RefreshTriangles();
                    RunVoxelization();
                    _lastVoxelizationTime = Time.time;
                    _needsUpdate = false;
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (!ShowVoxelDebug || !Application.isPlaying) return;

            Vector3 gridMin = GridSettings.GetGridMin(transform.position);
            Vector3 gridMax = GridSettings.GetGridMax(transform.position);
            Vector3 size = gridMax - gridMin;
            Vector3 center = (gridMin + gridMax) * 0.5f;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(center, size);

            if (LastHydrostaticData.DisplacedVolume > 0.01f)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(LastHydrostaticData.CenterOfBuoyancy, 0.15f);

                Gizmos.color = Color.blue;
                Vector3 buoyancyDir = Vector3.up * Mathf.Min(LastHydrostaticData.BuoyantForce * 0.001f, 5f);
                Gizmos.DrawLine(LastHydrostaticData.CenterOfBuoyancy, LastHydrostaticData.CenterOfBuoyancy + buoyancyDir);
            }

            Gizmos.color = new Color(0f, 0.3f, 1f, 0.3f);
            Vector3 waterlineCenter = new Vector3(center.x, WaterlineY, center.z);
            Vector3 waterlineSize = new Vector3(size.x, 0.01f, size.z);
            Gizmos.DrawCube(waterlineCenter, waterlineSize);
        }

        private void OnDestroy()
        {
            _triangleBuffer?.Release();
            _voxelGridBuffer?.Release();
            _reductionBuffer?.Release();
            _voxelTexture3D?.Release();
        }

        public VoxelData[] GetVoxelDataCPU()
        {
            if (_voxelGridBuffer == null) return null;
            _voxelGridBuffer.GetData(_voxelDataCPU);
            return _voxelDataCPU;
        }

        public void MarkForUpdate()
        {
            _needsUpdate = true;
        }
    }
}

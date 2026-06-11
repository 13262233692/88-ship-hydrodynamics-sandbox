using System;
using System.Collections.Generic;
using UnityEngine;
using ShipHydrodynamics.Core;
using ShipHydrodynamics.Voxelization;
using ShipHydrodynamics.Water;
using ShipHydrodynamics.HullForces;

namespace ShipHydrodynamics.Core
{
    public class HydrodynamicsSandbox : MonoBehaviour
    {
        public static HydrodynamicsSandbox Instance { get; private set; }

        [Header("Core Components")]
        public SWEWaterSimulator WaterSimulator;
        public WaterSurfaceRenderer WaterRenderer;
        public HullVoxelizer ShipVoxelizer;
        public HullHydrodynamics ShipHydrodynamics;

        [Header("Ship Setup")]
        public GameObject ShipPrefab;
        public Transform ShipSpawnPoint;
        public float ShipMass = 1000000f;
        public float ShipLength = 100f;
        public float ShipBeam = 20f;
        public float ShipDraft = 8f;

        [Header("Environment")]
        public float WaterLevel = 0f;
        public Vector2 WindDirection = new Vector2(1f, 0.3f).normalized;
        public float WindSpeed = 10f;

        [Header("Ambient Waves")]
        public bool EnableAmbientWaves = true;
        public int AmbientWaveCount = 8;
        public float BaseWaveAmplitude = 0.3f;
        public float BaseWaveFrequency = 0.5f;

        [Header("Debug")]
        public bool ShowPerformanceStats = true;
        public bool ShowHydrostaticData = true;
        public bool PauseSimulation = false;

        private GameObject _currentShip;
        private float _frameTime;
        private int _frameCount;
        private float _fps;
        private readonly List<WaveSource> _ambientWaves = new List<WaveSource>();

        public event Action OnSandboxInitialized;
        public event Action<GameObject> OnShipSpawned;
        public event Action OnShipDestroyed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            InitializeSandbox();
        }

        public void InitializeSandbox()
        {
            CreateWaterSystem();
            SpawnShip();
            CreateAmbientWaves();

            OnSandboxInitialized?.Invoke();
            Debug.Log("Hydrodynamics Sandbox initialized successfully.");
        }

        private void CreateWaterSystem()
        {
            if (WaterSimulator == null)
            {
                GameObject waterObj = new GameObject("WaterSystem");
                waterObj.transform.SetParent(transform);
                waterObj.transform.position = Vector3.zero;

                WaterSimulator = waterObj.AddComponent<SWEWaterSimulator>();
                WaterSimulator.Settings = new WaterSimulationSettings
                {
                    GridResolution = 512,
                    WaterSize = 400f,
                    RestDepth = 10f,
                    Gravity = 9.81f,
                    Viscosity = 0.01f,
                    Damping = 0.05f,
                    TimeStepScale = 0.5f,
                    SubSteps = 2
                };

                WaterRenderer = waterObj.AddComponent<WaterSurfaceRenderer>();
                WaterRenderer.SurfaceSize = 400f;
                WaterRenderer.MeshResolution = 256;
                WaterRenderer.WaveHeightScale = 2.0f;
            }

            if (WaterSimulator != null)
            {
                WaterSimulator.ShipLength = ShipLength;
                WaterSimulator.ShipBeam = ShipBeam;
                WaterSimulator.ShipDraft = ShipDraft;
            }
        }

        private void SpawnShip()
        {
            if (_currentShip != null)
            {
                Destroy(_currentShip);
            }

            Vector3 spawnPos = ShipSpawnPoint != null ? ShipSpawnPoint.position : new Vector3(0f, ShipDraft + 2f, 0f);
            Quaternion spawnRot = ShipSpawnPoint != null ? ShipSpawnPoint.rotation : Quaternion.identity;

            if (ShipPrefab != null)
            {
                _currentShip = Instantiate(ShipPrefab, spawnPos, spawnRot);
            }
            else
            {
                _currentShip = CreateProceduralShip();
                _currentShip.transform.position = spawnPos;
                _currentShip.transform.rotation = spawnRot;
            }

            SetupShipComponents(_currentShip);

            if (WaterSimulator != null)
            {
                WaterSimulator.ShipTransform = _currentShip.transform;
            }

            OnShipSpawned?.Invoke(_currentShip);
        }

        private GameObject CreateProceduralShip()
        {
            GameObject ship = new GameObject("ProceduralShip");

            MeshFilter mf = ship.AddComponent<MeshFilter>();
            MeshRenderer mr = ship.AddComponent<MeshRenderer>();
            Rigidbody rb = ship.AddComponent<Rigidbody>();

            mf.mesh = GenerateShipHullMesh();

            Material hullMat = new Material(Shader.Find("Hull/ShipHullStandard"));
            hullMat.color = new Color(0.3f, 0.35f, 0.4f);
            mr.material = hullMat;

            rb.mass = ShipMass;
            rb.drag = 0.1f;
            rb.angularDrag = 0.5f;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            MeshCollider mc = ship.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = mf.mesh;

            return ship;
        }

        private Mesh GenerateShipHullMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "ProceduralShipHull",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };

            int lengthSegments = 40;
            int beamSegments = 10;
            int heightSegments = 8;

            float halfLength = ShipLength * 0.5f;
            float halfBeam = ShipBeam * 0.5f;
            float depth = ShipDraft * 1.5f;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector3> normals = new List<Vector3>();

            for (int l = 0; l <= lengthSegments; l++)
            {
                float t = (float)l / lengthSegments;
                float x = Mathf.Lerp(-halfLength, halfLength, t);

                float bowShape = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.5f);
                float sternShape = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 1.2f);
                float hullShape = Mathf.Lerp(bowShape, sternShape, t);

                for (int h = 0; h <= heightSegments; h++)
                {
                    float hT = (float)h / heightSegments;
                    float y = Mathf.Lerp(-depth, depth * 0.2f, hT);

                    float submergedFactor = Mathf.Clamp01(-y / ShipDraft);
                    float beamAtHeight = halfBeam * hullShape * (1f - submergedFactor * 0.3f);

                    for (int b = 0; b <= beamSegments; b++)
                    {
                        float bT = (float)b / beamSegments;
                        float z = Mathf.Lerp(-beamAtHeight, beamAtHeight, bT);

                        Vector3 vertex = new Vector3(x, y, z);
                        vertices.Add(vertex);
                        uvs.Add(new Vector2(t, bT));

                        Vector3 normal = CalculateHullNormal(t, hT, bT);
                        normals.Add(normal);
                    }
                }
            }

            int verticesPerRing = beamSegments + 1;
            int verticesPerSlice = (heightSegments + 1) * verticesPerRing;

            for (int l = 0; l < lengthSegments; l++)
            {
                for (int h = 0; h < heightSegments; h++)
                {
                    for (int b = 0; b < beamSegments; b++)
                    {
                        int idx = l * verticesPerSlice + h * verticesPerRing + b;

                        int a = idx;
                        int bIdx = a + 1;
                        int c = a + verticesPerRing;
                        int d = c + 1;
                        int e = a + verticesPerSlice;
                        int f = e + 1;
                        int g = e + verticesPerRing;
                        int hIdx = g + 1;

                        triangles.Add(a);
                        triangles.Add(c);
                        triangles.Add(bIdx);
                        triangles.Add(bIdx);
                        triangles.Add(c);
                        triangles.Add(d);

                        triangles.Add(e);
                        triangles.Add(f);
                        triangles.Add(g);
                        triangles.Add(f);
                        triangles.Add(hIdx);
                        triangles.Add(g);

                        triangles.Add(a);
                        triangles.Add(bIdx);
                        triangles.Add(e);
                        triangles.Add(bIdx);
                        triangles.Add(f);
                        triangles.Add(e);

                        triangles.Add(c);
                        triangles.Add(g);
                        triangles.Add(d);
                        triangles.Add(d);
                        triangles.Add(g);
                        triangles.Add(hIdx);
                    }
                }
            }

            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.normals = normals.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.Optimize();

            return mesh;
        }

        private Vector3 CalculateHullNormal(float lengthT, float heightT, float beamT)
        {
            float dx = Mathf.Cos(lengthT * Mathf.PI);
            float dz = Mathf.Cos(beamT * Mathf.PI) * 0.5f;
            float dy = heightT - 0.5f;

            return new Vector3(dx, dy, dz).normalized;
        }

        private void SetupShipComponents(GameObject ship)
        {
            ShipVoxelizer = ship.GetComponent<HullVoxelizer>();
            if (ShipVoxelizer == null)
            {
                ShipVoxelizer = ship.AddComponent<HullVoxelizer>();
            }

            ShipVoxelizer.GridSettings = new VoxelGridSettings
            {
                GridSize = new Vector3Int(64, 48, 48),
                CellSize = Mathf.Max(ShipLength / 64f, ShipBeam / 48f, ShipDraft / 24f),
                AutoUpdate = true,
                UpdateInterval = 0.05f
            };
            ShipVoxelizer.WaterlineY = WaterLevel;

            ShipHydrodynamics = ship.GetComponent<HullHydrodynamics>();
            if (ShipHydrodynamics == null)
            {
                ShipHydrodynamics = ship.AddComponent<HullHydrodynamics>();
            }

            ShipHydrodynamics.WaterSimulator = WaterSimulator;
            ShipHydrodynamics.CenterOfGravityOffset = new Vector3(0f, -ShipDraft * 0.3f, 0f);
        }

        private void CreateAmbientWaves()
        {
            if (!EnableAmbientWaves || WaterSimulator == null) return;

            _ambientWaves.Clear();

            System.Random rng = new System.Random(42);
            for (int i = 0; i < AmbientWaveCount; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                float amp = BaseWaveAmplitude * (0.3f + (float)rng.NextDouble() * 0.7f);
                float freq = BaseWaveFrequency * (0.5f + (float)rng.NextDouble() * 1.5f);
                float decay = 0.001f + (float)rng.NextDouble() * 0.005f;

                Vector2 pos = new Vector2(
                    (float)(rng.NextDouble() - 0.5) * 200f,
                    (float)(rng.NextDouble() - 0.5) * 200f
                );

                WaveSource wave = WaveSource.Create(pos, amp, freq, dir, decay);
                _ambientWaves.Add(wave);
                WaterSimulator.AddWaveSource(wave);
            }

            if (WindSpeed > 1f)
            {
                WaveSource windWave = WaveSource.Create(
                    -WindDirection * 150f,
                    WindSpeed * 0.05f,
                    0.4f,
                    WindDirection,
                    0.002f
                );
                WaterSimulator.AddWaveSource(windWave);
            }
        }

        public void ResetSandbox()
        {
            if (_currentShip != null)
            {
                Destroy(_currentShip);
                OnShipDestroyed?.Invoke();
            }

            WaterSimulator?.ClearWaveSources();

            SpawnShip();
            CreateAmbientWaves();
        }

        public void SetWind(Vector2 direction, float speed)
        {
            WindDirection = direction.normalized;
            WindSpeed = speed;

            if (WaterSimulator != null)
            {
                for (int i = 0; i < WaterSimulator.WaveSources.Count; i++)
                {
                    WaveSource ws = WaterSimulator.WaveSources[i];
                    ws.direction = Vector2.Lerp(ws.direction, WindDirection, 0.1f);
                    ws.amplitude = Mathf.Lerp(ws.amplitude, WindSpeed * 0.02f, 0.1f);
                    WaterSimulator.WaveSources[i] = ws;
                }
            }
        }

        public void ApplyForceToShip(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (_currentShip == null) return;
            Rigidbody rb = _currentShip.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(force, mode);
            }
        }

        public void ApplyTorqueToShip(Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            if (_currentShip == null) return;
            Rigidbody rb = _currentShip.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddTorque(torque, mode);
            }
        }

        private void Update()
        {
            if (PauseSimulation) return;

            _frameTime += Time.unscaledDeltaTime;
            _frameCount++;

            if (_frameTime >= 0.5f)
            {
                _fps = _frameCount / _frameTime;
                _frameTime = 0f;
                _frameCount = 0;
            }

            if (WaterSimulator != null && ShipHydrodynamics != null)
            {
                WaterSimulator.ShipVelocity = ShipHydrodynamics.Velocity;
            }
        }

        private void OnGUI()
        {
            if (!ShowPerformanceStats && !ShowHydrostaticData) return;

            GUILayout.BeginArea(new Rect(10, 10, 320, 400));
            GUILayout.BeginVertical("box");

            GUILayout.Label("<b><size=14>船舶水动力学流体推演沙盒</size></b>");
            GUILayout.Space(5);

            if (ShowPerformanceStats)
            {
                GUILayout.Label($"FPS: {_fps:F1}");
                if (WaterSimulator != null)
                {
                    GUILayout.Label($"水面分辨率: {WaterSimulator.Settings.GridResolution}x{WaterSimulator.Settings.GridResolution}");
                    GUILayout.Label($"子步数: {WaterSimulator.Settings.SubSteps}");
                }
                if (ShipVoxelizer != null)
                {
                    GUILayout.Label($"体素网格: {ShipVoxelizer.GridSettings.GridSize.x}x{ShipVoxelizer.GridSettings.GridSize.y}x{ShipVoxelizer.GridSettings.GridSize.z}");
                    GUILayout.Label($"体素尺寸: {ShipVoxelizer.GridSettings.CellSize:F3}m");
                }
                GUILayout.Space(10);
            }

            if (ShowHydrostaticData && ShipHydrodynamics != null)
            {
                GUILayout.Label("<b>流体力学参数:</b>");
                GUILayout.Space(3);

                HydrostaticData hd = ShipVoxelizer != null ? ShipVoxelizer.CurrentHydrostaticData : default;

                GUILayout.Label($"排水体积: {hd.DisplacedVolume:F2} m³");
                GUILayout.Label($"浮力: {hd.BuoyantForce * 0.001f:F2} kN");
                GUILayout.Label($"湿表面积: {hd.WettedSurfaceArea:F2} m²");
                GUILayout.Label($"水线面面积: {hd.WaterplaneArea:F2} m²");
                GUILayout.Label($"浮心位置: ({hd.CenterOfBuoyancy.x:F2}, {hd.CenterOfBuoyancy.y:F2}, {hd.CenterOfBuoyancy.z:F2})");
                GUILayout.Label($"GM稳心高: {hd.MetacentricHeightGM:F3} m");

                GUILayout.Space(5);
                GUILayout.Label("<b>水动力参数:</b>");
                GUILayout.Space(3);

                GUILayout.Label($"总阻力: {ShipHydrodynamics.ResistanceTotal * 0.001f:F2} kN");
                GUILayout.Label($"傅汝德数: {ShipHydrodynamics.FroudeNumber:F3}");
                GUILayout.Label($"雷诺数: {ShipHydrodynamics.ReynoldsNumber:E2}");

                Vector3 vel = ShipHydrodynamics.Velocity;
                GUILayout.Label($"航速: {vel.magnitude * 1.944f:F2} 节");

                GUILayout.Space(5);
                GUILayout.Label("<b>受力状态:</b>");
                GUILayout.Space(3);

                GUILayout.Label($"浮力: ({ShipHydrodynamics.BuoyantForce.x:F0}, {ShipHydrodynamics.BuoyantForce.y:F0}, {ShipHydrodynamics.BuoyantForce.z:F0}) N");
                GUILayout.Label($"水动力: ({ShipHydrodynamics.HydrodynamicForce.x:F0}, {ShipHydrodynamics.HydrodynamicForce.y:F0}, {ShipHydrodynamics.HydrodynamicForce.z:F0}) N");
                GUILayout.Label($"总力: ({ShipHydrodynamics.TotalForce.x:F0}, {ShipHydrodynamics.TotalForce.y:F0}, {ShipHydrodynamics.TotalForce.z:F0}) N");
            }

            GUILayout.Space(10);
            if (GUILayout.Button("重置仿真"))
            {
                ResetSandbox();
            }
            PauseSimulation = GUILayout.Toggle(PauseSimulation, " 暂停仿真");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}

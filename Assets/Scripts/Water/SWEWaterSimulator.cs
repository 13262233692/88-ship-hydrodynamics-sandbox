using System;
using System.Collections.Generic;
using UnityEngine;
using ShipHydrodynamics.Core;

namespace ShipHydrodynamics.Water
{
    public class SWEWaterSimulator : MonoBehaviour
    {
        [Header("Compute Shader")]
        public ComputeShader SWEShader;

        [Header("Settings")]
        public WaterSimulationSettings Settings = new WaterSimulationSettings();

        [Header("Wave Sources")]
        public List<WaveSource> WaveSources = new List<WaveSource>();

        [Header("Ship Interaction")]
        public Transform ShipTransform;
        public Vector3 ShipVelocity;
        public float ShipLength = 100f;
        public float ShipBeam = 20f;
        public float ShipDraft = 8f;

        [Header("Output")]
        public RenderTexture HeightField;
        public RenderTexture NormalField;
        public RenderTexture VelocityField;

        private RenderTexture _heightFieldPrev;
        private RenderTexture _velocityFieldPrev;
        private RenderTexture _hullHeightMask;

        private ComputeBuffer _waveSourceBuffer;

        private int _kernelInitialize;
        private int _kernelUpdate;
        private int _kernelNormals;
        private int _kernelHullInteraction;
        private int _kernelBoundary;
        private int _kernelAddWaves;

        private bool _initialized;
        private float _totalTime;

        public event Action OnWaterUpdated;

        private void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            CreateRenderTextures();
            InitializeKernels();
            CreateWaveSourceBuffer();
            RunInitialization();
            _initialized = true;
        }

        private void CreateRenderTextures()
        {
            int res = Settings.GridResolution;

            ReleaseTexture(ref HeightField);
            ReleaseTexture(ref _heightFieldPrev);
            ReleaseTexture(ref VelocityField);
            ReleaseTexture(ref _velocityFieldPrev);
            ReleaseTexture(ref NormalField);
            ReleaseTexture(ref _hullHeightMask);

            HeightField = CreateFloatRT(res, 1, "HeightField");
            _heightFieldPrev = CreateFloatRT(res, 1, "HeightFieldPrev");
            VelocityField = CreateFloatRT(res, 2, "VelocityField");
            _velocityFieldPrev = CreateFloatRT(res, 2, "VelocityFieldPrev");
            NormalField = CreateFloatRT(res, 4, "NormalField");
            _hullHeightMask = CreateFloatRT(res, 1, "HullHeightMask");
        }

        private RenderTexture CreateFloatRT(int resolution, int channels, string name)
        {
            RenderTextureFormat format = channels switch
            {
                1 => RenderTextureFormat.RFloat,
                2 => RenderTextureFormat.RGFloat,
                _ => RenderTextureFormat.ARGBFloat
            };

            RenderTexture rt = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                useMipMap = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name
            };
            rt.Create();
            return rt;
        }

        private void ReleaseTexture(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                rt = null;
            }
        }

        private void InitializeKernels()
        {
            if (SWEShader == null)
            {
                Debug.LogError("SWEWaterSimulator: Compute Shader is not assigned!");
                return;
            }

            _kernelInitialize = SWEShader.FindKernel("InitializeWater");
            _kernelUpdate = SWEShader.FindKernel("UpdateSWE");
            _kernelNormals = SWEShader.FindKernel("UpdateNormals");
            _kernelHullInteraction = SWEShader.FindKernel("ApplyHullInteraction");
            _kernelBoundary = SWEShader.FindKernel("ApplyBoundaryConditions");
            _kernelAddWaves = SWEShader.FindKernel("AddWaveSource");
        }

        private void CreateWaveSourceBuffer()
        {
            _waveSourceBuffer?.Release();
            _waveSourceBuffer = new ComputeBuffer(Mathf.Max(WaveSources.Count, 1), 40);
            if (WaveSources.Count > 0)
            {
                _waveSourceBuffer.SetData(WaveSources);
            }
        }

        private void RunInitialization()
        {
            SetAllTextures(_kernelInitialize);
            SetCommonParameters(_kernelInitialize);
            Dispatch16x16(_kernelInitialize);
        }

        private void SetCommonParameters(int kernel)
        {
            SWEShader.SetInt("_GridWidth", Settings.GridResolution);
            SWEShader.SetInt("_GridHeight", Settings.GridResolution);
            SWEShader.SetFloat("_CellSize", Settings.WaterSize / Settings.GridResolution);
            SWEShader.SetFloat("_TimeStep", Time.fixedDeltaTime * Settings.TimeStepScale);
            SWEShader.SetFloat("_Gravity", Settings.Gravity);
            SWEShader.SetFloat("_Viscosity", Settings.Viscosity);
            SWEShader.SetFloat("_RestDepth", Settings.RestDepth);
            SWEShader.SetFloat("_Damping", Settings.Damping);
            SWEShader.SetFloat("_Time", _totalTime);
        }

        private void SetShipParameters(int kernel)
        {
            if (ShipTransform == null) return;

            Vector3 shipPos = ShipTransform.position;
            Vector2 shipVel = new Vector2(ShipVelocity.x, ShipVelocity.z);

            SWEShader.SetFloats("_ShipPosition", shipPos.x, shipPos.y, shipPos.z);
            SWEShader.SetFloats("_ShipVelocity", shipVel.x, shipVel.y);
            SWEShader.SetFloat("_ShipLength", ShipLength);
            SWEShader.SetFloat("_ShipBeam", ShipBeam);
            SWEShader.SetFloat("_ShipDraft", ShipDraft);
        }

        private void SetAllTextures(int kernel)
        {
            SWEShader.SetTexture(kernel, "_HeightField", HeightField);
            SWEShader.SetTexture(kernel, "_HeightFieldPrev", _heightFieldPrev);
            SWEShader.SetTexture(kernel, "_VelocityField", VelocityField);
            SWEShader.SetTexture(kernel, "_VelocityFieldPrev", _velocityFieldPrev);
            SWEShader.SetTexture(kernel, "_NormalField", NormalField);
            SWEShader.SetTexture(kernel, "_HullHeightMask", _hullHeightMask);
        }

        private void Dispatch16x16(int kernel)
        {
            int groups = Mathf.CeilToInt(Settings.GridResolution / 16f);
            SWEShader.Dispatch(kernel, groups, groups, 1);
        }

        private void FixedUpdate()
        {
            if (!_initialized || SWEShader == null) return;

            _totalTime += Time.fixedDeltaTime;

            for (int i = 0; i < Settings.SubSteps; i++)
            {
                SimulationStep();
            }
        }

        private void SimulationStep()
        {
            SetCommonParameters(_kernelAddWaves);
            SetAllTextures(_kernelAddWaves);
            if (WaveSources.Count > 0 && Settings.InteractiveWaveSources)
            {
                SWEShader.SetBuffer(_kernelAddWaves, "_WaveSources", _waveSourceBuffer);
                SWEShader.SetInt("_WaveSourceCount", WaveSources.Count);
                Dispatch16x16(_kernelAddWaves);
            }

            SetCommonParameters(_kernelHullInteraction);
            SetAllTextures(_kernelHullInteraction);
            SetShipParameters(_kernelHullInteraction);
            if (ShipTransform != null && Settings.EnableKelvinWakes)
            {
                Dispatch16x16(_kernelHullInteraction);
            }

            SetCommonParameters(_kernelUpdate);
            SetAllTextures(_kernelUpdate);
            Dispatch16x16(_kernelUpdate);

            SetCommonParameters(_kernelNormals);
            SetAllTextures(_kernelNormals);
            Dispatch16x16(_kernelNormals);

            SetCommonParameters(_kernelBoundary);
            SetAllTextures(_kernelBoundary);
            Dispatch16x16(_kernelBoundary);

            OnWaterUpdated?.Invoke();
        }

        public float GetWaterHeightAtWorldPosition(Vector3 worldPos)
        {
            float uvX = Mathf.InverseLerp(
                transform.position.x - Settings.WaterSize * 0.5f,
                transform.position.x + Settings.WaterSize * 0.5f,
                worldPos.x
            );
            float uvZ = Mathf.InverseLerp(
                transform.position.z - Settings.WaterSize * 0.5f,
                transform.position.z + Settings.WaterSize * 0.5f,
                worldPos.z
            );

            return GetWaterHeightAtUV(uvX, uvZ);
        }

        public float GetWaterHeightAtUV(float u, float v)
        {
            RenderTexture current = RenderTexture.active;
            RenderTexture.active = HeightField;

            Texture2D tex = new Texture2D(Settings.GridResolution, Settings.GridResolution, TextureFormat.RFloat, false);
            tex.ReadPixels(new Rect(0, 0, Settings.GridResolution, Settings.GridResolution), 0, 0);
            tex.Apply();

            int x = Mathf.Clamp(Mathf.FloorToInt(u * Settings.GridResolution), 0, Settings.GridResolution - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(v * Settings.GridResolution), 0, Settings.GridResolution - 1);

            float height = tex.GetPixel(x, y).r;

            RenderTexture.active = current;
            Destroy(tex);

            return height;
        }

        public Vector3 GetWaterNormalAtWorldPosition(Vector3 worldPos)
        {
            float uvX = Mathf.InverseLerp(
                transform.position.x - Settings.WaterSize * 0.5f,
                transform.position.x + Settings.WaterSize * 0.5f,
                worldPos.x
            );
            float uvZ = Mathf.InverseLerp(
                transform.position.z - Settings.WaterSize * 0.5f,
                transform.position.z + Settings.WaterSize * 0.5f,
                worldPos.z
            );

            RenderTexture current = RenderTexture.active;
            RenderTexture.active = NormalField;

            Texture2D tex = new Texture2D(Settings.GridResolution, Settings.GridResolution, TextureFormat.RGBAFloat, false);
            tex.ReadPixels(new Rect(0, 0, Settings.GridResolution, Settings.GridResolution), 0, 0);
            tex.Apply();

            int x = Mathf.Clamp(Mathf.FloorToInt(uvX * Settings.GridResolution), 0, Settings.GridResolution - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uvZ * Settings.GridResolution), 0, Settings.GridResolution - 1);

            Color normalColor = tex.GetPixel(x, y);
            Vector3 normal = new Vector3(
                normalColor.r * 2f - 1f,
                normalColor.g * 2f - 1f,
                normalColor.b * 2f - 1f
            ).normalized;

            RenderTexture.active = current;
            Destroy(tex);

            return normal;
        }

        public Vector2 GetWaterVelocityAtUV(float u, float v)
        {
            RenderTexture current = RenderTexture.active;
            RenderTexture.active = VelocityField;

            Texture2D tex = new Texture2D(Settings.GridResolution, Settings.GridResolution, TextureFormat.RGFloat, false);
            tex.ReadPixels(new Rect(0, 0, Settings.GridResolution, Settings.GridResolution), 0, 0);
            tex.Apply();

            int x = Mathf.Clamp(Mathf.FloorToInt(u * Settings.GridResolution), 0, Settings.GridResolution - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(v * Settings.GridResolution), 0, Settings.GridResolution - 1);

            Color velColor = tex.GetPixel(x, y);
            Vector2 velocity = new Vector2(velColor.r, velColor.g);

            RenderTexture.active = current;
            Destroy(tex);

            return velocity;
        }

        public void AddWaveSource(WaveSource source)
        {
            WaveSources.Add(source);
            CreateWaveSourceBuffer();
        }

        public void RemoveWaveSourceAt(int index)
        {
            if (index >= 0 && index < WaveSources.Count)
            {
                WaveSources.RemoveAt(index);
                CreateWaveSourceBuffer();
            }
        }

        public void ClearWaveSources()
        {
            WaveSources.Clear();
            CreateWaveSourceBuffer();
        }

        private void OnDestroy()
        {
            ReleaseTexture(ref HeightField);
            ReleaseTexture(ref _heightFieldPrev);
            ReleaseTexture(ref VelocityField);
            ReleaseTexture(ref _velocityFieldPrev);
            ReleaseTexture(ref NormalField);
            ReleaseTexture(ref _hullHeightMask);

            _waveSourceBuffer?.Release();
        }

        public void ResetSimulation()
        {
            if (SWEShader == null) return;
            RunInitialization();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            Vector3 center = transform.position;
            Vector3 size = new Vector3(Settings.WaterSize, 0.01f, Settings.WaterSize);

            Gizmos.color = new Color(0f, 0.5f, 1f, 0.2f);
            Gizmos.DrawWireCube(center, size);
        }
    }
}

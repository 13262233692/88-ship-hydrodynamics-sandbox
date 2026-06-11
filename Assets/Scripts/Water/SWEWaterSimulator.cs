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

        [Header("Read-Only Output (current readable state)")]
        public RenderTexture HeightField => _heightFields[_readIndex];
        public RenderTexture VelocityField => _velocityFields[_readIndex];
        public RenderTexture NormalField => _normalField;

        private RenderTexture[] _heightFields = new RenderTexture[2];
        private RenderTexture[] _velocityFields = new RenderTexture[2];
        private RenderTexture _normalField;
        private RenderTexture _hullHeightMask;

        private int _readIndex = 0;

        private int ReadIdx => _readIndex;
        private int WriteIdx => 1 - _readIndex;

        private ComputeBuffer _waveSourceBuffer;

        private int _kernelInitialize;
        private int _kernelUpdateSWE;
        private int _kernelNormals;
        private int _kernelHullRead;
        private int _kernelHullWrite;
        private int _kernelBoundaryRead;
        private int _kernelBoundaryWrite;
        private int _kernelWaveRead;
        private int _kernelWaveWrite;

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

            for (int i = 0; i < 2; i++)
            {
                ReleaseTexture(ref _heightFields[i]);
                ReleaseTexture(ref _velocityFields[i]);
            }
            ReleaseTexture(ref _normalField);
            ReleaseTexture(ref _hullHeightMask);

            _heightFields[0] = CreateFloatRT(res, 1, "HeightField_A");
            _heightFields[1] = CreateFloatRT(res, 1, "HeightField_B");
            _velocityFields[0] = CreateFloatRT(res, 2, "VelocityField_A");
            _velocityFields[1] = CreateFloatRT(res, 2, "VelocityField_B");
            _normalField = CreateFloatRT(res, 4, "NormalField");
            _hullHeightMask = CreateFloatRT(res, 1, "HullHeightMask");

            _readIndex = 0;
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
            _kernelUpdateSWE = SWEShader.FindKernel("UpdateSWE");
            _kernelNormals = SWEShader.FindKernel("UpdateNormals");
            _kernelHullRead = SWEShader.FindKernel("ApplyHullInteractionRead");
            _kernelHullWrite = SWEShader.FindKernel("ApplyHullInteractionWrite");
            _kernelBoundaryRead = SWEShader.FindKernel("ApplyBoundaryConditionsRead");
            _kernelBoundaryWrite = SWEShader.FindKernel("ApplyBoundaryConditionsWrite");
            _kernelWaveRead = SWEShader.FindKernel("AddWaveSourceRead");
            _kernelWaveWrite = SWEShader.FindKernel("AddWaveSourceWrite");
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
            SetCommonParameters(_kernelInitialize);
            SWEShader.SetTexture(_kernelInitialize, "_HeightFieldRead", _heightFields[ReadIdx]);
            SWEShader.SetTexture(_kernelInitialize, "_HeightFieldWrite", _heightFields[WriteIdx]);
            SWEShader.SetTexture(_kernelInitialize, "_VelocityFieldRead", _velocityFields[ReadIdx]);
            SWEShader.SetTexture(_kernelInitialize, "_VelocityFieldWrite", _velocityFields[WriteIdx]);
            SWEShader.SetTexture(_kernelInitialize, "_NormalField", _normalField);
            SWEShader.SetTexture(_kernelInitialize, "_HullHeightMask", _hullHeightMask);
            Dispatch16x16(_kernelInitialize);

            SwapBuffers();

            SWEShader.SetTexture(_kernelInitialize, "_HeightFieldRead", _heightFields[ReadIdx]);
            SWEShader.SetTexture(_kernelInitialize, "_HeightFieldWrite", _heightFields[WriteIdx]);
            SWEShader.SetTexture(_kernelInitialize, "_VelocityFieldRead", _velocityFields[ReadIdx]);
            SWEShader.SetTexture(_kernelInitialize, "_VelocityFieldWrite", _velocityFields[WriteIdx]);
            SWEShader.SetTexture(_kernelInitialize, "_NormalField", _normalField);
            SWEShader.SetTexture(_kernelInitialize, "_HullHeightMask", _hullHeightMask);
            Dispatch16x16(_kernelInitialize);

            SwapBuffers();
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

        private void BindPingPongTextures(int kernel)
        {
            SWEShader.SetTexture(kernel, "_HeightFieldRead", _heightFields[ReadIdx]);
            SWEShader.SetTexture(kernel, "_HeightFieldWrite", _heightFields[WriteIdx]);
            SWEShader.SetTexture(kernel, "_VelocityFieldRead", _velocityFields[ReadIdx]);
            SWEShader.SetTexture(kernel, "_VelocityFieldWrite", _velocityFields[WriteIdx]);
            SWEShader.SetTexture(kernel, "_NormalField", _normalField);
            SWEShader.SetTexture(kernel, "_HullHeightMask", _hullHeightMask);
        }

        private void SwapBuffers()
        {
            _readIndex = 1 - _readIndex;
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
            // ── PASS 1: Add wave sources ──
            // READ from current, WRITE to alternate
            if (WaveSources.Count > 0 && Settings.InteractiveWaveSources)
            {
                int waveKernel = _kernelWaveRead;
                SetCommonParameters(waveKernel);
                BindPingPongTextures(waveKernel);
                SWEShader.SetBuffer(waveKernel, "_WaveSources", _waveSourceBuffer);
                SWEShader.SetInt("_WaveSourceCount", WaveSources.Count);
                Dispatch16x16(waveKernel);
                SwapBuffers();
            }

            // ── PASS 2: Hull interaction ──
            // READ from current (wave sources applied), WRITE to alternate
            if (ShipTransform != null && Settings.EnableKelvinWakes)
            {
                int hullKernel = _kernelHullWrite;
                SetCommonParameters(hullKernel);
                BindPingPongTextures(hullKernel);
                SetShipParameters(hullKernel);
                Dispatch16x16(hullKernel);
                SwapBuffers();
            }

            // ── PASS 3: SWE physics update (core) ──
            // READ from current snapshot, WRITE to alternate — NO RACE CONDITION
            SetCommonParameters(_kernelUpdateSWE);
            BindPingPongTextures(_kernelUpdateSWE);
            Dispatch16x16(_kernelUpdateSWE);
            SwapBuffers();

            // ── PASS 4: Boundary conditions ──
            // READ from SWE output, WRITE to alternate
            SetCommonParameters(_kernelBoundaryWrite);
            BindPingPongTextures(_kernelBoundaryWrite);
            Dispatch16x16(_kernelBoundaryWrite);
            SwapBuffers();

            // ── PASS 5: Update normals ──
            // Pure READ from current, WRITE to _NormalField (no race — different target)
            SetCommonParameters(_kernelNormals);
            BindPingPongTextures(_kernelNormals);
            Dispatch16x16(_kernelNormals);

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
            RenderTexture rt = HeightField;
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RFloat, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            int x = Mathf.Clamp(Mathf.FloorToInt(u * rt.width), 0, rt.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(v * rt.height), 0, rt.height - 1);

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
            RenderTexture.active = _normalField;

            Texture2D tex = new Texture2D(_normalField.width, _normalField.height, TextureFormat.RGBAFloat, false);
            tex.ReadPixels(new Rect(0, 0, _normalField.width, _normalField.height), 0, 0);
            tex.Apply();

            int x = Mathf.Clamp(Mathf.FloorToInt(uvX * _normalField.width), 0, _normalField.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uvZ * _normalField.height), 0, _normalField.height - 1);

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
            RenderTexture rt = VelocityField;
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGFloat, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            int x = Mathf.Clamp(Mathf.FloorToInt(u * rt.width), 0, rt.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(v * rt.height), 0, rt.height - 1);

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
            for (int i = 0; i < 2; i++)
            {
                ReleaseTexture(ref _heightFields[i]);
                ReleaseTexture(ref _velocityFields[i]);
            }
            ReleaseTexture(ref _normalField);
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

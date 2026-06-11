using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ShipHydrodynamics.Core
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Triangle
    {
        public Vector3 v0;
        public Vector3 v1;
        public Vector3 v2;
        public Vector3 normal;

        public Triangle(Vector3 vertex0, Vector3 vertex1, Vector3 vertex2)
        {
            v0 = vertex0;
            v1 = vertex1;
            v2 = vertex2;
            normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
        }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelData
    {
        public uint isInside;
        public uint isSubmerged;
        public float distanceToSurface;
        public Vector3 normal;
        public float submergedFraction;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct ReductionResult
    {
        public float displacedVolume;
        public float wettedSurfaceArea;
        public float waterplaneArea;
        public Vector3 centerOfBuoyancy;
        public uint voxelCount;
    }

    [Serializable]
    public struct HydrostaticData
    {
        public float DisplacedVolume;
        public float WettedSurfaceArea;
        public float WaterplaneArea;
        public Vector3 CenterOfBuoyancy;
        public float BuoyantForce;
        public float MetacentricHeightGM;
        public float LongitudinalMetacentricHeightGML;
        public float TransverseMetacentricHeightGMT;
        public float WaterlineY;
        public uint SubmergedVoxelCount;

        public static HydrostaticData operator +(HydrostaticData a, HydrostaticData b)
        {
            return new HydrostaticData
            {
                DisplacedVolume = a.DisplacedVolume + b.DisplacedVolume,
                WettedSurfaceArea = a.WettedSurfaceArea + b.WettedSurfaceArea,
                WaterplaneArea = a.WaterplaneArea + b.WaterplaneArea,
                CenterOfBuoyancy = a.CenterOfBuoyancy + b.CenterOfBuoyancy,
                BuoyantForce = a.BuoyantForce + b.BuoyantForce,
                MetacentricHeightGM = a.MetacentricHeightGM + b.MetacentricHeightGM,
                LongitudinalMetacentricHeightGML = a.LongitudinalMetacentricHeightGML + b.LongitudinalMetacentricHeightGML,
                TransverseMetacentricHeightGMT = a.TransverseMetacentricHeightGMT + b.TransverseMetacentricHeightGMT,
                WaterlineY = Mathf.Max(a.WaterlineY, b.WaterlineY),
                SubmergedVoxelCount = a.SubmergedVoxelCount + b.SubmergedVoxelCount
            };
        }
    }

    [Serializable]
    public struct WaveSource
    {
        public Vector2 position;
        public float amplitude;
        public float frequency;
        public float phase;
        public Vector2 direction;
        public float decay;

        public static WaveSource Create(Vector2 pos, float amp, float freq, Vector2 dir, float dec = 0.01f)
        {
            return new WaveSource
            {
                position = pos,
                amplitude = amp,
                frequency = freq,
                phase = Random.Range(0f, Mathf.PI * 2f),
                direction = dir.normalized,
                decay = dec
            };
        }
    }

    [Serializable]
    public class VoxelGridSettings
    {
        public Vector3Int GridSize = new Vector3Int(64, 32, 64);
        public float CellSize = 0.2f;
        public Vector3 GridOffset = Vector3.zero;
        public bool ShowDebug = true;
        public bool AutoUpdate = true;
        public float UpdateInterval = 0.02f;

        public Vector3 GetGridMin(Vector3 hullCenter)
        {
            return hullCenter + GridOffset - GetGridExtents();
        }

        public Vector3 GetGridMax(Vector3 hullCenter)
        {
            return hullCenter + GridOffset + GetGridExtents();
        }

        public Vector3 GetGridExtents()
        {
            return new Vector3(
                GridSize.x * CellSize * 0.5f,
                GridSize.y * CellSize * 0.5f,
                GridSize.z * CellSize * 0.5f
            );
        }

        public int TotalVoxels => GridSize.x * GridSize.y * GridSize.z;
    }

    [Serializable]
    public class WaterSimulationSettings
    {
        public int GridResolution = 512;
        public float WaterSize = 200f;
        public float RestDepth = 5f;
        public float Gravity = 9.81f;
        public float Viscosity = 0.01f;
        public float Damping = 0.1f;
        public float TimeStepScale = 0.5f;
        public bool InteractiveWaveSources = true;
        public bool EnableKelvinWakes = true;
        public int SubSteps = 4;
    }

    [Serializable]
    public class HullForceSettings
    {
        public float WaterDensity = 1025f;
        public float KinematicViscosity = 1.004e-6f;
        public float FormDragCoefficient = 0.08f;
        public float FrictionalDragCoefficient = 0.004f;
        public float WaveDragCoefficient = 0.05f;
        public float AddedMassCoefficient = 0.05f;
        public float RollDamping = 500f;
        public float PitchDamping = 800f;
        public float YawDamping = 200f;
        public float HeaveDamping = 1000f;
        public bool EnableAddedMass = true;
        public bool EnableRadiationDamping = true;
        public bool EnableViscousEffects = true;
    }
}

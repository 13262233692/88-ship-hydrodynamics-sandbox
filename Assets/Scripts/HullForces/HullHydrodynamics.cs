using System;
using UnityEngine;
using ShipHydrodynamics.Core;
using ShipHydrodynamics.Voxelization;
using ShipHydrodynamics.Water;

namespace ShipHydrodynamics.HullForces
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(HullVoxelizer))]
    public class HullHydrodynamics : MonoBehaviour
    {
        [Header("References")]
        public HullVoxelizer Voxelizer;
        public SWEWaterSimulator WaterSimulator;
        public Rigidbody ShipRigidbody;

        [Header("Settings")]
        public HullForceSettings ForceSettings = new HullForceSettings();
        public Vector3 CenterOfGravityOffset = new Vector3(0f, 2f, 0f);
        public bool ApplyForces = true;

        [Header("Debug Output")]
        public Vector3 BuoyantForce;
        public Vector3 HydrodynamicForce;
        public Vector3 TotalForce;
        public Vector3 TotalTorque;
        public float FroudeNumber;
        public float ReynoldsNumber;
        public float ResistanceTotal;

        [Header("Sea State")]
        public bool EnableWaveExcitation = true;
        public float WaveAmplitude = 0.5f;
        public float WaveFrequency = 0.8f;
        public Vector2 WaveDirection = new Vector2(1f, 0f);

        private HydrostaticData _lastHydrostaticData;
        private Vector3 _lastPosition;
        private Vector3 _velocity;
        private Vector3 _angularVelocity;
        private Vector3[] _addedMassMatrix;
        private float _shipMass;
        private Vector3 _prevVelocity;

        public event Action<Vector3, Vector3> OnForcesCalculated;

        public Vector3 Velocity => _velocity;
        public Vector3 AngularVelocity => _angularVelocity;

        private void Awake()
        {
            if (Voxelizer == null) Voxelizer = GetComponent<HullVoxelizer>();
            if (ShipRigidbody == null) ShipRigidbody = GetComponent<Rigidbody>();

            if (Voxelizer != null)
            {
                Voxelizer.OnHydrostaticDataUpdated += OnHydrostaticDataUpdated;
            }
        }

        private void Start()
        {
            _lastPosition = transform.position;
            _shipMass = ShipRigidbody != null ? ShipRigidbody.mass : 1000000f;
            InitializeAddedMassMatrix();
        }

        private void InitializeAddedMassMatrix()
        {
            _addedMassMatrix = new Vector3[6];

            float volume = 1f;
            if (Voxelizer != null && Voxelizer.CurrentHydrostaticData.DisplacedVolume > 0.001f)
            {
                volume = Voxelizer.CurrentHydrostaticData.DisplacedVolume;
            }

            float m = ForceSettings.WaterDensity * volume;

            _addedMassMatrix[0] = new Vector3(m * ForceSettings.AddedMassCoefficient, 0f, 0f);
            _addedMassMatrix[1] = new Vector3(0f, m * ForceSettings.AddedMassCoefficient * 0.9f, 0f);
            _addedMassMatrix[2] = new Vector3(0f, 0f, m * ForceSettings.AddedMassCoefficient * 0.7f);
            _addedMassMatrix[3] = new Vector3(m * 0.01f, 0f, 0f);
            _addedMassMatrix[4] = new Vector3(0f, m * 0.02f, 0f);
            _addedMassMatrix[5] = new Vector3(0f, 0f, m * 0.015f);
        }

        private void OnHydrostaticDataUpdated(HydrostaticData data)
        {
            _lastHydrostaticData = data;
        }

        private void FixedUpdate()
        {
            if (!ApplyForces || ShipRigidbody == null) return;

            CalculateKinematicData();
            PushWakeParametersToWaterSimulator();
            CalculateForces();
        }

        private void PushWakeParametersToWaterSimulator()
        {
            if (WaterSimulator == null || Voxelizer == null) return;

            (float bowDraft, float sternDraft) = Voxelizer.GetBowAndSternDraft();

            float heading = Mathf.Atan2(transform.forward.x, transform.forward.z);

            float shipLength = Voxelizer.GridSettings.GridSize.x * Voxelizer.GridSettings.CellSize;
            float shipBeam = Voxelizer.GridSettings.GridSize.z * Voxelizer.GridSettings.CellSize;

            WaterSimulator.UpdateShipWakeParameters(
                transform.position,
                _velocity,
                shipLength,
                shipBeam,
                Voxelizer.WaterlineY,
                bowDraft,
                sternDraft,
                heading
            );
        }

        private void CalculateKinematicData()
        {
            _velocity = (transform.position - _lastPosition) / Time.fixedDeltaTime;
            _lastPosition = transform.position;
            _angularVelocity = ShipRigidbody.angularVelocity;
            _prevVelocity = _velocity;
        }

        private void CalculateForces()
        {
            Vector3 buoyancy = CalculateBuoyancy();
            Vector3 drag = CalculateHydrodynamicDrag();
            Vector3 waveExcitation = EnableWaveExcitation ? CalculateWaveExcitationForce() : Vector3.zero;
            Vector3 radiationDamping = CalculateRadiationDamping();
            Vector3 addedMassForce = ForceSettings.EnableAddedMass ? CalculateAddedMassForce() : Vector3.zero;
            Vector3 rollPitchDamping = CalculateAngularDamping();

            BuoyantForce = buoyancy;
            HydrodynamicForce = drag + waveExcitation + radiationDamping + addedMassForce;
            TotalForce = buoyancy + HydrodynamicForce;

            Vector3 cog = transform.position + transform.TransformVector(CenterOfGravityOffset);
            Vector3 cob = _lastHydrostaticData.CenterOfBuoyancy;
            if (cob == Vector3.zero) cob = transform.position;

            Vector3 buoyancyTorque = Vector3.Cross(cob - cog, buoyancy);
            Vector3 dragTorque = Vector3.Cross(cob - cog, drag);
            Vector3 dampingTorque = rollPitchDamping;

            TotalTorque = buoyancyTorque + dragTorque + dampingTorque;

            ShipRigidbody.AddForce(TotalForce, ForceMode.Force);
            ShipRigidbody.AddTorque(TotalTorque, ForceMode.Force);

            OnForcesCalculated?.Invoke(TotalForce, TotalTorque);
        }

        private Vector3 CalculateBuoyancy()
        {
            if (_lastHydrostaticData.DisplacedVolume < 0.001f)
            {
                return Vector3.zero;
            }

            float buoyantForceMag = ForceSettings.WaterDensity * 9.81f * _lastHydrostaticData.DisplacedVolume;

            if (WaterSimulator != null)
            {
                float waterHeight = WaterSimulator.GetWaterHeightAtWorldPosition(transform.position);
                float waterlineDelta = (WaterSimulator.transform.position.y + waterHeight) - Voxelizer.WaterlineY;

                if (Mathf.Abs(waterlineDelta) > 0.01f)
                {
                    Voxelizer.WaterlineY = WaterSimulator.transform.position.y + waterHeight;
                    Voxelizer.MarkForUpdate();
                }
            }

            Vector3 buoyancy = Vector3.up * buoyantForceMag;

            float displacement = buoyantForceMag / 9.81f;
            if (displacement > _shipMass * 0.5f && displacement < _shipMass * 2f)
            {
                float targetDraft = _shipMass / (ForceSettings.WaterDensity * _lastHydrostaticData.WaterplaneArea);
                buoyancy *= Mathf.Clamp(targetDraft / Mathf.Max(targetDraft - 0.5f, 0.1f), 0.8f, 1.2f);
            }

            return buoyancy;
        }

        private Vector3 CalculateHydrodynamicDrag()
        {
            Vector3 dragForce = Vector3.zero;
            float speed = _velocity.magnitude;

            if (speed < 0.01f) return Vector3.zero;

            Vector3 forwardDir = transform.forward;
            Vector3 velocityDir = _velocity.normalized;
            float angleOfAttack = Vector3.Angle(forwardDir, velocityDir) * Mathf.Deg2Rad;

            float L = Mathf.Max(transform.localScale.x * 10f, 10f);
            FroudeNumber = speed / Mathf.Sqrt(9.81f * L);
            ReynoldsNumber = speed * L / ForceSettings.KinematicViscosity;

            float formDrag = CalculateFormDrag(speed, angleOfAttack);
            float frictionalDrag = CalculateFrictionalDrag(speed);
            float waveDrag = CalculateWaveDrag(speed, FroudeNumber);

            ResistanceTotal = formDrag + frictionalDrag + waveDrag;

            Vector3 dragDir = -velocityDir;
            dragForce = dragDir * ResistanceTotal;

            float liftCoeff = Mathf.Sin(2f * angleOfAttack) * 0.3f;
            Vector3 lift = transform.up * liftCoeff * 0.5f * ForceSettings.WaterDensity * speed * speed * _lastHydrostaticData.WaterplaneArea;

            return dragForce + lift;
        }

        private float CalculateFormDrag(float speed, float angleOfAttack)
        {
            float dragCoeff = ForceSettings.FormDragCoefficient * (1f + 3f * Mathf.Pow(Mathf.Sin(angleOfAttack), 2f));
            float projectedArea = CalculateProjectedArea();
            return 0.5f * ForceSettings.WaterDensity * speed * speed * dragCoeff * projectedArea;
        }

        private float CalculateFrictionalDrag(float speed)
        {
            float Re = Mathf.Max(ReynoldsNumber, 1000f);
            float Cf;

            if (Re < 500000f)
            {
                Cf = 1.328f / Mathf.Sqrt(Re);
            }
            else
            {
                float logRe = Mathf.Log10(Re);
                Cf = 0.455f / Mathf.Pow(logRe, 2.58f);
            }

            Cf *= (1f + ForceSettings.FrictionalDragCoefficient);

            float wettedArea = _lastHydrostaticData.WettedSurfaceArea;
            if (wettedArea < 0.01f)
            {
                wettedArea = transform.localScale.x * transform.localScale.z * 100f;
            }

            return 0.5f * ForceSettings.WaterDensity * speed * speed * Cf * wettedArea;
        }

        private float CalculateWaveDrag(float speed, float Fr)
        {
            float waveDragCoeff = ForceSettings.WaveDragCoefficient;

            float humpFactor = Mathf.Exp(-Mathf.Pow((Fr - 0.5f) / 0.15f, 2f));
            waveDragCoeff *= (1f + humpFactor * 2f);

            float hollowFactor = Mathf.Exp(-Mathf.Pow((Fr - 0.3f) / 0.1f, 2f));
            waveDragCoeff *= (1f - hollowFactor * 0.5f);

            float displacement = _shipMass;
            float gravity = 9.81f;
            float volume = displacement / ForceSettings.WaterDensity;
            float volumetricFroude = speed / Mathf.Pow(gravity * volume, 1f / 6f);

            waveDragCoeff *= Mathf.Pow(volumetricFroude, 4f) / Mathf.Pow(1f + Mathf.Pow(volumetricFroude, 6f), 2f / 3f);

            return 0.5f * ForceSettings.WaterDensity * speed * speed * waveDragCoeff * Mathf.Pow(volume, 2f / 3f);
        }

        private float CalculateProjectedArea()
        {
            Vector3 scale = transform.localScale;
            float midshipArea = scale.y * scale.z * 0.8f;

            if (_lastHydrostaticData.WaterplaneArea > 0.01f)
            {
                midshipArea = _lastHydrostaticData.WaterplaneArea * 0.3f;
            }

            return midshipArea;
        }

        private Vector3 CalculateWaveExcitationForce()
        {
            Vector3 excitation = Vector3.zero;
            Vector3 cog = transform.position + transform.TransformVector(CenterOfGravityOffset);

            float phase = Time.time * WaveFrequency * 2f * Mathf.PI;
            float waveLength = 2f * Mathf.PI * 9.81f / (WaveFrequency * WaveFrequency);
            float k = 2f * Mathf.PI / waveLength;

            Vector2 waveDir = WaveDirection.normalized;
            float dotProduct = waveDir.x * (cog.x - WaterSimulator?.transform.position.x ?? 0f) +
                               waveDir.y * (cog.z - WaterSimulator?.transform.position.z ?? 0f);

            float waveElevation = WaveAmplitude * Mathf.Sin(k * dotProduct - phase);
            float waterPressure = ForceSettings.WaterDensity * 9.81f * waveElevation;

            excitation.y = waterPressure * _lastHydrostaticData.WaterplaneArea * 0.5f;

            float rollMoment = waveElevation * _shipMass * 9.81f * Mathf.Sin(Time.time * WaveFrequency) * 0.1f;
            ShipRigidbody.AddTorque(transform.forward * rollMoment, ForceMode.Force);

            float pitchMoment = waveElevation * _shipMass * 9.81f * Mathf.Sin(Time.time * WaveFrequency + Mathf.PI / 4f) * 0.15f;
            ShipRigidbody.AddTorque(transform.right * pitchMoment, ForceMode.Force);

            return excitation;
        }

        private Vector3 CalculateRadiationDamping()
        {
            if (!ForceSettings.EnableRadiationDamping) return Vector3.zero;

            Vector3 damping = Vector3.zero;

            float heaveDamping = ForceSettings.HeaveDamping;
            damping.y = -_velocity.y * heaveDamping;

            damping.x = -_velocity.x * ForceSettings.HeaveDamping * 0.3f;
            damping.z = -_velocity.z * ForceSettings.HeaveDamping * 0.5f;

            return damping;
        }

        private Vector3 CalculateAddedMassForce()
        {
            Vector3 acceleration = (_velocity - _prevVelocity) / Time.fixedDeltaTime;
            Vector3 addedMassForce = Vector3.zero;

            addedMassForce.x = -_addedMassMatrix[0].x * acceleration.x;
            addedMassForce.y = -_addedMassMatrix[1].y * acceleration.y;
            addedMassForce.z = -_addedMassMatrix[2].z * acceleration.z;

            return addedMassForce;
        }

        private Vector3 CalculateAngularDamping()
        {
            Vector3 dampingTorque = Vector3.zero;

            dampingTorque.x = -_angularVelocity.x * ForceSettings.RollDamping;
            dampingTorque.y = -_angularVelocity.y * ForceSettings.YawDamping;
            dampingTorque.z = -_angularVelocity.z * ForceSettings.PitchDamping;

            return dampingTorque;
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            Vector3 cog = transform.position + transform.TransformVector(CenterOfGravityOffset);

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(cog, 0.2f);

            if (TotalForce.magnitude > 0.01f)
            {
                Gizmos.color = Color.green;
                Vector3 forceEnd = cog + TotalForce.normalized * Mathf.Min(TotalForce.magnitude * 0.001f, 5f);
                Gizmos.DrawLine(cog, forceEnd);
                DrawArrowHead(cog, forceEnd, Color.green);
            }

            if (BuoyantForce.magnitude > 0.01f && _lastHydrostaticData.CenterOfBuoyancy != Vector3.zero)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(_lastHydrostaticData.CenterOfBuoyancy, 0.15f);
                Vector3 buoyEnd = _lastHydrostaticData.CenterOfBuoyancy + BuoyantForce.normalized * Mathf.Min(BuoyantForce.magnitude * 0.001f, 5f);
                Gizmos.DrawLine(_lastHydrostaticData.CenterOfBuoyancy, buoyEnd);
            }

            if (TotalTorque.magnitude > 0.01f)
            {
                Gizmos.color = Color.magenta;
                Vector3 torqueEnd = cog + TotalTorque.normalized * Mathf.Min(TotalTorque.magnitude * 0.01f, 3f);
                Gizmos.DrawLine(cog, torqueEnd);
                DrawArrowHead(cog, torqueEnd, Color.magenta);
            }
        }

        private void DrawArrowHead(Vector3 start, Vector3 end, Color color)
        {
            Vector3 dir = (end - start).normalized;
            float size = 0.2f;
            Vector3 right = Vector3.Cross(dir, Vector3.up).normalized * size;
            Vector3 up = Vector3.Cross(dir, right).normalized * size;

            Gizmos.color = color;
            Gizmos.DrawLine(end, end - dir * size * 2f + right);
            Gizmos.DrawLine(end, end - dir * size * 2f - right);
            Gizmos.DrawLine(end, end - dir * size * 2f + up);
            Gizmos.DrawLine(end, end - dir * size * 2f - up);
        }

        private void OnDestroy()
        {
            if (Voxelizer != null)
            {
                Voxelizer.OnHydrostaticDataUpdated -= OnHydrostaticDataUpdated;
            }
        }
    }
}

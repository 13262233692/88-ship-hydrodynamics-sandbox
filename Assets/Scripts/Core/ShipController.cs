using UnityEngine;
using ShipHydrodynamics.Core;
using ShipHydrodynamics.HullForces;

namespace ShipHydrodynamics.Core
{
    [RequireComponent(typeof(HullHydrodynamics))]
    public class ShipController : MonoBehaviour
    {
        [Header("Control Settings")]
        public float EngineForce = 5000000f;
        public float RudderTorque = 2000000f;
        public float MaxSpeed = 15f;
        public KeyCode ForwardKey = KeyCode.W;
        public KeyCode ReverseKey = KeyCode.S;
        public KeyCode LeftKey = KeyCode.A;
        public KeyCode RightKey = KeyCode.D;
        public KeyCode BrakeKey = KeyCode.Space;

        [Header("Propulsion")]
        [Range(0f, 1f)] public float Throttle = 0f;
        [Range(-1f, 1f)] public float RudderAngle = 0f;
        public bool AutoStabilize = true;

        [Header("Output")]
        public float CurrentSpeedKnots;
        public float CurrentHeading;

        private Rigidbody _rb;
        private HullHydrodynamics _hydro;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _hydro = GetComponent<HullHydrodynamics>();
        }

        private void Update()
        {
            HandleInput();
        }

        private void FixedUpdate()
        {
            ApplyPropulsion();
            ApplySteering();

            if (AutoStabilize)
            {
                ApplyStabilization();
            }

            CurrentSpeedKnots = _rb.velocity.magnitude * 1.944f;
            CurrentHeading = transform.eulerAngles.y;
        }

        private void HandleInput()
        {
            if (Input.GetKey(ForwardKey))
                Throttle = Mathf.Clamp01(Throttle + Time.deltaTime * 0.3f);
            else if (Input.GetKey(ReverseKey))
                Throttle = Mathf.Clamp(Throttle - Time.deltaTime * 0.3f, -0.5f);
            else
                Throttle *= (1f - Time.deltaTime * 0.2f);

            if (Input.GetKey(BrakeKey))
                Throttle *= (1f - Time.deltaTime * 2f);

            if (Input.GetKey(LeftKey))
                RudderAngle = Mathf.Clamp(RudderAngle - Time.deltaTime * 1.5f, -1f, 1f);
            else if (Input.GetKey(RightKey))
                RudderAngle = Mathf.Clamp(RudderAngle + Time.deltaTime * 1.5f, -1f, 1f);
            else
                RudderAngle *= (1f - Time.deltaTime * 0.5f);
        }

        private void ApplyPropulsion()
        {
            if (CurrentSpeedKnots > MaxSpeed && Throttle > 0f)
                Throttle *= 0.95f;

            Vector3 thrustForce = transform.forward * EngineForce * Throttle;

            Vector3 propellerPos = transform.position - transform.forward * transform.localScale.x * 4f
                                + Vector3.down * _hydro != null ? _hydro.CenterOfGravityOffset.y : 2f;

            _rb.AddForceAtPosition(thrustForce, propellerPos, ForceMode.Force);
        }

        private void ApplySteering()
        {
            float steeringFactor = Mathf.Clamp01(_rb.velocity.magnitude / 2f);

            Vector3 rudderTorque = Vector3.up * RudderTorque * RudderAngle * steeringFactor;
            Vector3 rudderPos = transform.position - transform.forward * transform.localScale.x * 3.5f;

            _rb.AddTorque(rudderTorque, ForceMode.Force);

            Vector3 lateralForce = transform.right * RudderAngle * EngineForce * 0.1f * steeringFactor;
            _rb.AddForceAtPosition(lateralForce, rudderPos, ForceMode.Force);
        }

        private void ApplyStabilization()
        {
            float rollAngle = Mathf.DeltaAngle(0, transform.eulerAngles.z);
            float pitchAngle = Mathf.DeltaAngle(0, transform.eulerAngles.x);

            Vector3 stabilizerTorque = Vector3.zero;
            stabilizerTorque.z = -rollAngle * 500000f;
            stabilizerTorque.x = -pitchAngle * 800000f;

            _rb.AddTorque(stabilizerTorque, ForceMode.Force);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 230, 10, 220, 150));
            GUILayout.BeginVertical("box");

            GUILayout.Label("<b><size=12>船舶控制</size></b>");
            GUILayout.Space(5);

            GUILayout.Label($"航速: {CurrentSpeedKnots:F2} 节");
            GUILayout.Label($"航向: {CurrentHeading:F1}°");
            GUILayout.Label($"油门: {Throttle * 100f:F0}%");
            GUILayout.Label($"舵角: {RudderAngle * 35f:F1}°");

            GUILayout.Space(5);
            GUILayout.Label("W/S: 前进/后退");
            GUILayout.Label("A/D: 左右舵");
            GUILayout.Label("空格: 制动");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}

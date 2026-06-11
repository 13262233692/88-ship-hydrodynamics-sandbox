using UnityEngine;
using ShipHydrodynamics.Core;

namespace ShipHydrodynamics.Core
{
    public class SceneBootstrap : MonoBehaviour
    {
        public static bool Initialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeAtRuntime()
        {
            if (Initialized) return;
            Initialized = true;

            GameObject sandboxObj = GameObject.Find("HydrodynamicsSandbox");
            if (sandboxObj == null)
            {
                sandboxObj = new GameObject("HydrodynamicsSandbox");
                HydrodynamicsSandbox sandbox = sandboxObj.AddComponent<HydrodynamicsSandbox>();

                SetupCamera();
                SetupLighting();
            }
        }

        private static void SetupCamera()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                mainCamera = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
                camObj.AddComponent<FlareLayer>();
            }

            mainCamera.clearFlags = CameraClearFlags.Skybox;
            mainCamera.backgroundColor = new Color(0.4f, 0.6f, 0.8f);
            mainCamera.transform.position = new Vector3(0f, 15f, -80f);
            mainCamera.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            mainCamera.fieldOfView = 60f;
            mainCamera.farClipPlane = 2000f;
            mainCamera.nearClipPlane = 0.3f;

            if (mainCamera.gameObject.GetComponent<CameraOrbitController>() == null)
            {
                mainCamera.gameObject.AddComponent<CameraOrbitController>();
            }
        }

        private static void SetupLighting()
        {
            Light sunLight = GameObject.FindObjectOfType<Light>();
            if (sunLight == null)
            {
                GameObject lightObj = new GameObject("Directional Light");
                sunLight = lightObj.AddComponent<Light>();
                sunLight.type = LightType.Directional;
            }

            sunLight.color = new Color(1f, 0.95f, 0.85f);
            sunLight.intensity = 1.2f;
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowStrength = 0.8f;
            sunLight.transform.rotation = Quaternion.Euler(45f, 30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.4f, 0.6f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.4f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.3f, 0.2f);
            RenderSettings.ambientIntensity = 1.0f;
        }
    }

    public class CameraOrbitController : MonoBehaviour
    {
        public Transform Target;
        public float Distance = 100f;
        public float MinDistance = 20f;
        public float MaxDistance = 300f;
        public float OrbitSpeed = 200f;
        public float ScrollSpeed = 50f;
        public float PanSpeed = 1f;
        public float MinVerticalAngle = 5f;
        public float MaxVerticalAngle = 85f;

        private float _currentX;
        private float _currentY;
        private Vector3 _targetOffset = Vector3.zero;
        private bool _isDragging;
        private Vector3 _lastMousePos;

        private void Start()
        {
            Vector3 angles = transform.eulerAngles;
            _currentX = angles.y;
            _currentY = angles.x;

            if (Target == null)
            {
                GameObject ship = GameObject.Find("ProceduralShip");
                if (ship != null)
                {
                    Target = ship.transform;
                }
                else
                {
                    _targetOffset = Vector3.zero;
                }
            }
        }

        private void Update()
        {
            HandleMouseInput();
            HandleKeyboardInput();
            UpdateCameraPosition();
        }

        private void HandleMouseInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _isDragging = true;
                _lastMousePos = Input.mousePosition;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                _isDragging = false;
            }

            if (_isDragging)
            {
                Vector3 delta = Input.mousePosition - _lastMousePos;
                _currentX += delta.x * OrbitSpeed * 0.002f;
                _currentY -= delta.y * OrbitSpeed * 0.002f;
                _currentY = Mathf.Clamp(_currentY, MinVerticalAngle, MaxVerticalAngle);
                _lastMousePos = Input.mousePosition;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                Distance -= scroll * ScrollSpeed;
                Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
            }

            if (Input.GetMouseButton(2))
            {
                float panX = -Input.GetAxis("Mouse X") * PanSpeed * Distance * 0.01f;
                float panY = -Input.GetAxis("Mouse Y") * PanSpeed * Distance * 0.01f;

                Vector3 right = transform.right;
                Vector3 up = Vector3.up;
                _targetOffset += right * panX + up * panY;
            }
        }

        private void HandleKeyboardInput()
        {
            float moveSpeed = PanSpeed * Distance * 0.5f * Time.deltaTime;

            if (Input.GetKey(KeyCode.KeypadPlus) || Input.GetKey(KeyCode.Equals))
                Distance = Mathf.Max(MinDistance, Distance - ScrollSpeed * Time.deltaTime);
            if (Input.GetKey(KeyCode.KeypadMinus) || Input.GetKey(KeyCode.Minus))
                Distance = Mathf.Min(MaxDistance, Distance + ScrollSpeed * Time.deltaTime);

            if (Input.GetKey(KeyCode.UpArrow)) _targetOffset += Vector3.forward * moveSpeed;
            if (Input.GetKey(KeyCode.DownArrow)) _targetOffset += Vector3.back * moveSpeed;
            if (Input.GetKey(KeyCode.LeftArrow)) _targetOffset += Vector3.left * moveSpeed;
            if (Input.GetKey(KeyCode.RightArrow)) _targetOffset += Vector3.right * moveSpeed;
        }

        private void UpdateCameraPosition()
        {
            Vector3 lookAt = (Target != null ? Target.position : Vector3.zero) + _targetOffset;

            Quaternion rotation = Quaternion.Euler(_currentY, _currentX, 0);
            Vector3 negDistance = new Vector3(0.0f, 0.0f, -Distance);
            Vector3 position = rotation * negDistance + lookAt;

            transform.rotation = rotation;
            transform.position = position;
        }
    }
}

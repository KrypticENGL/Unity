using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Evaluation rig for the anime fantasy shaders. Places the sun (Front / Side / Back relative to the
/// camera) and the camera (Close / Medium / Far around the castle, or the swatch test area) so the
/// materials can be judged under every lighting direction and at every gameplay distance.
/// Works in edit mode (change the presets in the Inspector) and in play mode
/// (1 / 2 / 3 = Front / Side / Back light, Q / W / E / R = Close / Medium / Far / Swatch camera).
/// Rendering-only helper: it moves just the assigned camera and light.
/// </summary>
[ExecuteAlways]
public class AnimeLightingEvaluator : MonoBehaviour
{
    public enum LightPreset { Front, Side, Back }
    public enum CameraPreset { Close, Medium, Far, SwatchArea }

    [Header("Rig")]
    public Light sun;
    public Camera evaluationCamera;

    [Header("Presets")]
    public LightPreset lightPreset = LightPreset.Side;
    public CameraPreset cameraPreset = CameraPreset.Medium;

    [Header("Castle Framing")]
    public Vector3 castleFocus = new Vector3(0f, 60f, 0f);
    [Tooltip("Compass direction the 3/4 camera looks from, in degrees around the castle.")]
    public float viewAzimuth = -140f;
    public Vector2 closeDistanceHeight = new Vector2(140f, 70f);
    public Vector2 mediumDistanceHeight = new Vector2(420f, 150f);
    public Vector2 farDistanceHeight = new Vector2(1300f, 260f);

    [Header("Swatch Area")]
    public Transform swatchFocus;
    public Vector2 swatchDistanceHeight = new Vector2(38f, 14f);

    [Header("Sun")]
    [Range(5f, 85f)] public float sunElevation = 40f;

    LightPreset appliedLight = (LightPreset)(-1);
    CameraPreset appliedCamera = (CameraPreset)(-1);

    void OnValidate()
    {
        // Applied from Update: moving transforms inside OnValidate is not allowed in every context.
        appliedLight = (LightPreset)(-1);
        appliedCamera = (CameraPreset)(-1);
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Application.isPlaying && Keyboard.current != null)
        {
            var k = Keyboard.current;
            if (k.digit1Key.wasPressedThisFrame) lightPreset = LightPreset.Front;
            if (k.digit2Key.wasPressedThisFrame) lightPreset = LightPreset.Side;
            if (k.digit3Key.wasPressedThisFrame) lightPreset = LightPreset.Back;
            if (k.qKey.wasPressedThisFrame) cameraPreset = CameraPreset.Close;
            if (k.wKey.wasPressedThisFrame) cameraPreset = CameraPreset.Medium;
            if (k.eKey.wasPressedThisFrame) cameraPreset = CameraPreset.Far;
            if (k.rKey.wasPressedThisFrame) cameraPreset = CameraPreset.SwatchArea;
        }
#endif
        if (cameraPreset != appliedCamera) ApplyCamera();
        if (lightPreset != appliedLight) ApplyLight();
    }

    [ContextMenu("Apply Presets")]
    public void ApplyAll()
    {
        ApplyCamera();
        ApplyLight();
    }

    void ApplyCamera()
    {
        appliedCamera = cameraPreset;
        if (evaluationCamera == null) return;

        Vector3 focus = castleFocus;
        Vector2 dh;
        switch (cameraPreset)
        {
            case CameraPreset.Close: dh = closeDistanceHeight; break;
            case CameraPreset.Far: dh = farDistanceHeight; break;
            case CameraPreset.SwatchArea:
                dh = swatchDistanceHeight;
                if (swatchFocus != null) focus = swatchFocus.position;
                break;
            default: dh = mediumDistanceHeight; break;
        }

        float az = viewAzimuth * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Sin(az) * dh.x, dh.y, Mathf.Cos(az) * dh.x);
        Transform t = evaluationCamera.transform;
        t.position = focus + offset;
        t.rotation = Quaternion.LookRotation(focus - t.position, Vector3.up);
        evaluationCamera.farClipPlane = Mathf.Max(evaluationCamera.farClipPlane, dh.x * 2.5f);
    }

    void ApplyLight()
    {
        appliedLight = lightPreset;
        if (sun == null) return;

        // Sun azimuth relative to the camera's view azimuth: behind the camera, 90 degrees to the side,
        // or opposite the camera (castle silhouetted against the sun).
        float relative = lightPreset == LightPreset.Front ? 0f : lightPreset == LightPreset.Side ? 90f : 180f;
        float az = (viewAzimuth + relative) * Mathf.Deg2Rad;
        float el = sunElevation * Mathf.Deg2Rad;
        Vector3 towardSun = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        sun.transform.rotation = Quaternion.LookRotation(-towardSun, Vector3.up);
    }
}

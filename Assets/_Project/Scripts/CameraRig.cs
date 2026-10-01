using UnityEngine;

public class CameraRig : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new(0f, 5f, -6.5f);
    [Tooltip("How much the camera follows the player sideways (0 = fixed, 1 = fully).")]
    [Range(0f, 1f)] public float sideFollow = 1f;
    public float smoothing = 6f;
    public float lookAhead = 6f;
    [Tooltip("Follow with no lag. The player turns it on for the final flight (hundreds of m/s).")]
    public bool snap;
    [Tooltip("Offset multiplier, eased towards. The player raises it when the squadron grows.")]
    public float zoom = 1f;
    [Tooltip("Aim this far above the target (puts the target lower in the frame).")]
    public float lookHeight;
    [Tooltip("Stop moving: the target flies out of a fixed shot.")]
    public bool freeze;

    Vector3 basePosition;
    float shake, currentZoom = 1f;

    void Start() => basePosition = transform.position;

    public void Shake(float amount) => shake = Mathf.Max(shake, amount);

    void LateUpdate()
    {
        if (freeze) return;
        float ease = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
        currentZoom = Mathf.Lerp(currentZoom, zoom, ease * 0.5f);
        var t = target.position;
        var wanted = new Vector3(t.x * sideFollow, t.y - 1f, t.z) + offset * currentZoom;
        basePosition = snap ? wanted : Vector3.Lerp(basePosition, wanted, ease);
        shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 1.5f);
        transform.position = basePosition + Random.insideUnitSphere * shake;
        transform.LookAt(new Vector3(t.x * sideFollow, t.y + lookHeight, t.z + lookAhead));
    }
}

using UnityEngine;

// Spinning gold coin on the runway: pops up and vanishes when the squadron flies through it.
public class Coin : MonoBehaviour
{
    public float spinSpeed = 180f;
    [Tooltip("Half-width of the pickup zone; wide enough for a 2-abreast squadron.")]
    public float pickupHalfWidth = 2.2f;

    Player player;
    Vector3 baseScale;
    float popTime = -1f;

    void Start()
    {
        player = FindAnyObjectByType<Player>();
        baseScale = transform.localScale;
    }

    void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        if (popTime >= 0f)
        {
            popTime += Time.deltaTime;
            transform.localScale = baseScale * Mathf.Max(0f, 1f - popTime * 4f); // shrinks away so it never hides the planes
            transform.position += Vector3.up * (6f * Time.deltaTime);
            if (popTime > 0.25f) gameObject.SetActive(false);
            return;
        }
        var d = transform.position - player.transform.position;
        if (Mathf.Abs(d.z) >= 1.5f || Mathf.Abs(d.x) >= pickupHalfWidth) return;
        popTime = 0f;
        player.PlayCoin();
    }
}

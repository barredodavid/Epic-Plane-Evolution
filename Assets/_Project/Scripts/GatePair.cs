using System.Collections.Generic;
using UnityEngine;

public class GatePair : MonoBehaviour
{
    public enum Side { Left, Right }

    static readonly List<GatePair> all = new();

    public Gate left, right;
    [Tooltip("Gate the player takes when Player.autopilot is on.")]
    public Side autopilotSide = Side.Right;
    [Tooltip("Optional slow-motion caption before the gates, e.g. \"+500 or -300 years?\"")]
    public string caption;
    public float captionDistance = 10f;
    [Range(0.02f, 1f)] public float captionSlowMo = 0.08f;

    [Tooltip("Autopilot heads for the other gate and swerves into its own at the last moment (fail takes).")]
    public bool feint;
    [Tooltip("Distance before the gate where the feint swerves.")]
    public float feintDistance = 4.5f;

    public float AutopilotX => (autopilotSide == Side.Left ? left : right).transform.position.x;

    bool used, captionShown;
    Player player;

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);
    void Start() => player = FindAnyObjectByType<Player>();

    public static GatePair NextAhead(float z)
    {
        GatePair next = null;
        foreach (var pair in all)
            if (!pair.used && pair.transform.position.z > z && (!next || pair.transform.position.z < next.transform.position.z))
                next = pair;
        return next;
    }

    void Update()
    {
        if (used) return;
        float ahead = transform.position.z - player.transform.position.z;
        if (!captionShown && !string.IsNullOrEmpty(caption) && ahead <= captionDistance)
        {
            captionShown = true;
            player.ShowPopup(caption, Color.white, captionSlowMo, 1.5f);
        }
        // Crossing the gate line picks the gate on the player's side. No physics trigger: a slow frame
        // could jump right over a thin trigger and silently skip the gate.
        if (ahead <= 0f) Pass(player.transform.position.x < transform.position.x ? left : right);
    }

    void Pass(Gate gate)
    {
        used = true;
        player.Apply(gate);
        left.gameObject.SetActive(false);
        right.gameObject.SetActive(false);
    }
}

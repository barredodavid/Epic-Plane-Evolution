using UnityEngine;
using UnityEngine.SceneManagement;

public class Player : MonoBehaviour
{
    public EraTable eras;
    public int startYear = 200;
    public float runSpeed = 10f;
    public float steerSpeed = 12f;
    public float laneHalfWidth = 4f;
    [Tooltip("Follows each GatePair's Autopilot Side: identical takes every time you record.")]
    public bool autopilot;
    [Range(0.05f, 1f)] public float evolveSlowMo = 0.4f;

    [Header("Ramp & flight")]
    public float rampStartZ = 150f;
    public float rampLength = 15f;
    public float rampHeight = 3f;
    [Tooltip("Flying height above the runway (wobble and banking must never touch the ground).")]
    public float hoverHeight = 1.8f;
    [Tooltip("Extra distance per additional plane (0.25 = +25% each).")]
    public float squadronBonus = 0.25f;

    [Header("References")]
    public Transform squadron;
    public TextMesh yearLabel;
    public TextMesh popup;
    public CameraRig cameraRig;
    public Material trailMaterial;
    [Tooltip("Dust puff spawned at each crash impact.")]
    public ParticleSystem dustFx;

    [Header("Sounds")]
    public AudioClip evolveSfx;
    public AudioClip planesUpSfx, badGateSfx, coinSfx, takeOffSfx, rocketSfx, crashSfx, bounceSfx, winSfx, failSfx;
    [Tooltip("Shown once the rockets reach space (starts inactive).")]
    public GameObject stars;
    public int maxVisiblePlanes = 12;
    [Tooltip("Gap between plane centres in the squadron.")]
    public float planeSpacing = 2.3f;
    [Tooltip("Each plane shrinks to this once there is more than one, so 2 abreast fit in one gate.")]
    public float squadronPlaneScale = 0.65f;
    [Tooltip("Camera distance multiplier once there is more than one plane.")]
    public float squadronZoom = 1.2f;
    [Tooltip("Camera offset the rocket ending swings to: under the squadron, looking up at the sky.")]
    public Vector3 spaceCameraOffset = new(0f, -6f, -6f);
    [Tooltip("How far above the rockets the camera aims at the end (higher = rockets lower on screen).")]
    public float spaceLookHeight = 12f;
    [Tooltip("How much each plane bobs, sways and rocks on its own (0 = rigid). The paper plane moves 1.6x more.")]
    public float wobble = 1f;

    public int Year { get; private set; }
    public int Planes { get; private set; } = 1;

    enum Phase { Run, Flight, Crash, Done }
    Phase phase;
    int eraIndex = -1;
    float targetX, steerVelocity, popupTimer, punch, flightTime, flightDistance, flightHeight, skyExposure;
    bool finished, waitingForTap;
    Vector3 crashVelocity;
    Vector3[] tumbleAxes;
    Vector3[] planeHomes; // each plane's place in the formation, before its wobble
    Vector3 takeoff, runCameraOffset;
    Material sky;

    Era CurrentEra => eras.eras[eraIndex];

    AudioSource sfx;
    float coinPitch = 1f, lastCoinTime = -10f;

    void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        autopilot = false; // the web build is the playable version; the ads are recorded in the editor
#endif
        sfx = gameObject.AddComponent<AudioSource>();
        Time.timeScale = 1f;
        SetText(popup, "");
        if (!autopilot)
        {
            waitingForTap = true;
            ShowPopup("DRAG TO\nSTEER", Color.white, 0f, 999f); // frozen until the first tap
        }
        // Own copy of the skybox: the rocket ending darkens it without touching the saved asset.
        if (RenderSettings.skybox)
        {
            sky = RenderSettings.skybox = new Material(RenderSettings.skybox);
            skyExposure = sky.GetFloat("_Exposure");
        }
        Year = startYear;
        Refresh(announce: false, planesChanged: false);
    }

    void Update()
    {
        bool tap = Input.GetMouseButtonDown(0);
        if (Input.GetKeyDown(KeyCode.R) || (tap && phase == Phase.Done && !autopilot))
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        if (waitingForTap)
        {
            if (!tap) return;
            waitingForTap = false;
            popupTimer = 0.01f; // clears the popup and restores time on the next tick
        }
        TickPopup();
        punch = Mathf.MoveTowards(punch, 0f, Time.unscaledDeltaTime * 3f);
        squadron.localScale = Vector3.one * (1f + 0.4f * punch);

        if (phase == Phase.Run) Run();
        else if (phase == Phase.Flight) Fly();
        else if (phase == Phase.Crash) Crash();
        if (phase == Phase.Run) Wobble();
    }

    void Run()
    {
        var pos = transform.position;
        pos.z += runSpeed * Time.deltaTime;

        float ramp = Mathf.InverseLerp(rampStartZ, rampStartZ + rampLength, pos.z);
        if (autopilot || ramp > 0f)
        {
            var gate = GatePair.NextAhead(pos.z);
            // No gate left or on the ramp: line up with it.
            targetX = gate && ramp <= 0f ? AutopilotTarget(gate, pos.z) : 0f;
        }
        else if (Input.GetMouseButton(0))
            targetX = Mathf.Lerp(-laneHalfWidth, laneHalfWidth, Input.mousePosition.x / Screen.width);
        // Eased like a finger dragging on a phone, not a constant-speed slide.
        pos.x = Mathf.SmoothDamp(pos.x, targetX, ref steerVelocity, 0.15f, steerSpeed * 1.5f);
        pos.y = hoverHeight + ramp * rampHeight;
        transform.position = pos;

        // Nose up along the ramp slope, and bank into turns while changing lanes.
        float pitch = ramp > 0f ? -Mathf.Atan2(rampHeight, rampLength) * Mathf.Rad2Deg : 0f;
        float roll = Mathf.Clamp(-steerVelocity * 3f, -30f, 30f);
        squadron.localRotation = Quaternion.RotateTowards(squadron.localRotation, Quaternion.Euler(pitch, 0f, roll), 150f * Time.deltaTime);

        if (ramp >= 1f) TakeOff();
    }

    // Plays like a person: far from the gates the squadron weaves around (coins, hesitation), then commits
    // to its gate. A feint heads for the other gate and swerves at the very last moment.
    // Noise follows z, not time, so every recording of a scene moves exactly the same way.
    float AutopilotTarget(GatePair gate, float z)
    {
        float ahead = gate.transform.position.z - z;
        float x = gate.AutopilotX;
        float aim = gate.feint && ahead > gate.feintDistance ? -x : x;
        float weave = (Mathf.PerlinNoise(z * 0.12f, 3.7f) - 0.5f) * 2f * laneHalfWidth * 0.7f; // squadron stays inside the rails
        return Mathf.Lerp(weave, aim, Mathf.InverseLerp(16f, 7f, ahead));
    }

    void TakeOff()
    {
        phase = Phase.Flight;
        takeoff = transform.position;
        flightTime = 0f;
        flightDistance = CurrentEra.flightDistance * (1f + squadronBonus * (Planes - 1));
        flightHeight = Mathf.Min(flightDistance * 0.15f, 80f);
        cameraRig.snap = true;
        runCameraOffset = cameraRig.offset;
        // Straighten up for the flight: wobbling planes would twist their trails across each other.
        for (int i = 0; i < squadron.childCount && i < planeHomes.Length; i++)
        {
            squadron.GetChild(i).localPosition = planeHomes[i];
            squadron.GetChild(i).localRotation = Quaternion.identity;
        }

        var colour = CurrentEra.reachesSpace ? new Color(1f, 0.6f, 0.2f) : Color.white;
        foreach (Transform plane in squadron)
        {
            var trail = plane.gameObject.AddComponent<TrailRenderer>();
            trail.material = trailMaterial;
            trail.time = 0.6f;
            trail.startWidth = 0.35f;
            trail.endWidth = 0f;
            trail.startColor = colour;
            trail.endColor = new Color(colour.r, colour.g, colour.b, 0f);
        }
        ShowPopup("TAKE OFF!", Color.yellow, 0.35f, 0.7f);
        Play(takeOffSfx);
        if (CurrentEra.reachesSpace) Play(rocketSfx, 0.35f);
    }

    // Texts have outline/shadow copies as children: keep them all in sync. Long lines shrink the whole
    // label so they never run off the portrait screen.
    const float FitChars = 9.5f; // longest line that fits the screen width at full size (checked on recordings)
    static void SetText(TextMesh label, string text)
    {
        foreach (var mesh in label.GetComponentsInChildren<TextMesh>()) mesh.text = text;
        int longest = 0;
        foreach (var line in text.Split('\n')) longest = Mathf.Max(longest, line.Length);
        label.transform.localScale = Vector3.one * Mathf.Min(1f, FitChars / Mathf.Max(1, longest));
    }

    void Fly()
    {
        var era = CurrentEra;
        flightTime += Time.deltaTime;
        float s = flightTime / era.flightSeconds;
        var previous = transform.position;

        float distance, height;
        if (era.reachesSpace)
        {
            // Never stops: the ad ends on the rockets still climbing into a darkening sky.
            distance = s * 1500f;
            height = s * s * 1200f; // bends up into the sky
            GoToSpace(s);
        }
        else
        {
            s = Mathf.Min(s, 1f);
            distance = s * flightDistance;
            height = era.crashes
                ? 1.2f * 4f * s * (1f - s) - s * s * (takeoff.y - GroundY) // small hop, then a nose-dive
                : flightHeight * 4f * s * (1f - s) - s * (takeoff.y - GroundY); // arc, lands on the grass
        }
        transform.position = takeoff + new Vector3(0f, height, distance);
        var velocity = transform.position - previous;
        if (velocity.sqrMagnitude > 1e-6f) squadron.rotation = Quaternion.LookRotation(velocity);
        if (finished) return;
        SetText(yearLabel, $"{Mathf.Min(s, 1f) * (era.reachesSpace ? 1500f : flightDistance):N0} m");
        if (s < 1f) return;
        finished = true;
        if (era.crashes)
        {
            StartCrash(velocity / Mathf.Max(Time.deltaTime, 1e-4f));
            return;
        }
        if (!era.reachesSpace)
        {
            // Landed short of space: the distance stays on the label, the ad asks for a better run.
            phase = Phase.Done;
            Impact(bounceSfx);
            ShowPopup("CAN YOU DO\nBETTER?", Color.yellow, 1f, 999f);
            Play(failSfx);
            return;
        }
        cameraRig.Shake(0.6f);
        ShowPopup("TO SPACE!", Color.yellow, 1f, 999f);
        Play(winSfx);
    }

    // ---------- Crash: impact, a few tumbling bounces, a slide, then the "can you do better?" end card ----------

    const float GroundY = 0.3f;

    void StartCrash(Vector3 impactVelocity)
    {
        phase = Phase.Crash;
        crashVelocity = impactVelocity;
        tumbleAxes = new Vector3[squadron.childCount];
        for (int i = 0; i < tumbleAxes.Length; i++) tumbleAxes[i] = Random.onUnitSphere;
        Impact(crashSfx);
        ShowPopup("CRASH!", Color.red, 0.3f, 0.6f);
    }

    void Crash()
    {
        float dt = Time.deltaTime;
        crashVelocity.y -= 25f * dt;
        var pos = transform.position + crashVelocity * dt;
        if (pos.y <= GroundY)
        {
            pos.y = GroundY;
            if (crashVelocity.y < -2f)
            {
                crashVelocity.y *= -0.45f; // bounce, losing energy
                crashVelocity.z *= 0.6f;
                Impact(bounceSfx);
            }
            else
            {
                crashVelocity.y = 0f;
                crashVelocity.z = Mathf.MoveTowards(crashVelocity.z, 0f, 10f * dt); // slide to a stop
            }
        }
        transform.position = pos;

        float speed = crashVelocity.magnitude;
        for (int i = 0; i < squadron.childCount && i < tumbleAxes.Length; i++)
            squadron.GetChild(i).Rotate(tumbleAxes[i], speed * 80f * dt, Space.World);
        SetText(yearLabel, $"{pos.z - takeoff.z:N0} m");

        if (speed > 0.05f || pos.y > GroundY) return;
        phase = Phase.Done;
        ShowPopup("CAN YOU DO\nBETTER?", Color.yellow, 1f, 999f); // the distance stays on the label above
        Play(failSfx);
    }

    void Impact(AudioClip sound)
    {
        Play(sound, 0.8f);
        cameraRig.Shake(0.25f);
        if (dustFx) Instantiate(dustFx, transform.position + Vector3.down * 0.2f, Quaternion.identity);
    }

    // Rocket ending: the camera swings under the squadron to look up, the sky fades to black, stars appear.
    void GoToSpace(float s)
    {
        float swing = Mathf.SmoothStep(0f, 1f, s * 1.5f);
        cameraRig.offset = Vector3.Lerp(runCameraOffset, spaceCameraOffset, swing);
        cameraRig.lookHeight = swing * spaceLookHeight; // rockets sit low in the frame, under the texts
        // Final shot: the camera stops and the rockets shrink away among the stars, then the ad is over.
        if (s >= 1.4f) cameraRig.freeze = true;
        if (s >= 3f) phase = Phase.Done;
        float dark = Mathf.InverseLerp(0.3f, 1.1f, s);
        if (sky) sky.SetFloat("_Exposure", Mathf.Lerp(skyExposure, 0.02f, dark));
        RenderSettings.fog = dark < 0.3f;
        if (!stars) return;
        stars.SetActive(dark > 0.6f);
        if (!cameraRig.freeze) stars.transform.position = transform.position; // the star field travels with the rockets
    }

    public void Apply(Gate gate)
    {
        int planesBefore = Planes, eraBefore = eraIndex;
        switch (gate.op)
        {
            case Gate.Op.AddYears: Year += gate.value; break;
            case Gate.Op.MultiplyYears: Year *= gate.value; break;
            case Gate.Op.DivideYears: Year /= Mathf.Max(1, gate.value); break;
            case Gate.Op.MultiplyPlanes: Planes = Mathf.Max(1, Planes * gate.value); break;
        }
        Refresh(announce: true, planesChanged: Planes != planesBefore);

        if (!gate.IsGood) Play(badGateSfx);
        else if (eraIndex > eraBefore) Play(evolveSfx);
        else if (Planes > planesBefore) Play(planesUpSfx);
        else Play(evolveSfx, 0.5f, 1.3f); // good gate, same era: a lighter version
    }

    // One shared 2D source. Unity's PlayOneShot lets sounds overlap; the pitch applies to the new one.
    void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (!clip) return;
        sfx.pitch = pitch;
        sfx.PlayOneShot(clip, volume);
    }

    // Each coin in a quick streak rings a little higher, the classic pickup feel.
    public void PlayCoin()
    {
        coinPitch = Time.time - lastCoinTime > 1f ? 1f : Mathf.Min(coinPitch + 0.06f, 1.6f);
        lastCoinTime = Time.time;
        Play(coinSfx, 0.6f, coinPitch);
    }

    void Refresh(bool announce, bool planesChanged)
    {
        SetText(yearLabel, Year < 0 ? $"{-Year} BC" : $"YEAR {Year}");
        int index = eras.IndexFor(Year);
        bool newEra = index != eraIndex;
        eraIndex = index;
        if (newEra || planesChanged) BuildSquadron();
        if (!announce) return;

        string message = newEra ? CurrentEra.name.ToUpper() + "!"
            : planesChanged ? $"x{Planes} PLANES!\n+{squadronBonus * (Planes - 1) * 100f:0}% DISTANCE" : null;
        if (message == null) return;
        punch = 1f;
        cameraRig.Shake(0.3f);
        ShowPopup(message, Color.yellow, evolveSlowMo, 0.8f);
    }

    // Rows of 2 (the odd plane out flies centred): a squadron is never wider than one gate, so it never
    // straddles both. Rows are centred on the player, not stacked towards the camera, and every extra
    // row pulls the camera back a little, so the whole squadron stays in the portrait frame.
    void BuildSquadron()
    {
        for (int i = squadron.childCount - 1; i >= 0; i--) Destroy(squadron.GetChild(i).gameObject);
        int count = Mathf.Min(Planes, maxVisiblePlanes);
        int rows = (count + 1) / 2;
        planeHomes = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            int row = i / 2;
            bool alone = count - row * 2 == 1;
            float x = alone ? 0f : (i % 2 == 0 ? -0.5f : 0.5f) * planeSpacing;
            var plane = Instantiate(CurrentEra.model, squadron, false);
            plane.transform.localScale = Vector3.one * (Planes > 1 ? squadronPlaneScale : 1f);
            planeHomes[i] = new Vector3(x, 0f, ((rows - 1) * 0.5f - row) * planeSpacing * 1.05f);
            plane.transform.localPosition = planeHomes[i];
        }
        cameraRig.zoom = Planes > 1 ? squadronZoom + 0.15f * (rows - 1) : 1f;
    }

    // Each plane drifts on its own: a slow bob, a little sideways sway, rocking wings and nose. Different
    // speeds per axis and a phase offset per plane, so the squadron never moves in lockstep.
    void Wobble()
    {
        float amount = wobble * (CurrentEra.crashes ? 1.6f : 1f);
        for (int i = 0; i < squadron.childCount && i < planeHomes.Length; i++)
        {
            float t = Time.time * 2.2f + i * 1.7f;
            var plane = squadron.GetChild(i);
            plane.localPosition = planeHomes[i] + amount * new Vector3(Mathf.Sin(t * 0.7f) * 0.15f, Mathf.Sin(t) * 0.18f, 0f);
            plane.localRotation = Quaternion.Euler(amount * Mathf.Sin(t * 1.3f) * 4f, 0f, amount * Mathf.Sin(t * 0.9f) * 9f);
        }
    }

    public void ShowPopup(string text, Color color, float slowMo, float realSeconds)
    {
        SetText(popup, text);
        popup.color = color;
        popupTimer = realSeconds;
        Time.timeScale = slowMo;
    }

    void TickPopup()
    {
        if (popupTimer <= 0f) return;
        popupTimer -= Time.unscaledDeltaTime;
        if (popupTimer > 0f) return;
        SetText(popup, "");
        Time.timeScale = 1f;
    }
}

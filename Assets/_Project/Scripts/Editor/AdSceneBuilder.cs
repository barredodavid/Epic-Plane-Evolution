using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the whole ad scene from scratch. Run it once, then do the staging by hand in the scene.
// Re-running it OVERWRITES Main.unity and the generated assets.
public static class AdSceneBuilder
{
    const string Root = "Assets/_Project";
    const float RampStartZ = 150f, RampLength = 15f, RampHeight = 3f;
    static Font font;
    static readonly Dictionary<string, Material> materials = new();

    // Level layout. Default autopilot path: 200 (Paper) -> 1900 (Biplane) -> x3 planes -> 1935 (Propeller)
    // -> 1955 (Jet) -> x6 planes -> 1969 (Rocket).
    static readonly (float z, Gate.Op leftOp, int left, Gate.Op rightOp, int right, GatePair.Side pick)[] Gates =
    {
        (20f, Gate.Op.AddYears, -100, Gate.Op.AddYears, 1700, GatePair.Side.Right),
        (45f, Gate.Op.MultiplyPlanes, 3, Gate.Op.DivideYears, 2, GatePair.Side.Left),
        (70f, Gate.Op.AddYears, -500, Gate.Op.AddYears, 35, GatePair.Side.Right),
        (95f, Gate.Op.AddYears, 20, Gate.Op.DivideYears, 3, GatePair.Side.Left),
        (120f, Gate.Op.AddYears, -900, Gate.Op.MultiplyPlanes, 2, GatePair.Side.Right),
        (140f, Gate.Op.AddYears, 14, Gate.Op.DivideYears, 2, GatePair.Side.Left),
    };

    enum Variant { Main, Fail, Mid }

    [MenuItem("Plane Evolution/Build Ad Scene")]
    public static void Build() => BuildScene(Variant.Main);

    // Variant A: same level, but the autopilot takes every bad gate and ends on the paper plane crash.
    [MenuItem("Plane Evolution/Build Fail Variant Scene")]
    public static void BuildFail() => BuildScene(Variant.Fail);

    // Variant C, "so close": climbs to the Jet (x3 planes), then swerves into two bad gates in a row -
    // "-30" back to the Biplane, "-20" keeps it there - and the biplanes land far short of space.
    // Rise then fall: the most frustrating path, the one that makes you want to try.
    [MenuItem("Plane Evolution/Build Mid Variant Scene")]
    public static void BuildMid() => BuildScene(Variant.Mid);

    static void BuildScene(Variant variant)
    {
        foreach (var folder in new[] { "Data", "Materials", "Prefabs", "Scenes" })
            if (!AssetDatabase.IsValidFolder($"{Root}/{folder}")) AssetDatabase.CreateFolder(Root, folder);
        // Lilita One (Google Fonts, OFL): a chunky mobile-game font. Falls back to Arial if missing.
        font = AssetDatabase.LoadAssetAtPath<Font>($"{Root}/Fonts/LilitaOne-Regular.ttf") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        materials.Clear();
        PlayerSettings.productName = "PlaneEvolutionAd";

        var eras = CreateEras();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.farClipPlane = 5000f;

        CreateEnvironment();
        CreateTrack();
        CreateScenery();
        var player = CreatePlayer(eras, cam.transform);
        var rig = cam.gameObject.AddComponent<CameraRig>();
        rig.target = player.transform;
        player.cameraRig = rig;
        player.stars = CreateStars();
        player.dustFx = CreateDust();
        AudioClip Sfx(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/Audio/{name}.ogg");
        player.evolveSfx = Sfx("Evolve");
        player.planesUpSfx = Sfx("PlanesUp");
        player.badGateSfx = Sfx("BadGate");
        player.coinSfx = Sfx("Coin");
        player.takeOffSfx = Sfx("TakeOff");
        player.rocketSfx = Sfx("Rocket");
        player.crashSfx = Sfx("Crash");
        player.bounceSfx = Sfx("Bounce");
        player.winSfx = Sfx("Win");
        player.failSfx = Sfx("Fail");
        CreateCoins();
        cam.transform.position = player.transform.position + rig.offset;

        var good = Mat("GateGood", new Color(0.2f, 0.55f, 1f, 0.5f), transparent: true);
        var bad = Mat("GateBad", new Color(1f, 0.3f, 0.35f, 0.5f), transparent: true);
        for (int i = 0; i < Gates.Length; i++)
        {
            var g = Gates[i];
            if (variant == Variant.Fail) g.pick = g.pick == GatePair.Side.Left ? GatePair.Side.Right : GatePair.Side.Left;
            if (variant == Variant.Mid && i == 4) (g.leftOp, g.left, g.pick) = (Gate.Op.AddYears, -30, GatePair.Side.Left);
            if (variant == Variant.Mid && i == 5) (g.rightOp, g.right, g.pick) = (Gate.Op.AddYears, -20, GatePair.Side.Right);
            var pair = new GameObject($"Gates z{g.z}").AddComponent<GatePair>();
            pair.transform.position = new Vector3(0f, 0f, g.z);
            pair.autopilotSide = g.pick;
            pair.left = CreateGate(pair.transform, -2.5f, g.leftOp, g.left, good, bad);
            pair.right = CreateGate(pair.transform, 2.5f, g.rightOp, g.right, good, bad);
            // Every bad pick is a last-second swerve away from the good gate: the "argh, why?!" moment.
            pair.feint = !(g.pick == GatePair.Side.Left ? pair.left : pair.right).IsGood;
        }

        // Scenery never moves: static batching merges it into a few draw calls (smoother WebGL).
        foreach (var root in scene.GetRootGameObjects())
            if (!root.GetComponent<Player>() && !root.GetComponent<GatePair>() && !root.GetComponent<Camera>() && !root.GetComponent<Light>() && root != player.stars && !root.GetComponentInChildren<Coin>())
                foreach (var t in root.GetComponentsInChildren<Transform>())
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        string scenePath = $"{Root}/Scenes/{variant}.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene($"{Root}/Scenes/Main.unity", true) };
        AssetDatabase.SaveAssets();
        Debug.Log("Plane Evolution ad scene built: " + scenePath);
    }

    [MenuItem("Plane Evolution/Build WebGL")]
    public static void BuildWebGL()
    {
        // Brotli: the portfolio host (Netlify, see its _headers) serves the .br files with the right encoding.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = false;
        // Hashed file names: a browser can never mix new code with a cached old .data file.
        PlayerSettings.WebGL.nameFilesAsHashes = true;
        FileUtil.DeleteFileOrDirectory("Builds/WebGL");
        var report = BuildPipeline.BuildPlayer(new[] { $"{Root}/Scenes/Main.unity" }, "Builds/WebGL", BuildTarget.WebGL, BuildOptions.None);
        Debug.Log("WebGL build: " + report.summary.result);
    }

    static EraTable CreateEras()
    {
        var table = ScriptableObject.CreateInstance<EraTable>();
        table.eras.Add(new Era { name = "Paper Plane", startYear = 0, model = ModelPrefab("Plane_Paper", "PaperPlane.glb", 3.2f, Vector3.zero), flightDistance = 12f, flightSeconds = 1.3f, crashes = true });
        table.eras.Add(new Era { name = "Biplane", startYear = 1900, model = ModelPrefab("Plane_Biplane", "Biplane.glb", 3.4f, new Vector3(0f, -90f, 0f)), flightDistance = 360f, flightSeconds = 2.5f });
        table.eras.Add(new Era { name = "Propeller", startYear = 1930, model = ModelPrefab("Plane_Propeller", "Propeller.glb", 3.4f, new Vector3(0f, 180f, 0f)), flightDistance = 900f, flightSeconds = 3f });
        table.eras.Add(new Era { name = "Jet", startYear = 1950, model = ModelPrefab("Plane_Jet", "Jet.glb", 3.4f, Vector3.zero), flightDistance = 3400f, flightSeconds = 3.2f });
        table.eras.Add(new Era { name = "Rocket", startYear = 1969, model = ModelPrefab("Plane_Rocket", "Rocket.glb", 3f, new Vector3(90f, 0f, 0f)), flightSeconds = 3.5f, reachesSpace = true });

        // Self-check of the era lookup (runs every build).
        if (table.IndexFor(200) != 0 || table.IndexFor(1899) != 0 || table.IndexFor(1935) != 2 || table.IndexFor(5000) != 4)
            throw new System.Exception("EraTable.IndexFor is broken");

        string path = $"{Root}/Data/Eras.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(table, path);
        return table;
    }

    // ---------- Planes: toy models from primitives, nose along +Z, wings along X ----------

    // Wraps an imported model (Models/) in a prefab: nose along +Z, centred on its bounds, longest side = size.
    // Imported planes face different ways, hence the per-model rotation (checked with side renders).
    // Fine-tune the child's rotation/scale in the generated Prefabs/*.prefab if one looks off.
    static GameObject ModelPrefab(string prefabName, string file, float size, Vector3 euler, bool sitOnGround = false)
    {
        var root = new GameObject(prefabName);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/{file}"), root.transform);
        model.transform.localRotation = Quaternion.Euler(euler) * model.transform.localRotation;
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        foreach (var r in model.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        float k = size / (sitOnGround ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
        model.transform.localScale *= k;
        var centre = bounds.center * k;
        model.transform.localPosition = sitOnGround ? new Vector3(-centre.x, -bounds.min.y * k, -centre.z) : -centre;
        return SavePrefab(root);
    }

    static GameObject SavePrefab(GameObject go)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{go.name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ---------- Scene ----------

    static void CreateEnvironment()
    {
        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetColor("_SkyTint", new Color(0.45f, 0.7f, 1f));
        sky.SetColor("_GroundColor", new Color(0.55f, 0.75f, 0.45f));
        sky.SetFloat("_AtmosphereThickness", 0.8f);
        sky.SetFloat("_Exposure", 1.3f);
        AssetDatabase.DeleteAsset($"{Root}/Materials/Sky.mat");
        AssetDatabase.CreateAsset(sky, $"{Root}/Materials/Sky.mat");
        RenderSettings.skybox = sky;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.75f, 0.85f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.8f, 0.85f, 0.8f);
        RenderSettings.ambientGroundColor = new Color(0.45f, 0.5f, 0.4f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.78f, 0.88f, 1f);
        RenderSettings.fogStartDistance = 80f;
        RenderSettings.fogEndDistance = 600f;

        var sun = Object.FindAnyObjectByType<Light>();
        sun.color = new Color(1f, 0.95f, 0.85f);
        sun.intensity = 1.2f;
        sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.55f;
    }

    // Trees along the runway and the flight path, clouds beside the track and along the rocket's climb.
    static void CreateScenery()
    {
        Random.InitState(7); // same layout every build
        var trees = new[]
        {
            ModelPrefab("Tree_Common1", "CommonTree_1.fbx", 1f, Vector3.zero, sitOnGround: true),
            ModelPrefab("Tree_Common2", "CommonTree_2.fbx", 1f, Vector3.zero, sitOnGround: true),
            ModelPrefab("Tree_Common3", "CommonTree_3.fbx", 1f, Vector3.zero, sitOnGround: true),
            ModelPrefab("Tree_Pine", "PineTree_1.fbx", 1f, Vector3.zero, sitOnGround: true),
            ModelPrefab("Bush", "Bush_1.fbx", 1f, Vector3.zero, sitOnGround: true),
        };
        var parent = new GameObject("Scenery").transform;
        for (int i = 0; i < 220; i++)
        {
            float z = i < 100 ? Random.Range(-20f, 220f) : Random.Range(220f, 4200f);
            float x = Random.Range(8f, 60f) * (Random.value < 0.5f ? -1f : 1f);
            var prefab = trees[Random.Range(0, trees.Length)];
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            tree.transform.position = new Vector3(x, 0f, z);
            tree.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            tree.transform.localScale = Vector3.one * (prefab.name == "Bush" ? Random.Range(1.2f, 2f) : Random.Range(4f, 8f));
        }

        var cloud = Mat("Cloud", Color.white);
        cloud.EnableKeyword("_EMISSION");
        cloud.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.38f));
        for (int i = 0; i < 45; i++)
            Cloud(parent, new Vector3(Random.Range(15f, 70f) * (Random.value < 0.5f ? -1f : 1f), Random.Range(12f, 45f), Random.Range(0f, 4000f)), Random.Range(4f, 9f), cloud);
        // The rocket climbs height = (d / 1500)^2 * 1200 (see Player.Fly): scatter clouds around that curve.
        for (int i = 0; i < 30; i++)
        {
            float d = Random.Range(200f, 1500f);
            float y = d / 1500f * (d / 1500f) * 1200f + 3f;
            Cloud(parent, new Vector3(Random.Range(-35f, 35f), y + Random.Range(-15f, 15f), RampStartZ + RampLength + d), Random.Range(6f, 12f), cloud);
        }
    }

    // Star shell around the rockets once the sky has gone dark (Player.GoToSpace turns it on and moves it).
    static GameObject CreateStars()
    {
        var root = new GameObject("Stars");
        var mat = new Material(Shader.Find("Unlit/Color")) { color = Color.white };
        AssetDatabase.DeleteAsset($"{Root}/Materials/Star.mat");
        AssetDatabase.CreateAsset(mat, $"{Root}/Materials/Star.mat");
        var centre = Vector3.zero; // Player moves the whole field along with the rockets
        for (int i = 0; i < 300; i++)
        {
            var dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y);
            var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(star.GetComponent<Collider>());
            star.transform.SetParent(root.transform, false);
            star.transform.position = centre + dir * Random.Range(900f, 1600f);
            star.transform.localScale = Vector3.one * Random.Range(3f, 9f);
            var r = star.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        root.SetActive(false);
        return root;
    }

    // Puff of dust for each crash impact: sphere particles that pop up and shrink away.
    static ParticleSystem CreateDust()
    {
        var go = new GameObject("Dust");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();
        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.gravityModifier = -0.05f;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.5f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // puff upwards
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        renderer.sharedMaterial = Mat("Dust", new Color(0.85f, 0.78f, 0.62f));
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/Dust.prefab");
        Object.DestroyImmediate(go);
        return prefab.GetComponent<ParticleSystem>();
    }

    // Lines of spinning gold coins in both lanes between the gates, like the reference ad.
    static void CreateCoins()
    {
        var gold = Mat("Gold", new Color(1f, 0.78f, 0.15f));
        gold.SetFloat("_Metallic", 0.4f);
        gold.SetFloat("_Glossiness", 0.7f);
        gold.EnableKeyword("_EMISSION");
        gold.SetColor("_EmissionColor", new Color(0.35f, 0.25f, 0.02f));
        var parent = new GameObject("Coins").transform;
        float[] segmentStarts = { 26f, 51f, 76f, 101f, 125f };
        foreach (float start in segmentStarts)
            foreach (float x in new[] { -2.5f, 2.5f })
                for (int i = 0; i < 4; i++)
                {
                    var coin = new GameObject("Coin").transform;
                    coin.SetParent(parent, false);
                    coin.position = new Vector3(x, 1f, start + i * 3f);
                    var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(disc.GetComponent<Collider>());
                    disc.transform.SetParent(coin, false);
                    disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    disc.transform.localScale = new Vector3(0.8f, 0.06f, 0.8f);
                    disc.GetComponent<Renderer>().sharedMaterial = gold;
                    coin.gameObject.AddComponent<Coin>();
                }
    }

    static void Cloud(Transform parent, Vector3 position, float size, Material mat)
    {
        var root = new GameObject("Cloud").transform;
        root.SetParent(parent, false);
        root.position = position;
        for (int i = 0; i < 4; i++)
        {
            var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(puff.GetComponent<Collider>());
            puff.transform.SetParent(root, false);
            puff.transform.localPosition = new Vector3((i - 1.5f) * size * 0.45f, Random.Range(-0.1f, 0.25f) * size, Random.Range(-0.2f, 0.2f) * size);
            puff.transform.localScale = new Vector3(1f, 0.7f, 0.8f) * size * Random.Range(0.7f, 1.1f);
            var r = puff.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    static void CreateTrack()
    {
        Block("Grass", new Vector3(0f, -0.55f, 5800f), new Vector3(600f, 1f, 12000f), new Color(0.36f, 0.64f, 0.3f));
        Block("Runway", new Vector3(0f, -0.05f, 75f), new Vector3(10f, 0.1f, 160f), new Color(0.35f, 0.62f, 0.95f));
        for (float z = 0f; z < RampStartZ; z += 10f)
            Block("Stripe", new Vector3(0f, 0.01f, z), new Vector3(10f, 0.02f, 3f), new Color(0.2f, 0.4f, 0.8f));
        foreach (float x in new[] { -5.25f, 5.25f })
            Block("Rail", new Vector3(x, 0.3f, 75f), new Vector3(0.5f, 0.6f, 160f), new Color(1f, 0.85f, 0.2f));

        // Ramp: from the ground at RampStartZ up to RampHeight at the end (the plane rides 1 unit above it).
        float length = Mathf.Sqrt(RampLength * RampLength + RampHeight * RampHeight);
        var ramp = Block("Ramp", new Vector3(0f, RampHeight * 0.5f - 0.15f, RampStartZ + RampLength * 0.5f), new Vector3(10f, 0.3f, length), new Color(1f, 0.55f, 0.2f));
        ramp.transform.rotation = Quaternion.Euler(-Mathf.Atan2(RampHeight, RampLength) * Mathf.Rad2Deg, 0f, 0f);

        // Distance signs after the ramp: they fly past during the final flight and sell the speed.
        float start = RampStartZ + RampLength;
        for (int m = 100; m <= 4000; m += m < 1000 ? 100 : 500)
        {
            Block($"Sign {m} m", new Vector3(9f, 2f, start + m), new Vector3(0.3f, 4f, 0.3f), Color.white);
            Text($"{m} m", null, new Vector3(9f, 4.8f, start + m), 0.45f, Color.white);
        }
    }

    static GameObject Block(string name, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = Mat("C_" + ColorUtility.ToHtmlStringRGB(color), color);
        return go;
    }

    static Player CreatePlayer(EraTable eras, Transform cam)
    {
        var go = new GameObject("Player");
        go.transform.position = new Vector3(0f, 1.8f, 0f);
        var squadron = new GameObject("Squadron").transform;
        squadron.SetParent(go.transform, false);

        var player = go.AddComponent<Player>();
        player.eras = eras;
        player.autopilot = true; // the ad plays itself; untick to steer with the mouse
        player.squadron = squadron;
        player.trailMaterial = new Material(Shader.Find("Sprites/Default"));
        AssetDatabase.DeleteAsset($"{Root}/Materials/Trail.mat");
        AssetDatabase.CreateAsset(player.trailMaterial, $"{Root}/Materials/Trail.mat");
        player.rampStartZ = RampStartZ;
        player.rampLength = RampLength;
        player.rampHeight = RampHeight;
        // Texts ride on the camera (4 units ahead) so they stay on screen and never hide the gates.
        player.yearLabel = Text("YearLabel", cam, new Vector3(0f, 1.85f, 4f), 0.07f, Color.white);
        player.popup = Text("Popup", cam, new Vector3(0f, 1.25f, 4f), 0.065f, Color.yellow);
        // Grows downwards from under the year/distance label, so 2-3 line messages never cover it.
        foreach (var mesh in player.popup.GetComponentsInChildren<TextMesh>()) mesh.anchor = TextAnchor.UpperCenter;
        player.popup.transform.localPosition = new Vector3(0f, 1.55f, 4f);
        // Screen texts draw over the world's texts (distance signs, gate labels) instead of mixing with them.
        foreach (var r in cam.GetComponentsInChildren<MeshRenderer>()) r.sortingOrder = 10;
        return player;
    }

    static Gate CreateGate(Transform parent, float x, Gate.Op op, int value, Material good, Material bad)
    {
        var go = new GameObject(x < 0 ? "Left" : "Right");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, 0f, 0f);

        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Panel";
        Object.DestroyImmediate(panel.GetComponent<Collider>());
        panel.transform.SetParent(go.transform, false);
        panel.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        panel.transform.localScale = new Vector3(4.8f, 3f, 0.1f);

        var gate = go.AddComponent<Gate>();
        gate.op = op;
        gate.value = value;
        gate.panel = panel.GetComponent<Renderer>();
        gate.goodMaterial = good;
        gate.badMaterial = bad;
        gate.label = Text("Label", go.transform, new Vector3(0f, 1.6f, -0.2f), 0.16f, Color.white);
        var post = new Color(0.15f, 0.2f, 0.5f);
        foreach (float px in new[] { -2.45f, 2.45f })
        {
            var pole = Block("Post", Vector3.zero, new Vector3(0.18f, 3.4f, 0.18f), post);
            pole.transform.SetParent(go.transform, false);
            pole.transform.localPosition = new Vector3(px, 1.7f, 0f);
        }
        gate.OnValidate();
        return gate;
    }

    static TextMesh Text(string name, Transform parent, Vector3 localPosition, float size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var text = go.AddComponent<TextMesh>();
        text.font = font;
        text.text = name;
        text.fontSize = 64;
        text.characterSize = size;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        // Dark outline (8 offset copies) plus a drop shadow, all just behind the text: the chunky
        // mobile-game title look, readable on any background. Player/Gate update every copy.
        var template = Object.Instantiate(go);
        template.GetComponent<TextMesh>().color = new Color(0.05f, 0.08f, 0.22f);
        float o = size * 0.12f;
        for (int i = 0; i < 9; i++)
        {
            float angle = i * Mathf.PI / 4f;
            var offset = i < 8 ? new Vector3(Mathf.Cos(angle) * o, Mathf.Sin(angle) * o, 0.01f) : new Vector3(size * 0.15f, -size * 0.35f, 0.02f);
            var copy = Object.Instantiate(template, go.transform, false);
            copy.name = i < 8 ? "Outline" : "Shadow";
            copy.transform.localPosition = offset;
        }
        Object.DestroyImmediate(template);
        return text;
    }

    static Material Mat(string name, Color color, bool transparent = false)
    {
        if (materials.TryGetValue(name, out var cached)) return cached;
        string path = $"{Root}/Materials/{name}.mat";
        AssetDatabase.DeleteAsset(path);
        var mat = new Material(Shader.Find("Standard")) { color = color };
        mat.SetFloat("_Glossiness", 0.2f);
        if (transparent)
        {
            mat.SetFloat("_Mode", 2f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        AssetDatabase.CreateAsset(mat, path);
        materials[name] = mat;
        return mat;
    }
}

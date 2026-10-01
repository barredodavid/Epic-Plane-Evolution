using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;

// Records the raw ad takes (1080x1920, 30 fps, with game audio) into Recordings/.
// Menu: Plane Evolution/Record Raw Videos. Each take plays its scene from the start and stops by itself.
public static class AdRecorder
{
    const string Scenes = "Assets/_Project/Scenes";

    // Scene, output file name, seconds to record, optional slow-motion caption on the first gates.
    static readonly (string scene, string file, float seconds, string caption)[] Takes =
    {
        ("Main", "Raw_B_Evolution", 30f, null),
        ("Fail", "Raw_A_Fail", 24f, null),
        ("Mid", "Raw_C_Choice", 30f, "WHICH GATE?"),
    };

    static Queue<(string scene, string file, float seconds, string caption)> queue;
    static RecorderController controller;
    static bool quitWhenDone;

    [MenuItem("Plane Evolution/Record Raw Videos")]
    public static void RecordAll() => Start(quit: false);

    // Command line: Unity.exe -projectPath . -executeMethod AdRecorder.RecordAllAndQuit (not -batchmode: the
    // Game view has to render).
    public static void RecordAllAndQuit() => Start(quit: true);

    static void Start(bool quit)
    {
        quitWhenDone = quit;
        queue = new Queue<(string, string, float, string)>(Takes);
        // Keep our static state alive across entering/leaving play mode.
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Next();
    }

    static void Next()
    {
        if (queue.Count == 0)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorSettings.enterPlayModeOptionsEnabled = false; // back to Unity's default
            Debug.Log("AdRecorder: all takes recorded in Recordings/");
            // Drop the caption tweak (never saved) so quitting cannot stop on a "save changes?" dialog.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (quitWhenDone) EditorApplication.Exit(0);
            return;
        }
        var take = queue.Peek();
        EditorSceneManager.OpenScene($"{Scenes}/{take.scene}.unity");
        if (take.caption != null)
            foreach (var pair in UnityEngine.Object.FindObjectsByType<GatePair>(FindObjectsSortMode.None))
                if (Mathf.Approximately(pair.transform.position.z, 20f)) pair.caption = take.caption; // not saved
        EditorApplication.isPlaying = true;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            // The recorder settings have to be created once play mode has started (they don't survive entering it).
            var take = queue.Peek();
            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = take.file;
            movie.Enabled = true;
            movie.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1080, OutputHeight = 1920 };
            movie.AudioInputSettings.PreserveAudio = true;
            movie.OutputFile = $"Recordings/{take.file}";
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToTimeInterval(0f, take.seconds);
            settings.FrameRate = 30f;
            settings.CapFrameRate = true;
            controller = new RecorderController(settings);
            controller.PrepareRecording();
            controller.StartRecording();
            EditorApplication.update += WaitForEnd;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            Debug.Log($"AdRecorder: recorded {queue.Dequeue().file}.mp4");
            Next();
        }
    }

    static void WaitForEnd()
    {
        if (controller.IsRecording()) return;
        EditorApplication.update -= WaitForEnd;
        EditorApplication.isPlaying = false;
    }
}

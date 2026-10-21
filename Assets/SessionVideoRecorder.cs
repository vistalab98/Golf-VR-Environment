using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
#endif

public class SessionVideoRecorder : MonoBehaviour
{
    public bool recordVideo = true;

    [Header("Folder")]
    [Tooltip("Use the same folder as the SessionRecorder in the scene, so the video sits next to its CSV.")]
    public bool useSessionRecorderFolder = true;
    [Tooltip("Used when the option above is off, or there's no SessionRecorder. In the Editor this folder is created next to your Assets folder.")]
    public string folderName = "SessionData";

    [Header("Video")]
    public int width = 1280;
    public int height = 720;
    public bool recordAudio = true;

    [Header("Frame Rate")]
    [Tooltip("Record at the headset's own refresh rate (72, 90, 120...). Strongly recommended for VR.")]
    public bool matchHeadsetRefreshRate = true;
    [Tooltip("Used when Match Headset Refresh Rate is off, or the headset's rate can't be read.")]
    public float frameRate = 72f;

    // ---------- shared with SessionRecorder (outside the Editor-only part so builds still compile) ----------

    public static bool HasVideo { get; private set; }

    public static string CurrentVideoFile { get; private set; } = "";

    static float videoStartGameTime;

    public static float VideoTimeAt(float gameTime) => HasVideo ? gameTime - videoStartGameTime : -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        HasVideo = false;
        CurrentVideoFile = "";
#if UNITY_EDITOR
        controller = null;
        controllerSettings = null;
        part = 0;
#endif
    }

#if UNITY_EDITOR
    // Static so the recording keeps going while the scene reloads and this component is replaced
    static RecorderController controller;
    static RecorderControllerSettings controllerSettings;
    static int part;
    static float fps;
    static float startRealTime;
    static string expectedPath;
    static DateTime recordingStartedUtc;

    void OnEnable()
    {
        SessionRecorder.SessionStarted += OnAttemptStarted;
    }

    void OnDisable()
    {
        SessionRecorder.SessionStarted -= OnAttemptStarted;
    }

    void OnAttemptStarted(string playBasePath)
    {
        if (!recordVideo) return;

        playBasePath = Path.Combine(GetFolder(), Path.GetFileName(playBasePath));

        if (controller != null && controller.IsRecording()) return;

        if (controller != null)
        {
            Debug.LogWarning($"SessionVideoRecorder: {CurrentVideoFile} stopped unexpectedly. Continuing in a new part.", this);
            CleanUp();
            MoveIntoSessionData(expectedPath, recordingStartedUtc);
        }

        StartVideo(playBasePath);
    }

    void StartVideo(string playBasePath)
    {
        part++;
        string outputBase = part == 1 ? playBasePath : $"{playBasePath}_part{part}";
        var outputFolder = new DirectoryInfo(Path.GetDirectoryName(outputBase));
        outputFolder.Create();
        fps = matchHeadsetRefreshRate ? GetHeadsetRefreshRate() : frameRate;

        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Session Video";
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High
        };
        movie.ImageInputSettings = new GameViewInputSettings
        {
            OutputWidth = width,
            OutputHeight = height
        };
        movie.AudioInputSettings.PreserveAudio = recordAudio;
        // Full folder path + file name, in the format Unity's own Recorder samples use (the Recorder adds ".mp4")
        movie.OutputFile = outputFolder.FullName.Replace('\\', '/') + "/" + Path.GetFileName(outputBase);

        controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        controllerSettings.AddRecorderSettings(movie);
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
        controllerSettings.FrameRate = fps;
        controllerSettings.CapFrameRate = true;

        RecorderOptions.VerboseMode = false;
        controller = new RecorderController(controllerSettings);
        controller.PrepareRecording();
        controller.StartRecording();

        videoStartGameTime = Time.time;
        startRealTime = Time.realtimeSinceStartup;
        HasVideo = true;
        CurrentVideoFile = Path.GetFileName(outputBase) + ".mp4";
        expectedPath = Path.Combine(outputFolder.FullName, CurrentVideoFile);
        recordingStartedUtc = DateTime.UtcNow;

        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        Debug.Log($"SessionVideoRecorder: recording {CurrentVideoFile} at {fps:F0} fps. It will be saved to:\n{expectedPath}", this);
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        if (controller == null) return;

        float gameElapsed = Time.time - videoStartGameTime;
        float realElapsed = Time.realtimeSinceStartup - startRealTime;

        controller.StopRecording();
        CleanUp();
        MoveIntoSessionData(expectedPath, recordingStartedUtc);

        if (gameElapsed > 1f && realElapsed > gameElapsed * 1.03f)
        {
            Debug.LogWarning($"SessionVideoRecorder: the game couldn't keep up with {fps:F0} fps, so this Play session ran " +
                             $"{(realElapsed / gameElapsed - 1f) * 100f:F0}% slower than real time " +
                             $"({gameElapsed:F0} s of game time took {realElapsed:F0} s). The video and CSV still match " +
                             "each other exactly, but motion was slowed. Try a lower video resolution.");
        }
    }


    static void MoveIntoSessionData(string target, DateTime startedUtc)
    {
        if (string.IsNullOrEmpty(target)) return;

        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string fileName = Path.GetFileName(target);
        double giveUpAt = EditorApplication.timeSinceStartup + 30.0;
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            bool timedOut = EditorApplication.timeSinceStartup > giveUpAt;
            try
            {
                if (File.Exists(target))
                {
                    EditorApplication.update -= tick;
                    Debug.Log($"SessionVideoRecorder: video saved.\n{target}");
                    return;
                }

                string found = FindVideo(projectRoot, fileName, startedUtc);
                if (found == null)
                {
                    if (timedOut)
                    {
                        EditorApplication.update -= tick;
                        Debug.LogWarning($"SessionVideoRecorder: couldn't find the finished video {fileName} anywhere " +
                                         "in the project. Check the Recorder window's output settings.");
                    }
                    return;
                }

                File.Move(found, target);
                EditorApplication.update -= tick;
                Debug.Log($"SessionVideoRecorder: the Recorder saved the video to {found}, so it was moved next to the CSV.\n{target}");
            }
            catch (IOException e)
            {
                if (timedOut)
                {
                    EditorApplication.update -= tick;
                    Debug.LogError($"SessionVideoRecorder: couldn't move the video into SessionData: {e.Message}");
                }
            }
        };
        EditorApplication.update += tick;
    }

    static string FindVideo(string root, string fileName, DateTime startedUtc)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Library", "Temp", "Logs", "obj", "UserSettings", ".git" };
        var folders = new Stack<string>();
        folders.Push(root);
        string newest = null;
        DateTime newestTime = startedUtc.AddSeconds(-5);

        while (folders.Count > 0)
        {
            string folder = folders.Pop();
            string[] files, subfolders;
            try
            {
                files = Directory.GetFiles(folder, "*.mp4");
                subfolders = Directory.GetDirectories(folder);
            }
            catch (Exception) { continue; }

            foreach (string file in files)
            {
                if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                    return file;
                DateTime created = File.GetCreationTimeUtc(file);
                if (created > newestTime && Path.GetFileName(file).StartsWith(Path.GetFileNameWithoutExtension(fileName)))
                {
                    newestTime = created;
                    newest = file;
                }
            }
            foreach (string sub in subfolders)
            {
                if (!skip.Contains(Path.GetFileName(sub)))
                    folders.Push(sub);
            }
        }
        return newest;
    }

    static void CleanUp()
    {
        controller = null;
        if (controllerSettings != null) DestroyImmediate(controllerSettings);
        controllerSettings = null;
    }

    string GetFolder()
    {
        string name = folderName;
        if (useSessionRecorderFolder)
        {
            var sessionRecorder = FindAnyObjectByType<SessionRecorder>();
            if (sessionRecorder != null && !string.IsNullOrWhiteSpace(sessionRecorder.folderName))
                name = sessionRecorder.folderName;
        }
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", name));
    }

    float GetHeadsetRefreshRate()
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var display in displays)
        {
            if (display.running && display.TryGetDisplayRefreshRate(out float rate) && rate > 1f)
                return Mathf.Round(rate);
        }
        Debug.LogWarning($"SessionVideoRecorder: couldn't read the headset's refresh rate, using {frameRate} fps.", this);
        return frameRate;
    }
#else
    void Awake()
    {
        if (recordVideo)
            Debug.LogWarning("SessionVideoRecorder: video recording only works in the Unity Editor.", this);
    }
#endif
}
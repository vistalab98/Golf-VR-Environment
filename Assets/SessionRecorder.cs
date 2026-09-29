using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

///   Records the PlayerTracker's head and hand data during a session.
///   R = the session was good: save it to a CSV file, then start a new session.
///   F = the session had a mistake: throw the data away, reset the scene, and count it.
[DefaultExecutionOrder(100)]
public class SessionRecorder : MonoBehaviour
{
    [Header("References")]
    public PlayerTracker tracker;

    [Header("Controls")]
    public Key saveKey = Key.R;
    public Key discardKey = Key.F;
    [Tooltip("Reload the scene after saving, so the club and ball go back to their starting spots.")]
    public bool reloadSceneAfterSave = true;

    [Header("Recording")]
    [Tooltip("Samples per second. 0 = record every frame.")]
    public float sampleRate = 60f;
    public string folderName = "SessionData";

    [Header("Display")]
    [Tooltip("Show the counters in the corner of the Game view.")]
    public bool showOnScreenInfo = true;

    // Static values survive scene reloads
    static int discardsSinceLastSave;
    static int discardsThisPlay;
    static int savesThisPlay;

    // Stored between Play sessions
    const string TotalDiscardsKey = "SessionRecorder.TotalDiscards";
    const string TotalSavesKey = "SessionRecorder.TotalSaves";

    readonly List<string> rows = new List<string>();
    float startTime;
    float nextSampleTime;
    bool busy;

    public static int DiscardsSinceLastSave => discardsSinceLastSave;
    public static int TotalDiscards => PlayerPrefs.GetInt(TotalDiscardsKey, 0);
    public static int TotalSaves => PlayerPrefs.GetInt(TotalSavesKey, 0);

    // Reset the counters each time Play mode starts
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        discardsSinceLastSave = 0;
        discardsThisPlay = 0;
        savesThisPlay = 0;
    }

    void Start()
    {
        if (tracker == null)
            tracker = FindAnyObjectByType<PlayerTracker>();

        if (tracker == null)
            Debug.LogWarning("SessionRecorder: no PlayerTracker found in the scene.", this);

        StartNewSession();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || busy) return;

        if (keyboard[saveKey].wasPressedThisFrame)
            SaveSession();
        else if (keyboard[discardKey].wasPressedThisFrame)
            DiscardSession();
    }

    void LateUpdate()
    {
        if (busy || tracker == null || tracker.Head == null) return;

        if (sampleRate > 0f)
        {
            if (Time.time < nextSampleTime) return;
            nextSampleTime = Time.time + 1f / sampleRate;
        }

        var sb = new StringBuilder();
        sb.Append(F(Time.time - startTime));
        AppendPart(sb, tracker.Head);
        AppendPart(sb, tracker.LeftHand);
        AppendPart(sb, tracker.RightHand);
        rows.Add(sb.ToString());
    }

    // R: save

    public void SaveSession()
    {
        if (rows.Count == 0)
        {
            Debug.LogWarning("SessionRecorder: nothing recorded yet, so nothing was saved.");
            return;
        }

        busy = true;

        string folder = GetFolder();
        Directory.CreateDirectory(folder);

        int sessionNumber = TotalSaves + 1;
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string fileName = $"session_{sessionNumber:D3}_{stamp}.csv";
        string filePath = Path.Combine(folder, fileName);

        var sb = new StringBuilder();
        sb.AppendLine(BuildHeader());
        foreach (var row in rows)
            sb.AppendLine(row);
        File.WriteAllText(filePath, sb.ToString());

        // One summary line per saved session
        float duration = Time.time - startTime;
        int totalDiscards = TotalDiscards;
        string summaryPath = Path.Combine(folder, "sessions_summary.csv");
        if (!File.Exists(summaryPath))
        {
            File.WriteAllText(summaryPath,
                "session,date,file,duration_s,samples,discards_before_success,total_discards_so_far," +
                "head_peak_speed,left_peak_speed,right_peak_speed" + Environment.NewLine);
        }
        File.AppendAllText(summaryPath, string.Join(",",
            sessionNumber,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            fileName,
            F(duration),
            rows.Count,
            discardsSinceLastSave,
            totalDiscards,
            F(tracker.Head?.PeakSpeed ?? 0f),
            F(tracker.LeftHand?.PeakSpeed ?? 0f),
            F(tracker.RightHand?.PeakSpeed ?? 0f)) + Environment.NewLine);

        PlayerPrefs.SetInt(TotalSavesKey, sessionNumber);
        PlayerPrefs.Save();
        savesThisPlay++;

        Debug.Log($"SessionRecorder: saved session {sessionNumber} ({rows.Count} samples, {duration:F1} s) " +
                  $"after {discardsSinceLastSave} discarded attempt(s).\n{filePath}");

        discardsSinceLastSave = 0;

        if (reloadSceneAfterSave)
            ReloadScene();
        else
            StartNewSession();
    }

    // F: discard

    public void DiscardSession()
    {
        busy = true;

        discardsSinceLastSave++;
        discardsThisPlay++;
        PlayerPrefs.SetInt(TotalDiscardsKey, TotalDiscards + 1);
        PlayerPrefs.Save();

        Debug.Log($"SessionRecorder: discarded attempt. {discardsSinceLastSave} since last save, " +
                  $"{TotalDiscards} total.");

        ReloadScene();
    }

    // helpers

    void StartNewSession()
    {
        rows.Clear();
        startTime = Time.time;
        nextSampleTime = 0f;
        tracker?.ResetPeaks();
        busy = false;
    }

    void ReloadScene()
    {
        int index = SceneManager.GetActiveScene().buildIndex;
        if (index < 0)
        {
            Debug.LogError("SessionRecorder: this scene isn't in the build's scene list, so it can't be reloaded. " +
                           "Add it under File > Build Profiles > Scene List. Starting a new session without reloading.");
            StartNewSession();
            return;
        }
        SceneManager.LoadScene(index);
    }

    string GetFolder()
    {
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", folderName));
#else
        return Path.Combine(Application.persistentDataPath, folderName);
#endif
    }

    static string BuildHeader()
    {
        var columns = new List<string> { "time_s" };
        foreach (var part in new[] { "head", "left", "right" })
        {
            columns.AddRange(new[]
            {
                $"{part}_pos_x", $"{part}_pos_y", $"{part}_pos_z",
                $"{part}_rot_x", $"{part}_rot_y", $"{part}_rot_z",
                $"{part}_vel_x", $"{part}_vel_y", $"{part}_vel_z",
                $"{part}_speed", $"{part}_spin_deg_s"
            });
        }
        return string.Join(",", columns);
    }

    static void AppendPart(StringBuilder sb, TrackedPart part)
    {
        if (part == null || part.Target == null)
        {
            sb.Append(",,,,,,,,,,,"); // 11 empty columns
            return;
        }

        Vector3 p = part.Position;
        Vector3 r = part.Rotation.eulerAngles;
        Vector3 v = part.Velocity;

        sb.Append(',').Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z));
        sb.Append(',').Append(F(r.x)).Append(',').Append(F(r.y)).Append(',').Append(F(r.z));
        sb.Append(',').Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z));
        sb.Append(',').Append(F(part.Speed));
        sb.Append(',').Append(F(part.AngularVelocity.magnitude * Mathf.Rad2Deg));
    }
    static string F(float value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    [ContextMenu("Reset Saved Counters")]
    void ResetSavedCounters()
    {
        PlayerPrefs.DeleteKey(TotalDiscardsKey);
        PlayerPrefs.DeleteKey(TotalSavesKey);
        PlayerPrefs.Save();
        ResetStatics();
        Debug.Log("SessionRecorder: counters reset. Existing files were not deleted.");
    }

    void OnGUI()
    {
        if (!showOnScreenInfo) return;

        string text =
            $"Recording: {(Time.time - startTime):F1} s  ({rows.Count} samples)\n" +
            $"Discards since last save: {discardsSinceLastSave}\n" +
            $"This Play session: {savesThisPlay} saved, {discardsThisPlay} discarded\n" +
            $"All time: {TotalSaves} saved, {TotalDiscards} discarded\n" +
            $"{saveKey} = save    {discardKey} = discard & reset";

        GUI.Box(new Rect(10, 10, 330, 95), GUIContent.none);
        GUI.Label(new Rect(18, 14, 320, 90), text);
    }
}
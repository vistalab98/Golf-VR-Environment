using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(100)]
public class SessionRecorder : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Leave empty to find it automatically.")]
    public PlayerTracker tracker;

    [Header("Controls")]
    public Key saveKey = Key.R;
    public Key discardKey = Key.F;
    [Tooltip("Reload the scene after saving, so the club and ball go back to their starting spots.")]
    public bool reloadSceneAfterSave = true;

    [Header("Recording")]
    [Tooltip("Samples per second. 0 = record every frame.")]
    public float sampleRate = 60f;
    [Tooltip("In the Editor this folder is created next to your Assets folder. In a build it goes in Application.persistentDataPath.")]
    public string folderName = "SessionData";

    [Header("Display")]
    [Tooltip("Show the counters in the corner of the Game view.")]
    public bool showOnScreenInfo = true;

    // ---------- values that survive scene reloads (reset when Play starts) ----------
    static int discardsSinceLastSave;
    static int discardsThisPlay;
    static int savesThisPlay;
    static int attemptsThisPlay;
    static string lastSaveMessage = "";
    static bool lastSaveOk = true;

    static string playBaseName;
    static readonly Dictionary<string, List<string>> pendingLines = new Dictionary<string, List<string>>();
    static readonly Dictionary<string, int> expectedLineCounts = new Dictionary<string, int>();

    // Stored between Play sessions
    const string TotalDiscardsKey = "SessionRecorder.TotalDiscards";
    const string TotalSavesKey = "SessionRecorder.TotalSaves";

    // ---------- the attempt currently being recorded ----------
    readonly List<string> rows = new List<string>();
    readonly List<float> rowTimes = new List<float>();
    float startTime;
    float nextSampleTime;
    bool busy = true;
    bool started;
    bool attemptOpen;

    public static event Action<string> SessionStarted;
    public static event Action<string> SessionSaved;
    public static event Action SessionDiscarded;

    public static int DiscardsSinceLastSave => discardsSinceLastSave;
    public static int TotalDiscards => PlayerPrefs.GetInt(TotalDiscardsKey, 0);
    public static int TotalSaves => PlayerPrefs.GetInt(TotalSavesKey, 0);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        discardsSinceLastSave = 0;
        discardsThisPlay = 0;
        savesThisPlay = 0;
        attemptsThisPlay = 0;
        lastSaveMessage = "";
        lastSaveOk = true;
        playBaseName = null;
        pendingLines.Clear();
        expectedLineCounts.Clear();
        headers.Clear();
        SessionStarted = null;
        SessionSaved = null;
        SessionDiscarded = null;
    }

    IEnumerator Start()
    {
        if (tracker == null)
            tracker = FindAnyObjectByType<PlayerTracker>();

        if (tracker == null)
            Debug.LogWarning("SessionRecorder: no PlayerTracker found in the scene.", this);

        // One combined file (and video) per Play session
        if (playBaseName == null)
            playBaseName = "play_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        yield return PlayerSpawnPoint.WaitUntilReady();
        StartNewSession(PlayerSpawnPoint.ReadyTime);
        started = true;
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
            float interval = 1f / sampleRate;
            while (nextSampleTime <= Time.time)
                nextSampleTime += interval;
        }

        float videoTime = SessionVideoRecorder.VideoTimeAt(Time.time);

        var sb = new StringBuilder();
        sb.Append(videoTime >= 0f ? F(videoTime) : "");
        sb.Append(',').Append(F(Time.time - startTime));
        AppendPart(sb, tracker.Head);
        AppendPart(sb, tracker.LeftHand);
        AppendPart(sb, tracker.RightHand);
        rows.Add(sb.ToString());
        rowTimes.Add(Time.time - startTime);
    }

    // ---------- R: save ----------

    public void SaveSession()
    {
        if (rows.Count == 0)
        {
            Debug.LogWarning("SessionRecorder: nothing recorded yet, so nothing was saved.");
            return;
        }

        busy = true;

        int sessionNumber = TotalSaves + 1;
        float duration = Time.time - startTime;
        var check = CheckSession(duration);
        bool written = FinishAttempt("saved", sessionNumber, check);

        PlayerPrefs.SetInt(TotalSavesKey, sessionNumber);
        PlayerPrefs.Save();
        savesThisPlay++;

        lastSaveOk = check.ok && written;
        if (!written)
            lastSaveMessage = $"Session {sessionNumber} is kept in memory, but the CSV is locked (open in Excel?). " +
                              "Close it; it will be written on the next R or F.";
        else
            lastSaveMessage = check.ok
                ? $"Saved session {sessionNumber}: {rows.Count} samples, {duration:F1} s. All checks passed."
                : $"Saved session {sessionNumber}, but CHECK IT: {check.issues}";

        string log = $"SessionRecorder: attempt {attemptsThisPlay} saved as session {sessionNumber} " +
                     $"({rows.Count} samples, {duration:F1} s) after {discardsSinceLastSave} discarded attempt(s).\n" +
                     CombinedCsvPath();
        if (check.ok) Debug.Log(log, this);
        else Debug.LogWarning(log + $"\nProblems: {check.issues}", this);

        discardsSinceLastSave = 0;

        SessionSaved?.Invoke(CombinedCsvPath());

        if (reloadSceneAfterSave)
            ReloadScene();
        else
            StartNewSession(Time.time);
    }

    // ---------- F: discard ----------

    public void DiscardSession()
    {
        busy = true;

        var check = CheckSession(Time.time - startTime);
        bool written = FinishAttempt("discarded", 0, check);

        discardsSinceLastSave++;
        discardsThisPlay++;
        PlayerPrefs.SetInt(TotalDiscardsKey, TotalDiscards + 1);
        PlayerPrefs.Save();

        if (!written)
        {
            lastSaveOk = false;
            lastSaveMessage = "The CSV is locked (open in Excel?). Close it; the data will be written on the next R or F.";
        }

        Debug.Log($"SessionRecorder: attempt {attemptsThisPlay} discarded. {discardsSinceLastSave} since last save, " +
                  $"{TotalDiscards} total.");

        SessionDiscarded?.Invoke();

        ReloadScene();
    }

    // ---------- stopping Play mid-attempt ----------

    void OnApplicationQuit()
    {
        if (attemptOpen && rows.Count > 0)
            FinishAttempt("unfinished", 0, CheckSession(Time.time - startTime));

        if (!FlushPending())
        {
            foreach (var entry in pendingLines.ToList())
            {
                string backup = Path.Combine(Path.GetDirectoryName(entry.Key),
                    Path.GetFileNameWithoutExtension(entry.Key) + $"_backup_{DateTime.Now:HH-mm-ss}.csv");
                try
                {
                    File.WriteAllLines(backup, entry.Value);
                    Debug.LogWarning($"SessionRecorder: {Path.GetFileName(entry.Key)} was locked, so its last rows " +
                                     $"were written to {backup}. Paste them onto the end of the original.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"SessionRecorder: could not write {backup}: {e.Message}");
                }
            }
        }
    }

    // ---------- writing ----------

    bool FinishAttempt(string result, int sessionNumber, SessionCheck check)
    {
        attemptOpen = false;
        string session = sessionNumber > 0 ? sessionNumber.ToString() : "";

        // Combined frame-by-frame data
        var lines = new List<string>(rows.Count);
        string prefix = $"{attemptsThisPlay},{result},{session},";
        foreach (var row in rows)
            lines.Add(prefix + row);
        Queue(CombinedCsvPath(), BuildHeader(), lines);

        return FlushPending();
    }

    static readonly Dictionary<string, string> headers = new Dictionary<string, string>();

    static void Queue(string path, string header, List<string> lines)
    {
        headers[path] = header;
        if (!pendingLines.TryGetValue(path, out var list))
            pendingLines[path] = list = new List<string>();
        list.AddRange(lines);
    }

    static bool FlushPending()
    {
        bool allWritten = true;
        foreach (var path in pendingLines.Keys.ToList())
        {
            var lines = pendingLines[path];
            if (lines.Count == 0) { pendingLines.Remove(path); continue; }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var sb = new StringBuilder();
                if (!File.Exists(path))
                {
                    sb.AppendLine(headers[path]);
                    expectedLineCounts[path] = 1;
                }
                else if (!expectedLineCounts.ContainsKey(path))
                {
                    expectedLineCounts[path] = File.ReadLines(path).Count();
                }

                foreach (var line in lines)
                    sb.AppendLine(line);
                File.AppendAllText(path, sb.ToString());
                expectedLineCounts[path] += lines.Count;

                // Read it back to confirm every row made it to disk
                int onDisk = File.ReadLines(path).Count();
                if (onDisk != expectedLineCounts[path])
                {
                    Debug.LogError($"SessionRecorder: {Path.GetFileName(path)} has {onDisk} lines but should have " +
                                   $"{expectedLineCounts[path]}. Was it edited while recording?");
                    expectedLineCounts[path] = onDisk;
                }

                pendingLines.Remove(path);
            }
            catch (Exception e)
            {
                allWritten = false;
                Debug.LogError($"SessionRecorder: could not write {Path.GetFileName(path)} ({e.Message}). " +
                               "The data is kept and will be retried on the next R or F. Close the file in Excel.");
            }
        }
        return allWritten;
    }

    // ---------- checks ----------

    struct SessionCheck
    {
        public bool ok;
        public string issues;
        public int expectedSamples;
        public float largestGap;
        public string missingParts;
    }

    // Looks for the problems that would make an attempt's data unusable
    SessionCheck CheckSession(float duration)
    {
        var problems = new List<string>();
        var result = new SessionCheck();

        // 1. Every tracked part was found
        var missing = new List<string>();
        if (tracker == null || tracker.Head?.Target == null) missing.Add("head");
        if (tracker == null || tracker.LeftHand?.Target == null) missing.Add("left");
        if (tracker == null || tracker.RightHand?.Target == null) missing.Add("right");
        result.missingParts = missing.Count == 0 ? "none" : string.Join(" ", missing);
        if (missing.Count > 0) problems.Add($"not tracked: {result.missingParts}");

        // 2. Enough samples for the attempt's length (frame drops reduce this)
        float rate = sampleRate > 0f ? sampleRate : 1f / Mathf.Max(Time.smoothDeltaTime, 0.001f);
        result.expectedSamples = Mathf.FloorToInt(duration * rate) + 1;
        if (rows.Count < result.expectedSamples * 0.9f)
            problems.Add($"only {rows.Count} of ~{result.expectedSamples} samples (frame drops?)");

        // 3. No long gaps between samples (a freeze or the headset losing focus)
        for (int i = 1; i < rowTimes.Count; i++)
            result.largestGap = Mathf.Max(result.largestGap, rowTimes[i] - rowTimes[i - 1]);
        if (result.largestGap > 0.1f)
            problems.Add($"{result.largestGap:F2} s gap with no data");

        // 4. The player actually moved (catches a controller that was asleep the whole time)
        if (tracker != null)
        {
            if (tracker.LeftHand?.Target != null && tracker.LeftHand.PeakSpeed < 0.05f) problems.Add("left hand never moved");
            if (tracker.RightHand?.Target != null && tracker.RightHand.PeakSpeed < 0.05f) problems.Add("right hand never moved");
        }

        result.ok = problems.Count == 0;
        result.issues = result.ok ? "none" : string.Join("; ", problems);
        return result;
    }

    // ---------- helpers ----------

    void StartNewSession(float timeZero)
    {
        rows.Clear();
        rowTimes.Clear();
        startTime = timeZero;
        nextSampleTime = timeZero;
        tracker?.ResetPeaks();
        attemptsThisPlay++;
        attemptOpen = true;
        busy = false;
        SessionStarted?.Invoke(Path.Combine(GetFolder(), playBaseName));
    }

    void ReloadScene()
    {
        int index = SceneManager.GetActiveScene().buildIndex;
        if (index < 0)
        {
            Debug.LogError("SessionRecorder: this scene isn't in the build's scene list, so it can't be reloaded. " +
                           "Add it under File > Build Profiles > Scene List. Starting a new attempt without reloading.");
            StartNewSession(Time.time);
            return;
        }
        SceneManager.LoadScene(index);
    }

    public string GetFolder()
    {
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", folderName));
#else
        return Path.Combine(Application.persistentDataPath, folderName);
#endif
    }

    string CombinedCsvPath() => Path.Combine(GetFolder(), playBaseName + ".csv");

    static string BuildHeader()
    {
        var columns = new List<string> { "attempt", "result", "session", "video_time_s", "time_s" };
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

    // Always use "." for decimals so the CSV opens correctly in any region
    static string F(float value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    [ContextMenu("Reset Saved Counters")]
    void ResetSavedCounters()
    {
        PlayerPrefs.DeleteKey(TotalDiscardsKey);
        PlayerPrefs.DeleteKey(TotalSavesKey);
        PlayerPrefs.Save();
        discardsSinceLastSave = 0;
        discardsThisPlay = 0;
        savesThisPlay = 0;
        Debug.Log("SessionRecorder: counters reset. Existing files were not deleted.");
    }

    void OnGUI()
    {
        if (!showOnScreenInfo) return;

        if (!started)
        {
            GUI.Box(new Rect(10, 10, 330, 30), GUIContent.none);
            GUI.Label(new Rect(18, 14, 320, 25), "Waiting for headset tracking...");
            return;
        }

        float videoNow = SessionVideoRecorder.VideoTimeAt(Time.time);
        string video = videoNow >= 0f
            ? $"Video: {TimeSpan.FromSeconds(videoNow):mm\\:ss}  ({SessionVideoRecorder.CurrentVideoFile})"
            : "Video: not recording";

        string text =
            $"Attempt {attemptsThisPlay}: {(Time.time - startTime):F1} s  ({rows.Count} samples)\n" +
            $"Discards since last save: {discardsSinceLastSave}\n" +
            $"This Play session: {savesThisPlay} saved, {discardsThisPlay} discarded\n" +
            video + "\n" +
            $"{saveKey} = save    {discardKey} = discard & reset";

        GUI.Box(new Rect(10, 10, 360, 112), GUIContent.none);
        GUI.Label(new Rect(18, 14, 350, 106), text);

        if (!string.IsNullOrEmpty(lastSaveMessage))
        {
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
            style.normal.textColor = lastSaveOk ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.6f, 0.3f);
            GUI.Box(new Rect(10, 127, 360, 50), GUIContent.none);
            GUI.Label(new Rect(18, 130, 350, 46), lastSaveMessage, style);
        }
    }
}
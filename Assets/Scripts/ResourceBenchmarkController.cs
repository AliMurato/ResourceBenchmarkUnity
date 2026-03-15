using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// Main benchmark controller.
/// Handles timing, spawning, logging and automatic stop.
/// </summary>
public class ResourceBenchmarkController : MonoBehaviour
{
    private const string LogPrefix = "Unity 2022.3.62f3";

    [Header("Spawn")]
    [Tooltip("Prefab that will be spawned (must contain a Rigidbody).")]
    public GameObject spherePrefab;

    [Tooltip("Maximum number of spawned objects for this run.")]
    public int maxSpawnCount = 15000;

    [Tooltip("Time between spawns in seconds (0.01 => ~100 spawns per second).")]
    public float spawnInterval = 0.005f;

    [Tooltip("Half-size of the square spawn area.")]
    public float spawnAreaHalfExtent = 11.5f;

    [Tooltip("Vertical offset of the spawn plane.")]
    public float spawnHeightOffset = 11.5f;

    [Header("Initial Motion")]
    [Tooltip("Initial linear velocity magnitude applied to each spawned Rigidbody.")]
    public float initialSpeed = 6f;

    [Tooltip("Initial angular velocity magnitude applied to each spawned Rigidbody.")]
    public float angularSpeed = 2f;

    [Header("Reproducibility")]
    [Tooltip("Random seed used for spawn positions and initial directions (same seed => similar run).")]
    public int randomSeed = 12345;

    [Header("Warm-up & Logging")]
    [Tooltip("No markers are written before this time (seconds) to avoid startup noise.")]
    public float warmupSeconds = 5f;

    [Tooltip("Enable CSV output (markers + run info).")]
    public bool writeCsv = true;

    [Tooltip("Write one marker every N spawns after warm-up.")]
    public int logEveryNSpawns = 100;

    [Tooltip("Base file name for time-series markers (a run index is appended automatically).")]
    public string markersFileName = "resource_benchmark_markers.csv";

    [Tooltip("Base file name for static run information (a run index is appended automatically).")]
    public string runInfoFileName = "resource_benchmark_runinfo.csv";

    [Header("FPS smoothing (markers only)")]
    [Range(0.01f, 0.5f)]
    [Tooltip("EMA smoothing factor for delta time used to compute an approximate FPS value for markers.")]
    public float fpsSmoothing = 0.05f;

    [Header("Pooling")]
    [Tooltip("Number of pooled objects created at startup. Should be >= Max Spawn Count.")]
    public int prewarmPoolCount = 15000;

    [Header("Auto stop when complete")]
    [Tooltip("Automatically stop the benchmark after reaching maxSpawnCount.")]
    public bool autoStopWhenComplete = true;

    [Tooltip("Delay before stopping play mode / quitting.")]
    public float stopDelaySeconds = 1f;

    [Tooltip("If true, Standalone build will call Application.Quit() after completion.")]
    public bool quitAppInBuild = true;

    [Tooltip("Stop automatically when smoothed FPS drops below this threshold.")]
    public float stopFpsThreshold = 10f;

    [Header("Manual stop")]
    [Tooltip("Allow stopping the benchmark at any time by pressing ESC.")]
    public bool allowManualStopWithEsc = true;

    [Header("Start Control")]
    [Tooltip("Wait for Space before starting the benchmark.")]
    public bool waitForSpaceToStart = true;

    [Header("Camera (optional)")]
    [Tooltip("Optional orbit camera that starts together with the benchmark.")]
    public BenchmarkOrbitCamera orbitCamera;

    [Header("HUD (required)")]
    [Tooltip("Benchmark HUD that already exists in the scene.")]
    public ResourceBenchmarkHUD hud;

    // Runtime state
    private float _spawnTimer;
    private int _currentSpawnCount;
    private float _smoothedDeltaTime;

    // Average FPS calculation
    private int _frameCount;
    private double _benchmarkStartTimeS;

    // Warm-up and logging state
    private bool _warmupCompleted;
    private int _lastLoggedSpawnCount;

    // Pending marker state
    private bool _hasPendingMarker;
    private int _pendingMarkerCount;

    // Start state
    private bool _benchmarkStarted;
    private bool _startPromptShown;

    // Stop state
    private bool _stopRequested;
    private float _stopAtTimeUnscaled;
    private string _stopReason = "unknown";

    // Final run snapshot
    private float _averageFps;
    private float _finalFpsSmooth;
    private float _finalFpsInstant;
    private float _finalFrameTimeMs;
    private double _finalStopTimeS;

    // Stable time base
    private Stopwatch _sw;
    private double TimeS => _sw != null ? _sw.Elapsed.TotalSeconds : Time.unscaledTimeAsDouble;

    // Helper modules
    private readonly ResourceBenchmarkPool _pool = new ResourceBenchmarkPool();
    private readonly ResourceBenchmarkCsvWriter _csv = new ResourceBenchmarkCsvWriter();

    /// <summary>
    /// Configures the benchmark and prewarms the pool.
    /// </summary>
    private void Awake()
    {
        ValidateHudReference();

        if (hud != null)
        {
            hud.Clear();
            hud.SetVisible(true);
        }

        // Disable frame limiting for the benchmark
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        // Seed deterministic random generator
        Random.InitState(randomSeed);

        // Initialize smoothed delta time
        _smoothedDeltaTime = Mathf.Max(1e-6f, Time.unscaledDeltaTime);

        // Create stopwatch but do not start it yet if waiting is enabled
        _sw = new Stopwatch();

        if (!waitForSpaceToStart)
        {
            _sw.Start();
            _benchmarkStarted = true;
            _benchmarkStartTimeS = TimeS;

            if (orbitCamera != null)
            {
                orbitCamera.StartOrbit();
            }
        }

        if (spherePrefab == null)
        {
            RequestStop("invalid_config", 0f, 0f, 0f);
            return;
        }

        // Prewarm pooled objects before the benchmark loop
        if (prewarmPoolCount > 0)
        {
            _pool.Initialize(spherePrefab, prewarmPoolCount);
        }

        // Prepare CSV output
        if (writeCsv)
        {
            _csv.Initialize(markersFileName, runInfoFileName);
        }
    }

    /// <summary>
    /// Ensures output is closed and HUD is hidden when the object is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (hud != null)
        {
            hud.Clear();
            hud.SetVisible(false);
        }

        _csv.Close();
    }

    /// <summary>
    /// Main benchmark loop.
    /// </summary>
    private void Update()
    {
        float deltaTime = Mathf.Max(1e-6f, Time.unscaledDeltaTime);

        // Allow manual stop at any time
        if (allowManualStopWithEsc && Input.GetKeyDown(KeyCode.Escape))
        {
            float fpsInstantEsc = 1f / deltaTime;
            float fpsSmoothEsc = 1f / Mathf.Max(1e-6f, _smoothedDeltaTime);
            float frameMsEsc = _smoothedDeltaTime * 1000f;

            RequestStop("manual_stop", fpsSmoothEsc, fpsInstantEsc, frameMsEsc);
            return;
        }

        // Wait for Space before starting the benchmark
        if (!_benchmarkStarted)
        {
            if (!_startPromptShown)
            {
                PushHudLine($"[{LogPrefix}] Press SPACE to start benchmark");
                _startPromptShown = true;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                _sw.Start();
                _benchmarkStarted = true;
                _benchmarkStartTimeS = TimeS;

                if (orbitCamera != null)
                {
                    orbitCamera.StartOrbit();
                }

                PushHudLine($"[{LogPrefix}] Benchmark started");
            }

            return;
        }

        _frameCount++;

        // Smooth frame time for marker-only FPS
        _smoothedDeltaTime = Mathf.Lerp(
            _smoothedDeltaTime,
            deltaTime,
            fpsSmoothing
        );

        float fpsSmooth = 1f / _smoothedDeltaTime;
        float frameMs = _smoothedDeltaTime * 1000f;
        float fpsInstant = 1f / deltaTime;

        // Stop benchmark if smoothed FPS drops below the threshold
        if (!_stopRequested && fpsSmooth <= stopFpsThreshold)
        {
            PushHudLine($"[{LogPrefix}] FPS threshold reached ({fpsSmooth:F1} FPS). Stopping benchmark.");
            RequestStop("fps_threshold", fpsSmooth, fpsInstant, frameMs);
            return;
        }

        // Enable logging after the warm-up period
        if (!_warmupCompleted && TimeS >= warmupSeconds)
        {
            _warmupCompleted = true;

            if (writeCsv && logEveryNSpawns > 0)
            {
                int snapped = (_currentSpawnCount / logEveryNSpawns) * logEveryNSpawns;

                if (snapped > 0)
                {
                    _lastLoggedSpawnCount = snapped;
                    _pendingMarkerCount = snapped;
                    _hasPendingMarker = true;
                }
            }
        }

        // Spawn catch-up loop
        _spawnTimer += deltaTime;

        while (_spawnTimer >= spawnInterval && _currentSpawnCount < maxSpawnCount)
        {
            _spawnTimer -= spawnInterval;

            bool spawnSucceeded = SpawnSphere(fpsSmooth, fpsInstant, frameMs);
            if (spawnSucceeded && ShouldQueueMarkerAfterWarmup())
            {
                _lastLoggedSpawnCount = _currentSpawnCount;
                _pendingMarkerCount = _currentSpawnCount;
                _hasPendingMarker = true;
            }

            if (_stopRequested)
                break;
        }

        // Write at most one marker per frame
        if (_hasPendingMarker && !_stopRequested)
        {
            WriteMarkerAtCount(_pendingMarkerCount, frameMs, fpsSmooth, fpsInstant);
            _hasPendingMarker = false;
        }

        // Stop automatically after reaching the target count
        if (!_stopRequested && _currentSpawnCount >= maxSpawnCount)
        {
            RequestStop("max_spawn_count", fpsSmooth, fpsInstant, frameMs);
        }

        // Execute delayed stop
        if (_stopRequested &&
            autoStopWhenComplete &&
            Time.unscaledTime >= _stopAtTimeUnscaled &&
            _stopAtTimeUnscaled > 0f)
        {
            _stopAtTimeUnscaled = float.PositiveInfinity;
            StopRun();
        }
    }

    /// <summary>
    /// Checks whether the HUD reference is assigned.
    /// </summary>
    private void ValidateHudReference()
    {
        if (hud == null)
            return;
    }

    /// <summary>
    /// Adds one line to the HUD if it exists.
    /// </summary>
    private void PushHudLine(string message)
    {
        if (hud != null)
            hud.PushLine(message);
    }

    /// <summary>
    /// Requests benchmark stop and schedules delayed exit.
    /// </summary>
    private void RequestStop(string reason, float fpsSmooth, float fpsInstant, float frameMs)
    {
        if (_stopRequested)
            return;

        _stopRequested = true;
        _stopReason = reason;

        // Capture final benchmark state at stop request time
        _finalStopTimeS = TimeS;
        double totalTime = _finalStopTimeS - _benchmarkStartTimeS;
        _averageFps = totalTime > 0 ? (float)(_frameCount / totalTime) : 0f;
        _finalFpsSmooth = fpsSmooth;
        _finalFpsInstant = fpsInstant;
        _finalFrameTimeMs = frameMs;

        PushHudLine($"[{LogPrefix}] Stop requested: {_stopReason} (t={_finalStopTimeS:F2}s, count={_currentSpawnCount}).");

        _stopAtTimeUnscaled = autoStopWhenComplete
            ? Time.unscaledTime + Mathf.Max(0f, stopDelaySeconds)
            : 0f;

        if (!autoStopWhenComplete)
            StopRun();
    }

    /// <summary>
    /// Stops play mode in Editor or quits the build.
    /// </summary>
    private void StopRun()
    {
        // Write final run information before stopping
        if (writeCsv)
        {
            _csv.WriteRunInfo(
                randomSeed,
                _stopReason,
                _finalStopTimeS,
                _currentSpawnCount,
                _averageFps,
                _finalFpsSmooth,
                _finalFpsInstant,
                _finalFrameTimeMs
            );

            _csv.Close();
        }

#if UNITY_EDITOR
        PushHudLine($"[{LogPrefix}] Stopping Play Mode (Editor). Reason: {_stopReason}");
        UnityEditor.EditorApplication.isPlaying = false;
#else
        if (quitAppInBuild)
        {
            PushHudLine($"[{LogPrefix}] Quitting application (Build). Reason: {_stopReason}");
            Application.Quit();
        }
        else
        {
            PushHudLine($"[{LogPrefix}] Benchmark finished (Build). Reason: {_stopReason}. quitAppInBuild is false.");
        }
#endif
    }

    /// <summary>
    /// Returns true when a marker should be queued after warm-up.
    /// </summary>
    private bool ShouldQueueMarkerAfterWarmup()
    {
        if (!_warmupCompleted) return false;
        if (!writeCsv) return false;
        if (_currentSpawnCount <= 0) return false;
        if (logEveryNSpawns <= 0) return false;
        if ((_currentSpawnCount % logEveryNSpawns) != 0) return false;

        // Avoid duplicate marker after warm-up snap
        return _currentSpawnCount != _lastLoggedSpawnCount;
    }

    /// <summary>
    /// Activates one pooled sphere and applies deterministic motion.
    /// </summary>
    private bool SpawnSphere(float fpsSmooth, float fpsInstant, float frameMs)
    {
        if (spherePrefab == null)
        {
            RequestStop("invalid_config", fpsSmooth, fpsInstant, frameMs);
            return false;
        }

        if (_pool.Count <= 0)
        {
            RequestStop("pool_exhausted", fpsSmooth, fpsInstant, frameMs);
            return false;
        }

        Rigidbody rb = _pool.Acquire();
        if (rb == null)
        {
            RequestStop("spawn_failed", fpsSmooth, fpsInstant, frameMs);
            return false;
        }

        GameObject go = rb.gameObject;
        if (go == null)
        {
            RequestStop("spawn_failed", fpsSmooth, fpsInstant, frameMs);
            return false;
        }

        // Pick a random point inside a square spawn area above the controller
        float spawnX = Random.Range(-spawnAreaHalfExtent, spawnAreaHalfExtent);
        float spawnZ = Random.Range(-spawnAreaHalfExtent, spawnAreaHalfExtent);

        Vector3 pos = transform.position + new Vector3(spawnX, spawnHeightOffset, spawnZ);

        go.transform.SetPositionAndRotation(pos, Quaternion.identity);

        // Reset physics state before reuse
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.Sleep();
        go.SetActive(true);
        rb.WakeUp();

        // Apply deterministic initial motion
        Vector3 dir = Random.onUnitSphere;
        rb.velocity = dir * initialSpeed;

        Vector3 ang = Random.onUnitSphere * angularSpeed;
        rb.angularVelocity = ang;

        _currentSpawnCount++;
        return true;
    }

    /// <summary>
    /// Writes one marker row and mirrors it to the HUD.
    /// </summary>
    private void WriteMarkerAtCount(int spawnedCount, float frameMs, float fpsSmooth, float fpsInstant)
    {
        if (writeCsv)
        {
            _csv.WriteMarker(TimeS, spawnedCount, frameMs, fpsSmooth, fpsInstant);
        }

        PushHudLine($"[{LogPrefix}] t={TimeS:F1}s count={spawnedCount} fps~{fpsSmooth:F0} frame~{frameMs:F1}ms");
    }
}
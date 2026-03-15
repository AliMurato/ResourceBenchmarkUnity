using System.IO;
using UnityEngine;

/// <summary>
/// Handles CSV output for benchmark runs.
/// Marker file stays open to reduce I/O overhead during the run.
/// </summary>
public sealed class ResourceBenchmarkCsvWriter
{
    private const string BenchmarkRootFolderName = "BenchmarkResults";

    private StreamWriter _markersWriter;
    private string _markersPath;
    private string _runInfoPath;

    /// <summary>
    /// Returns the full path to the marker CSV file.
    /// </summary>
    public string MarkersPath => _markersPath;

    /// <summary>
    /// Returns the full path to the run info CSV file.
    /// </summary>
    public string RunInfoPath => _runInfoPath;

    /// <summary>
    /// Creates output files for a new benchmark run.
    /// </summary>
    public void Initialize(string markersFileName, string runInfoFileName)
    {
        string root = GetBenchmarkRoot();
        Directory.CreateDirectory(root);

        string markersBaseName = Path.GetFileNameWithoutExtension(markersFileName);
        string runInfoBaseName = Path.GetFileNameWithoutExtension(runInfoFileName);

        int runIndex = GetNextRunIndex(root, markersBaseName);
        string indexString = runIndex.ToString("D3");

        _markersPath = Path.Combine(root, $"{markersBaseName}_{indexString}.csv");
        _runInfoPath = Path.Combine(root, $"{runInfoBaseName}_{indexString}.csv");

        _markersWriter = new StreamWriter(_markersPath, append: false);
        _markersWriter.WriteLine("time_s,spawned_objects,frame_time_ms,fps_smooth,fps_instant");
    }

    /// <summary>
    /// Writes one final metadata row for the current run.
    /// </summary>
    public void WriteRunInfo(
        int randomSeed,
        string stopReason,
        double stopTimeS,
        int finalSpawnCount,
        float avgFps,
        float finalFpsSmooth,
        float finalFpsInstant,
        float finalFrameTimeMs)
    {
        using (var w = new StreamWriter(_runInfoPath, append: false))
        {
            w.WriteLine("engine_version,platform,gpu,cpu,sys_ram_mb,spawn_mode,random_seed,stop_reason,stop_time_s,final_spawn_count,avg_fps,final_fps_smooth,final_fps_instant,final_frame_time_ms");
            w.WriteLine(
                $"Unity_{Application.unityVersion}," +
                $"{Application.platform}," +
                $"\"{SystemInfo.graphicsDeviceName}\"," +
                $"\"{SystemInfo.processorType}\"," +
                $"{SystemInfo.systemMemorySize}," +
                $"Pool," +
                $"{randomSeed}," +
                $"{stopReason}," +
                $"{stopTimeS:F3}," +
                $"{finalSpawnCount}," +
                $"{avgFps:F2}," +
                $"{finalFpsSmooth:F2}," +
                $"{finalFpsInstant:F2}," +
                $"{finalFrameTimeMs:F3}"
            );
        }
    }

    /// <summary>
    /// Writes one performance sample to the marker CSV.
    /// </summary>
    public void WriteMarker(double timeS, int spawnedCount, float frameMs, float fpsSmooth, float fpsInstant)
    {
        if (_markersWriter == null)
            return;

        _markersWriter.WriteLine($"{timeS:F3},{spawnedCount},{frameMs:F3},{fpsSmooth:F2},{fpsInstant:F2}");
    }

    /// <summary>
    /// Flushes and closes the marker file.
    /// </summary>
    public void Close()
    {
        _markersWriter?.Flush();
        _markersWriter?.Dispose();
        _markersWriter = null;
    }

    /// <summary>
    /// Finds the next free numeric suffix for the given file base name.
    /// </summary>
    private int GetNextRunIndex(string directory, string baseFileName)
    {
        if (!Directory.Exists(directory))
            return 1;

        string[] files = Directory.GetFiles(directory, baseFileName + "_*.csv");
        int maxIndex = 0;

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string[] parts = name.Split('_');
            if (parts.Length == 0)
                continue;

            if (int.TryParse(parts[^1], out int index))
                maxIndex = Mathf.Max(maxIndex, index);
        }

        return maxIndex + 1;
    }

    /// <summary>
    /// Returns the benchmark output directory next to the build executable.
    /// </summary>
    private string GetBenchmarkRoot()
    {
        string exeFolder = Path.GetDirectoryName(Application.dataPath);

        if (string.IsNullOrEmpty(exeFolder))
            exeFolder = Directory.GetCurrentDirectory();

        return Path.Combine(exeFolder, BenchmarkRootFolderName);
    }
}
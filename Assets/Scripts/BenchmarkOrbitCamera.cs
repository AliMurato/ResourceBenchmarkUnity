using UnityEngine;

/// <summary>
/// Optional orbit camera mode for the benchmark.
/// Rotates horizontally around the world origin and always looks at it.
/// Orbit motion can be started by the benchmark controller.
/// </summary>
public class BenchmarkOrbitCamera : MonoBehaviour
{
    [Header("Orbit mode")]
    [Tooltip("Enable or disable orbit motion.")]
    public bool orbitEnabled = false;

    [Tooltip("Orbit center in world space.")]
    public Vector3 orbitCenter = Vector3.zero;

    [Tooltip("Orbit radius in world units.")]
    public float orbitRadius = 40f;

    [Tooltip("Angular speed in degrees per second.")]
    public float orbitSpeedDegPerSec = -6f;

    [Tooltip("Fixed camera height during orbit.")]
    public float orbitHeight = 0f;

    [Tooltip("Starting orbit angle in degrees.")]
    public float startAngleDeg = -90f;

    [Tooltip("If true, orbit waits for an external start command.")]
    public bool waitForExternalStart = true;

    private float _currentAngleDeg;
    private bool _orbitStarted;

    private void Start()
    {
        // Initialize orbit angle and snap camera to the orbit path
        _currentAngleDeg = startAngleDeg;
        ApplyOrbitTransform();

        // Start immediately only if external waiting is disabled
        _orbitStarted = !waitForExternalStart;
    }

    private void Update()
    {
        if (!orbitEnabled)
            return;

        if (!_orbitStarted)
            return;

        // Advance camera along the orbit
        _currentAngleDeg += orbitSpeedDegPerSec * Time.unscaledDeltaTime;

        if (_currentAngleDeg >= 360f)
            _currentAngleDeg -= 360f;
        else if (_currentAngleDeg < 0f)
            _currentAngleDeg += 360f;

        ApplyOrbitTransform();
    }

    /// <summary>
    /// Starts orbit motion.
    /// Called by the benchmark controller when the test begins.
    /// </summary>
    public void StartOrbit()
    {
        _orbitStarted = true;
    }

    /// <summary>
    /// Stops orbit motion.
    /// Can be useful for manual testing.
    /// </summary>
    public void StopOrbit()
    {
        _orbitStarted = false;
    }

    /// <summary>
    /// Places the camera on the orbit and points it to the center.
    /// </summary>
    private void ApplyOrbitTransform()
    {
        float angleRad = _currentAngleDeg * Mathf.Deg2Rad;

        float x = orbitCenter.x + Mathf.Cos(angleRad) * orbitRadius;
        float z = orbitCenter.z + Mathf.Sin(angleRad) * orbitRadius;
        float y = orbitHeight;

        transform.position = new Vector3(x, y, z);
        transform.LookAt(orbitCenter);
    }
}
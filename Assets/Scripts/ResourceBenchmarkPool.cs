using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight object pool used by the benchmark.
/// Objects are created once and then reused during the run.
/// </summary>
public sealed class ResourceBenchmarkPool
{
    private readonly Queue<Rigidbody> _pool = new Queue<Rigidbody>();

    /// <summary>
    /// Current number of inactive pooled objects.
    /// </summary>
    public int Count => _pool.Count;

    /// <summary>
    /// Pre-creates pooled objects and disables them.
    /// </summary>
    public void Initialize(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0)
            return;

        // Avoid queue reallocations during pool creation
        _pool.Clear();

        for (int i = 0; i < count; i++)
        {
            GameObject go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            if (go == null)
                continue;

            if (!go.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
                Object.Destroy(go);
                continue;
            }

            go.SetActive(false);
            _pool.Enqueue(rb);
        }
    }

    /// <summary>
    /// Returns one object from the pool or null if the pool is empty.
    /// </summary>
    public Rigidbody Acquire()
    {
        if (_pool.Count == 0)
            return null;

        return _pool.Dequeue();
    }
}
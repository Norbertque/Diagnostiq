using System.Numerics;

namespace Diagnostiq.Core.Stress;

/// <summary>
/// Loads every logical core with vectorized fused-multiply-add work (hotter than scalar
/// math, closer to real burn-in tools). Workers run below normal priority: they still
/// take 100 % of an otherwise idle CPU, but the UI thread keeps priority so the live
/// dashboard stays responsive.
/// </summary>
public sealed class CpuStress : IDisposable
{
    private readonly List<Thread> _workers = [];
    private volatile bool _stop;
    private static double _sink; // keeps the JIT from eliding the loop

    public bool IsRunning => _workers.Count > 0;
    public int ThreadCount => _workers.Count;

    public void Start(int? threads = null)
    {
        if (IsRunning) return;
        _stop = false;
        int n = threads ?? Environment.ProcessorCount;
        for (int i = 0; i < n; i++)
        {
            var t = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = $"CpuStress{i}" };
            _workers.Add(t);
            t.Start();
        }
    }

    public void Stop()
    {
        _stop = true;
        foreach (var t in _workers) t.Join(TimeSpan.FromSeconds(2));
        _workers.Clear();
    }

    private void Work()
    {
        // Small working set that stays in L1, so the cores (not memory) are the bottleneck.
        int width = Vector<float>.Count;
        var a = new Vector<float>(1.0001f);
        var b = new Vector<float>(0.9999f);
        var acc = new Vector<float>[8];
        for (int i = 0; i < acc.Length; i++) acc[i] = new Vector<float>(i + 1);

        while (!_stop)
        {
            for (int iter = 0; iter < 200_000; iter++)
            {
                for (int i = 0; i < acc.Length; i++)
                    acc[i] = acc[i] * a + b;   // compiles to FMA on AVX2 machines
            }
            float s = 0;
            for (int i = 0; i < acc.Length; i++) s += Vector.Sum(acc[i]);
            if (float.IsInfinity(s) || float.IsNaN(s))
                for (int i = 0; i < acc.Length; i++) acc[i] = new Vector<float>(i + 1);
            Interlocked.Exchange(ref _sink, s + width);
        }
    }

    public void Dispose() => Stop();
}

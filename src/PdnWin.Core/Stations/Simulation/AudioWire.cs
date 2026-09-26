using System.Diagnostics;
using M0LTE.Radio.Audio;
using PdnWin.Core.Stations.SoundModem;

namespace PdnWin.Core.Stations.Simulation;

/// <summary>
/// One direction of an audio cable, paced in real time: what is written at one end comes out of
/// the other at the sample rate, over a floor of noise, the way a real card and an open-squelch
/// receiver behave. Real time matters: the modem's carrier sense, CSMA and the AX.25 timers all
/// measure it. Used by the tests and by the app's simulator.
/// </summary>
public sealed class AudioWire : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<float> _pending = new();
    private readonly Queue<float> _delivered = new();
    private readonly Thread _clock;
    private readonly Random _noise = new(1);
    private readonly int _rate;
    private readonly float _noiseRms;
    private readonly float _gain;
    private volatile bool _stopping;

    /// <summary>Starts a wire at <paramref name="rate"/> with noise of <paramref name="noiseRms"/>
    /// (linear RMS; 0.001 is about -60 dBFS, 0.05 about -26 dBFS) and the written audio scaled by
    /// <paramref name="gain"/> (path loss).</summary>
    public AudioWire(int rate, float noiseRms = 0.0006f, float gain = 1f)
    {
        _rate = rate;
        _noiseRms = noiseRms;
        _gain = gain;
        _clock = new Thread(Run) { IsBackground = true, Name = "audio-wire" };
        _clock.Start();
    }

    /// <summary>The receiving end.</summary>
    public IAudioInput Input => new End(this);

    /// <summary>The sending end.</summary>
    public IAudioOutput Output => new End(this);

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping = true;
        lock (_gate)
        {
            Monitor.PulseAll(_gate);
        }

        _clock.Join();
    }

    private float Noise()
    {
        // Box-Muller: Gaussian hiss rather than a uniform buzz.
        double u1 = 1.0 - _noise.NextDouble(), u2 = _noise.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2)) * _noiseRms;
    }

    private void Run()
    {
        var watch = Stopwatch.StartNew();
        long sent = 0;
        while (!_stopping)
        {
            Thread.Sleep(5);
            long due = watch.ElapsedTicks * _rate / Stopwatch.Frequency;
            lock (_gate)
            {
                for (; sent < due; sent++)
                {
                    float sample = _pending.Count > 0 ? _pending.Dequeue() * _gain : 0f;
                    _delivered.Enqueue(Math.Clamp(sample + Noise(), -1f, 1f));
                }

                while (_delivered.Count > _rate * 2)
                {
                    _delivered.Dequeue();
                }

                Monitor.PulseAll(_gate);
            }
        }
    }

    private sealed class End(AudioWire wire) : IAudioInput, IAudioOutput
    {
        public int SampleRate => wire._rate;

        public int Read(Span<float> destination)
        {
            lock (wire._gate)
            {
                while (wire._delivered.Count == 0)
                {
                    if (wire._stopping)
                    {
                        return 0;
                    }

                    Monitor.Wait(wire._gate, 100);
                }

                int n = Math.Min(destination.Length, wire._delivered.Count);
                for (int i = 0; i < n; i++)
                {
                    destination[i] = wire._delivered.Dequeue();
                }

                return n;
            }
        }

        public void Write(ReadOnlySpan<float> samples)
        {
            lock (wire._gate)
            {
                foreach (float sample in samples)
                {
                    while (wire._pending.Count > wire._rate / 2 && !wire._stopping)
                    {
                        Monitor.Wait(wire._gate, 100);
                    }

                    wire._pending.Enqueue(sample);
                }
            }
        }

        public void Drain()
        {
            lock (wire._gate)
            {
                while (wire._pending.Count > 0 && !wire._stopping)
                {
                    Monitor.Wait(wire._gate, 100);
                }
            }
        }
    }
}

/// <summary>A sound card made of two wires, with a PTT that records its keyups.</summary>
public sealed class WiredSoundCard(string name, AudioWire fromRadio, AudioWire toRadio) : ISoundCard
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public IAudioInput Input { get; } = fromRadio.Input;

    /// <inheritdoc />
    public IAudioOutput Output { get; } = toRadio.Output;

    /// <summary>The PTT, for counting keyups.</summary>
    public CountingPtt Counting { get; } = new();

    /// <inheritdoc />
    public IPttControl Ptt => Counting;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>A PTT that only counts.</summary>
public sealed class CountingPtt : IPttControl
{
    private int _keyups;

    /// <summary>How many times it has been keyed.</summary>
    public int Keyups => Volatile.Read(ref _keyups);

    /// <summary>Whether it is keyed now.</summary>
    public bool Keyed { get; private set; }

    /// <inheritdoc />
    public void Key()
    {
        Keyed = true;
        Interlocked.Increment(ref _keyups);
    }

    /// <inheritdoc />
    public void Unkey() => Keyed = false;
}

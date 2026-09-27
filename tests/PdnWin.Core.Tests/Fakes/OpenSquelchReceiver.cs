using System.Diagnostics;
using M0LTE.Radio.Audio;
using PdnWin.Core.Stations.Simulation;
using PdnWin.Core.Stations.SoundModem;

namespace PdnWin.Core.Tests.Fakes;

/// <summary>
/// A sound card on an FM receiver with its squelch open, which is how every packet FM station
/// runs: louder when nobody is transmitting than when somebody is. It plays a script, idle hiss
/// then a far end's over then hiss again, at 48 kHz and in real time, and hiss after that.
/// </summary>
/// <remarks>
/// The audio is pdn-soundmodem's measured fixture (<c>OpenSquelchFmReceiver</c> in its tests):
/// per-kilohertz spectra of a real discriminator's output, idle and with a NinoTNC keyed, from
/// <c>ninorx.wav</c>, rendered as Gaussian noise with that spectrum. The in-band level falls by
/// 3 dB when the far end keys, and the hiss above the signal collapses by 18 dB, which is why an
/// in-band energy detector reads this channel the wrong way round.
/// </remarks>
internal sealed class OpenSquelchReceiver : ISoundCard
{
    public const int SampleRate = 48000;

    private static readonly double[] IdleBandDb =
    [
        -24.55, -24.67, -24.84, -25.19, -25.79, -26.05, -26.77, -27.96,
        -29.02, -30.37, -32.20, -33.73, -35.65, -38.08, -40.19, -42.43,
        -44.66, -46.30, -48.00, -50.12, -52.05, -54.66, -58.23, -61.66,
    ];

    private static readonly double[] KeyedBandDb =
    [
        -23.47, -24.68, -26.08, -28.36, -31.53, -35.47, -40.38, -47.90,
        -54.93, -58.27, -59.60, -59.66, -60.01, -60.82, -61.65, -62.73,
        -64.41, -65.40, -66.01, -67.32, -68.76, -69.82, -73.13, -76.38,
    ];

    private readonly AudioWire _toRadio = new(SampleRate);
    private readonly Receiver _receiver;

    /// <summary>Idle for <paramref name="idleSeconds"/>, a far end keyed for
    /// <paramref name="keyedSeconds"/>, then idle for good.</summary>
    public OpenSquelchReceiver(double idleSeconds, double keyedSeconds)
    {
        float[] after = Render(4, seed: 3, IdleBandDb);
        _receiver = new Receiver([.. Render(idleSeconds, seed: 1, IdleBandDb), .. Render(keyedSeconds, seed: 2, KeyedBandDb)], after);
    }

    public string Name => "open squelch";

    public IAudioInput Input => _receiver;

    public IAudioOutput Output => _toRadio.Output;

    public IPttControl Ptt { get; } = new CountingPtt();

    /// <summary>How far into the script the station has read, in seconds.</summary>
    public double Seconds => _receiver.Seconds;

    public void Dispose()
    {
        _receiver.Stop();
        _toRadio.Dispose();
    }

    // Noise with the given per-kilohertz spectrum, built as a spectrum and transformed: each bin an
    // independent complex Gaussian, so it is Gaussian noise and each band lands on its figure.
    private static float[] Render(double seconds, int seed, double[] bandDb)
    {
        int count = (int)(seconds * SampleRate);
        int n = 1;
        while (n < count)
        {
            n <<= 1;
        }

        double binHz = (double)SampleRate / n;
        double binsInBand = 1000 / binHz;
        var random = new Random(seed);
        var re = new double[n];
        var im = new double[n];
        for (int k = 1; k < n / 2; k++)
        {
            int band = (int)(k * binHz / 1000);
            if (band >= bandDb.Length)
            {
                continue;
            }

            // With an inverse scaled by 1/n, mean square = (2 / n^2) * sum |c_k|^2.
            double sigma = Math.Sqrt(Math.Pow(10, bandDb[band] / 10) * n * n / (2 * binsInBand));
            double magnitude = Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) / Math.Sqrt(2);
            double phase = 2 * Math.PI * random.NextDouble();
            re[k] = sigma * magnitude * Math.Cos(phase);
            im[k] = sigma * magnitude * Math.Sin(phase);
            re[n - k] = re[k];
            im[n - k] = -im[k];
        }

        Inverse(re, im);
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = (float)re[i];
        }

        return samples;
    }

    // Radix-2, in place, scaled by 1/n.
    private static void Inverse(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = 2 * Math.PI / length;
            for (int start = 0; start < n; start += length)
            {
                for (int k = 0; k < length / 2; k++)
                {
                    double wr = Math.Cos(angle * k);
                    double wi = Math.Sin(angle * k);
                    int a = start + k;
                    int b = a + (length / 2);
                    double tr = (re[b] * wr) - (im[b] * wi);
                    double ti = (re[b] * wi) + (im[b] * wr);
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            re[i] /= n;
            im[i] /= n;
        }
    }

    private sealed class Receiver(float[] script, float[] after) : IAudioInput
    {
        private readonly Stopwatch _clock = new();
        private long _position;
        private volatile bool _stopped;

        public int SampleRate => OpenSquelchReceiver.SampleRate;

        public double Seconds => (double)Interlocked.Read(ref _position) / SampleRate;

        public int Read(Span<float> destination)
        {
            if (_stopped)
            {
                return 0;
            }

            _clock.Start();
            long at = Interlocked.Read(ref _position);
            TimeSpan ahead = TimeSpan.FromSeconds((double)at / SampleRate) - _clock.Elapsed;
            if (ahead > TimeSpan.Zero)
            {
                Thread.Sleep(ahead);
            }

            for (int i = 0; i < destination.Length; i++)
            {
                long s = at + i;
                destination[i] = s < script.Length ? script[s] : after[(s - script.Length) % after.Length];
            }

            Interlocked.Add(ref _position, destination.Length);
            return destination.Length;
        }

        public void Stop() => _stopped = true;
    }
}

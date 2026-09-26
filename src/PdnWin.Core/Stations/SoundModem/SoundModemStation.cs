using System.Runtime.CompilerServices;
using System.Threading.Channels;
using M0LTE.Dsp;
using M0LTE.Radio.Audio;
using Packet.Ax25.Transport;
using Packet.SoundModem.Audio;
using Packet.SoundModem.Channel;
using Packet.SoundModem.Dsp;
using Packet.SoundModem.Modems;

namespace PdnWin.Core.Stations.SoundModem;

/// <summary>What a <see cref="SoundModemStation"/> runs.</summary>
public sealed record SoundModemStationOptions
{
    /// <summary>The modem mode, one of <see cref="ModemCatalog.KnownModes"/>.</summary>
    public required string Mode { get; init; }

    /// <summary>The modem's audio centre, for the modes that take one; null for the default.</summary>
    public double? CentreFrequencyHz { get; init; }

    /// <summary>Channel access parameters.</summary>
    public ChannelAccess ChannelAccess { get; init; } = new();

    /// <summary>Waterfall lines per second. Must divide both DSP rates (12 and 48 kHz).</summary>
    public int WaterfallLinesPerSecond { get; init; } = 30;

    /// <summary>The modes to offer; null offers the whole catalogue.</summary>
    public IReadOnlyList<string>? Modes { get; init; }

    /// <summary>The longest a transmit test may run.</summary>
    public double MaxTxTestSeconds { get; init; } = 30;
}

/// <summary>
/// pdn-soundmodem running in this process over a sound card: the station device for a radio on an
/// audio interface (an AIOC, a CM108 dongle, a Digirig).
/// </summary>
/// <remarks>
/// <para><b>One receive thread feeds everything.</b> It reads the card, meters it (clipping on
/// the card's own samples, peak and RMS after decimation, as the daemon does), decimates to the
/// mode's DSP rate, and hands the same block to the waterfall and to the modem channel. The
/// channel's transmitter runs as its own task and writes the card's output through the upsampler
/// when the mode's rate is below the card's.</para>
/// <para><b>Changing mode swaps the pipeline, not the card.</b> A new channel, decimator and
/// waterfall are built for the new mode's DSP rate and the receive thread picks them up at its
/// next block; the old transmitter is stopped (which unkeys, if it was keyed). The transport the
/// session layer holds is this station's, not the channel's, so sessions survive the swap.</para>
/// <para><b>Two receive paths, as the daemon has.</b> The channel's host path
/// (<see cref="SoundModemChannel.FrameReceived"/>) is what the session layer gets; the monitor
/// path (<see cref="SoundModemChannel.FrameReceivedWithQuality"/>) is a superset with
/// diagnostics, and is what the operator sees.</para>
/// </remarks>
public sealed class SoundModemStation :
    IStationDevice, ISpectrumFeature, IInputLevelFeature, IModeFeature, ITxTestFeature, IChannelAccessFeature
{
    private readonly ISoundCard _card;
    private readonly TimeProvider _time;
    private readonly Channel<Ax25InboundFrame> _inbound = Channel.CreateUnbounded<Ax25InboundFrame>();
    private readonly CancellationTokenSource _stopping = new();
    private readonly InputLevelMeter _meter;
    private readonly Thread _rxPump;
    private readonly Lock _gate = new();
    private readonly StationTransport _transport;
    private readonly int _waterfallLinesPerSecond;
    private readonly double _maxTxTestSeconds;
    private readonly IReadOnlyList<string> _modes;
    private volatile Pipeline _pipeline;
    private ChannelAccess _access;
    private double? _centre;
    private CancellationTokenSource? _txTest;
    private bool _disposed;

    /// <summary>Starts a station over <paramref name="card"/>, which it then owns.</summary>
    public SoundModemStation(ISoundCard card, SoundModemStationOptions options, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(options);
        _card = card;
        _time = time ?? TimeProvider.System;
        _access = options.ChannelAccess;
        _centre = options.CentreFrequencyHz;
        _waterfallLinesPerSecond = options.WaterfallLinesPerSecond;
        _maxTxTestSeconds = options.MaxTxTestSeconds;
        _modes = options.Modes ?? ModemCatalog.KnownModes;
        _meter = new InputLevelMeter(_time);
        _transport = new StationTransport(this);
        _pipeline = Build(options.Mode);

        _rxPump = new Thread(ReceivePump) { IsBackground = true, Name = "soundmodem-rx" };
        _rxPump.Start();
    }

    /// <inheritdoc />
    public string DisplayName => _card.Name;

    /// <inheritdoc />
    public IAx25Transport Transport => _transport;

    /// <inheritdoc />
    public IReadOnlyList<string> Modes => _modes;

    /// <inheritdoc />
    public string Mode => _pipeline.Mode;

    /// <summary>The DSP rate of the current mode.</summary>
    public int DspRate => _pipeline.Channel.SampleRate;

    /// <summary>Whether the channel is occupied (we are transmitting, or carrier sense says so).</summary>
    public bool ChannelBusy => _pipeline.Channel.ChannelBusy;

    /// <inheritdoc />
    public ChannelAccess ChannelAccess => _access;

    /// <inheritdoc />
    public bool TxTestRunning
    {
        get
        {
            lock (_gate)
            {
                return _txTest is not null;
            }
        }
    }

    /// <inheritdoc />
    public event Action<HeardFrame>? FrameHeard;

    /// <inheritdoc />
    public event Action<bool>? TransmittingChanged;

    /// <inheritdoc />
    public event Action<StationNotice>? Notice;

    /// <inheritdoc />
    public event Action<SpectrumLine>? SpectrumLine;

    /// <inheritdoc />
    public event Action<LevelReading>? InputLevel;

    /// <inheritdoc />
    public async Task SetModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mode);
        if (!ModemCatalog.IsKnown(mode))
        {
            throw new ArgumentException($"unknown mode '{mode}'", nameof(mode));
        }

        Pipeline old;
        lock (_gate)
        {
            if (string.Equals(_pipeline.Mode, mode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            old = _pipeline;
            _pipeline = Build(mode);
        }

        await old.StopAsync().ConfigureAwait(false);
        Notice?.Invoke(new StationNotice(NoticeLevel.Info, $"mode {mode}, {DspRate} Hz DSP"));
    }

    /// <summary>Moves the modem's audio centre (modes that take one), rebuilding the pipeline.</summary>
    public async Task SetCentreFrequencyAsync(double? hz)
    {
        Pipeline old;
        lock (_gate)
        {
            _centre = hz;
            old = _pipeline;
            _pipeline = Build(old.Mode);
        }

        await old.StopAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void SetChannelAccess(ChannelAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        lock (_gate)
        {
            _access = access;
            Apply(_pipeline.Channel.Csma, access);
        }
    }

    /// <inheritdoc />
    public async Task RunTxTestAsync(TxTestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        CancellationTokenSource stop;
        lock (_gate)
        {
            if (_txTest is not null)
            {
                throw new InvalidOperationException("a transmit test is already running");
            }

            stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
            _txTest = stop;
        }

        try
        {
            Pipeline pipeline = _pipeline;
            int rate = pipeline.Channel.SampleRate;
            double seconds = Math.Clamp(request.Seconds, 0.5, _maxTxTestSeconds);
            IReadOnlyList<double> tones = request.TwoTone
                ? [TestTone.TwoToneLowHz, TestTone.TwoToneHighHz]
                : [Math.Clamp(request.ToneHz, 50, rate / 2.0 - 50)];

            // The level a modem's frames go out at, so what is measured is what data gets.
            var tone = new TestTone(tones, peakAmplitude: 0.8, rate, seconds);
            Notice?.Invoke(new StationNotice(NoticeLevel.Info,
                $"tx test: {(request.TwoTone ? "two-tone 700+1900 Hz" : $"one tone {tones[0]:0} Hz")}, {seconds:0.0} s"));
            await pipeline.Channel.EnqueueTransmit(
                    _ => tone.Render(),
                    rejected: ex => Notice?.Invoke(new StationNotice(NoticeLevel.Warning, "tx test refused: " + ex.Message)),
                    source: tone,
                    withdraw: stop.Token,
                    stopEarly: () => stop.IsCancellationRequested)
                .WaitAsync(TimeSpan.FromSeconds(seconds + 90), _time, CancellationToken.None)
                .ConfigureAwait(false);
            Notice?.Invoke(new StationNotice(NoticeLevel.Info, stop.IsCancellationRequested ? "tx test: stopped" : "tx test: done"));
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            Notice?.Invoke(new StationNotice(NoticeLevel.Info, "tx test: stopped before it went out"));
        }
        finally
        {
            lock (_gate)
            {
                _txTest = null;
            }

            stop.Dispose();
        }
    }

    /// <inheritdoc />
    public void StopTxTest()
    {
        lock (_gate)
        {
            _txTest?.Cancel();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stopping.CancelAsync().ConfigureAwait(false);
        _inbound.Writer.TryComplete();
        await _pipeline.StopAsync().ConfigureAwait(false);

        // Closing the card ends a Read the pump may be blocked in.
        _card.Dispose();
        _rxPump.Join(TimeSpan.FromSeconds(2));
        _stopping.Dispose();
    }

    private Pipeline Build(string mode)
    {
        string name = mode.ToLowerInvariant();
        int dspRate = ModemCatalog.DspRateFor(name);
        int cardRate = _card.Input.SampleRate;
        if (cardRate % dspRate != 0 || _card.Output.SampleRate != cardRate)
        {
            throw new InvalidOperationException(
                $"the card runs at {cardRate} Hz in and {_card.Output.SampleRate} Hz out; {name} needs a multiple of {dspRate} Hz both ways");
        }

        var channel = new SoundModemChannel(dspRate, _time, audioFallback: true);
        Apply(channel.Csma, _access);
        double? centre = ModemCatalog.AcceptsCentreFrequency(name) ? _centre : null;
        channel.AddModem(0, sink => ModemCatalog.Create(name, dspRate, sink, new ModemOptions(CentreFrequencyHz: centre)));

        channel.FrameReceived += (_, frame) =>
            _inbound.Writer.TryWrite(new Ax25InboundFrame(frame, 0, _time.GetUtcNow()));
        channel.FrameReceivedWithQuality += (_, frame, quality) =>
            FrameHeard?.Invoke(new HeardFrame(_time.GetUtcNow(), FrameDirection.Received, frame, Describe(quality)));
        channel.FrameTransmitted += (_, frame) =>
            FrameHeard?.Invoke(new HeardFrame(_time.GetUtcNow(), FrameDirection.Transmitted, frame));
        channel.TransmittingChanged += keyed => TransmittingChanged?.Invoke(keyed);
        channel.PttFailed += ex => Notice?.Invoke(new StationNotice(NoticeLevel.Error, "PTT failed: " + ex.Message));
        channel.TransmitRejected += (_, _, ex) =>
            Notice?.Invoke(new StationNotice(NoticeLevel.Warning, "frame not sent: " + ex.Message));

        // Cards commonly refuse 12 kHz, and WASAPI converts badly if asked to, so the card runs
        // at its own rate and the modem's audio is upsampled on the way out. The upsampler is
        // never disposed: it would dispose the card's output, which outlives any one mode.
        IAudioOutput output = cardRate == dspRate ? _card.Output : new UpsamplingAudioOutput(_card.Output, dspRate);
        var stop = new CancellationTokenSource();
        Task transmitter = RunTransmitterAsync(channel, output, stop.Token);

        long lineBase = _pipeline?.Waterfall.NextLineIndex ?? 0;
        WaterfallSource? waterfall = null;
        waterfall = new WaterfallSource(dspRate, (index, line) =>
            SpectrumLine?.Invoke(new SpectrumLine(lineBase + index, line, waterfall!.BinWidthHz, _waterfallLinesPerSecond)),
            _waterfallLinesPerSecond);

        int factor = cardRate / dspRate;
        return new Pipeline(name, channel, waterfall, factor > 1 ? new Decimator(cardRate, factor) : null, stop, transmitter);
    }

    private async Task RunTransmitterAsync(SoundModemChannel channel, IAudioOutput output, CancellationToken stop)
    {
        try
        {
            await channel.RunTransmitterAsync(output, _card.Ptt, stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Notice?.Invoke(new StationNotice(NoticeLevel.Error, "transmitter stopped: " + ex.Message));
        }
    }

    private static void Apply(CsmaParameters csma, ChannelAccess access)
    {
        csma.TxDelayMilliseconds = access.TxDelayMs;
        csma.Persistence = access.Persistence;
        csma.SlotTimeMilliseconds = access.SlotTimeMs;
        csma.TxTailMilliseconds = access.TxTailMs;
    }

    private static HeardFrameQuality Describe(FrameQuality q) => new(
        Mode: q.Mode,
        SnrDb: q.SnrWorthShowing ? q.SnrDb : null,
        FrequencyOffsetHz: q.FrequencyOffsetHz,
        PeakDbFs: q.PeakWorthShowing != false ? q.PeakDbFs : null,
        LevelVerdict: q.Level switch
        {
            FrameLevel.Loud => "TOO LOUD",
            FrameLevel.Quiet => "TOO QUIET",
            _ => null,
        },
        CorrectedBytes: q.CorrectedBytes,
        MonitorOnly: q.MonitorOnly);

    private void ReceivePump()
    {
        int cardRate = _card.Input.SampleRate;
        var block = new float[cardRate / 50]; // 20 ms: a smooth waterfall, and small enough to keep DCD current
        float[] dsp = [];
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                int got = _card.Input.Read(block);
                if (got <= 0)
                {
                    if (_stopping.IsCancellationRequested)
                    {
                        return;
                    }

                    Notice?.Invoke(new StationNotice(NoticeLevel.Error, $"{_card.Name}: the audio input has stopped"));
                    return;
                }

                ReadOnlySpan<float> raw = block.AsSpan(0, got);
                _meter.AddCardSamples(raw);

                Pipeline pipeline = _pipeline;
                ReadOnlySpan<float> audio = raw;
                if (pipeline.Decimator is { } decimator)
                {
                    int needed = decimator.MaxOutput(got);
                    if (dsp.Length < needed)
                    {
                        dsp = new float[needed];
                    }

                    int produced = decimator.Process(raw, dsp);
                    audio = dsp.AsSpan(0, produced);
                }

                _meter.Add(audio);
                pipeline.Waterfall.Process(audio);
                pipeline.Channel.ProcessReceive(audio);

                if (_meter.TryTake(out InputLevel level))
                {
                    InputLevel?.Invoke(new LevelReading(level.PeakDbFs, level.RmsDbFs, level.Clipped));
                }
            }
        }
        catch (Exception ex) when (!_stopping.IsCancellationRequested)
        {
            Notice?.Invoke(new StationNotice(NoticeLevel.Error, "receive stopped: " + ex.Message));
        }
        catch (Exception)
        {
            // A read racing the card's disposal is the disposal.
        }
    }

    private Task Enqueue(ReadOnlyMemory<byte> ax25)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _pipeline.Channel.EnqueueTransmit(0, ax25.ToArray());
    }

    private sealed record Pipeline(
        string Mode,
        SoundModemChannel Channel,
        WaterfallSource Waterfall,
        Decimator? Decimator,
        CancellationTokenSource Stop,
        Task Transmitter)
    {
        public async Task StopAsync()
        {
            await Stop.CancelAsync().ConfigureAwait(false);
            await Transmitter.ConfigureAwait(false);
            Stop.Dispose();
        }
    }

    /// <summary>
    /// The transport the session layer holds. The station's rather than a channel's, so it
    /// outlives a mode change.
    /// </summary>
    private sealed class StationTransport(SoundModemStation station) : IAx25Transport, ICarrierSense, ITxCompletionTransport
    {
        public bool? ChannelBusy => station._disposed ? null : station.ChannelBusy;

        public Task SendAsync(ReadOnlyMemory<byte> ax25, CancellationToken cancellationToken = default)
        {
            // Queue and return: the CSMA and PTT machinery takes it from here, and failures are
            // reported on the Notice event and on the completion-aware path.
            _ = station.Enqueue(ax25).ContinueWith(
                static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return Task.CompletedTask;
        }

        public async Task<TxCompletion> SendAwaitingCompletionAsync(
            ReadOnlyMemory<byte> ax25, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            DateTimeOffset queued = station._time.GetUtcNow();
            Task sent = station.Enqueue(ax25);
            if (timeout is { } limit)
            {
                await sent.WaitAsync(limit, station._time, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await sent.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new TxCompletion(queued, station._time.GetUtcNow());
        }

        public async IAsyncEnumerable<Ax25InboundFrame> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (Ax25InboundFrame frame in station._inbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
        }

        // The station owns the channel and the card; the listener disposing its transport must
        // not tear the radio down under the waterfall.
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

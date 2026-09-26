using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Packet.Ax25.Transport;

namespace PdnWin.Core.Tests.Fakes;

/// <summary>Two transports joined back to back: what one sends, the other receives.</summary>
internal sealed class FramePipe : IAx25Transport
{
    private readonly Channel<Ax25InboundFrame> _inbound = Channel.CreateUnbounded<Ax25InboundFrame>();
    private FramePipe? _peer;

    public List<byte[]> Sent { get; } = [];

    public static (FramePipe A, FramePipe B) Create()
    {
        var a = new FramePipe();
        var b = new FramePipe();
        a._peer = b;
        b._peer = a;
        return (a, b);
    }

    public Task SendAsync(ReadOnlyMemory<byte> ax25, CancellationToken cancellationToken = default)
    {
        byte[] copy = ax25.ToArray();
        lock (Sent)
        {
            Sent.Add(copy);
        }

        _peer!._inbound.Writer.TryWrite(new Ax25InboundFrame(copy, 0, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<Ax25InboundFrame> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (Ax25InboundFrame frame in _inbound.Reader.ReadAllAsync(cancellationToken))
        {
            yield return frame;
        }
    }

    public ValueTask DisposeAsync()
    {
        _inbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

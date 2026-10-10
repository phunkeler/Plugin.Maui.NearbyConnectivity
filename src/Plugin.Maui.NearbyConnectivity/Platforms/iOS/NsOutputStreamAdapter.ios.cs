using System.Runtime.InteropServices;

namespace Plugin.Maui.NearbyConnectivity;

// A writable Stream over the NSOutputStream MPC hands out for an outbound named stream.
sealed class NsOutputStreamAdapter(NSOutputStream inner) : NsStreamAdapter<NSOutputStream>(inner)
{
    public override bool CanRead => false;

    public override bool CanWrite => true;

    public override void Flush()
    {
        // NSOutputStream writes through; there is no buffer of this adapter's own to flush.
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);

        // Never the caller's own array. NSOutputStream.Write binds byte[] only, so a partial
        // write has to re-copy the outstanding remainder to the front of whatever it writes
        // from — and doing that to `buffer` itself would shift the caller's data down inside
        // their array, corrupting a buffer they may reuse.
        var slice = new byte[count];

        while (count > 0)
        {
            if (Inner.Status is NSStreamStatus.Closed or NSStreamStatus.Error)
            {
                throw new IOException("The stream to the remote device closed.");
            }

            if (!Inner.HasSpaceAvailable())
            {
                Thread.Sleep(s_pollInterval);
                continue;
            }

            Array.Copy(buffer, offset, slice, 0, count);

            var written = (int)Inner.Write(slice, (nuint)count);

            if (written < 0)
            {
                throw new IOException("The stream to the remote device rejected the write.");
            }

            offset += written;
            count -= written;
        }
    }

    // Overridden so the poll wait yields the thread. Stream's base implementation would dispatch
    // to the blocking Write and park a thread-pool thread in Thread.Sleep for as long as the peer
    // stalls.
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);

        return WriteCoreAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        // The carrier binds byte[], so a memory that is not already array-backed has to be
        // copied once before the write loop can use it at all.
        var (array, offset) = MemoryMarshal.TryGetArray(buffer, out var segment) && segment.Array is not null
            ? (segment.Array, segment.Offset)
            : (buffer.ToArray(), 0);

        return new ValueTask(WriteCoreAsync(array, offset, buffer.Length, cancellationToken));
    }

    async Task WriteCoreAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var slice = new byte[count];

        while (count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Inner.Status is NSStreamStatus.Closed or NSStreamStatus.Error)
            {
                throw new IOException("The stream to the remote device closed.");
            }

            if (!Inner.HasSpaceAvailable())
            {
                await Task.Delay(s_pollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            Array.Copy(buffer, offset, slice, 0, count);

            var written = (int)Inner.Write(slice, (nuint)count);

            if (written < 0)
            {
                throw new IOException("The stream to the remote device rejected the write.");
            }

            offset += written;
            count -= written;
        }
    }
}

using System.Runtime.InteropServices;

namespace Plugin.Maui.NearbyConnectivity;

// A readable Stream over the NSInputStream MPC delivers for an inbound named stream.
sealed class NsInputStreamAdapter(NSInputStream inner) : NsStreamAdapter<NSInputStream>(inner)
{
    public override bool CanRead => true;

    public override bool CanWrite => false;

    public override void Flush()
    {
        // Read-only.
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);

        while (true)
        {
            if (Inner.HasBytesAvailable())
            {
                // Read straight into the caller's buffer when it can take the bytes at offset 0,
                // rather than allocating an intermediate array per Read — once per 81920-byte chunk
                // under Stream.CopyToAsync — and memcpy'ing it into a destination that is already
                // the right size. `<=` rather than `==`: a caller reading a partial buffer — a
                // framing header, a pooled slice — is still reading at offset 0, and Inner.Read is
                // bounded by count, so it does not need an intermediate array either.
                var direct = offset == 0 && count <= buffer.Length;
                var slice = direct ? buffer : new byte[count];
                var read = (int)Inner.Read(slice, (nuint)count);

                if (read < 0)
                {
                    throw new IOException("The stream from the remote device failed.");
                }

                if (read == 0)
                {
                    return 0;
                }

                if (!direct)
                {
                    Array.Copy(slice, 0, buffer, offset, read);
                }

                return read;
            }

            if (Inner.Status is NSStreamStatus.AtEnd or NSStreamStatus.Closed or NSStreamStatus.Error)
            {
                return 0;
            }

            Thread.Sleep(s_pollInterval);
        }
    }

    // Overridden so the poll wait yields the thread. Stream's base implementation would dispatch
    // to the blocking Read and park a thread-pool thread in Thread.Sleep for as long as the sender
    // stalls — and CopyToAsync, the documented way to consume an inbound stream payload, goes
    // through exactly this path.
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);

        return ReadCoreAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment) && segment.Array is not null)
        {
            return new ValueTask<int>(
                ReadCoreAsync(segment.Array, segment.Offset, segment.Count, cancellationToken));
        }

        return ReadIntoMemoryAsync(buffer, cancellationToken);
    }

    async ValueTask<int> ReadIntoMemoryAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var scratch = new byte[buffer.Length];
        var read = await ReadCoreAsync(scratch, 0, scratch.Length, cancellationToken).ConfigureAwait(false);

        scratch.AsMemory(0, read).CopyTo(buffer);

        return read;
    }

    async Task<int> ReadCoreAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Inner.HasBytesAvailable())
            {
                var direct = offset == 0 && count <= buffer.Length;
                var slice = direct ? buffer : new byte[count];
                var read = (int)Inner.Read(slice, (nuint)count);

                if (read < 0)
                {
                    throw new IOException("The stream from the remote device failed.");
                }

                if (read == 0)
                {
                    return 0;
                }

                if (!direct)
                {
                    Array.Copy(slice, 0, buffer, offset, read);
                }

                return read;
            }

            if (Inner.Status is NSStreamStatus.AtEnd or NSStreamStatus.Closed or NSStreamStatus.Error)
            {
                return 0;
            }

            await Task.Delay(s_pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

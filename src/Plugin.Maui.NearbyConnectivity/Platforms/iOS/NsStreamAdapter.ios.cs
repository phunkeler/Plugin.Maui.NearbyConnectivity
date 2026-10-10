namespace Plugin.Maui.NearbyConnectivity;

abstract class NsStreamAdapter<TStream> : Stream
    where TStream : NSStream
{
    private protected static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(10);

    private protected TStream Inner { get; }

    private protected NsStreamAdapter(TStream inner)
    {
        Inner = inner;
        Inner.Open();
    }

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Inner.Close();
            Inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

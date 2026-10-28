using AgentSpeed.Core.IO;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Droid;

/// <summary>
/// Reads a Factory Droid transcript's appended lines and then its sidecar, whole, when it is new, changed (size,
/// modification time or identity) or was unreadable last time (METRICS 4.16 Read cycle). The read's time is the newer of
/// the two files', so change detection notices a sidecar rewrite as it does a transcript append.
/// </summary>
internal sealed class DroidTailer(string path) : FileTailer(path)
{
    private DroidSidecarSignature? _sidecar;

    public string SidecarPath { get; } = DroidPaths.SidecarOf(path);

    public override void StartAt(long offset, StartAlignment alignment = StartAlignment.None)
    {
        base.StartAt(offset, alignment);
        _sidecar = null;
    }

    public override TailReadResult ReadAppended(ILineSink sink, CancellationToken cancellationToken = default)
    {
        var result = base.ReadAppended(sink, cancellationToken);
        if (result.WasReset)
        {
            _sidecar = null;
        }

        var read = DroidSidecar.Read(SidecarPath, _sidecar);
        _sidecar = read.Status switch
        {
            DroidSidecarStatus.Readable or DroidSidecarStatus.Unchanged => read.Signature,
            DroidSidecarStatus.Unreadable => null,
            _ => _sidecar,
        };
        if (sink is DroidBatch batch)
        {
            batch.Sidecar = read;
        }

        var sidecarWrite = read.Signature is { } signature && read.Status != DroidSidecarStatus.Missing
            ? new DateTime(signature.WriteTicks, DateTimeKind.Utc)
            : (DateTime?)null;
        return sidecarWrite is { } written && written > result.LastWriteUtc ? result with { LastWriteUtc = written } : result;
    }
}

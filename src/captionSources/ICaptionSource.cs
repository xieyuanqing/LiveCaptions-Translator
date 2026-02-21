using System.Threading.Channels;

namespace LiveCaptionsTranslator.captionSources
{
    public interface ICaptionSource
    {
        ChannelReader<CaptionUpdate> Updates { get; }
        Task StartAsync(CancellationToken token = default);
        Task StopAsync(CancellationToken token = default);
    }
}

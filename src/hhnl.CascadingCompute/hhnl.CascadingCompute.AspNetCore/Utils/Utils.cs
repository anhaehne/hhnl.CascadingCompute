using hhnl.CascadingCompute.AspNetCore.Interfaces;
using hhnl.CascadingCompute.AspNetCore.Shared.Models;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace hhnl.CascadingCompute.AspNetCore.Utils;

public static class Utils
{
    public static async IAsyncEnumerable<InvalidationDto> StreamInvalidationEvents<TController>(
        IReadOnlyCollection<(string Key, object Value)> tollerations,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TController : ICascadingComputeController
    {
        var setTollerations = new EquatableSet<(string Key, object Value)>(tollerations);
        var channel = Channel.CreateUnbounded<(string Url, IReadOnlyCollection<(string Key, object Value)> Taints)>();
        EventHandler<(string Url, IReadOnlyCollection<(string Key, object Value)> Taints)> handler = (_, args) => channel.Writer.TryWrite(args);
        TController.CacheEntryInvalidated += handler;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var next = await channel.Reader.ReadAsync(cancellationToken);

                // Only send the events if the current user has tolleration for all the taints.
                if (!setTollerations.IsSupersetOf(next.Taints))
                    continue;

                var dto = new InvalidationDto(next.Url, next.Taints.Select(x => new TaintDto(x.Key, x.Value)).ToList());
                yield return dto;
            }
        }
        finally
        {
            TController.CacheEntryInvalidated -= handler;
        }
    }
}

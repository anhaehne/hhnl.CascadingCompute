namespace TestProject.Server.Services;

public interface IInvalidationPublisher
{
    Task PublishAsync(string cacheKey, CancellationToken cancellationToken = default);
}

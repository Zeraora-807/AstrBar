using AstrBar.Models;

namespace AstrBar.Services;

public interface IClientEventSource
{
    event EventHandler<ClientEvent>? EventReceived;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

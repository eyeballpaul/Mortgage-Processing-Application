namespace PD.Mortgages.Client;

public interface IMortgageApiCredentials
{
    Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

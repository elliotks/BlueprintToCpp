using BlueRange.ViewModels.Api;
using RestSharp;

namespace BlueRange.ViewModels;

public class ApiEndpointViewModel
{
    public FortniteCentralApiEndpoint FortniteCentral;

    private readonly RestClient _client = new(new RestClientOptions
    {
        UserAgent = "BlueRange",
    });

    public ApiEndpointViewModel()
    {
        FortniteCentral = new FortniteCentralApiEndpoint(_client);
    }
}

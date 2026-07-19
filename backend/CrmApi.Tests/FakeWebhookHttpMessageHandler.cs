using System.Net;

namespace CrmApi.Tests;

// Replaces the real "webhooks" named HttpClient's primary handler in tests
// (CrmApiFactory) — captures every outbound webhook delivery request and
// lets a test control the response status without a real network call, same
// "never call a real external endpoint in tests" posture as
// FakeAnthropicMessagesClient/FakeGoogleOAuthClient.
public class FakeWebhookHttpMessageHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];
    public HttpStatusCode NextStatusCode { get; set; } = HttpStatusCode.OK;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return new HttpResponseMessage(NextStatusCode);
    }
}

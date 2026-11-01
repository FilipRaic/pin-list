using System.Net;
using System.Text;

namespace PinList.Tests.Sync;

/// <summary>Answers HTTP requests from a script and records them.</summary>
sealed class FakeHttp : HttpMessageHandler
{
    public readonly List<(HttpRequestMessage request, string body)> Requests = new();
    public readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> Answers = new();

    public void Answer(HttpStatusCode code, string body = "", string type = "application/json") =>
        Answers.Enqueue(_ => new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, type) });

    public void Throw(Exception ex) => Answers.Enqueue(_ => throw ex);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));
        return Answers.Count > 0 ? Answers.Dequeue()(request) : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public string Url(int i) => Requests[i].request.RequestUri!.ToString();
    public HttpMethod Method(int i) => Requests[i].request.Method;
}

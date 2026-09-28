using System.Net;
using System.Text;
using FlareSync.Core.Commands;
using FlareSync.Core.Config;

namespace FlareSync.Tests;

internal sealed class TempConfig : IDisposable
{
    public TempConfig()
    {
        Directory = Path.Combine(Path.GetTempPath(), "flaresync-tests", Guid.NewGuid().ToString("N"));
        Paths = new ConfigPaths(Directory);
    }

    public string Directory { get; }

    public ConfigPaths Paths { get; }

    public JsonConfigStore Store { get; } = new();

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}

/// <summary>HTTP handler answering through a delegate and recording requests.</summary>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.ToString(), body));
        return await respond(request);
    }

    public static HttpResponseMessage Text(string content, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(content, Encoding.UTF8, "text/plain") };

    public static HttpResponseMessage Json(string content, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class FakeConsole : ICommandConsole
{
    public StringBuilder Output { get; } = new();

    public StringBuilder Errors { get; } = new();

    public Queue<string> Inputs { get; } = new();

    public bool IsInteractive { get; set; } = true;

    public void WriteLine(string text = "") => Output.AppendLine(text);

    public void WriteError(string text) => Errors.AppendLine(text);

    public void WriteTable(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        Output.AppendLine(string.Join(" | ", headers));
        foreach (var row in rows)
        {
            Output.AppendLine(string.Join(" | ", row));
        }
    }

    public string? ReadLine(string prompt) => Inputs.Count > 0 ? Inputs.Dequeue() : null;

    public string? ReadSecret(string prompt) => ReadLine(prompt);

    public bool Confirm(string prompt, bool defaultValue) => defaultValue;
}

internal sealed class FakeValues : IParsedValues
{
    private readonly Dictionary<object, object?> _values = new(ReferenceEqualityComparer.Instance);

    public FakeValues Set<T>(ArgumentDefinition<T> argument, T value)
    {
        _values[argument] = value;
        return this;
    }

    public FakeValues Set<T>(OptionDefinition<T> option, T value)
    {
        _values[option] = value;
        return this;
    }

    public T? Get<T>(ArgumentDefinition<T> argument) => _values.TryGetValue(argument, out var v) ? (T?)v : argument.DefaultValue;

    public T? Get<T>(OptionDefinition<T> option) => _values.TryGetValue(option, out var v) ? (T?)v : option.DefaultValue;

    public bool IsSpecified(OptionDefinition option) => _values.ContainsKey(option);
}

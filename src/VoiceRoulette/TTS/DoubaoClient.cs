using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceRoulette.TTS;

public sealed class DoubaoClient : IAsyncDisposable
{
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly ClientWebSocket _ws = new();

    public DoubaoClient(string endpoint, string apiKey)
    {
        _endpoint = new Uri(endpoint);
        _apiKey = apiKey;
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
    }

    public async IAsyncEnumerable<DoubaoEvent> SynthesizeAsync(
        string text, string voiceType,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await _ws.ConnectAsync(_endpoint, ct).ConfigureAwait(false);
        await SendJson(new
        {
            type = "tts_session.update",
            session = new
            {
                voice_type = voiceType,
                output_audio_format = new { type = "mp3", sample_rate = 24000 }
            }
        }, ct);
        await SendJson(new { type = "input_text.append", text }, ct);
        await SendJson(new { type = "input_text.done" }, ct);

        var buf = new byte[16 * 1024];
        var sb = new StringBuilder();
        while (_ws.State == WebSocketState.Open)
        {
            var res = await _ws.ReceiveAsync(buf, ct).ConfigureAwait(false);
            if (res.MessageType == WebSocketMessageType.Close) yield break;
            sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
            if (!res.EndOfMessage) continue;

            var msg = sb.ToString();
            sb.Clear();

            DoubaoEvent ev;
            try
            {
                ev = DoubaoEventParser.Parse(msg);
            }
            catch
            {
                // Malformed frame: surface as UnknownEvent rather than crashing the stream
                ev = new UnknownEvent("__parse_error__");
            }

            yield return ev;
            if (ev is AudioDone or DoubaoError) yield break;
        }
    }

    private Task SendJson(object payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws.State == WebSocketState.Open)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", default);
        _ws.Dispose();
    }
}

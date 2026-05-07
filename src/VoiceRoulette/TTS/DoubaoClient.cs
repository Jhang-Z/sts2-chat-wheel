using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceRoulette.TTS;

// Doubao TTS V3 WebSocket unidirectional binary protocol.
// Doc: https://www.volcengine.com/docs/6561/1719100
//
// Endpoint: wss://openspeech.bytedance.com/api/v3/tts/unidirectional/stream
// Headers (新版控制台):
//   X-Api-Key:         <uuid api key>
//   X-Api-Resource-Id: seed-tts-2.0   (or seed-tts-1.0 / icl-* etc.)
//   X-Api-Request-Id:  <uuid>          (optional)
//
// Frame format (big-endian):
//   byte 0: [version=1 (4-bit)] [header_size=1 (4-bit, in 4-byte units)]
//   byte 1: [msg_type (4-bit)]   [type_specific_flags (4-bit)]
//   byte 2: [serialization (4-bit)] [compression (4-bit)]
//   byte 3: 0x00 reserved
//   then optional: event_id(uint32), session_id_len(uint32) + session_id_bytes
//   then payload_size(uint32) + payload_bytes
//
// Client sends ONE FullClientRequest frame with JSON payload (no event flag).
// Server streams back AudioOnlyServer (msg_type=0b1011) frames carrying raw audio
// bytes, plus FullServerResponse (0b1001) JSON frames for events 350/351/152.
// Synthesis ends when server emits SessionFinished (event=152).
public sealed class DoubaoClient : ITTSBackend
{
    // Live reference to DoubaoConfig — re-read on each call so settings UI
    // changes (esp. API key, voice ID) take effect without recreating this
    // object. Falls back to constructor-captured values when null.
    private readonly VoiceRoulette.Config.DoubaoConfig? _liveConfig;

    private const byte ProtocolVersion = 0b0001;
    private const byte HeaderSize4Bytes = 0b0001;

    // Message types
    private const byte MsgFullClientRequest = 0b0001;
    private const byte MsgAudioOnlyServer   = 0b1011;
    private const byte MsgFullServerResponse = 0b1001;
    private const byte MsgError             = 0b1111;

    // Type-specific flags
    private const byte FlagsNone      = 0b0000;
    private const byte FlagsWithEvent = 0b0100;

    // Serialization
    private const byte SerJson = 0b0001;
    private const byte CompNone = 0b0000;

    // Events
    private const int EventSessionFinished     = 152;
    private const int EventTTSSentenceStart    = 350;
    private const int EventTTSSentenceEnd      = 351;
    private const int EventTTSResponse         = 352;

    // Status codes
    private const int StatusOk = 20000000;

    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _resourceId;

    // Static-snapshot constructor (used by tests + back-compat).
    public DoubaoClient(string endpoint, string apiKey, string resourceId)
    {
        _endpoint = new Uri(endpoint);
        _apiKey = apiKey;
        _resourceId = string.IsNullOrEmpty(resourceId) ? "seed-tts-2.0" : resourceId;
    }

    // Live-config constructor — preferred in production. Each call to
    // SynthesizeAsync re-reads the current key/endpoint/resource so the
    // settings page can update them without restarting the mod.
    public DoubaoClient(VoiceRoulette.Config.DoubaoConfig config)
        : this(config.Endpoint, config.ApiKey, config.ResourceId)
    {
        _liveConfig = config;
    }

    private string ResolveEndpoint()  => _liveConfig?.Endpoint   ?? _endpoint.ToString();
    private string ResolveApiKey()    => _liveConfig?.ApiKey     ?? _apiKey;
    private string ResolveResourceId()=> string.IsNullOrEmpty(_liveConfig?.ResourceId) ? _resourceId : _liveConfig!.ResourceId;

    public async IAsyncEnumerable<DoubaoEvent> SynthesizeAsync(
        string text, string voiceType, string? emotion = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey();
        var resourceId = ResolveResourceId();
        var endpointUri = new Uri(ResolveEndpoint());

        if (string.IsNullOrEmpty(apiKey))
        {
            Godot.GD.PrintErr("[VR][TTS] missing API key — set doubao.apiKey in data/config.jsonc");
            yield return new DoubaoError("missing_api_key",
                "Doubao API key is empty. Set doubao.apiKey in data/config.jsonc.");
            yield break;
        }

        using var ws = new ClientWebSocket();
        var requestId = Guid.NewGuid().ToString();
        ws.Options.SetRequestHeader("X-Api-Key", apiKey);
        ws.Options.SetRequestHeader("X-Api-Resource-Id", resourceId);
        ws.Options.SetRequestHeader("X-Api-Request-Id", requestId);

        Godot.GD.Print($"[VR][TTS] connect → {endpointUri.Host}{endpointUri.AbsolutePath} resource={resourceId} reqid={requestId[..8]}");

        Exception? connectEx = null;
        try { await ws.ConnectAsync(endpointUri, ct).ConfigureAwait(false); }
        catch (Exception ex) { connectEx = ex; }

        if (connectEx != null)
        {
            // V3 returns the error in HTTP response body BEFORE the WS upgrade — but
            // ClientWebSocket throws WebSocketException without exposing the body.
            // The X-Tt-Logid response header IS captured though; surface what we can.
            var detail = connectEx is WebSocketException wsEx
                ? $"{wsEx.Message} (code={wsEx.WebSocketErrorCode})"
                : connectEx.Message;
            Godot.GD.PrintErr($"[VR][TTS] WS connect FAILED → {detail}");
            yield return new DoubaoError("connect_failed", detail);
            yield break;
        }

        // ── Build SendText frame ────────────────────────────────────────────
        var payload = BuildRequestPayload(text, voiceType, emotion);
        Godot.GD.Print($"[VR][TTS] send payload bytes={payload.Length} text='{Truncate(text, 32)}' voice={voiceType} emotion={emotion ?? "(none)"}");

        var frame = BuildSendTextFrame(payload);

        Exception? sendEx = null;
        try
        {
            await ws.SendAsync(frame, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }
        catch (Exception ex) { sendEx = ex; }

        if (sendEx != null)
        {
            Godot.GD.PrintErr($"[VR][TTS] WS send FAILED → {sendEx.GetType().Name}: {sendEx.Message}");
            yield return new DoubaoError("send_failed", sendEx.Message);
            yield break;
        }

        // ── Read frames until SessionFinished or Error ──────────────────────
        int audioFrames = 0;
        long audioBytes = 0;

        while (ws.State == WebSocketState.Open)
        {
            ParsedFrame? parsed = null;
            Exception? recvEx = null;
            try { parsed = await ReadFrame(ws, ct).ConfigureAwait(false); }
            catch (Exception ex) { recvEx = ex; }

            if (recvEx != null)
            {
                Godot.GD.PrintErr($"[VR][TTS] WS recv FAILED → {recvEx.GetType().Name}: {recvEx.Message}");
                yield return new DoubaoError("recv_failed", recvEx.Message);
                yield break;
            }

            if (parsed == null)
            {
                Godot.GD.Print("[VR][TTS] WS closed by server");
                yield break;
            }

            switch (parsed.MessageType)
            {
                case MsgAudioOnlyServer:
                    audioFrames++;
                    audioBytes += parsed.Payload.Length;
                    yield return new AudioDelta(parsed.Payload);
                    break;

                case MsgFullServerResponse:
                    if (parsed.EventId == EventSessionFinished)
                    {
                        // Server returns either {} (success) or {"status_code": N, "message": "..."}.
                        // Only treat as failure when status_code is explicitly set AND not OK.
                        var (status, msg) = ParseStatusEnvelope(parsed.Payload);
                        if (status > 0 && status != StatusOk)
                        {
                            Godot.GD.PrintErr($"[VR][TTS] SessionFinished ERROR status={status} msg='{msg}'");
                            yield return new DoubaoError(status.ToString(), msg ?? "(no message)");
                        }
                        else
                        {
                            Godot.GD.Print($"[VR][TTS] SessionFinished OK. audio_frames={audioFrames} bytes={audioBytes}");
                            yield return new AudioDone();
                        }
                        yield break;
                    }
                    // 350 (TTSSentenceStart) / 351 (TTSSentenceEnd) — informational; ignore.
                    break;

                case MsgError:
                    var errMsg = SafeDecode(parsed.Payload);
                    Godot.GD.PrintErr($"[VR][TTS] error frame code={parsed.ErrorCode} payload={Truncate(errMsg, 200)}");
                    yield return new DoubaoError(parsed.ErrorCode.ToString(), errMsg);
                    yield break;

                default:
                    Godot.GD.Print($"[VR][TTS] unknown msg_type=0b{Convert.ToString(parsed.MessageType, 2)} event={parsed.EventId}");
                    break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Frame building / parsing
    // ─────────────────────────────────────────────────────────────────────────

    // Map our stable api-value → (Doubao base emotion, free-form prompt).
    // The prompt rides on `req_params.additions.context_texts` (TTS 2.0 only) and
    // is what makes the emotion "fuller" — e.g. anger picks up a 暴躁 edge,
    // 委屈 picks up 哭腔. The apiValues stay stable so existing config doesn't
    // break; UI labels can rename freely (e.g. "悲伤" → "无奈").
    private static (string baseEmotion, string? contextText) MapEmotion(string? apiValue) => apiValue switch
    {
        // Prompts are intentionally over-the-top so the emotion reads on a single short line.
        // Single line gets ~1s of audio, so we need maximum signal density.
        "happy" => ("happy",   "用非常开心、活泼俏皮、声音上扬带笑意的语气兴奋地说"),
        "angry" => ("angry",   "用怒不可遏、暴躁咆哮、几乎要吼出来的语气大声说"),
        "sad"   => ("annoyed", "用一副两手一摊、摆烂躺平、彻底无所谓、随它去的语气说"),  // UI label is 无奈
        "sorry" => ("sorry",   "用一种无辜小心翼翼、像是被冤枉了一样的语气轻轻地说"),  // UI label is 委屈
        _       => ("",        null),  // novel_dialog / null / unknown → neutral
    };

    private byte[] BuildRequestPayload(string text, string voiceType, string? emotion)
    {
        var (baseEmotion, contextText) = MapEmotion(emotion);

        object audioParams = string.IsNullOrEmpty(baseEmotion)
            ? new { format = "mp3", sample_rate = 24000 }
            : new { format = "mp3", sample_rate = 24000, emotion = baseEmotion, emotion_scale = 5 };

        // `additions` per Doubao docs is a STRING containing JSON (not a nested object).
        string? additionsJson = contextText == null
            ? null
            : JsonSerializer.Serialize(new { context_texts = new[] { contextText } });

        object reqParams = additionsJson != null
            ? new { text, speaker = voiceType, audio_params = audioParams, additions = additionsJson }
            : new { text, speaker = voiceType, audio_params = audioParams };

        var body = new
        {
            user = new { uid = "sts2_chat_wheel" },
            req_params = reqParams,
        };
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    private static byte[] BuildSendTextFrame(byte[] jsonPayload)
    {
        // 4-byte header + 4-byte payload size + payload
        var frame = new byte[4 + 4 + jsonPayload.Length];
        frame[0] = (byte)((ProtocolVersion << 4) | HeaderSize4Bytes);
        frame[1] = (byte)((MsgFullClientRequest << 4) | FlagsNone);
        frame[2] = (byte)((SerJson << 4) | CompNone);
        frame[3] = 0x00;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4, 4), (uint)jsonPayload.Length);
        Array.Copy(jsonPayload, 0, frame, 8, jsonPayload.Length);
        return frame;
    }

    private sealed class ParsedFrame
    {
        public byte MessageType;
        public byte Flags;
        public byte Compression;
        public int EventId;
        public string SessionId = "";
        public uint ErrorCode;
        public byte[] Payload = Array.Empty<byte>();
    }

    private static async Task<ParsedFrame?> ReadFrame(ClientWebSocket ws, CancellationToken ct)
    {
        // Reassemble the full WebSocket message (Doubao sends one logical frame per WS message).
        using var ms = new MemoryStream();
        var buf = new byte[16 * 1024];
        WebSocketReceiveResult res;
        do
        {
            res = await ws.ReceiveAsync(buf, ct).ConfigureAwait(false);
            if (res.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, res.Count);
        } while (!res.EndOfMessage);

        var raw = ms.ToArray();
        if (raw.Length < 4)
            throw new InvalidDataException($"frame too short ({raw.Length} bytes)");

        var headerSize = (raw[0] & 0x0F) * 4;
        var msgType = (byte)((raw[1] >> 4) & 0xF);
        var flags = (byte)(raw[1] & 0xF);
        var compression = (byte)(raw[2] & 0xF);

        var p = new ParsedFrame
        {
            MessageType = msgType,
            Flags = flags,
            Compression = compression,
        };

        var offset = headerSize;

        switch (msgType)
        {
            case MsgError:
                // [error_code(4)] [payload_size(4)] [payload]
                p.ErrorCode = BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(offset, 4));
                offset += 4;
                {
                    var size = BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(offset, 4));
                    offset += 4;
                    p.Payload = raw.AsSpan(offset, (int)size).ToArray();
                }
                break;

            case MsgFullServerResponse:
            case MsgAudioOnlyServer:
                // With WithEvent flag: [event(4)] [session_id_len(4)] [session_id]
                if ((flags & FlagsWithEvent) != 0)
                {
                    p.EventId = (int)BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(offset, 4));
                    offset += 4;
                    var sidLen = (int)BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(offset, 4));
                    offset += 4;
                    p.SessionId = Encoding.UTF8.GetString(raw, offset, sidLen);
                    offset += sidLen;
                }
                {
                    var size = BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(offset, 4));
                    offset += 4;
                    p.Payload = raw.AsSpan(offset, (int)size).ToArray();
                }
                break;

            default:
                // Unknown: just take everything after header as payload
                p.Payload = raw.AsSpan(offset).ToArray();
                break;
        }

        if (compression == 0b0001) // gzip
            p.Payload = GzipDecompress(p.Payload);

        return p;
    }

    private static byte[] GzipDecompress(byte[] input)
    {
        using var src = new MemoryStream(input);
        using var gz = new System.IO.Compression.GZipStream(src, System.IO.Compression.CompressionMode.Decompress);
        using var dst = new MemoryStream();
        gz.CopyTo(dst);
        return dst.ToArray();
    }

    private static (int status, string? message) ParseStatusEnvelope(byte[] payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status_code", out var s) ? s.GetInt32() : -1;
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (status, msg);
        }
        catch { return (-1, "(unparseable status envelope)"); }
    }

    private static string SafeDecode(byte[] bytes)
    {
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return $"<{bytes.Length} bytes>"; }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}

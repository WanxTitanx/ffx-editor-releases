using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Core.LLM;

/// <summary>
/// Resultado tipado de uma chamada ao modelo. Reason != None ⇒ falha;
/// Message é SEMPRE redigida (nunca contém chave, payload bruto ou stack cru).
/// </summary>
public sealed record LlmResult
{
    public LlmRejectionReason Reason { get; init; } = LlmRejectionReason.None;
    public string Message { get; init; } = string.Empty;
    public PatchProposal? Proposal { get; init; }

    /// <summary>finish_reason do choice[0] ("stop" | "tool_calls" | "length" | ...).</summary>
    public string? FinishReason { get; init; }

    /// <summary>Tool calls pedidas pelo modelo (modo Agent) — o loop executa e devolve.</summary>
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; init; }

    /// <summary>Content cru do turno assistant (modo Agent pode vir null quando só pede tools).</summary>
    public string? AssistantContent { get; init; }
}

/// <summary>
/// Adapter HTTP do contrato (L2 P2-B): timeout (CancelAfter), cancelamento,
/// MaxResponseBytes (leitura limitada de stream) e parse estrito JSON com
/// JsonUnmappedMemberHandling.Disallow — campo desconhecido = MalformedPayload.
/// A chave da sessão entra SOMENTE no header Authorization (Bearer), nunca no
/// body, log ou exceção. Toda falha vira LlmResult com Message redigida.
/// </summary>
public sealed class LlmHttpAdapter
{
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly HttpClient _http;

    public LlmHttpAdapter(HttpClient httpClient) => _http = httpClient;

    public async Task<LlmResult> RequestAsync(
        LlmRequestEnvelope envelope,
        LlmCredential credential,
        bool remoteConfirmed,
        CancellationToken ct)
    {
        // T3: host não-loopback sem confirmação humana registrada → recusa ANTES da rede.
        if (envelope.RequiresRemoteConfirmation && !remoteConfirmed)
            return Failure(LlmRejectionReason.RemoteEndpointNotConfirmed,
                Strings.U_Lbl_RemoteEndpointNeedsConfirmation);

        try
        {
            using var request = BuildRequest(envelope, credential);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(envelope.Timeout); // T8: RequestTimeout

            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

            // T8: resposta gigante → leitura limitada de stream → LimitsExceeded.
            byte[]? body = await ReadBoundedAsync(response, envelope.MaxResponseBytes, timeoutCts.Token);
            if (body is null)
                return Failure(LlmRejectionReason.LimitsExceeded,
                    string.Format(Strings.U_Lbl_ResponseExceedsMaxBytes, envelope.MaxResponseBytes));

            // Unwrap (2026-09-15, Jarvis-UI): endpoints OpenAI-compatible embrulham o texto do
            // modelo em choices[0].message — antes deste gate, QUALQUER endpoint real falhava em
            // MalformedPayload porque o wrapper nunca era aberto. Chat devolve o texto redigido;
            // Proposal faz parse estrito do content (ou do body cru, back-compat com raw-JSON);
            // Agent devolve tool_calls p/ o loop executar localmente.
            ChatChoice? choice = TryParseChoice(body);
            if (envelope.Mode == LlmResponseMode.Agent)
            {
                if (choice?.ToolCalls is { Count: > 0 } calls)
                    return new LlmResult
                    {
                        Reason = LlmRejectionReason.None,
                        FinishReason = choice.FinishReason,
                        ToolCalls = calls,
                        AssistantContent = choice.Content,
                        Message = LlmRedactor.Redact(choice.Content ?? string.Empty),
                    };

                string agentText = choice?.Content ?? Encoding.UTF8.GetString(body);
                return new LlmResult
                {
                    Reason = LlmRejectionReason.None,
                    FinishReason = choice?.FinishReason,
                    AssistantContent = choice?.Content,
                    Message = LlmRedactor.Redact(agentText),
                };
            }

            byte[]? content = choice?.Content is { } c ? Encoding.UTF8.GetBytes(c) : null;
            if (envelope.Mode == LlmResponseMode.Chat)
            {
                string text = Encoding.UTF8.GetString(content ?? body);
                return new LlmResult { Reason = LlmRejectionReason.None, Message = LlmRedactor.Redact(text) };
            }

            PatchProposal proposal = ParseStrict(content ?? body); // T1: schema estrito, base64 validado
            return new LlmResult { Reason = LlmRejectionReason.None, Proposal = proposal };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Failure(LlmRejectionReason.Cancelled, Strings.F2_request_canceled_by_the_user_7b2a9036);
        }
        catch (OperationCanceledException)
        {
            return Failure(LlmRejectionReason.Timeout, Strings.U_Lbl_ResponseTimeout);
        }
        catch (JsonException)
        {
            return Failure(LlmRejectionReason.MalformedPayload,
                Strings.F2_model_response_is_not_valid_json_for_the_a55f7ab9);
        }
        catch (FormatException)
        {
            return Failure(LlmRejectionReason.MalformedPayload, Strings.U_Lbl_InvalidBase64);
        }
        catch (Exception ex) // T4: qualquer falha vira mensagem redigida, sem stack cru
        {
            return Failure(LlmRejectionReason.VerificationFailed, RedactWithSessionKey(ex.Message, credential));
        }
    }


    private static HttpRequestMessage BuildRequest(LlmRequestEnvelope envelope, LlmCredential credential)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, envelope.Endpoint);

        // T3: chave SÓ no header Authorization; nunca no body/log (T4).
        if (credential.HasKey && ReadApiKey(credential) is { Length: > 0 } key)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        // Body montado apenas com campos do envelope aprovado — a chave nunca entra aqui.
        // Modo Agent serializa o histórico Messages + as tool specs; os modos single-shot
        // (Chat/Proposal) mantêm a mensagem única derivada do PayloadPreview.
        string body = envelope.Messages is { } messages
            ? JsonSerializer.Serialize(BuildAgentBody(envelope, messages))
            : JsonSerializer.Serialize(new
            {
                model = envelope.ModelId,
                messages = new[] { new { role = "user", content = envelope.PayloadPreview } },
            });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>Monta o body OpenAI-compatible do modo Agent: messages multi-turno + tools.</summary>
    private static Dictionary<string, object?> BuildAgentBody(
        LlmRequestEnvelope envelope, IReadOnlyList<LlmChatMessage> messages)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = envelope.ModelId,
            ["messages"] = messages.Select(SerializeMessage).ToArray(),
        };
        if (envelope.Tools is { Count: > 0 } tools)
            body["tools"] = tools.Select(t => (object)new Dictionary<string, object?>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    // ParametersJson é schema cru — desserializa pra embutir como objeto (não string).
                    ["parameters"] = SafeSchema(t.ParametersJson),
                },
            }).ToArray();
        return body;
    }

    private static object SerializeMessage(LlmChatMessage m)
    {
        if (m.Role == "tool")
            return new Dictionary<string, object?>
            { ["role"] = "tool", ["tool_call_id"] = m.ToolCallId, ["content"] = m.Content ?? "" };
        if (m.ToolCalls is { Count: > 0 } calls)
            return new Dictionary<string, object?>
            {
                ["role"] = "assistant",
                ["content"] = m.Content,
                ["tool_calls"] = calls.Select(c => (object)new Dictionary<string, object?>
                {
                    ["id"] = c.Id,
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?>
                    { ["name"] = c.Name, ["arguments"] = c.ArgumentsJson },
                }).ToArray(),
            };
        return new Dictionary<string, object?> { ["role"] = m.Role, ["content"] = m.Content ?? "" };
    }

    /// <summary>Schema inválido do provider nunca derruba o request — cai em objeto vazio.</summary>
    private static JsonElement SafeSchema(string parametersJson)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(parametersJson); }
        catch (JsonException) { return JsonSerializer.Deserialize<JsonElement>("{}"); }
    }

    /// <summary>choice[0] parseado: content + finish_reason + tool_calls (quando o modelo pede tools).</summary>
    private sealed record ChatChoice(string? Content, string? FinishReason, List<LlmToolCall>? ToolCalls);

    /// <summary>
    /// Extrai <c>choices[0].message</c> de uma resposta OpenAI-compatible (content, finish_reason
    /// e tool_calls). Retorna null quando o body não é esse wrapper — parse leniente de propósito:
    /// o wrapper é transporte, não contrato; o que segue estrito é o PatchProposal dentro dele.
    /// </summary>
    private static ChatChoice? TryParseChoice(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            // ── Wrapped envelopes ──
            // Some gateways (cline.bot, OpenCodex upstreams) return {"data":{choices:[...]},
            // "success":true} instead of the bare OpenAI body. Descend one level when the
            // top-level has no `choices` but `data` is an object — still transport, not contract.
            if (root.ValueKind == JsonValueKind.Object
                && !root.TryGetProperty("choices", out _)
                && root.TryGetProperty("data", out JsonElement data)
                && data.ValueKind == JsonValueKind.Object)
                root = data;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return null;

            JsonElement first = choices[0];
            if (first.ValueKind != JsonValueKind.Object)
                return null;

            string? finish = first.TryGetProperty("finish_reason", out JsonElement fr)
                && fr.ValueKind == JsonValueKind.String ? fr.GetString() : null;

            if (!first.TryGetProperty("message", out JsonElement message)
                || message.ValueKind != JsonValueKind.Object)
                return null;

            string? content = message.TryGetProperty("content", out JsonElement ce)
                && ce.ValueKind == JsonValueKind.String ? ce.GetString() : null;

            List<LlmToolCall>? calls = null;
            if (message.TryGetProperty("tool_calls", out JsonElement tc)
                && tc.ValueKind == JsonValueKind.Array && tc.GetArrayLength() > 0)
            {
                calls = new List<LlmToolCall>();
                foreach (JsonElement call in tc.EnumerateArray())
                {
                    if (!call.TryGetProperty("function", out JsonElement fn)
                        || fn.ValueKind != JsonValueKind.Object
                        || !fn.TryGetProperty("name", out JsonElement nameEl)
                        || nameEl.ValueKind != JsonValueKind.String)
                        continue;
                    calls.Add(new LlmToolCall
                    {
                        Id = call.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String
                            ? idEl.GetString()! : string.Empty,
                        Name = nameEl.GetString()!,
                        ArgumentsJson = fn.TryGetProperty("arguments", out JsonElement argsEl)
                            && argsEl.ValueKind == JsonValueKind.String
                            ? argsEl.GetString() ?? "{}" : "{}",
                    });
                }
                if (calls.Count == 0) calls = null;
            }

            return new ChatChoice(content, finish, calls);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lê a resposta em chunks; retorna null se o total exceder maxBytes.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(chunk.AsMemory(), ct);
            if (read == 0) break;
            total += read;
            if (total > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>Parse estrito (campo desconhecido = JsonException) + validação de base64 (g).</summary>
    private static PatchProposal ParseStrict(byte[] body)
    {
        var proposal = JsonSerializer.Deserialize<PatchProposal>(body, StrictJson)
            ?? throw new JsonException(Strings.U_Llm_EmptyResponse);
        _ = Convert.FromBase64String(proposal.Target.NewBytesBase64); // FormatException → MalformedPayload
        return proposal;
    }

    /// <summary>Redação por padrão (LlmRedactor) + valor exato da chave da sessão (T4).</summary>
    private static string RedactWithSessionKey(string message, LlmCredential credential)
    {
        string redacted = LlmRedactor.Redact(message);
        if (ReadApiKey(credential) is { Length: > 0 } key)
            redacted = redacted.Replace(key, "[REDACTED]", StringComparison.Ordinal);
        return redacted;
    }

    /// <summary>
    /// Lê a chave da sessão via accessor controlado do próprio LlmCredential
    /// (mesmo assembly, nunca serializado/logado). Alimenta o header Authorization
    /// e a redação por valor exato (T4).
    /// </summary>
    private static string? ReadApiKey(LlmCredential credential)
    {
        char[]? chars = credential.ReadApiKeyCopy();
        if (chars is null || chars.Length == 0)
            return null;
        try { return new string(chars); }
        finally { Array.Clear(chars); }
    }

    private static LlmResult Failure(LlmRejectionReason reason, string message) =>
        new() { Reason = reason, Message = LlmRedactor.Redact(message) };
}

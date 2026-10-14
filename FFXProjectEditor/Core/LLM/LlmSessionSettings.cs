using System;
using System.Collections.Generic;

namespace FFXProjectEditor.Core.LLM;

/// <summary>Flags de capacidade da sessão. Insert/Delete/Grow/RemoteEndpoint = FALSE por padrão.</summary>
[Flags]
public enum LlmCapabilityFlag
{
    None = 0,
    ByteReplace = 1 << 0,   // perfil 1: troca byte-local (default de sessão nova)
    Insert = 1 << 1,        // exige evidência independente por formato
    Delete = 1 << 2,        // idem
    Grow = 1 << 3,          // idem
    RemoteEndpoint = 1 << 4,// exige confirmação por request (host + payload preview)
    HistoryPersist = 1 << 5 // gravar histórico local (origem/decisão, sem payload)
}

/// <summary>
/// Configuração opt-in da sessão. NUNCA contém chave/segredo (isso vive em LlmCredential,
/// só em memória). Pode ser persistida em settings JSON porque não carrega nada sensível.
/// </summary>
public sealed record LlmSessionSettings
{
    public bool IsEnabled { get; init; } = false; // opt-in: desabilitado por padrão
    public required string Provider { get; init; }
    public required Uri Endpoint { get; init; }
    public required string ModelId { get; init; }
    public required IReadOnlyList<LlmDataKind> AllowedDataKinds { get; init; }
    public required LlmCapabilityFlag CapabilityFlags { get; init; }
    public required int MaxResponseBytes { get; init; }
    public required int MaxProposalBytes { get; init; }
    public required TimeSpan RequestTimeout { get; init; }
    public bool RedactLogs { get; init; } = true;
}

/// <summary>
/// Segredo da sessão: vive SÓ em memória; nunca serializado, logado ou em exceção.
/// Dispose() zera o buffer. Header Authorization é montado apenas no adapter HTTP (L2).
/// </summary>
public sealed class LlmCredential : IDisposable
{
    private char[]? _apiKey;

    public bool HasKey => _apiKey is { Length: > 0 };

    /// <summary>
    /// Acesso controlado para o adapter HTTP (mesmo assembly). Devolve uma CÓPIA
    /// do buffer; nunca é serializado, logado ou exposto fora do subsistema.
    /// </summary>
    internal char[]? ReadApiKeyCopy()
    {
        if (_apiKey is null) return null;
        return (char[])_apiKey.Clone();
    }

    public static LlmCredential FromUserInput(string apiKey) =>
        new() { _apiKey = apiKey.ToCharArray() };

    public void Dispose()
    {
        if (_apiKey is { } key) Array.Clear(key);
        _apiKey = null;
    }
}

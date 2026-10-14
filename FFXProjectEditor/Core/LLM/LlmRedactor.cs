using System;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.Core.LLM;

/// <summary>
/// Redação central: preview, log, exceção, justificativa e histórico passam por aqui.
/// Padrões de chave/token são mascarados; a chave da sessão (LlmCredential) é checada
/// por valor exato — é a única defesa contra chaves arbitrárias sem formato conhecido.
/// </summary>
public static partial class LlmRedactor
{
    [GeneratedRegex(
        @"(sk-[A-Za-z0-9_-]{8,})|(Bearer\s+[A-Za-z0-9._-]{8,})|" +
        @"(api[_-]?key\s*[=:]\s*[A-Za-z0-9._-]{8,})|(authorization\s*:\s*[^\r\n]{4,})",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return SecretPattern().Replace(text, "[REDACTED]");
    }

    public static string BuildPreview(byte[] payload, int maxBytes)
    {
        var text = Convert.ToBase64String(payload);
        var cut = text.Length > maxBytes ? text[..maxBytes] + "…" : text;
        return Redact(cut);
    }

    public static bool ContainsSecret(string? text) =>
        !string.IsNullOrEmpty(text) && SecretPattern().IsMatch(text);
}

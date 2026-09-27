using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ai.AtelScript
{
    public enum AtelTokenKind
    {
        Identifier,
        IntLiteral,
        Keyword,     // var, if, else, while, return, func (while/func reservados — L3)
        Operator,    // = == != < > <= >= + - * / % ! ( ) { } , ;
        EndOfFile,
    }

    public readonly record struct AtelToken(AtelTokenKind Kind, string Text, int Line, int Column)
    {
        public override string ToString() => $"{Kind} '{Text}' @{Line}:{Column}";
    }

    /// <summary>
    /// F5-L1: tokenizador da linguagem ATEL de alto nível. Suporta:
    /// identificadores ([A-Za-z_][A-Za-z0-9_]*, incluindo $ para var de batalha), inteiros
    /// (decimal e hex 0x..), operadores (incluindo compostos ==, !=, &lt;=, &gt;=), comentários
    /// // e /* */, e palavras-chave. Erros de token lançam <see cref="AtelSyntaxException"/> com posição.
    /// </summary>
    public sealed class AtelLexer
    {
        private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        {
            "var", "if", "else", "while", "return", "func", "call",
        };

        private readonly string _src;
        private int _pos;
        private int _line = 1;
        private int _col = 1;

        public AtelLexer(string source)
        {
            _src = source ?? throw new ArgumentNullException(nameof(source));
        }

        public IReadOnlyList<AtelToken> Tokenize()
        {
            var tokens = new List<AtelToken>();
            while (true)
            {
                AtelToken t = Next();
                tokens.Add(t);
                if (t.Kind == AtelTokenKind.EndOfFile)
                    return tokens;
            }
        }

        private AtelToken Next()
        {
            SkipTrivia();
            if (_pos >= _src.Length)
                return new AtelToken(AtelTokenKind.EndOfFile, "<eof>", _line, _col);

            char c = _src[_pos];
            int startLine = _line, startCol = _col;

            if (char.IsLetter(c) || c == '_' || c == '$')
                return ReadIdentifier(startLine, startCol);

            if (char.IsDigit(c))
                return ReadNumber(startLine, startCol);

            return ReadOperatorOrPunct(startLine, startCol);
        }

        private void SkipTrivia()
        {
            while (_pos < _src.Length)
            {
                char c = _src[_pos];
                if (c is ' ' or '\t' or '\r' or '\n')
                {
                    Advance();
                }
                else if (c == '/' && _pos + 1 < _src.Length && _src[_pos + 1] == '/')
                {
                    while (_pos < _src.Length && _src[_pos] != '\n')
                        Advance();
                }
                else if (c == '/' && _pos + 1 < _src.Length && _src[_pos + 1] == '*')
                {
                    Advance();
                    Advance();
                    while (_pos < _src.Length && !(_src[_pos] == '*' && _pos + 1 < _src.Length && _src[_pos + 1] == '/'))
                        Advance();
                    if (_pos < _src.Length)
                    {
                        Advance();
                        Advance();
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private AtelToken ReadIdentifier(int line, int col)
        {
            int start = _pos;
            while (_pos < _src.Length)
            {
                char c = _src[_pos];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '$')
                    Advance();
                else
                    break;
            }
            string text = _src.Substring(start, _pos - start);
            AtelTokenKind kind = Keywords.Contains(text) ? AtelTokenKind.Keyword : AtelTokenKind.Identifier;
            return new AtelToken(kind, text, line, col);
        }

        private AtelToken ReadNumber(int line, int col)
        {
            int start = _pos;
            if (_pos + 1 < _src.Length && _src[_pos] == '0' && (_src[_pos + 1] is 'x' or 'X'))
            {
                Advance();
                Advance();
                while (_pos < _src.Length && Uri.IsHexDigit(_src[_pos]))
                    Advance();
            }
            else
            {
                while (_pos < _src.Length && char.IsDigit(_src[_pos]))
                    Advance();
            }
            return new AtelToken(AtelTokenKind.IntLiteral, _src.Substring(start, _pos - start), line, col);
        }

        private AtelToken ReadOperatorOrPunct(int line, int col)
        {
            int start = _pos;
            char c = _src[_pos];
            Advance();

            switch (c)
            {
                case '=':
                    if (Peek() == '=')
                    {
                        Advance();
                        return new AtelToken(AtelTokenKind.Operator, "==", line, col);
                    }
                    return new AtelToken(AtelTokenKind.Operator, "=", line, col);
                case '!':
                    if (Peek() == '=')
                    {
                        Advance();
                        return new AtelToken(AtelTokenKind.Operator, "!=", line, col);
                    }
                    return new AtelToken(AtelTokenKind.Operator, "!", line, col);
                case '&':
                    if (Peek() == '&')
                    {
                        Advance();
                        return new AtelToken(AtelTokenKind.Operator, "&&", line, col);
                    }
                    throw AtelSyntaxException.Unexpected($"caractere '&'", line, col);
                case '|':
                    if (Peek() == '|')
                    {
                        Advance();
                        return new AtelToken(AtelTokenKind.Operator, "||", line, col);
                    }
                    throw AtelSyntaxException.Unexpected($"caractere '|'", line, col);
                case '<':
                    if (Peek() == '=')
                    {
                        Advance();
                        return new AtelToken(AtelTokenKind.Operator, "<=", line, col);
                    }
                    return new AtelToken(AtelTokenKind.Operator, "<", line, col);
                case '>':
                    if (Peek() == '=')
                    {
                        Advance();
                        return new AtelToken(AtelTokenKind.Operator, ">=", line, col);
                    }
                    return new AtelToken(AtelTokenKind.Operator, ">", line, col);
                case '+' or '-' or '*' or '/' or '%' or '(' or ')' or '{' or '}' or ',' or ';':
                    return new AtelToken(AtelTokenKind.Operator, c.ToString(), line, col);
                default:
                    throw AtelSyntaxException.Unexpected($"caractere '{c}'", line, col);
            }
        }

        private char Peek() => _pos < _src.Length ? _src[_pos] : '\0';

        private void Advance()
        {
            if (_pos >= _src.Length)
                return;
            if (_src[_pos] == '\n')
            {
                _line++;
                _col = 1;
            }
            else
            {
                _col++;
            }
            _pos++;
        }
    }
}


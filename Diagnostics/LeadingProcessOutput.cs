namespace RoslynMcpServer.Diagnostics;

/// <summary>Bounded hints for finding the end of a secret carried over from the previous pipe.</summary>
internal sealed class LeadingProcessOutput
{
    internal void Scan(ReadOnlySpan<char> text, long line)
    {
        if (_characterCount < _characters.Length)
        {
            foreach (var character in text)
            {
                if (char.IsWhiteSpace(character))
                {
                    continue;
                }

                _characters[_characterCount++] = new(character, line);
                if (_characterCount == _characters.Length)
                {
                    break;
                }
            }
        }

        FindQuotes(text, '\'', line, ref _firstSingleQuote, ref _secondSingleQuote);
        FindQuotes(text, '"', line, ref _firstDoubleQuote, ref _secondDoubleQuote);
    }

    internal long FindContinuationEnd(MultilineProcessSecret.Continuation continuation)
    {
        if (continuation.Kind == MultilineProcessSecret.ContinuationKind.QuotedValue)
        {
            return (continuation.Quote == '\'' ? _firstSingleQuote : _firstDoubleQuote) ?? long.MaxValue;
        }

        var index = 0;
        if (continuation.Kind == MultilineProcessSecret.ContinuationKind.AssignmentSeparator)
        {
            if (_characterCount == 0 || _characters[0].Value is not (':' or '='))
            {
                return -1;
            }

            index++;
        }

        if (continuation.Kind == MultilineProcessSecret.ContinuationKind.None || _characterCount <= index)
        {
            return -1;
        }

        var value = _characters[index];
        if (continuation.Kind == MultilineProcessSecret.ContinuationKind.Bearer)
        {
            return MultilineProcessSecret.IsBearerValueCharacter(value.Value) ? value.Line : -1;
        }

        if (!continuation.Authorization && value.Value is '\'' or '"')
        {
            return (value.Value == '\'' ? _secondSingleQuote : _secondDoubleQuote) ?? long.MaxValue;
        }

        return value.Line;
    }

    private static void FindQuotes(ReadOnlySpan<char> text, char quote, long line, ref long? first, ref long? second)
    {
        if (second is not null)
        {
            return;
        }

        if (first is null)
        {
            var index = text.IndexOf(quote);
            if (index < 0)
            {
                return;
            }

            first = line;
            text = text[(index + 1)..];
        }

        if (text.Contains(quote))
        {
            second = line;
        }
    }

    private readonly record struct Character(char Value, long Line);

    private readonly Character[] _characters = new Character[2];
    private int _characterCount;
    private long? _firstSingleQuote;
    private long? _secondSingleQuote;
    private long? _firstDoubleQuote;
    private long? _secondDoubleQuote;
}

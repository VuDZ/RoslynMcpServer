namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Identifies lines whose secrets depend on adjacent output surviving capture.
/// Single-line secrets are still redacted by <see cref="ProcessOutputRedactor"/> when reports are stored.
/// </summary>
internal sealed class MultilineProcessSecret
{
    internal Continuation PendingContinuation => _state switch
    {
        ScanState.AssignmentSeparator => new(ContinuationKind.AssignmentSeparator, _authorization),
        ScanState.AssignmentValue => new(ContinuationKind.AssignmentValue, _authorization),
        ScanState.BearerWhitespace or ScanState.BearerValue => new(ContinuationKind.Bearer),
        ScanState.QuotedValue => new(ContinuationKind.QuotedValue, Quote: _quote),
        _ => default,
    };

    internal void Scan(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (_state == ScanState.Text && !IsWordCharacter(character))
            {
                FinishWord();
            }

            if (!ConsumeSecretCharacter(character) && IsWordCharacter(character) && _wordLength <= _word.Length)
            {
                if (_wordLength < _word.Length)
                {
                    _word[_wordLength] = character;
                }

                _wordLength++;
            }
        }
    }

    internal bool CompleteLine()
    {
        FinishWord();
        if (_state == ScanState.BearerWhitespace)
        {
            _state = ScanState.BearerValue;
        }

        // Neither side of a multiline value is safe to retain on its own if another line is dropped.
        var omit = _sensitiveLine || _state is ScanState.AssignmentValue or ScanState.BearerValue or ScanState.QuotedValue;
        _startedOnEarlierLine = _state != ScanState.Text;
        _sensitiveLine = false;
        if (_state == ScanState.AuthorizationValue)
        {
            SetTextState();
        }

        return omit;
    }

    internal static bool IsBearerValueCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~' or '+' or '/';

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private void FinishWord()
    {
        if (_wordLength == 0)
        {
            return;
        }

        if (_wordLength <= _word.Length)
        {
            var word = _word.AsSpan(0, _wordLength);
            _authorization = word.Equals("Authorization", StringComparison.OrdinalIgnoreCase);
            if (_authorization || ProcessOutputRedactor.IsAssignmentSecretKey(word))
            {
                _state = ScanState.AssignmentSeparator;
            }
            else if (word.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                _state = ScanState.BearerWhitespace;
            }
        }

        _wordLength = 0;
    }

    private bool ConsumeSecretCharacter(char character)
    {
        switch (_state)
        {
            case ScanState.Text:
                return false;
            case ScanState.AssignmentSeparator:
                if (char.IsWhiteSpace(character))
                {
                    return true;
                }

                if (character is ':' or '=')
                {
                    _state = ScanState.AssignmentValue;
                    return true;
                }

                SetTextState();
                return false;
            case ScanState.AssignmentValue:
                if (char.IsWhiteSpace(character))
                {
                    return true;
                }

                _sensitiveLine |= _startedOnEarlierLine;
                if (_authorization)
                {
                    _state = ScanState.AuthorizationValue;
                    return true;
                }

                if (character is '\'' or '"')
                {
                    _quote = character;
                    _state = ScanState.QuotedValue;
                    return true;
                }

                SetTextState();
                return false;
            case ScanState.BearerWhitespace:
                if (char.IsWhiteSpace(character))
                {
                    _state = ScanState.BearerValue;
                    return true;
                }

                SetTextState();
                return false;
            case ScanState.BearerValue:
                if (char.IsWhiteSpace(character))
                {
                    return true;
                }

                _sensitiveLine |= _startedOnEarlierLine
                    && IsBearerValueCharacter(character);
                SetTextState();
                return false;
            case ScanState.QuotedValue:
                _sensitiveLine |= _startedOnEarlierLine;
                if (character == _quote)
                {
                    SetTextState();
                }

                return true;
            case ScanState.AuthorizationValue:
                if (character == '\r')
                {
                    SetTextState();
                }

                return true;
            default:
                throw new InvalidOperationException("Unknown process output redaction state.");
        }
    }

    private void SetTextState()
    {
        _state = ScanState.Text;
        _startedOnEarlierLine = false;
    }

    internal readonly record struct Continuation(ContinuationKind Kind, bool Authorization = false, char Quote = default);

    internal enum ContinuationKind
    {
        None,
        AssignmentSeparator,
        AssignmentValue,
        Bearer,
        QuotedValue,
    }

    private enum ScanState
    {
        Text,
        AssignmentSeparator,
        AssignmentValue,
        BearerWhitespace,
        BearerValue,
        QuotedValue,
        AuthorizationValue,
    }

    // Authorization is the longest recognized key at 13 characters.
    private readonly char[] _word = new char[13];
    private int _wordLength;
    private ScanState _state;
    private char _quote;
    private bool _authorization;
    private bool _startedOnEarlierLine;
    private bool _sensitiveLine;
}

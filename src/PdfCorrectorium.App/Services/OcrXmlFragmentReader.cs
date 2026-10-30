using System.IO;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// Makes concatenated OCR XML declarations harmless processing instructions without
/// copying a whole document. Comments, CDATA, attribute values and other PIs are retained.
/// </summary>
internal sealed class OcrXmlFragmentReader(TextReader input) : TextReader
{
    private enum LexicalState { Text, Tag, Comment, CData, ProcessingInstruction }
    private readonly char[] _buffer = new char[8192];
    private string _pending = string.Empty;
    private string _ready = string.Empty;
    private int _position;
    private bool _ended;
    private LexicalState _state;
    private char _quote;
    private char _previous;
    private char _beforePrevious;

    private void Prepare(int count)
    {
        _ended = count == 0;
        var text = (_pending + new string(_buffer, 0, count)).ToCharArray();
        // Hold lookahead across buffer boundaries, including an entire CDATA opener.
        var end = _ended ? text.Length : Math.Max(0, text.Length - 9);
        for (var i = 0; i < end; i++)
        {
            var c = text[i];
            if (_state == LexicalState.Text && c == '<')
            {
                var tail = text.AsSpan(i);
                if (tail.StartsWith("<!--")) _state = LexicalState.Comment;
                else if (tail.StartsWith("<![CDATA[")) _state = LexicalState.CData;
                else if (tail.StartsWith("<?"))
                {
                    if (tail.Length > 5 && tail[..5].Equals("<?xml", StringComparison.OrdinalIgnoreCase) &&
                        char.IsWhiteSpace(tail[5]))
                    {
                        text[i + 2] = 'o'; text[i + 3] = 'c'; text[i + 4] = 'r';
                    }
                    _state = LexicalState.ProcessingInstruction;
                }
                else _state = LexicalState.Tag;
            }
            else if (_state == LexicalState.Tag)
            {
                if (_quote != '\0') { if (c == _quote) _quote = '\0'; }
                else if (c is '\'' or '"') _quote = c;
                else if (c == '>') _state = LexicalState.Text;
            }
            else if ((_state == LexicalState.Comment && _beforePrevious == '-' && _previous == '-' && c == '>') ||
                     (_state == LexicalState.CData && _beforePrevious == ']' && _previous == ']' && c == '>') ||
                     (_state == LexicalState.ProcessingInstruction && _previous == '?' && c == '>'))
                _state = LexicalState.Text;
            _beforePrevious = _previous;
            _previous = c;
        }
        _ready = new string(text, 0, end);
        _pending = new string(text, end, text.Length - end);
        _position = 0;
    }

    public override int Read(char[] buffer, int index, int count)
    {
        var destination = buffer.AsSpan(index, count);
        if (destination.IsEmpty) return 0;
        while (_position == _ready.Length && !_ended) Prepare(input.Read(_buffer, 0, _buffer.Length));
        var size = Math.Min(destination.Length, _ready.Length - _position);
        _ready.AsSpan(_position, size).CopyTo(destination);
        _position += size;
        return size;
    }

    public override Task<int> ReadAsync(char[] buffer, int index, int count) =>
        ReadAsync(buffer.AsMemory(index, count)).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty) return 0;
        while (_position == _ready.Length && !_ended)
            Prepare(await input.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false));
        var size = Math.Min(buffer.Length, _ready.Length - _position);
        _ready.AsMemory(_position, size).CopyTo(buffer);
        _position += size;
        return size;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) input.Dispose();
        base.Dispose(disposing);
    }
}

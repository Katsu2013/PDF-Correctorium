using System.IO;
using System.Text.Json;

namespace PdfCorrectorium.App.Services;

/// <summary>固定予算のトークンだけを保持する、UTF-8 JSONの逐次リーダーです。</summary>
internal sealed class OcrJsonTokenReader(Stream stream, int maximumTokenBytes, int maximumTokens, int maximumDepth)
{
    internal readonly record struct Token(JsonTokenType Kind, int Depth, string? Text = null, double? Number = null);
    private byte[] _buffer = new byte[Math.Min(8192, maximumTokenBytes)];
    private int _position;
    private int _length;
    private bool _final;
    private bool _initial = true;
    private int _tokens;
    private JsonReaderState _state = new(new JsonReaderOptions { MaxDepth = maximumDepth + 1 });

    internal async ValueTask<Token?> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_initial)
            {
                while (_length < 3 && !_final) await FillAsync(cancellationToken);
                if (_length >= 3 && _buffer[0] == 0xEF && _buffer[1] == 0xBB && _buffer[2] == 0xBF) _position = 3;
                _initial = false;
            }
            if (TryRead(out var token)) return token;
            if (_final) return null;
            await FillAsync(cancellationToken);
        }
    }

    private bool TryRead(out Token token)
    {
        var reader = new Utf8JsonReader(_buffer.AsSpan(_position, _length - _position), _final, _state);
        var success = reader.Read();
        _position += checked((int)reader.BytesConsumed);
        _state = reader.CurrentState;
        token = default;
        if (!success) return false;
        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject && reader.CurrentDepth >= maximumDepth)
            throw new InvalidDataException("OCR JSONの深さが上限を超えています。");
        if (++_tokens > maximumTokens) throw new InvalidDataException("OCR JSONのトークン数が上限を超えています。");
        if (reader.ValueSpan.Length > maximumTokenBytes) throw new InvalidDataException("OCR JSONの値が大きすぎます。");
        var text = reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName ? reader.GetString() : null;
        double? number = reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var value) && double.IsFinite(value) ? value : null;
        token = new(reader.TokenType, reader.CurrentDepth, text, number);
        return true;
    }

    private async ValueTask FillAsync(CancellationToken cancellationToken)
    {
        var remaining = _length - _position;
        _buffer.AsSpan(_position, remaining).CopyTo(_buffer);
        _position = 0;
        _length = remaining;
        if (_length == _buffer.Length)
        {
            if (_buffer.Length >= maximumTokenBytes) throw new InvalidDataException("OCR JSONの値が大きすぎます。");
            Array.Resize(ref _buffer, (int)Math.Min((long)_buffer.Length * 2, maximumTokenBytes));
        }
        var read = await stream.ReadAsync(_buffer.AsMemory(_length), cancellationToken);
        _length += read;
        _final = read == 0;
    }

    internal async ValueTask<Token> RequireAsync(CancellationToken cancellationToken) =>
        await ReadAsync(cancellationToken) ?? throw new JsonException("Unexpected end of OCR JSON.");

    internal async ValueTask SkipAsync(Token token, CancellationToken cancellationToken)
    {
        if (token.Kind is not (JsonTokenType.StartArray or JsonTokenType.StartObject)) return;
        Token next;
        do { next = await RequireAsync(cancellationToken); }
        while (next.Depth != token.Depth || next.Kind is not (JsonTokenType.EndArray or JsonTokenType.EndObject));
    }
}

using System.Text.Json;
namespace PdfCorrectorium.Core.IO;

/// <summary>Checks the complete JSON before allocating its application model, including ignored fields.</summary>
public sealed record JsonInputBudget
{
    public int MaximumDepth { get; init; } = 64;
    public int MaximumTokens { get; init; } = 8_000_000;
    public int MaximumObjects { get; init; } = 250_000;
    public int MaximumArrayItems { get; init; } = 100_000;
    public int MaximumStringCharacters { get; init; } = 32_768;
    public long MaximumTotalStringCharacters { get; init; } = 32L * 1024 * 1024;
    public int MaximumTokenBytes { get; init; } = 256 * 1024;
    public IReadOnlyDictionary<string, int> StringLimits { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> ArrayLimits { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> AggregateArrayLimits { get; init; } = new Dictionary<string, int>();

    public async Task ValidateAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (MaximumDepth is < 1 or > 256 || MaximumTokens <= 0 || MaximumObjects <= 0 || MaximumArrayItems <= 0 ||
            MaximumStringCharacters <= 0 || MaximumTotalStringCharacters <= 0 || MaximumTokenBytes < 8 ||
            ArrayLimits.Values.Any(x => x <= 0) || AggregateArrayLimits.Values.Any(x => x <= 0))
            throw new InvalidOperationException("JSON input budgets must be positive and depth must be at most 256.");
        var scanner = new Scanner(this);
        var buffer = new byte[Math.Min(8192, MaximumTokenBytes)];
        var length = 0; var final = false; var initial = true;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!final && length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                length += read; final = read == 0;
            }
            if (initial && (length >= 3 || final))
            {
                if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                { buffer.AsSpan(3, length - 3).CopyTo(buffer); length -= 3; }
                initial = false;
            }
            if (initial) continue;
            var consumed = scanner.Scan(buffer.AsSpan(0, length), final, cancellationToken);
            buffer.AsSpan(consumed, length - consumed).CopyTo(buffer); length -= consumed;
            if (final) { scanner.Complete(); return; }
            if (length == buffer.Length)
            {
                if (buffer.Length >= MaximumTokenBytes) throw new InvalidDataException("JSON token byte budget exceeded.");
                Array.Resize(ref buffer, (int)Math.Min((long)buffer.Length * 2, MaximumTokenBytes));
            }
        }
    }

    private sealed class Frame(bool array, string name, int limit)
    {
        public bool Array { get; } = array;
        public string Name { get; } = name;
        public int Limit { get; } = limit;
        public int Count;
        public string Property = string.Empty;
        public HashSet<string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private sealed class Scanner(JsonInputBudget budget)
    {
        private JsonReaderState _state = new(new JsonReaderOptions { MaxDepth = budget.MaximumDepth });
        private readonly Stack<Frame> _frames = new();
        private readonly Dictionary<string, int> _aggregates = new(StringComparer.OrdinalIgnoreCase);
        private int _tokens, _objects;
        private long _characters;
        private bool _root;
        public int Scan(ReadOnlySpan<byte> bytes, bool final, CancellationToken ct)
        {
            var reader = new Utf8JsonReader(bytes, final, _state);
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested(); _root = true;
                if (++_tokens > budget.MaximumTokens) throw new InvalidDataException("JSON token budget exceeded.");
                if (reader.ValueSpan.Length > budget.MaximumTokenBytes) throw new InvalidDataException("JSON token byte budget exceeded.");
                if (reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject) { _frames.Pop(); continue; }
                var text = reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String ? reader.GetString()! : null;
                if (text is not null)
                {
                    var field = _frames.TryPeek(out var holder) ? holder.Property : string.Empty;
                    var textLimit = reader.TokenType == JsonTokenType.PropertyName ? 256 :
                        budget.StringLimits.TryGetValue(field, out var specific) ? Math.Min(specific, budget.MaximumStringCharacters) : budget.MaximumStringCharacters;
                    if (text.Length > textLimit) throw new InvalidDataException("JSON string budget exceeded.");
                    _characters = checked(_characters + text.Length);
                    if (_characters > budget.MaximumTotalStringCharacters) throw new InvalidDataException("JSON total string budget exceeded.");
                }
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var frame = _frames.Peek();
                    if (!frame.Properties.Add(text!)) throw new InvalidDataException("Duplicate JSON fields are not allowed.");
                    frame.Property = text!; continue;
                }
                var name = string.Empty;
                if (_frames.TryPeek(out var parent))
                {
                    name = parent.Array ? parent.Name : parent.Property;
                    if (parent.Array)
                    {
                        if (++parent.Count > parent.Limit) throw new InvalidDataException("JSON array budget exceeded: " + name);
                        if (budget.AggregateArrayLimits.TryGetValue(name, out var maximum))
                        {
                            var aggregateName = name.Equals("children", StringComparison.OrdinalIgnoreCase) && budget.AggregateArrayLimits.ContainsKey("bookmarks") ? "bookmarks" : name;
                            _aggregates.TryGetValue(aggregateName, out var count);
                            if (count >= maximum) throw new InvalidDataException("JSON aggregate array budget exceeded: " + name);
                            _aggregates[aggregateName] = count + 1;
                        }
                    }
                }
                if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
                {
                    if (reader.TokenType == JsonTokenType.StartObject && ++_objects > budget.MaximumObjects)
                        throw new InvalidDataException("JSON object budget exceeded.");
                    var maximum = budget.ArrayLimits.TryGetValue(name, out var limit) ? Math.Min(limit, budget.MaximumArrayItems) : budget.MaximumArrayItems;
                    _frames.Push(new Frame(reader.TokenType == JsonTokenType.StartArray, name, maximum));
                }
            }
            _state = reader.CurrentState;
            return checked((int)reader.BytesConsumed);
        }
        public void Complete()
        { if (!_root || _frames.Count != 0) throw new JsonException("Incomplete JSON document."); }
    }
}

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Minimal.Models;
using Minimal.Templates;

namespace Minimal;

internal readonly struct FortunesRazorParameters(List<Fortune> model) : IReadOnlyDictionary<string, object?>
{
    private const string ModelKeyName = nameof(FortunesRazor.Model);

    private readonly KeyValuePair<string, object?> _modelKvp = new(ModelKeyName, model);

    public object? this[string key] => KeyIsModel(key) ? model : null;

    public IEnumerable<string> Keys { get; } = [ModelKeyName];

    public IEnumerable<object?> Values { get; } = [model];

    public int Count { get; } = 1;

    public bool ContainsKey(string key) => KeyIsModel(key);

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => new Enumerator(_modelKvp);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value)
    {
        if (KeyIsModel(key))
        {
            value = model;
            return true;
        }
        value = default;
        return false;
    }

    private static bool KeyIsModel(string key) => ModelKeyName.Equals(key, StringComparison.Ordinal);

    private struct Enumerator(KeyValuePair<string, object?> kvp) : IEnumerator<KeyValuePair<string, object?>>
    {
        private bool _moved;

        public readonly KeyValuePair<string, object?> Current { get; } = kvp;

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_moved)
            {
                return false;
            }
            _moved = true;
            return true;
        }

        public readonly void Dispose() { }

        public void Reset() => throw new NotSupportedException();
    }
}

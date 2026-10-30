namespace Lamha.Core;

/// <summary>The files being previewed, in selection order. Navigation stops at both ends.</summary>
public sealed class PreviewSession
{
    readonly List<string> _files;
    readonly Func<string, bool> _exists;

    public PreviewSession(IEnumerable<string> paths, Func<string, bool>? exists = null)
    {
        _exists = exists ?? File.Exists;
        _files = paths.Where(p => FileTypes.IsSupported(p) && _exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public int Count => _files.Count;
    public int Index { get; private set; }
    public string? Current => Index < _files.Count ? _files[Index] : null;
    public string Position => $"{Index + 1} / {Count}";

    public bool MoveNext() => Move(+1);
    public bool MovePrevious() => Move(-1);

    // Files deleted since selection are dropped here, so the counter stays honest.
    bool Move(int step)
    {
        for (int i = Index + step; i >= 0 && i < _files.Count; i += step)
        {
            if (_exists(_files[i])) { Index = i; return true; }
            _files.RemoveAt(i);
            if (step > 0) i--;
            else if (Index >= _files.Count) Index = _files.Count - 1;
            else if (i < Index) Index--;
        }
        return false;
    }
}

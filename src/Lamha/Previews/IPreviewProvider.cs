using System.Windows;
using System.Windows.Input;
using Lamha.Core;

namespace Lamha.Previews;

public sealed record PreviewRequest(string Path, string? Password, AppSettings Settings);

/// <summary>One implementation per file category. Adding a format means one class and one line in PreviewRegistry.</summary>
public interface IPreviewProvider
{
    bool Handles(FileKind kind);
    Task<IPreview> OpenAsync(PreviewRequest request, CancellationToken ct);
}

/// <summary>A loaded document. Created on the UI thread and disposed when the user moves on.</summary>
public interface IPreview : IDisposable
{
    FrameworkElement View { get; }
    IReadOnlyList<UIElement> Tools { get; }
    string Info { get; }
    string Status => "";
    event Action? StatusChanged { add { } remove { } }

    /// <summary>Content size in DIPs, used to fit the window around it, or null to use the default size.</summary>
    Size? PreferredSize => null;

    /// <summary>Copies a text selection and returns true, or returns false so Ctrl+C copies the file.</summary>
    bool CopySelection() => false;
    void FocusSearch() { }

    /// <summary>Keys specific to this preview. Returns true if the key was used.</summary>
    bool OnKey(Key key, bool shift) => false;
}

public sealed class PreviewRegistry(params IPreviewProvider[] providers)
{
    public IPreviewProvider? Find(string path)
    {
        var kind = FileTypes.Detect(path);
        return providers.FirstOrDefault(p => p.Handles(kind));
    }
}

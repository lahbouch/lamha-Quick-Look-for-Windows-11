using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lamha.Core;
using static Lamha.Pdf.Pdfium;

namespace Lamha.Pdf;

public sealed class PdfPasswordException(bool wrongPassword) : Exception { public bool WrongPassword { get; } = wrongPassword; }
public sealed class PdfCorruptException : Exception;
public sealed class PdfLockedException : Exception;

public readonly record struct SearchHit(int Page, int Start, int Count);

/// <summary>Straight-alpha BGRA pixels and the page size, in points, to place them on.</summary>
public sealed record ImagePage(byte[] Bgra, int Width, int Height, double PageWidth, double PageHeight);

/// <summary>A PDF held open read-only. PDFium is not thread-safe, so every call goes through <see cref="Gate"/>.</summary>
public sealed unsafe class PdfDoc : IDisposable
{
    static readonly object Gate = new();
    static PdfDoc() => FPDF_InitLibrary();

    readonly FileStream _stream;
    readonly GetBlock _read; // kept alive: PDFium calls it for as long as the document is open
    readonly byte[] _readBuffer = new byte[64 * 1024];
    FPDF_FILEACCESS* _access;
    nint _doc;

    nint _cachedPage, _cachedText;
    int _cachedIndex = -1;
    nint _renderPage;
    int _renderIndex = -1;
    int _rotation;

    public Size[] Sizes { get; }
    public int PageCount => Sizes.Length;

    PdfDoc(FileStream stream, string? password)
    {
        _stream = stream;
        _read = ReadBlock;
        _access = (FPDF_FILEACCESS*)Marshal.AllocHGlobal(sizeof(FPDF_FILEACCESS));
        *_access = new FPDF_FILEACCESS { Length = (uint)stream.Length, Read = Marshal.GetFunctionPointerForDelegate(_read) };

        lock (Gate)
        {
            _doc = FPDF_LoadCustomDocument(_access, password);
            if (_doc == 0)
            {
                uint err = FPDF_GetLastError();
                Release();
                if (err == ERR_PASSWORD) throw new PdfPasswordException(password is not null);
                throw new PdfCorruptException();
            }
            int n = FPDF_GetPageCount(_doc);
            Sizes = new Size[n];
            for (int i = 0; i < n; i++)
            {
                FPDF_GetPageSizeByIndex(_doc, i, out double w, out double h);
                Sizes[i] = new Size(Math.Max(w, 1), Math.Max(h, 1));
            }
        }
    }

    /// <summary>Turns every page clockwise by quarter turns. <see cref="Sizes"/> then describes the page as displayed.</summary>
    // PDFium writes through a callback; Gate guarantees one save at a time, so one static target is enough.
    static Stream? _writing;
    static readonly WriteBlock Writer = (self, data, size) =>
    {
        try { _writing!.Write(new ReadOnlySpan<byte>(data, (int)size)); return 1; }
        catch (IOException) { return 0; } // an exception must not cross back into native code
    };
    static readonly nint WriterPtr = Marshal.GetFunctionPointerForDelegate(Writer);

    /// <summary>False when the author's permissions forbid taking pages out of the document.</summary>
    public bool CanExtractPages
    {
        get
        {
            lock (Gate)
            {
                uint p = FPDF_GetDocPermissions(_doc);
                return (p & (PERM_MODIFY | PERM_ASSEMBLE)) != 0;
            }
        }
    }

    /// <summary>Writes the given pages as a new PDF. Fails rather than replace an existing file.</summary>
    public void SavePages(int[] pages, string path)
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_doc == 0, this);
            WriteNew(path, dest =>
            {
                fixed (int* indices = pages)
                    if (FPDF_ImportPagesByIndex(dest, _doc, indices, (uint)pages.Length, 0) == 0) throw new PdfCorruptException();
            });
        }
    }

    /// <summary>
    /// Joins documents and pictures, in order, into a new PDF; each picture becomes a page of its own size. Fails rather
    /// than replace an existing file.
    /// </summary>
    public static void Merge(IReadOnlyList<string> sources, string path, Func<string, ImagePage>? decodeImage = null)
    {
        var docs = new List<PdfDoc>();
        var parts = new List<object>();
        try
        {
            // Decoding and parsing happen before taking Gate, so a large photo does not stall other previews.
            foreach (var s in sources)
            {
                if (FileTypes.Detect(s) == FileKind.Image) parts.Add(decodeImage!(s));
                else
                {
                    var d = Open(s);
                    docs.Add(d);
                    parts.Add(d);
                }
            }
            if (docs.Any(d => !d.CanExtractPages)) throw new PdfLockedException();
            lock (Gate)
                WriteNew(path, dest =>
                {
                    foreach (var part in parts)
                    {
                        if (part is PdfDoc d)
                        {
                            if (FPDF_ImportPagesByIndex(dest, d._doc, null, 0, FPDF_GetPageCount(dest)) == 0) throw new PdfCorruptException();
                        }
                        else AddImagePage(dest, (ImagePage)part);
                    }
                });
        }
        finally
        {
            foreach (var d in docs) d.Dispose();
        }
    }

    static void AddImagePage(nint dest, ImagePage image)
    {
        nint page = FPDFPage_New(dest, FPDF_GetPageCount(dest), image.PageWidth, image.PageHeight);
        try
        {
            nint obj = FPDFPageObj_NewImageObj(dest);
            fixed (byte* pixels = image.Bgra)
            {
                nint bitmap = FPDFBitmap_CreateEx(image.Width, image.Height, BitmapBGRA, pixels, image.Width * 4);
                int ok = FPDFImageObj_SetBitmap(&page, 1, obj, bitmap); // copies the pixels
                FPDFBitmap_Destroy(bitmap);
                if (ok == 0) throw new PdfCorruptException();
            }
            FPDFPageObj_Transform(obj, image.PageWidth, 0, 0, image.PageHeight, 0, 0); // a unit image scaled to fill the page
            FPDFPage_InsertObject(page, obj);
            if (FPDFPage_GenerateContent(page) == 0) throw new PdfCorruptException();
        }
        finally { FPDF_ClosePage(page); }
    }

    // Callers hold Gate.
    static void WriteNew(string path, Action<nint> fill)
    {
        nint dest = FPDF_CreateNewDocument();
        bool created = false;
        try
        {
            fill(dest);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                created = true;
                _writing = file;
                var writer = new FPDF_FILEWRITE { Version = 1, WriteBlock = WriterPtr };
                bool ok = FPDF_SaveAsCopy(dest, &writer, FPDF_NO_INCREMENTAL) != 0;
                _writing = null;
                if (!ok) throw new IOException("PDF save failed");
            }
        }
        catch when (created)
        {
            try { File.Delete(path); } catch (IOException) { } // never leave half a file behind
            throw;
        }
        finally
        {
            _writing = null;
            FPDF_CloseDocument(dest);
        }
    }

    public void Rotate(int quarters)
    {
        lock (Gate)
        {
            _rotation = (_rotation + quarters) % 4;
            if (quarters % 2 != 0)
                for (int i = 0; i < Sizes.Length; i++) Sizes[i] = new Size(Sizes[i].Height, Sizes[i].Width);
        }
    }

    public void ScalePages(double[] units)
    {
        for (int i = 0; i < Sizes.Length; i++)
            Sizes[i] = new Size(Sizes[i].Width * units[i], Sizes[i].Height * units[i]);
    }

    /// <summary>The stream stays open with full sharing, so renaming or deleting the file does not break the preview.</summary>
    public static PdfDoc Open(string path, string? password = null)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
        if (stream.Length > uint.MaxValue) { stream.Dispose(); throw new PdfCorruptException(); }
        try { return new PdfDoc(stream, password); }
        catch { stream.Dispose(); throw; }
    }

    int ReadBlock(nint param, uint position, byte* buffer, uint size)
    {
        _stream.Position = position;
        return _stream.ReadAtLeast(new Span<byte>(buffer, (int)size), (int)size, throwOnEndOfStream: false) == size ? 1 : 0;
    }

    /// <summary>Renders part of a page. <paramref name="px"/> is in device pixels of the page scaled by <paramref name="pxPerPt"/>.</summary>
    public BitmapSource RenderTile(int page, double pxPerPt, Int32Rect px, CancellationToken ct)
    {
        var size = Sizes[page];
        int fullW = (int)Math.Round(size.Width * pxPerPt), fullH = (int)Math.Round(size.Height * pxPerPt);
        int stride = px.Width * 4;
        var buffer = new byte[stride * px.Height];

        lock (Gate)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_doc == 0, this);
            nint p = RenderPage(page);
            if (p == 0) throw new PdfCorruptException();
            fixed (byte* b = buffer)
            {
                nint bmp = FPDFBitmap_CreateEx(px.Width, px.Height, BitmapBGRA, b, stride);
                FPDFBitmap_FillRect(bmp, 0, 0, px.Width, px.Height, 0xFFFFFFFF);
                FPDF_RenderPageBitmap(bmp, p, -px.X, -px.Y, fullW, fullH, _rotation, FPDF_ANNOT);
                FPDFBitmap_Destroy(bmp);
            }
        }

        var result = BitmapSource.Create(px.Width, px.Height, 96, 96, PixelFormats.Bgra32, null, buffer, stride);
        result.Freeze();
        return result;
    }

    // Parsing is the slow part of a huge page, and consecutive tiles nearly always come from the same one.
    nint RenderPage(int index)
    {
        if (_renderIndex != index)
        {
            if (_renderPage != 0) FPDF_ClosePage(_renderPage);
            _renderPage = FPDF_LoadPage(_doc, index);
            _renderIndex = index;
        }
        return _renderPage;
    }

    (nint Page, nint Text) TextPage(int index)
    {
        if (_cachedIndex != index)
        {
            CloseCachedText();
            _cachedPage = FPDF_LoadPage(_doc, index);
            _cachedText = _cachedPage == 0 ? 0 : FPDFText_LoadPage(_cachedPage);
            _cachedIndex = index;
        }
        return (_cachedPage, _cachedText);
    }

    void CloseCachedText()
    {
        if (_cachedText != 0) FPDFText_ClosePage(_cachedText);
        if (_cachedPage != 0) FPDF_ClosePage(_cachedPage);
        _cachedText = _cachedPage = 0;
        _cachedIndex = -1;
    }

    /// <summary>Character under a point given in displayed page points (top-left origin), or -1.</summary>
    public int CharAt(int page, Point pt)
    {
        lock (Gate)
        {
            if (_doc == 0) return -1;
            var (p, t) = TextPage(page);
            if (t == 0) return -1;
            var s = Sizes[page];
            FPDF_DeviceToPage(p, 0, 0, (int)(s.Width * 100), (int)(s.Height * 100), _rotation, (int)(pt.X * 100), (int)(pt.Y * 100), out double x, out double y);
            return FPDFText_GetCharIndexAtPos(t, x, y, 12, 12);
        }
    }

    /// <summary>Line rectangles of a character range, in displayed page points.</summary>
    public List<Rect> TextRects(int page, int start, int count)
    {
        var rects = new List<Rect>();
        lock (Gate)
        {
            if (_doc == 0) return rects;
            var (p, t) = TextPage(page);
            if (t == 0) return rects;
            var s = Sizes[page];
            int w = (int)(s.Width * 100), h = (int)(s.Height * 100);
            int n = FPDFText_CountRects(t, start, count);
            for (int i = 0; i < n; i++)
            {
                FPDFText_GetRect(t, i, out double l, out double top, out double r, out double b);
                FPDF_PageToDevice(p, 0, 0, w, h, _rotation, l, top, out int x1, out int y1);
                FPDF_PageToDevice(p, 0, 0, w, h, _rotation, r, b, out int x2, out int y2);
                rects.Add(new Rect(new Point(x1 / 100.0, y1 / 100.0), new Point(x2 / 100.0, y2 / 100.0)));
            }
        }
        return rects;
    }

    public string Text(int page, int start, int count)
    {
        lock (Gate)
        {
            if (_doc == 0) return "";
            var (_, t) = TextPage(page);
            if (t == 0) return "";
            var buffer = new char[count + 1];
            fixed (char* b = buffer)
            {
                int written = FPDFText_GetText(t, start, count, b);
                return new string(buffer, 0, Math.Max(0, written - 1));
            }
        }
    }

    public void Search(string query, Action<SearchHit> found, CancellationToken ct)
    {
        for (int page = 0; page < PageCount && !ct.IsCancellationRequested; page++)
        {
            lock (Gate)
            {
                if (_doc == 0) return;
                var (_, t) = TextPage(page);
                if (t == 0) continue;
                // PDFium's own search compares raw code points, which misses Arabic stored as presentation forms.
                foreach (var (start, count) in SearchFold.Find(Text(page, 0, FPDFText_CountChars(t)), query))
                    found(new SearchHit(page, start, count));
            }
        }
    }

    /// <summary>Top-level bookmarks. LibreOffice writes one per sheet, which is how sheet names reach the viewer.</summary>
    public List<(string Title, int Page)> Outline()
    {
        var items = new List<(string, int)>();
        lock (Gate)
        {
            for (nint b = FPDFBookmark_GetFirstChild(_doc, 0); b != 0; b = FPDFBookmark_GetNextSibling(_doc, b))
            {
                uint bytes = FPDFBookmark_GetTitle(b, null, 0);
                var chars = new char[bytes / 2];
                fixed (char* c = chars) FPDFBookmark_GetTitle(b, c, bytes);
                int page = FPDFDest_GetDestPageIndex(_doc, FPDFBookmark_GetDest(_doc, b));
                if (page >= 0) items.Add((new string(chars).TrimEnd('\0'), page));
            }
        }
        return items;
    }

    void Release()
    {
        if (_doc != 0) { FPDF_CloseDocument(_doc); _doc = 0; }
        if (_access != null) { Marshal.FreeHGlobal((nint)_access); _access = null; }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_doc == 0) return;
            CloseCachedText();
            if (_renderPage != 0) FPDF_ClosePage(_renderPage);
            _renderPage = 0;
            Release();
        }
        _stream.Dispose();
    }
}

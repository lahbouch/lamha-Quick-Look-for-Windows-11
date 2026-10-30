# Lamha (لمحة) — Architecture

Lamha is a tray utility: select files in File Explorer, press Space, read them in a floating window.

## Technology decisions

| Concern | Choice | Why | Rejected |
|---|---|---|---|
| Runtime / UI | .NET 10 LTS, WPF, self-contained publish | Native, no runtime install needed | Electron and WebView (forbidden by the brief) |
| Space detection | `WH_KEYBOARD_LL` on a dedicated thread + whitelist of the focused Win32 window | The only way to see a key before Explorer without a shell extension; a whitelist fails safe | `RegisterHotKey` (steals Space everywhere); Explorer shell extension (in-process, can crash Explorer, leaves residue) |
| Selection | Shell automation (`Shell.Application`) with `IShellBrowser` to pick the active tab | Supported API, returns Unicode paths | UI Automation (slow, returns display names, not paths) |
| PDF | PDFium (Apache-2.0/BSD) via own small P/Invoke layer, `bblanchon.PDFium.Win32` binaries | Same engine as Chrome; sharp Arabic; tiled rendering; text and search APIs | PdfiumViewer (unmaintained, WinForms); MuPDF (AGPL/commercial); Windows.Data.Pdf (no text API) |
| Word, Excel, PowerPoint | Bundled LibreOffice, headless convert to PDF, displayed by the PDF viewer | Best free fidelity, no Office needed, one viewer for all formats | Office COM (forbidden); Aspose/Syncfusion (paid); OpenXML rendering (poor fidelity) |
| Excel specifics | `SinglePageSheets` export: one PDF page per sheet, sheet names read from the PDF outline | Keeps column widths, borders, merges, number formats and calculated values without a custom grid | Custom grid over ClosedXML: large, and needs its own format engine |
| Images | WIC (PNG, JPEG, BMP, TIFF); SkiaSharp (GIF, WebP, animation); Svg.Skia (SVG) | WIC decodes huge files at reduced size; WebP cannot rely on an installed Windows codec | Skia for TIFF (unsupported) |
| Text | AvalonEdit | Virtualized, line numbers, highlighting, WPF bidi text | RichTextBox (too slow on large files) |
| Media | WPF `MediaElement` (Media Foundation) | No dependency | VLC/FFmpeg bundling (size, licensing) |
| Installer | Inno Setup, per-user, no admin | Clean uninstall of files, Run key and processes | MSI/WiX (more work, nothing gained) |
| Settings | JSON in `%APPDATA%\Lamha` | Nothing needs a database | SQLite |

The brief's `IPdfPreviewProvider`-style interfaces are collapsed into one `IPreviewProvider`, implemented once per
category. Adding a format means adding one class and one line in `PreviewRegistry`.

## Module map

```
src/Lamha/
  App.xaml.cs            lifecycle, single instance, composition root
  Core/                  FileTypes, PreviewSession, TextDecoder, CsvParser, AppSettings, Log (no WPF logic)
  Explorer/              KeyboardHook, ExplorerProbe (focus whitelist), ExplorerSelection (COM), Native
  Pdf/                   Pdfium P/Invoke, PdfDoc (thread-safe document), PdfSurface (tiled viewer)
  Previews/              IPreviewProvider and one provider per category
  Office/                OfficeConverter (LibreOffice sandboxed profile, cache)
  Ui/                    PreviewWindow, PreviewController, TrayIcon, SettingsWindow, Toast, Theme.xaml
```

## Space-key flow

1. The hook thread sees Space down. It does only cheap Win32 calls, never COM, because Windows silently removes hooks
   that are slow.
2. Space is swallowed only if **all** hold: no modifier, foreground is `CabinetWClass` or the desktop, no menu is open,
   the focused window is the file view (`DirectUIHWND` or `SysListView32` under `SHELLDLL_DefView`), and no type-ahead
   letter was typed in the last second. Anything else (address bar, search box, rename box, other apps) passes through.
3. The selection is read on a throwaway STA thread with a timeout, so a hung Explorer cannot freeze the app.
4. Closing the preview returns the foreground to the saved Explorer window.

The decision itself lives in `SpaceGate`, a pure state machine, so every path is unit tested without touching the OS.

## Findings from testing that shaped the design

- **Tall sheets:** PDF pages cap at 14400 units, so LibreOffice describes sheets over about 950 rows as a small page
  with a `/UserUnit` multiplier. PDFium ignores it and would draw a sliver, so `PdfUserUnits` reads the multiplier from
  the PDFs LibreOffice writes and `PdfDoc.ScalePages` applies it.
- **Arabic search:** PDFium's own search compares raw code points, which fails on PDFs that store presentation forms or
  visual-order text. `SearchFold` folds forms, tashkeel and kashida, and also tries the reversed query.
- **Damaged Office files:** LibreOffice imports a broken `.docx` as plain text, so files without a zip header are refused first.
- **Hidden sheets:** LibreOffice exports them, so sheet tabs are filtered against `xl/workbook.xml`.

## Document safety

- Files are opened read-only with sharing, so they cannot be locked or changed.
- Office files are **copied to a temp folder** before conversion, so LibreOffice cannot leave a lock file beside the original.
- LibreOffice runs with a private profile: macros disabled, link updates off, killed with the app through a job object,
  with a timeout.
- SVG is sanitized before parsing: DTDs prohibited, external references removed.
- PDF links and scripts are never followed or run: PDFium is built without V8 and link actions are not implemented.

## Rendering

All PDFium calls run under one lock because PDFium is not thread-safe. The viewer renders only a tile around the
viewport, at device pixels for the current zoom, so memory stays bounded at any zoom and text stays sharp. While a new
tile renders, the old one is stretched and then replaced.

Office conversions are cached in `%LOCALAPPDATA%\Lamha\cache`, keyed by path, size and modification time, and pruned to
500 MB. This cache holds PDF copies of previewed documents on the local disk.

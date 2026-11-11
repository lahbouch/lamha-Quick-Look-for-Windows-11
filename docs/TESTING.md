# Test report

Environment: Windows 11 Pro build 26200, one 1920×1080 monitor at 100%, .NET SDK 10.0.403, LibreOffice 26.8.1.1.

Most of this report was produced while the workstation was locked: rendering was checked by having WPF draw the real
window to a bitmap, and Explorer was queried through its real APIs. The Explorer flow was then run on the unlocked desktop
with real key presses. Sample documents were generated for the tests; no personal files were opened.

## Explorer flow (real keys and a real mouse click)

`tests\e2e-explorer.ps1`, four runs on the installed build, 2026-10-30. Results of the last run are in `docs\e2e-results.txt`.

| Check | Result |
|---|---|
| Space opens the preview, preview takes keyboard focus | Pass |
| Space closes, focus returns to Explorer | Pass |
| Escape closes | Pass |
| Clicking Explorer closes the preview | Pass on the earlier focus-based version. The current click-based version has not been run on the desktop |
| Arrows move through 3 files and stop at the end | Pass |
| Arabic filename | Pass |
| Space left alone while renaming, in the address bar, in the search box | Pass |
| Unsupported file shows no preview | Pass |
| The front window is read when two are open | Pass |
| Each Explorer tab's own selection is read | Pass |
| Space still types in Notepad, no preview over it | Pass |

No single run passed all 16 checks; each passed on the same build:
- Run 3 (15 of 16) failed only click-to-close, because the saved settings had "update preview automatically" on, which keeps the preview open by design.
- Run 4 (13 of 16) passed click-to-close but failed the first three checks. The log shows Lamha never saw those Space presses, so Explorer was not the front window. Two copies of the script may also have overlapped. The script now clicks Explorer first; that change has not been re-run.

## Automated tests

121 unit tests, all passing: file types, provider selection, navigation (including files deleted mid-session), encodings,
CSV, Arabic search folding, the Space state machine, focus rules, settings, error messages, the SVG sanitizer, `UserUnit`
parsing, and window sizing. `tests\Lamha.Tests`.

## Format results

Final regression: 37 files through the real preview window with 33 rendered and 4 refused as intended. Average time to
ready was 465 ms and the slowest 2.2 s (first conversion of an Office file). Screenshots were inspected by eye for the
formats below.

| Format | Result | Checked |
|---|---|---|
| PDF | Pass | Arabic, French, landscape, 500 pages/121 MB opens in 125 ms, thumbnails, zoom modes, French and Arabic search |
| PDF password | Pass | Prompt appears; opening with the right password was not exercised end to end |
| PDF corrupt | Pass | Arabic error message |
| DOCX, DOC | Pass | Headers, footers, tables, image, page break, mixed RTL/LTR |
| XLSX, XLS | Pass | Merged cells, borders, number formats, calculated values, column widths, sheet tabs, hidden sheet stays hidden in XLSX, 100,000 rows |
| PPTX, PPT | Pass | Arabic and French slides, images |
| PNG, JPG, WEBP, GIF, BMP, TIFF, SVG | Pass | Transparency, EXIF rotation, 12000×9000 image, first frame of animated GIF |
| TXT, MD, JSON, XML, HTML, CSV | Pass | UTF-8, UTF-16, Windows-1256, Windows-1252, RTL layout, highlighting, 41 MB file |
| MP4, MP3 | Pass | Opens and plays, timeline shows duration. Sound output and the fullscreen button were not checked |
| Unicode and Arabic filenames | Pass | Through the window; e2e covers Explorer |

## Security

| Test | Result |
|---|---|
| DOCX with remote image, XLSX with `WEBSERVICE`, SVG with remote image | Pass. A listener on the target port saw no connection; a control connection proved the listener worked |
| SVG with external entity (XXE) | Pass. Refused before parsing |
| SVG scripts, external CSS, file links | Pass (unit tests) |
| HTML | Shown as source; never executed |
| **Office macros** | **Inconclusive.** Macros are disabled by configuration, but the test document's macro did not fire even in a deliberately permissive control run, so the test proves nothing either way |
| Original files untouched | By construction: read-only opens, and Office files are copied before conversion |

## Packaging (final installer)

| Test | Result |
|---|---|
| Silent install, per-user, no administrator rights | Pass, 23 s |
| Files, Start Menu shortcut, Run key | Pass |
| No desktop shortcut by default | Pass |
| Runs with no .NET and no LibreOffice on the search path | Pass. Office conversion confirmed from the bundled engine in the log |
| Single instance | Pass |
| Uninstall removes files, Run key, shortcuts, settings, logs, cache; no process left | Pass |
| Interactive installer wizard (Arabic pages) | Not run; silent mode only |
| Clean machine with no development tools | **Not tested.** Windows Sandbox is unavailable here and there is no admin access. The nearest check was running with the search path stripped, but this machine still has system LibreOffice and .NET installed |

Sizes: installer 230 MB, installed 854 MB. At idle: 91 MB private memory, 6 MB working set after trimming, 0 ms CPU over 5 s.

## Manual checklist from the brief

| # | Test | Status |
|---|---|---|
| 1–4 | Open with Space, close with Space, Escape, X | Pass, except X, not exercised |
| 5–6 | Navigate multiple PDFs and images | Pass through the window; Explorer multi-select passed with text files |
| 7 | Word, Excel, PowerPoint | Pass |
| 8 | Arabic and French documents | Pass |
| 9 | Large documents | Pass |
| 10 | Unicode and Arabic filenames | Pass |
| 11 | Multiple Explorer windows, and tabs | Pass |
| 12 | Multiple monitors | **Not tested.** One monitor; mixed DPI also untested |
| 13 | Space works in other applications | Pass (Notepad) |
| 14 | Startup after a Windows restart | **Not tested.** The Run key is verified, but no restart was performed |
| 15 | Uninstall and cleanup | Pass |

Also not tested: the tray icon and its menu, printing (the print dialog and output), dragging to select PDF text,
Ctrl+wheel zoom, and double-click to open the original.

## Keyboard shortcuts and click-away closing

Most single-key shortcuts are tested through the real window with synthetic key events. Not exercised: `F` (open folder),
`F3`, `J`/`L` (seek), `Shift+R`, Ctrl+P and Ctrl+wheel.

## Splitting and extracting pages

Through the real window on copies of a PDF and a Word document: extracting the current page, splitting into one file per page,
repeat runs producing `(2)` and `(3)` names instead of overwriting, the originals unchanged byte for byte, each output a
single page with the right content, and no button on an Excel file. Unit tests cover the naming and the refusal to overwrite.
Not tested: a PDF whose permissions forbid extraction (the button should be hidden), and a read-only folder (an Arabic error
message should appear).

## Merging files

Through the real window: two PDFs, a Word file and an Excel file merged into one PDF of 6 pages in selection order, the
landscape page kept landscape, the Excel file skipped, a second merge named `(2)` instead of overwriting, and the originals
unchanged byte for byte. The button stayed hidden for one file, and for one PDF plus one Excel file. Unit tests cover order,
refusal to overwrite and naming. Pictures: JPEG, PNG with transparency, an EXIF-rotated photo, SVG, animated GIF, WebP and TIFF merged with a PDF into an 8-page, 239 KB PDF in selection order. Each picture filled a page of its own size, transparency showed as white, the photo was upright, and the GIF gave its first frame. Not tested: merging a password-protected or extraction-locked PDF (an Arabic error should appear).

## Known limitations

**Explorer**
- Space works in Explorer windows and on the desktop, not in the Open and Save dialogs of other programs.
- Space stays with Explorer for one second after a letter key, so type-to-find works. Previewing right after typing needs that pause.
- Explorer running as administrator cannot be previewed from the normal-rights app.
- A left or right click on Explorer or the desktop closes the preview. Alt+Tab, the taskbar and clicks in other programs leave it open by design. "Update preview automatically" turns the closing off; it is off by default for new installs, and a settings file saved earlier keeps its old value.
- If Explorer's selection cannot be matched to the active tab, the preview only opens when the choice is unambiguous.

**Documents**
- Text selection works within one page at a time.
- Some PDFs store Arabic in visual order. Search handles this, but copied text from those PDFs comes out reversed.
- In a workbook, search hits on another sheet are counted but the view does not switch to that sheet.
- Very tall sheets (tens of thousands of rows): an exact phrase near the bottom can be missed, while single words are found. Printing fits a tall sheet onto one page, so it becomes unreadably small.
- Legacy `.xls` hidden sheets are shown. They are hidden correctly in `.xlsx`.
- Password-protected Office files are not opened; a message explains this. A password-protected legacy `.doc` was not tested and may wait for the 2-minute timeout.
- Pages taken from a Word document are saved as PDF, not as Word files.
- Fonts missing from both the machine and the document are substituted, so layout can shift slightly from Word.
- Markdown and HTML are shown as highlighted source rather than rendered. HTML by design: running it would break the "no scripts" rule.
- Images over 8192 px on a side are decoded at reduced size to protect memory; the true dimensions are still shown.
- Text files over 64 MB show the first 64 MB, with a notice.
- PDFs over 4 GB are refused.
- Video codecs are whatever Windows has installed.

**Distribution**
- The installer is unsigned, so SmartScreen will warn until it is code-signed.
- Installed size is large because of the bundled LibreOffice. That is the cost of not requiring Office.
- Previewed Office documents remain as PDF copies in the local cache (up to 500 MB) until pruned or uninstalled.
- Toolbar icons use Segoe Fluent Icons, which ships with Windows 11, with a Segoe MDL2 fallback on Windows 10.

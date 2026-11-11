# Dependencies and licensing

Lamha's own code is under the MIT License (`LICENSE`). The components below keep their own licenses.

Everything shipped is free for commercial use. No component is GPL or AGPL, and none phones home.

| Component | Used for | Version | License | Shipped how |
|---|---|---|---|---|
| .NET runtime and WPF | Application | 10.0 | MIT | Inside the app folder (self-contained) |
| PDFium (`bblanchon.PDFium.Win32`) | PDF rendering | 158.0.8125 | Apache-2.0 and BSD-3-Clause; packaging MIT | `pdfium.dll` |
| LibreOffice | Office to PDF conversion | 26.8.1.1 | MPL-2.0 (some components LGPL/others, listed in its own notices) | Trimmed copy in `libreoffice\`, unmodified binaries, license files retained |
| AvalonEdit | Text viewer | 6.3.1.120 | MIT | NuGet |
| SkiaSharp | GIF, WebP and SVG rasterizing | 4.154.0 | MIT | NuGet |
| Svg.Skia | SVG parsing | 5.2.3 | MIT | NuGet |
| IBM Plex Sans Arabic | Interface font | Google Fonts copy | SIL OFL 1.1 (`licenses/IBM-Plex-Sans-Arabic-OFL.txt`) | Embedded resource |
| Segoe Fluent Icons | Toolbar glyphs | Windows | Part of Windows; not redistributed | Uses the system font |

Test-only: xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT).

## Obligations

- **MPL-2.0 (LibreOffice):** keep its license notices and say where the source is available
  (<https://www.libreoffice.org/download/source-code/>). The binaries are unmodified; removing files is not a source
  modification, so no source needs to be published.
- **OFL 1.1 (font):** the license stays with the font. The font may not be sold on its own, which bundling does not do.
  "Plex" is a reserved font name, so the font must not be modified and redistributed under that name.
- **Apache-2.0 (PDFium):** keep its notices. Copy the PDFium `LICENSE` into `licenses/` before any public release.

## Please verify before distributing commercially

These license entries come from my knowledge of the projects plus the font's own OFL file. They have not been read from
each package's bundled license text, and this is not legal advice.

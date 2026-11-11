# Building Lamha

## Requirements

- Windows 10 2004 or later, x64
- .NET 10 SDK
- Inno Setup 6 (`winget install JRSoftware.InnoSetup --source winget --scope user`)
- LibreOffice installed at `C:\Program Files\LibreOffice`, as the source of the bundled engine

## Commands

```powershell
dotnet build                                             # debug build
dotnet test tests\Lamha.Tests\Lamha.Tests.csproj         # unit tests
powershell -ExecutionPolicy Bypass -File build.ps1       # dist\Lamha-Setup-1.0.0.exe
powershell -ExecutionPolicy Bypass -File build.ps1 -SkipOffice    # reuse the trimmed LibreOffice from the last build
```

If the .NET SDK is installed per-user, set `DOTNET_ROOT` to its folder before running a debug build, because the debug
launcher looks for a machine-wide runtime. The published app needs neither.

`build.ps1` publishes a self-contained ReadyToRun build into `dist\app`, copies LibreOffice beside it and removes what
headless conversion does not need, then compiles `installer\Lamha.iss`.

For a smaller, slower-to-build installer, change `Compression` in `installer\Lamha.iss` to `lzma2/ultra64` and set
`SolidCompression=yes`.

## Releasing

1. Set the version in `src/Lamha/Lamha.csproj` and `installer/Lamha.iss`.
2. Run `build.ps1`.
3. Create a GitHub release and attach `dist\Lamha-Setup-<version>.exe`. It is about 230 MB, well under GitHub's 2 GB
   limit per release file. Do not commit `dist\`.

## Test tools

- `tests\Lamha.Harness`: drives the real window without a desktop. `snapshot OUT "file|Key|find:text"` saves PNGs,
  `selection HWND` and `probe HWND` query a live Explorer window, and `hook` injects a real key press.
- `tests\e2e-explorer.ps1`: the full Explorer flow with real key presses. It needs an unlocked session.

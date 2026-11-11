using System.Runtime.InteropServices;

namespace Lamha.Pdf;

static unsafe class Pdfium
{
    const string Dll = "pdfium";

    public const int ERR_FILE = 2, ERR_FORMAT = 3, ERR_PASSWORD = 4, ERR_SECURITY = 5;
    public const int FPDF_ANNOT = 0x01, FPDF_REVERSE_BYTE_ORDER = 0x10, BitmapBGRA = 4;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int GetBlock(nint param, uint position, byte* buffer, uint size);

    [StructLayout(LayoutKind.Sequential)]
    public struct FPDF_FILEACCESS { public uint Length; public nint Read; public nint Param; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int WriteBlock(FPDF_FILEWRITE* self, byte* data, uint size);

    [StructLayout(LayoutKind.Sequential)]
    public struct FPDF_FILEWRITE { public int Version; public nint WriteBlock; }

    public const uint FPDF_NO_INCREMENTAL = 2, PERM_MODIFY = 0x8, PERM_ASSEMBLE = 0x400;

    [DllImport(Dll)] public static extern void FPDF_InitLibrary();
    [DllImport(Dll)] public static extern nint FPDF_CreateNewDocument();
    [DllImport(Dll)] public static extern nint FPDFPage_New(nint doc, int index, double width, double height);
    [DllImport(Dll)] public static extern nint FPDFPageObj_NewImageObj(nint doc);
    [DllImport(Dll)] public static extern int FPDFImageObj_SetBitmap(nint* pages, int count, nint imageObject, nint bitmap);
    [DllImport(Dll)] public static extern void FPDFPageObj_Transform(nint pageObject, double a, double b, double c, double d, double e, double f);
    [DllImport(Dll)] public static extern void FPDFPage_InsertObject(nint page, nint pageObject);
    [DllImport(Dll)] public static extern int FPDFPage_GenerateContent(nint page);
    [DllImport(Dll)] public static extern int FPDF_ImportPagesByIndex(nint dest, nint src, int* indices, uint length, int index);
    [DllImport(Dll)] public static extern int FPDF_SaveAsCopy(nint doc, FPDF_FILEWRITE* writer, uint flags);
    [DllImport(Dll)] public static extern uint FPDF_GetDocPermissions(nint doc);
    [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern nint FPDF_LoadCustomDocument(FPDF_FILEACCESS* access, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);
    [DllImport(Dll)] public static extern void FPDF_CloseDocument(nint doc);
    [DllImport(Dll)] public static extern uint FPDF_GetLastError();
    [DllImport(Dll)] public static extern int FPDF_GetPageCount(nint doc);
    [DllImport(Dll)] public static extern int FPDF_GetPageSizeByIndex(nint doc, int index, out double width, out double height);
    [DllImport(Dll)] public static extern nint FPDF_LoadPage(nint doc, int index);
    [DllImport(Dll)] public static extern void FPDF_ClosePage(nint page);
    [DllImport(Dll)] public static extern nint FPDFBitmap_CreateEx(int w, int h, int format, byte* buffer, int stride);
    [DllImport(Dll)] public static extern void FPDFBitmap_Destroy(nint bitmap);
    [DllImport(Dll)] public static extern int FPDFBitmap_FillRect(nint bitmap, int x, int y, int w, int h, uint color);
    [DllImport(Dll)] public static extern void FPDF_RenderPageBitmap(nint bitmap, nint page, int x, int y, int w, int h, int rotate, int flags);
    [DllImport(Dll)] public static extern int FPDF_PageToDevice(nint page, int x, int y, int w, int h, int rotate, double px, double py, out int dx, out int dy);
    [DllImport(Dll)] public static extern int FPDF_DeviceToPage(nint page, int x, int y, int w, int h, int rotate, int dx, int dy, out double px, out double py);

    [DllImport(Dll)] public static extern nint FPDFText_LoadPage(nint page);
    [DllImport(Dll)] public static extern void FPDFText_ClosePage(nint text);
    [DllImport(Dll)] public static extern int FPDFText_CountChars(nint text);
    [DllImport(Dll)] public static extern int FPDFText_GetCharIndexAtPos(nint text, double x, double y, double xTol, double yTol);
    [DllImport(Dll)] public static extern int FPDFText_GetText(nint text, int start, int count, char* buffer);
    [DllImport(Dll)] public static extern int FPDFText_CountRects(nint text, int start, int count);
    [DllImport(Dll)] public static extern int FPDFText_GetRect(nint text, int index, out double left, out double top, out double right, out double bottom);

    [DllImport(Dll)] public static extern nint FPDFBookmark_GetFirstChild(nint doc, nint bookmark);
    [DllImport(Dll)] public static extern nint FPDFBookmark_GetNextSibling(nint doc, nint bookmark);
    [DllImport(Dll)] public static extern uint FPDFBookmark_GetTitle(nint bookmark, char* buffer, uint length);
    [DllImport(Dll)] public static extern nint FPDFBookmark_GetDest(nint doc, nint bookmark);
    [DllImport(Dll)] public static extern int FPDFDest_GetDestPageIndex(nint doc, nint dest);
}

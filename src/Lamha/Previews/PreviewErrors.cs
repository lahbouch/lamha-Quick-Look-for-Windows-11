using Lamha.Office;
using Lamha.Pdf;

namespace Lamha.Previews;

public static class PreviewErrors
{
    public static string Message(Exception e) => e switch
    {
        FileNotFoundException or DirectoryNotFoundException => "لم يعد هذا الملف موجودًا. ربما نُقل أو حُذف.",
        UnauthorizedAccessException => "لا تملك صلاحية الوصول إلى هذا الملف.",
        OfficeEngineMissingException => "محرك عرض مستندات Office غير موجود. أعد تثبيت لمحة لإصلاح ذلك.",
        OfficeConversionException => "تعذر تحويل هذا المستند. ربما يكون تالفًا أو محميًا بكلمة مرور.",
        PdfCorruptException => "هذا الملف تالف أو ليس بتنسيق PDF صالح.",
        IOException => "تعذرت قراءة الملف. ربما يستخدمه برنامج آخر.",
        _ => "تعذر عرض هذا الملف. ربما يكون تالفًا.",
    };
}

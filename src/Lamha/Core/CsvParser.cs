using System.Text;

namespace Lamha.Core;

public static class CsvParser
{
    public static char DetectDelimiter(string text)
    {
        int nl = text.IndexOf('\n');
        var head = nl < 0 ? text : text[..nl];
        return new[] { ',', ';', '\t', '|' }.MaxBy(d => head.Count(c => c == d));
    }

    public static List<string[]> Parse(string text, int maxRows)
    {
        char d = DetectDelimiter(text);
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;

        void EndRow()
        {
            row.Add(cell.ToString());
            cell.Clear();
            rows.Add(row.ToArray());
            row.Clear();
        }

        for (int i = 0; i < text.Length && rows.Count < maxRows; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c != '"') cell.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = false;
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == d) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n') EndRow();
            else if (c != '\r') cell.Append(c);
        }
        if ((cell.Length > 0 || row.Count > 0) && rows.Count < maxRows) EndRow();
        return rows;
    }
}

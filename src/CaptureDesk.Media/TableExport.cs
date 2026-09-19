using System.Data;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace CaptureDesk.Media;

public static class TableExport
{
    public static string ToTsv(DataTable table) => string.Join(Environment.NewLine, table.Rows.Cast<DataRow>()
        .Where(r => r.RowState != DataRowState.Deleted).Select(row => string.Join("\t", row.ItemArray.Select(x => (x?.ToString() ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ")))));
    public static void Save(DataTable table, string destination)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (Path.GetExtension(destination).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(temporary, string.Join(Environment.NewLine, table.Rows.Cast<DataRow>().Where(r => r.RowState != DataRowState.Deleted)
                    .Select(r => string.Join(",", r.ItemArray.Select(x => "\"" + SafeCsv(x?.ToString() ?? "").Replace("\"", "\"\"") + "\"")))), new UTF8Encoding(true));
            else
            {
                using var zip = ZipFile.Open(temporary, ZipArchiveMode.Create);
                void Add(string name, string xml) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(xml); }
                Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                Add("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Add("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"识别结果\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var rows = table.Rows.Cast<DataRow>().Where(r => r.RowState != DataRowState.Deleted).Select((row, i) => new XElement(ns + "row", new XAttribute("r", i + 1),
                    row.ItemArray.Select((value, column) => new XElement(ns + "c", new XAttribute("r", Column(column) + (i + 1)), new XAttribute("t", "inlineStr"),
                        new XElement(ns + "is", new XElement(ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value?.ToString() ?? ""))))));
                Add("xl/worksheets/sheet1.xml", new XElement(ns + "worksheet", new XElement(ns + "sheetData", rows)).ToString());
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string SafeCsv(string text) => text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+') || text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@') ? "'" + text : text;
    private static string Column(int index) { var name = ""; for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name; return name; }
}

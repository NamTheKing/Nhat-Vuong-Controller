using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using NhatVuong.Application;
using NhatVuong.Application.Timetables;

namespace NhatVuong.Adapters.Rest;

/// <summary>
/// Reads a timetable from CSV or Excel (FR-F2). The first row is the header naming the columns in
/// <see cref="TimetableImportService.Columns"/>; row numbers match what the administrator sees in the file.
/// </summary>
public static class TimetableFileReader
{
    public static async Task<IReadOnlyList<TimetableRow>> ReadAsync(Stream content, string fileName, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return extension switch
        {
            ".csv" => ReadCsv(buffer),
            ".xlsx" => ReadExcel(buffer),
            _ => throw new ValidationException("UnsupportedFileType", "Upload a .csv or .xlsx file."),
        };
    }

    private static List<TimetableRow> ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var records = ParseCsv(reader.ReadToEnd());
        if (records.Count == 0)
        {
            throw new ValidationException("EmptyFile", "The file is empty.");
        }

        var columns = MapHeader(records[0].Fields);
        return records.Skip(1)
            .Where(r => r.Fields.Any(f => !string.IsNullOrWhiteSpace(f)))
            .Select(r => ToRow(r.Line, i => i < r.Fields.Count ? r.Fields[i] : null, columns))
            .ToList();
    }

    private static List<TimetableRow> ReadExcel(Stream stream)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ValidationException("UnreadableFile", "The Excel file could not be read.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new ValidationException("EmptyFile", "The workbook has no sheets.");
            var used = sheet.RangeUsed();
            if (used is null)
            {
                throw new ValidationException("EmptyFile", "The sheet is empty.");
            }

            var firstRow = used.FirstRow().RowNumber();
            var lastRow = used.LastRow().RowNumber();
            var lastColumn = used.LastColumn().ColumnNumber();
            var header = Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(firstRow, c).GetFormattedString()).ToList();
            var columns = MapHeader(header);

            var rows = new List<TimetableRow>();
            for (var r = firstRow + 1; r <= lastRow; r++)
            {
                var rowNumber = r;
                string? Cell(int index)
                {
                    var cell = sheet.Cell(rowNumber, index + 1);
                    return cell.Value.IsDateTime
                        ? cell.Value.GetDateTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : cell.GetFormattedString();
                }

                if (Enumerable.Range(0, lastColumn).All(i => string.IsNullOrWhiteSpace(Cell(i))))
                {
                    continue;
                }

                rows.Add(ToRow(rowNumber, Cell, columns));
            }

            return rows;
        }
    }

    private static Dictionary<string, int> MapHeader(IReadOnlyList<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim().TrimStart('﻿');
            if (name.Length > 0)
            {
                map.TryAdd(name, i);
            }
        }

        var missing = TimetableImportService.Columns.Where(c => !map.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationException("MissingColumns", $"Missing column(s): {string.Join(", ", missing)}. Expected: {string.Join(", ", TimetableImportService.Columns)}.");
        }

        return map;
    }

    private static TimetableRow ToRow(int rowNumber, Func<int, string?> cell, Dictionary<string, int> columns) => new(
        rowNumber,
        cell(columns["timetable_id"]),
        cell(columns["room_code"]),
        cell(columns["lecturer_email"]),
        cell(columns["starts_at"]),
        cell(columns["ends_at"]));

    /// <summary>RFC 4180: quoted fields, doubled quotes, commas and newlines inside quotes. Line = file line where the record starts.</summary>
    private static List<(int Line, List<string> Fields)> ParseCsv(string text)
    {
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add((recordLine, fields));
                    fields = [];
                    line++;
                    recordLine = line;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add((recordLine, fields));
        }

        return records;
    }
}

using System.Globalization;
using System.Text;
using Microsoft.JSInterop;
using VFMS.Contracts.Common;

namespace VFMS.Client.Services;

/// <summary>
/// CSV files that Excel opens correctly: UTF-8 with a byte order mark so Vietnamese text shows, comma separated,
/// quoted where needed, and text that could run as a formula ("=", "+", "-", "@") made harmless.
/// </summary>
public static class CsvFile
{
    /// <summary>Most rows one export collects, so a very large list cannot freeze the browser.</summary>
    public const int MaxRows = 5000;
    private const int PageSize = 100;

    public static byte[] Build(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var csv = new StringBuilder();
        AppendRow(csv, header);
        foreach (var row in rows) AppendRow(csv, row);
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    public static void AppendRow(StringBuilder csv, IEnumerable<string?> cells)
    {
        csv.Append(string.Join(',', cells.Select(cell => Escape(cell ?? ""))));
        csv.Append("\r\n");
    }

    /// <summary>A plain number Excel can calculate with (no grouping, "." for decimals).</summary>
    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A date and time in the browser's time zone, e.g. "19/11/2026 14:05".</summary>
    public static string Time(DateTime? value) =>
        value?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "";

    public static string Date(DateOnly? value) => value?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";

    /// <summary>A file name with today's date, e.g. "VFMS-thiet-bi-2026-11-19.csv".</summary>
    public static string FileName(string name, DateTime? today = null) =>
        $"VFMS-{name}-{(today ?? DateTime.Now):yyyy-MM-dd}.csv";

    /// <summary>
    /// Reads every page of a list with the current filters, 100 rows at a time, up to <see cref="MaxRows"/>.
    /// Returns the rows and whether the list had more than that.
    /// </summary>
    public static async Task<(IReadOnlyList<T> Rows, bool Truncated)> CollectAsync<T>(Func<int, int, Task<PagedResponse<T>>> fetchPage)
    {
        var rows = new List<T>();
        for (var page = 1; ; page++)
        {
            var result = await fetchPage(page, PageSize);
            rows.AddRange(result.Items);
            if (rows.Count >= MaxRows)
                return (rows.Take(MaxRows).ToList(), result.TotalCount > MaxRows);
            if (result.Items.Count < PageSize || rows.Count >= result.TotalCount)
                return (rows, false);
        }
    }

    /// <summary>Hands the file to the browser as a download (the shared helper loaded by index.html).</summary>
    public static async Task DownloadAsync(IJSRuntime js, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(content);
        using var reference = new DotNetStreamReference(stream);
        await js.InvokeVoidAsync("vfmsInvoices.download", cancellationToken, fileName, "text/csv", reference);
    }

    private static string Escape(string cell)
    {
        if (cell.Length > 0 && "=+-@".Contains(cell[0]) && !decimal.TryParse(cell, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            cell = "'" + cell;
        return cell.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;
    }
}

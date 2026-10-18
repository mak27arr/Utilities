namespace Utilities.Core.BusinessLogic;

using System.Globalization;

public enum ReadingCsvErrorCode { InvalidColumns, InvalidDate, InvalidValue, CalculationOverflow, DuplicateDate, FutureDate, AfterMeterReplacement, LowerThanPreviousReading, MalformedRow }

public sealed class ReadingCsvRowException(int lineNumber, ReadingCsvErrorCode errorCode, string? detail = null)
    : FormatException(detail ?? $"CSV line {lineNumber} is invalid ({errorCode}).")
{
    public int LineNumber { get; } = lineNumber;
    public ReadingCsvErrorCode ErrorCode { get; } = errorCode;
}

public sealed record ImportedMeterReading(DateTime Date, double Value, int LineNumber = 0);

public static class ReadingCsvParser
{
    public static IReadOnlyList<ImportedMeterReading> Parse(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n').Select((text, index) => (Text: text.Trim(), LineNumber: index + 1))
            .Where(item => item.Text.Length > 0).ToArray();
        if (lines.Length == 0) throw new FormatException("The CSV file is empty.");

        var delimiter = DetectDelimiter(lines[0].Text);
        var readings = new List<ImportedMeterReading>();
        for (var index = 0; index < lines.Length; index++)
        {
            var (line, lineNumber) = lines[index];
            IReadOnlyList<string> fields;
            try { fields = SplitLine(line, delimiter); }
            catch (FormatException exception) { throw new ReadingCsvRowException(lineNumber, ReadingCsvErrorCode.MalformedRow, exception.Message); }
            if (fields.Count != 2)
            {
                if (index == 0 && LooksLikeHeader(fields)) continue;
                throw new ReadingCsvRowException(lineNumber, ReadingCsvErrorCode.InvalidColumns);
            }

            if (!DateTime.TryParseExact(fields[0].Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                if (index == 0 && LooksLikeHeader(fields)) continue;
                throw new ReadingCsvRowException(lineNumber, ReadingCsvErrorCode.InvalidDate);
            }

            var valueText = fields[1].Trim();
            var parsed = double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || (delimiter == ';' && (double.TryParse(valueText, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                    || double.TryParse(valueText, NumberStyles.Float, CultureInfo.GetCultureInfo("uk-UA"), out value)));
            if (!parsed || !double.IsFinite(value) || value < 0)
                throw new ReadingCsvRowException(lineNumber, ReadingCsvErrorCode.InvalidValue);

            readings.Add(new ImportedMeterReading(date.Date, value, lineNumber));
        }

        if (readings.Count == 0) throw new FormatException("The CSV file contains no readings.");
        var duplicateDate = readings.GroupBy(item => item.Date).FirstOrDefault(group => group.Count() > 1);
        if (duplicateDate is not null)
            throw new ReadingCsvRowException(duplicateDate.Last().LineNumber, ReadingCsvErrorCode.DuplicateDate);
        return readings.OrderBy(item => item.Date).ToArray();
    }

    private static char DetectDelimiter(string line) => line.Contains(';') ? ';' : ',';

    private static bool LooksLikeHeader(IReadOnlyList<string> fields)
    {
        if (fields.Count != 2) return false;
        var dateColumn = fields[0].Trim();
        var valueColumn = fields[1].Trim();
        return dateColumn.Equals("date", StringComparison.OrdinalIgnoreCase) ||
               dateColumn.Equals("дата", StringComparison.OrdinalIgnoreCase)
            ? valueColumn.Equals("reading", StringComparison.OrdinalIgnoreCase) ||
              valueColumn.Equals("value", StringComparison.OrdinalIgnoreCase) ||
              valueColumn.Equals("показання", StringComparison.OrdinalIgnoreCase) ||
              valueColumn.Equals("показник", StringComparison.OrdinalIgnoreCase) ||
              valueColumn.Equals("значення", StringComparison.OrdinalIgnoreCase)
            : false;
    }

    private static IReadOnlyList<string> SplitLine(string line, char delimiter)
    {
        var fields = new List<string>(2);
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (character == delimiter && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else field.Append(character);
        }
        if (quoted) throw new FormatException("The CSV file contains an unclosed quoted value.");
        fields.Add(field.ToString());
        return fields;
    }
}

namespace CrmApi.Dtos;

public record CsvImportPreviewRequest(string CsvContent);

// previewRows are keyed by raw CSV header text (no field mapping applied yet)
// — the frontend uses Headers to build the column-mapping UI and PreviewRows
// to show the user what they're about to map, per the csv-import-dedupe
// skill's "parse & preview" step.
public record CsvImportPreviewResponse(List<string> Headers, List<Dictionary<string, string>> PreviewRows, int TotalRows);

// ColumnMapping keys are the fixed importable Contact fields ("email",
// "firstName", "lastName", "phone", "companyName", "companyDomain"); values
// are the CSV header text to read that field from. A field absent from the
// mapping is treated as not provided by this import.
public record CsvImportRequest(string CsvContent, Dictionary<string, string> ColumnMapping);

public record CsvImportResponse(string JobId);

// Row is the 1-based CSV line number (header is line 1) so it matches what a
// user sees opening the file in a spreadsheet app.
public record ImportRowError(int Row, string Message);

public record ContactImportResult(int TotalRows, int Created, int Updated, int Skipped, List<ImportRowError> Errors);

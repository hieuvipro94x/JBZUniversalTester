using System.IO;
using System.Text;
using System.Text.Json;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public static class ErrorLogService
{
    public static void SaveIfEnabled(
        ProductionSettings settings,
        ProductModel model,
        long lotNo,
        CompletedTestResult result)
    {
        if (!settings.AutoSaveErrors || result.Passed)
            return;

        FaultDetail[] errors = result.Faults.ToArray();
        int minimum = Math.Max(0, settings.MinimumErrorLogValue);
        if (errors.Length < minimum)
            return;

        string directory = Path.Combine(AppContext.BaseDirectory, "Data", "ErrorLogs");
        Directory.CreateDirectory(directory);

        string safeModel = string.Concat((model.ModelName ?? "MODEL")
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        string resultTag = result.PrimaryFault?.Code ?? result.ResultText;
        string path = Path.Combine(
            directory,
            $"{result.Finished:yyyyMMdd_HHmmss}_{safeModel}_LOT{lotNo}_{Sanitize(resultTag)}.json");

        var payload = new
        {
            Timestamp = result.Finished,
            Model = model.ModelName,
            ModelFile = model.SourcePath,
            LotNo = lotNo,
            Result = result.ResultText,
            PrimaryFault = result.PrimaryFault,
            ErrorCount = errors.Length,
            Errors = errors,
            Resistance = result.Resistance
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    private static string Sanitize(string? text)
    {
        string value = string.IsNullOrWhiteSpace(text) ? "FAIL" : text.Trim();
        return string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    }
}

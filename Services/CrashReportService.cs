using System.IO;
using System.Text;
using JBZUniversalTester.Versioning;

namespace JBZUniversalTester.Services;

/// <summary>
/// Minimal crash-safe writer. The Crash directory and RPT are created lazily,
/// only when an unhandled exception reaches the application boundary.
/// </summary>
public static class CrashReportService
{
    private static readonly object Gate = new();

    public static void Write(Exception exception, string source, string? runtimeContext = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var report = new StringBuilder()
                .AppendLine("============================================================")
                .Append("Crash Time: ").AppendLine(DateTime.Now.ToString("O"))
                .Append("AppVersion: ").AppendLine(AppVersion.DisplayVersion)
                .Append("Source: ").AppendLine(source ?? string.Empty)
                .Append("ExceptionType: ").AppendLine(exception.GetType().FullName ?? exception.GetType().Name)
                .Append("ExceptionMessage: ").AppendLine(exception.Message)
                .Append("StackTrace: ").AppendLine(exception.StackTrace ?? string.Empty)
                .Append("InnerException: ").AppendLine(exception.InnerException?.ToString() ?? string.Empty)
                .Append("MachineName: ").AppendLine(Environment.MachineName)
                .Append("OSVersion: ").AppendLine(Environment.OSVersion.ToString())
                .Append("ProcessArchitecture: ").AppendLine(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString())
                .Append("RuntimeContext: ").AppendLine(runtimeContext ?? string.Empty)
                .AppendLine();

            lock (Gate)
            {
                Directory.CreateDirectory(RuntimePaths.CrashDirectory);
                File.AppendAllText(
                    RuntimePaths.CrashReportFile,
                    report.ToString(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // A crash reporter cannot safely throw back into an unhandled
            // exception boundary. The main logger remains the secondary path.
        }
    }
}

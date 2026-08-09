using System.IO;
using System.Text.Json;
using JBZUniversalTester.Models;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.Services;

public sealed class ResultStore
{
    private readonly string _path;

    public ResultStore(string path)
    {
        _path = path;

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = Open();
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Results
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Started TEXT NOT NULL,
                Finished TEXT NOT NULL,
                Model TEXT NOT NULL,
                Barcode TEXT,
                Passed INTEGER NOT NULL,
                OpenCount INTEGER NOT NULL,
                WrongCount INTEGER NOT NULL,
                ShortCount INTEGER NOT NULL,
                ResistanceJson TEXT
            );
            """;

        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        return new SqliteConnection(
            $"Data Source={_path}"
        );
    }

    public void Save(TestSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        using var connection = Open();
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO Results
            (
                Started,
                Finished,
                Model,
                Barcode,
                Passed,
                OpenCount,
                WrongCount,
                ShortCount,
                ResistanceJson
            )
            VALUES
            (
                $started,
                $finished,
                $model,
                $barcode,
                $passed,
                $openCount,
                $wrongCount,
                $shortCount,
                $resistanceJson
            );
            """;

        command.Parameters.AddWithValue(
            "$started",
            summary.Started.ToString("O")
        );

        command.Parameters.AddWithValue(
            "$finished",
            summary.Finished.ToString("O")
        );

        command.Parameters.AddWithValue(
            "$model",
            summary.Model ?? string.Empty
        );

        command.Parameters.AddWithValue(
            "$barcode",
            summary.Barcode ?? string.Empty
        );

        command.Parameters.AddWithValue(
            "$passed",
            summary.Passed ? 1 : 0
        );

        command.Parameters.AddWithValue(
            "$openCount",
            summary.OpenCount
        );

        command.Parameters.AddWithValue(
            "$wrongCount",
            summary.WrongCount
        );

        command.Parameters.AddWithValue(
            "$shortCount",
            summary.ShortCount
        );

        command.Parameters.AddWithValue(
            "$resistanceJson",
            JsonSerializer.Serialize(summary.Resistance)
        );

        command.ExecuteNonQuery();
    }
}
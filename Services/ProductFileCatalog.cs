using System.IO;

namespace JBZUniversalTester.Services;

public sealed record ProductFileEntry(string Name, string Folder, string FullPath);
public sealed record ProductFileCatalogResult(IReadOnlyList<ProductFileEntry> Files, IReadOnlyList<string> Errors);

public static class ProductFileCatalog
{
    public static bool Matches(ProductFileEntry file, string? search) =>
        file.Name.Contains(search?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    public static ProductFileCatalogResult Read(string root, CancellationToken ct = default)
    {
        root = Path.GetFullPath(root);
        var files = new List<ProductFileEntry>();
        var errors = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (string path in Directory.EnumerateFiles(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Path.GetExtension(path).Equals(".tht", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string folder = Path.GetRelativePath(root, directory);
                    files.Add(new ProductFileEntry(Path.GetFileNameWithoutExtension(path),
                        folder == "." ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) : folder,
                        path));
                }
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    // Do not follow junctions/symlinks back into an ancestor or outside ITEM.
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    {
                        errors.Add($"Không quét thư mục liên kết: {child}");
                        continue;
                    }
                    pending.Push(child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Không đọc được {directory}: {ex.Message}");
            }
        }
        return new ProductFileCatalogResult(files
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase).ToArray(), errors);
    }
}

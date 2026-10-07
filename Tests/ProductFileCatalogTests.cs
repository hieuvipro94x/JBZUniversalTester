using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestRecursiveProductCatalogSearch()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-ITEM-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "MX5", "HALMP"));
        Directory.CreateDirectory(Path.Combine(root, "OTHER"));
        try
        {
            File.WriteAllText(Path.Combine(root, "WH322285.tht"), "");
            File.WriteAllText(Path.Combine(root, "MX5", "285ABC.THT"), "");
            File.WriteAllText(Path.Combine(root, "MX5", "HALMP", "XX285YY.tht"), "");
            File.WriteAllText(Path.Combine(root, "OTHER", "WH322285.tht"), "");
            File.WriteAllText(Path.Combine(root, "OTHER", "UNRELATED.tht"), "");
            File.WriteAllText(Path.Combine(root, "ignore.json"), "");
            ProductFileCatalogResult catalog = ProductFileCatalog.Read(root);
            Assert(catalog.Files.Count == 5 && catalog.Errors.Count == 0,
                "Catalog includes root and every nested folder, including uppercase THT, and excludes other formats");
            Assert(catalog.Files.Select(file => file.Name).SequenceEqual(
                new[] { "285ABC", "UNRELATED", "WH322285", "WH322285", "XX285YY" }),
                "Catalog has a stable alphabetical product-name order across folders");
            ProductFileEntry[] matches = catalog.Files.Where(file => ProductFileCatalog.Matches(file, " 285 ")).ToArray();
            Assert(matches.Length == 4,
                "Typing a partial suffix, prefix or middle finds all matching product files");
            Assert(catalog.Files.Count(file => ProductFileCatalog.Matches(file, "wh322")) == 2 &&
                   catalog.Files.Count(file => ProductFileCatalog.Matches(file, "285YY")) == 1 &&
                   catalog.Files.Count(file => ProductFileCatalog.Matches(file, "")) == 5 &&
                   !catalog.Files.Any(file => ProductFileCatalog.Matches(file, "NO-MATCH")),
                "Search is case-insensitive, trims whitespace, supports arbitrary substrings and has no-match/empty behavior");
            Assert(matches.Where(file => file.Name == "WH322285").Select(file => file.FullPath).Distinct().Count() == 2 &&
                   matches.Any(file => file.Folder == Path.Combine("MX5", "HALMP")),
                "Same-name products remain separate with their exact paths and nested folders");
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            AssertThrows<OperationCanceledException>(() => ProductFileCatalog.Read(root, cancel.Token),
                "Closing the picker can cancel catalog enumeration");
            Assert(ProductFileCatalog.Read(Path.Combine(root, "missing")).Errors.Count == 1,
                "An inaccessible or missing root is reported instead of silently presenting a complete catalog");
        }
        finally
        {
            Directory.Delete(root, recursive: true); // Only this generated temporary fixture.
        }
    }
}

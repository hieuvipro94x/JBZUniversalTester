using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JBZUniversalTester.Views;

public partial class ProductFilePickerWindow : Window
{
    private string _currentDirectory;

    public string? SelectedFilePath { get; private set; }

    public ProductFilePickerWindow(string? initialDirectory)
    {
        InitializeComponent();
        _currentDirectory = ResolveInitialDirectory(initialDirectory);
        LoadDirectory(_currentDirectory);
    }

    private static string ResolveInitialDirectory(string? initialDirectory)
    {
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            return Path.GetFullPath(initialDirectory);

        return Environment.CurrentDirectory;
    }

    private void LoadDirectory(string directory)
    {
        try
        {
            string fullPath = Path.GetFullPath(directory);
            var entries = new List<PickerEntry>();

            entries.AddRange(
                Directory.EnumerateDirectories(fullPath)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                    .Select(path => new PickerEntry(path, isDirectory: true)));

            entries.AddRange(
                Directory.EnumerateFiles(fullPath)
                    .Where(IsSupportedProductFile)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                    .Select(path => new PickerEntry(path, isDirectory: false)));

            _currentDirectory = fullPath;
            DirectoryTextBox.Text = fullPath;
            FileList.ItemsSource = entries;
            FileList.SelectedItem = null;
            SelectedFileTextBox.Text = string.Empty;
            OpenButton.IsEnabled = false;
            StatusText.Text = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StatusText.Text = $"Không thể mở thư mục: {ex.Message}";
        }
    }

    private static bool IsSupportedProductFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".tht", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".model", StringComparison.OrdinalIgnoreCase);
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        DirectoryInfo? parent = Directory.GetParent(_currentDirectory);
        if (parent is not null)
            LoadDirectory(parent.FullName);
    }

    private void GoButton_Click(object sender, RoutedEventArgs e) =>
        LoadDirectory(DirectoryTextBox.Text.Trim());

    private void DirectoryTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        LoadDirectory(DirectoryTextBox.Text.Trim());
        e.Handled = true;
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is PickerEntry { IsDirectory: false } selected)
        {
            SelectedFileTextBox.Text = selected.DisplayName;
            OpenButton.IsEnabled = true;
            return;
        }

        SelectedFileTextBox.Text = string.Empty;
        OpenButton.IsEnabled = false;
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        OpenSelectedEntry();

    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        OpenSelectedEntry();
        e.Handled = true;
    }

    private void OpenSelectedEntry()
    {
        if (FileList.SelectedItem is not PickerEntry selected)
            return;

        if (selected.IsDirectory)
        {
            LoadDirectory(selected.FullPath);
            return;
        }

        AcceptFile(selected.FullPath);
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is PickerEntry { IsDirectory: false } selected)
            AcceptFile(selected.FullPath);
    }

    private void AcceptFile(string filePath)
    {
        if (!File.Exists(filePath) || !IsSupportedProductFile(filePath))
        {
            StatusText.Text = "Chỉ có thể chọn file mã hàng .tht hoặc .model.";
            return;
        }

        SelectedFilePath = filePath;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedFilePath = null;
        DialogResult = false;
    }

    private void ProductFilePickerWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DialogResult != true)
            SelectedFilePath = null;
    }

    private sealed class PickerEntry
    {
        public PickerEntry(string fullPath, bool isDirectory)
        {
            FullPath = fullPath;
            IsDirectory = isDirectory;
        }

        public string FullPath { get; }
        public bool IsDirectory { get; }
        public string DisplayName => Path.GetFileName(FullPath);
        public string TypeText => IsDirectory ? "Thư mục" : Path.GetExtension(FullPath).ToLowerInvariant();
    }
}

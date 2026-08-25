using System.Reflection;

// Metadata bổ sung cho assembly. Các giá trị Version/AssemblyVersion/FileVersion/Product
// được MSBuild sinh tự động từ Version.props để chỉ có MỘT nguồn version duy nhất.
[assembly: AssemblyMetadata("VersionManagement", "Version.props")]
[assembly: AssemblyMetadata("VersionPolicy", "Increment release version for every source revision")]
[assembly: AssemblyMetadata("ReleaseFamily", "V16.0")]

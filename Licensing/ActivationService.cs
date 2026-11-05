using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using JBZ.Licensing;

namespace JBZUniversalTester.Licensing;

public sealed class ActivationService
{
    private readonly string _directory;
    private readonly Func<string> _machineIdentity;
    private readonly string _publicKey;
    private readonly object _gate = new();
    private Task<RegistrationRequest>? _requestTask;
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("JBZUniversalTester.Licensing.V1");
    public ActivationService(string? directory=null, Func<string>? machineIdentity=null)
        : this(directory, machineIdentity, LicenseProtocol.PublicKey) { }
    internal ActivationService(string? directory, Func<string>? machineIdentity, string publicKey)
    {
        _publicKey=publicKey;
        _directory=directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"JBZUniversalTester","Licensing");
        _machineIdentity=machineIdentity ?? ReadMachineIdentity;
    }
    public bool HasReceipt => File.Exists(Path.Combine(_directory,"activation.dat"));
    public Task<RegistrationRequest> GetRequestAsync()
    {
        lock(_gate)
        {
            if (_requestTask is null || _requestTask.IsFaulted || _requestTask.IsCanceled)
                _requestTask=Task.Run(CreateRequest);
            return _requestTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    private RegistrationRequest CreateRequest()
    {
        // Read-only OS/disk identity is acquired once per process, outside the UI and IO scan.
        string identity=_machineIdentity();
        Directory.CreateDirectory(_directory);
        string installation;
        string path=Path.Combine(_directory,"installation.dat");
        try
        {
            installation=Encoding.UTF8.GetString(ProtectedData.Unprotect(ReadBounded(path, 4096),Entropy,DataProtectionScope.LocalMachine));
            if (!Guid.TryParseExact(installation,"N",out _)) throw new CryptographicException("Installation record invalid.");
        }
        catch(Exception ex) when(ex is IOException or CryptographicException)
        {
            // A clean Windows install cannot unseal the previous installation. A
            // fresh nonce invalidates its old grant instead of accepting a copied CFG.
            installation=Guid.NewGuid().ToString("N");
            WriteAtomic(path,ProtectedData.Protect(Encoding.UTF8.GetBytes(installation),Entropy,DataProtectionScope.LocalMachine));
        }
        return new(1,LicenseProtocol.Product,installation,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))),Environment.MachineName);
    }
    public async Task<bool> IsActivatedAsync()
    {
        if (!HasReceipt) return false;
        var request=await GetRequestAsync().ConfigureAwait(false);
        return await Task.Run(()=>
        {
            try
            {
                byte[] blob=ReadBounded(Path.Combine(_directory,"activation.dat"), 65536);
                if (blob.Length>65536) return false;
                byte[] plain=ProtectedData.Unprotect(blob,Entropy,DataProtectionScope.LocalMachine);
                try { LicenseProtocol.Verify(Encoding.UTF8.GetString(plain),request,_publicKey); return true; }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            catch(Exception ex) when(ex is IOException or CryptographicException or FormatException or JsonException) {return false;}
        }).ConfigureAwait(false);
    }
    public async Task ActivateAsync(string code)
    {
        var request=await GetRequestAsync().ConfigureAwait(false);
        await Task.Run(()=>
        {
            code=LicenseProtocol.NormalizeCode(code);
            LicenseProtocol.Verify(code,request,_publicKey); // Never trust an embedded or user-selected verification key.
            byte[] plain=Encoding.UTF8.GetBytes(code);
            try {WriteAtomic(Path.Combine(_directory,"activation.dat"),ProtectedData.Protect(plain,Entropy,DataProtectionScope.LocalMachine));}
            finally {CryptographicOperations.ZeroMemory(plain);}
        }).ConfigureAwait(false);
    }
    private static byte[] ReadBounded(string path,int limit)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>limit)throw new IOException("Dữ liệu kích hoạt vượt kích thước cho phép.");
        byte[] data=new byte[(int)file.Length];file.ReadExactly(data);return data;
    }
    private static void WriteAtomic(string path,byte[] data)
    {
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){stream.Write(data);stream.Flush(true);}File.Move(temporary,path,true);}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static string ReadMachineIdentity()
    {
        string drive=(Path.GetPathRoot(Environment.SystemDirectory) ?? throw new IOException("Không xác định được ổ Windows.")).TrimEnd('\\');
        var serials=new SortedSet<string>(StringComparer.Ordinal);
        using(var logical=new ManagementObject("Win32_LogicalDisk.DeviceID='"+drive+"'"))
        using(var partitions=logical.GetRelated("Win32_DiskPartition"))
        foreach(ManagementObject partition in partitions)
        using(partition)
        using(var disks=partition.GetRelated("Win32_DiskDrive"))
        foreach(ManagementObject disk in disks)
        using(disk)
        {
            string serial=(disk["SerialNumber"] as string ?? "").Trim().ToUpperInvariant();
            if(serial.Length>0)serials.Add(serial);
        }
        using var registry=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);
        using var crypto=registry.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        string windows=(crypto?.GetValue("MachineGuid") as string ?? "").Trim().ToUpperInvariant();
        if(serials.Count==0 || windows.Length==0)
            throw new IOException("Chưa đọc được thông tin ổ hệ thống hoặc Windows. Kiểm tra dịch vụ WMI/driver ổ đĩa rồi bấm THỬ LẠI.");
        return "JBZ-V1|"+windows+"|"+string.Join("|",serials);
    }
}

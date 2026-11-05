using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JBZ.Licensing;
namespace JBZLicenseIssuer;
public sealed record Issuance(string LicenseId,string Computer,string Customer,string Station,string Notes,string RequestCode,string ActivationCode,DateTimeOffset IssuedAtUtc,DateTimeOffset? ConfirmedAtUtc=null);
public sealed class Vault
{
 public int Version {get;set;}=1;
 public string PrivateKey {get;set;}="";
 public List<Issuance> Records {get;set;}=new();
}
public sealed class IssuerStore
{
 public static string VaultDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JBZLicenseIssuer","PrivateVault");
 private static string VaultPath=>Path.Combine(VaultDirectory,"issuer.dat");
 private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("JBZLicenseIssuer.PrivateVault.V1");
 private Vault _vault;
 private IssuerStore(Vault vault){_vault=vault;}
 public IReadOnlyList<Issuance> Records=>_vault.Records;
 public static IssuerStore? Open()
 {
  if(!File.Exists(VaultPath))return null;
  byte[] plain=ProtectedData.Unprotect(ReadBounded(VaultPath),Entropy,DataProtectionScope.CurrentUser);
  try {var v=JsonSerializer.Deserialize<Vault>(plain)??throw new FormatException("Kho cấp phép trống.");ValidateKey(v);return new(v);}
  finally{CryptographicOperations.ZeroMemory(plain);}
 }
 internal static ECDsa Import(Vault v)
 {
  byte[] bytes=Convert.FromBase64String(v.PrivateKey);var key=ECDsa.Create();
  try{key.ImportPkcs8PrivateKey(bytes,out int consumed);if(consumed!=bytes.Length||key.KeySize!=256)throw new CryptographicException("Khóa không hợp lệ.");return key;}
  catch{key.Dispose();throw;}finally{CryptographicOperations.ZeroMemory(bytes);}
 }
 private static void ValidateKey(Vault v)
 {
  if(v.Version!=1||v.Records is null)throw new FormatException("Phiên bản kho không hỗ trợ.");
  using var key=Import(v);
  if(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())!=LicenseProtocol.PublicKey)throw new CryptographicException("Bộ cấp phép không khớp khóa công khai của UniversalTester này.");
 }
 internal static string Sign(RegistrationRequest request,ECDsa key,string customer,string station,out LicenseGrant grant)
 {
  grant=new(1,LicenseProtocol.Product,Guid.NewGuid().ToString("N"),request.InstallationId,request.MachineHash,request.ComputerName,customer,station,DateTimeOffset.UtcNow,LicenseProtocol.Developer);
  string body=LicenseProtocol.Encode(JsonSerializer.SerializeToUtf8Bytes(grant));
  byte[] signature=key.SignData(Encoding.ASCII.GetBytes("JBZL1."+body),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
  return "JBZL1."+body+"."+LicenseProtocol.Encode(signature);
 }
 public Issuance Issue(string requestCode,string customer,string station,string notes)
 {
  var request=LicenseProtocol.DecodeRequest(requestCode);using var key=Import(_vault);
  string code=Sign(request,key,customer.Trim(),station.Trim(),out var grant);
  var record=new Issuance(grant.LicenseId,request.ComputerName,customer.Trim(),station.Trim(),notes.Trim(),LicenseProtocol.EncodeRequest(request),code,grant.IssuedAtUtc);
  var next=new Vault{PrivateKey=_vault.PrivateKey,Records=_vault.Records.Append(record).ToList()};Persist(next);_vault=next;return record;
 }
 public void Confirm(string id)
 {
  var next=new Vault{PrivateKey=_vault.PrivateKey,Records=_vault.Records.Select(r=>r.LicenseId==id?r with{ConfirmedAtUtc=DateTimeOffset.UtcNow}:r).ToList()};Persist(next);_vault=next;
 }
 public void ExportWorkspace(string path,string password)=>WriteAtomic(path,Encrypt(JsonSerializer.SerializeToUtf8Bytes(_vault),password));
 public static IssuerStore ImportWorkspace(string path,string password)
 {
  byte[] plain=Decrypt(ReadBounded(path),password);
  try
  {
   var vault=JsonSerializer.Deserialize<Vault>(plain)??throw new FormatException("Bộ cấp phép trống.");ValidateKey(vault);
   var current=Open();
   if(current is not null)vault.Records=vault.Records.Concat(current.Records).GroupBy(r=>r.LicenseId).Select(g=>g.OrderByDescending(r=>r.ConfirmedAtUtc).First()).ToList();
   Persist(vault);return new(vault);
  }
  finally{CryptographicOperations.ZeroMemory(plain);}
 }
 internal static byte[] Encrypt(byte[] plain,string password)
 {
  try
  {
   if(password.Length<12)throw new ArgumentException("Mật khẩu xuất bộ cấp phép cần ít nhất 12 ký tự.");
   byte[] salt=RandomNumberGenerator.GetBytes(16),nonce=RandomNumberGenerator.GetBytes(12),tag=new byte[16],cipher=new byte[plain.Length];
   byte[] key=Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA256,32);
   try{using var aes=new AesGcm(key,16);aes.Encrypt(nonce,plain,cipher,tag,Encoding.ASCII.GetBytes("JBZVAULT1"));}
   finally{CryptographicOperations.ZeroMemory(key);}
   return Encoding.ASCII.GetBytes("JBZVAULT1").Concat(salt).Concat(nonce).Concat(tag).Concat(cipher).ToArray();
  }
  finally{CryptographicOperations.ZeroMemory(plain);}
 }
 internal static byte[] Decrypt(byte[] data,string password)
 {
  if(data.Length<53||Encoding.ASCII.GetString(data,0,9)!="JBZVAULT1")throw new FormatException("Không đúng định dạng bộ cấp phép.");
  byte[] key=Rfc2898DeriveBytes.Pbkdf2(password,data.AsSpan(9,16),210000,HashAlgorithmName.SHA256,32),plain=new byte[data.Length-53];
  try{using var aes=new AesGcm(key,16);aes.Decrypt(data.AsSpan(25,12),data.AsSpan(53),data.AsSpan(37,16),plain,Encoding.ASCII.GetBytes("JBZVAULT1"));return plain;}
  catch{CryptographicOperations.ZeroMemory(plain);throw;}finally{CryptographicOperations.ZeroMemory(key);}
 }
 private static void Persist(Vault vault)
 {
  Directory.CreateDirectory(VaultDirectory);byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(vault);
  try{WriteAtomic(VaultPath,ProtectedData.Protect(bytes,Entropy,DataProtectionScope.CurrentUser));}finally{CryptographicOperations.ZeroMemory(bytes);}
 }
 internal static byte[] ReadBounded(string path)
 {
  using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(f.Length>32*1024*1024)throw new IOException("Tệp vượt giới hạn 32 MB.");
  byte[] data=new byte[(int)f.Length];f.ReadExactly(data);return data;
 }
 internal static void WriteAtomic(string path,byte[] data)
 {
  string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){f.Write(data);f.Flush(true);}File.Move(tmp,path,true);}
  finally{if(File.Exists(tmp))File.Delete(tmp);}
 }
}

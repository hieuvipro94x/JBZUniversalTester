using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
namespace JBZLicenseIssuer;
public static class ExcelExport
{
 public static void Save(string path,IReadOnlyList<Issuance> records)
 {
  using var buffer=new MemoryStream();
  using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true))
  {
   Add(zip,"[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
   Add(zip,"_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
   Add(zip,"xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Danh sách cấp phép\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
   Add(zip,"xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
   using var output=zip.CreateEntry("xl/worksheets/sheet1.xml").Open();using var xml=XmlWriter.Create(output,new XmlWriterSettings{Encoding=new UTF8Encoding(false)});
   const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
   xml.WriteStartElement("worksheet",ns);xml.WriteStartElement("sheetViews",ns);xml.WriteStartElement("sheetView",ns);xml.WriteAttributeString("workbookViewId","0");xml.WriteStartElement("pane",ns);xml.WriteAttributeString("ySplit","1");xml.WriteAttributeString("topLeftCell","A2");xml.WriteAttributeString("state","frozen");xml.WriteEndElement();xml.WriteEndElement();xml.WriteEndElement();
   xml.WriteStartElement("cols",ns);xml.WriteStartElement("col",ns);xml.WriteAttributeString("min","1");xml.WriteAttributeString("max","8");xml.WriteAttributeString("width","27");xml.WriteAttributeString("customWidth","1");xml.WriteEndElement();xml.WriteEndElement();
   xml.WriteStartElement("sheetData",ns);
   void Row(params string[] cells){xml.WriteStartElement("row",ns);foreach(string c in cells){xml.WriteStartElement("c",ns);xml.WriteAttributeString("t","inlineStr");xml.WriteStartElement("is",ns);xml.WriteStartElement("t",ns);xml.WriteString(c);xml.WriteEndElement();xml.WriteEndElement();xml.WriteEndElement();}xml.WriteEndElement();}
   Row("Mã giấy phép","Máy tính","Khách hàng / Đơn vị","Vị trí máy","Ngày cấp","Trạng thái","Ngày xác nhận thủ công","Ghi chú");
   foreach(var r in records)Row(r.LicenseId,r.Computer,r.Customer,r.Station,r.IssuedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),r.ConfirmedAtUtc is null?"ĐÃ CẤP MÃ":"ĐÃ XÁC NHẬN KÍCH HOẠT",r.ConfirmedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")??"",r.Notes);
   xml.WriteEndElement();xml.WriteStartElement("autoFilter",ns);xml.WriteAttributeString("ref",$"A1:H{records.Count+1}");xml.WriteEndElement();xml.WriteEndElement();
  }
  IssuerStore.WriteAtomic(path,buffer.ToArray());
 }
 private static void Add(ZipArchive zip,string name,string text){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(text);}
}

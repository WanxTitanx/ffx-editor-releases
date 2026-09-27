// Gera .resources PT a partir do resx XML, bypassando o ResGen (que lê cp1252 no Windows pt-BR).
// Uso: dotnet run --project tools/ResGenFix -- <resx> <out.resources>
using System.Resources;
using System.Xml.Linq;

string resxPath = args.Length > 0 ? args[0] : @"FFXProjectEditor\Resources\Strings.pt.resx";
string outPath = args.Length > 1 ? args[1] : @"FFXProjectEditor\obj\Debug\net8.0\FFXProjectEditor.Resources.Strings.pt.resources";

XDocument doc = XDocument.Load(resxPath);
var entries = doc.Root!
    .Elements("data")
    .Where(e => e.Attribute("name")?.Value != null)
    .Select(e => (Key: e.Attribute("name")!.Value, Value: e.Element("value")?.Value ?? ""))
    .ToList();

Console.WriteLine($"{entries.Count} chaves extraidas do resx PT");

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
using (var stream = File.Create(outPath))
using (var writer = new ResourceWriter(stream))
{
    foreach (var (key, value) in entries)
        writer.AddResource(key, value);
    writer.Generate();
}

Console.WriteLine($"SALVO: {outPath} ({new FileInfo(outPath).Length} bytes)");

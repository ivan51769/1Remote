using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.NET.HostModel.AppHost;
if (args[0] == "--verify") {
    var build = System.Reflection.Assembly.LoadFrom(args[1]);
    using var stream = build.GetManifestResourceStream("1Remote.CompatibilitySalt") ?? throw new Exception("Missing salt resource");
    using var sr = new StreamReader(stream);
    var sec = System.Reflection.Assembly.LoadFrom(args[3]);
    sec.GetType("_1Remote.Security.Config")!.GetMethod("SetSalt")!.Invoke(null,new object[]{sr.ReadToEnd()});
    var decrypt = sec.GetType("_1Remote.Security.SimpleStringEncipher")!.GetMethod("Decrypt")!; var values = decrypt.GetParameters().Select(p => p.DefaultValue).ToArray(); values[0] = File.ReadAllText(args[2]); var result = decrypt.Invoke(null, values)!;
    var typ = result.GetType();
    var success = typ.GetProperty("IsSuccess")?.GetValue(result) ?? typ.GetField("IsSuccess")?.GetValue(result);
    var plain = typ.GetProperty("PlainText")?.GetValue(result) ?? typ.GetField("PlainText")?.GetValue(result);
    if (!(success is true) || !Equals(plain,"EncryptionTest")) throw new Exception("Database compatibility failed");
    Console.WriteLine("PASS: the rebuilt assembly decrypts the original database compatibility sentinel.");
    return;
}
if (!Microsoft.NET.HostModel.Bundle.Bundler.IsBundle(args[0], out long header)) throw new Exception("Not a bundle");
using var exe = File.OpenRead(args[0]);
using var reader = new BinaryReader(exe);
exe.Position = header;
uint major = reader.ReadUInt32(); reader.ReadUInt32(); int count = reader.ReadInt32(); reader.ReadString();
if (major >= 2) exe.Position += 40;
for (int i = 0; i < count; i++) {
    long offset = reader.ReadInt64(), size = reader.ReadInt64(), compressed = major >= 6 ? reader.ReadInt64() : 0;
    reader.ReadByte(); string name = reader.ReadString();
    if (name != "1Remote.dll") continue;
    exe.Position = offset;
    byte[] data = reader.ReadBytes(checked((int)(compressed > 0 ? compressed : size)));
    using var payload = new MemoryStream(data);
    using var assembly = new MemoryStream();
    if (compressed > 0) { using var unzip = new DeflateStream(payload, CompressionMode.Decompress); unzip.CopyTo(assembly); } else payload.CopyTo(assembly);
    assembly.Position = 0;
    using var pe = new PEReader(assembly);
    var metadata = pe.GetMetadataReader();
    foreach (var typeHandle in metadata.TypeDefinitions) {
        var type = metadata.GetTypeDefinition(typeHandle);
        if (metadata.GetString(type.Namespace) != "_1RM" || metadata.GetString(type.Name) != "Assert") continue;
        foreach (var fieldHandle in type.GetFields()) {
            var field = metadata.GetFieldDefinition(fieldHandle);
            if (metadata.GetString(field.Name) != "STRING_SALT") continue;
            var constant = metadata.GetConstant(field.GetDefaultValue());
            if (constant.TypeCode != ConstantTypeCode.String) throw new Exception("Wrong constant type");
            var salt = Encoding.Unicode.GetString(metadata.GetBlobBytes(constant.Value));
            if (salt.Length < 1 || salt.Contains("REPLACE_ME")) throw new Exception("Missing compatibility value");
            File.WriteAllText(args[1], salt, new UTF8Encoding(false));
            Console.WriteLine("Original build compatibility value recovered without displaying it.");
            return;
        }
    }
    throw new Exception("Compatibility constant not found");
}
throw new Exception("Assembly not found");

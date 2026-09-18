using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Text;

namespace LTFS.WinFsp.Core;

/// <summary>Conservative reader for non-sparse LTFS XML indexes. Partition mapping comes from the media label.</summary>
public static class LtfsIndexReader
{
    public static ReadOnlyVolume Read(Stream input, IReadOnlyDictionary<string, byte> partitions, int blockSize)
    {
        if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 64 * 1024 * 1024, CloseInput = false
        });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Missing index.");
        if (root.Name != "ltfsindex") throw new InvalidDataException("Not an LTFS index.");
        var directory = root.Element("directory") ?? throw new InvalidDataException("Missing root directory.");
        int count = 0;
        VolumeDirectory ParseDirectory(XElement element, bool isRoot, int depth)
        {
            if (depth > 128 || ++count > 100000) throw new InvalidDataException("Index limits exceeded.");
            var name = isRoot ? "" : Name(element);
            var children = new List<VolumeEntry>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in element.Element("contents")?.Elements() ?? [])
            {
                VolumeEntry entry;
                if (child.Name == "directory") entry = ParseDirectory(child, false, depth + 1);
                else if (child.Name == "file")
                {
                    if (++count > 100000) throw new InvalidDataException("Too many entries.");
                    if (child.Element("symlink") != null) throw new InvalidDataException("Symbolic links are not supported yet.");
                    long length = Number(child, "length");
                    long covered = 0;
                    var extents = new List<FileExtent>();
                    foreach (var extent in child.Element("extentinfo")?.Elements("extent") ?? [])
                    {
                        string partition = Required(extent, "partition");
                        if (!partitions.TryGetValue(partition, out byte physical)) throw new InvalidDataException("Unknown partition.");
                        long offset = Number(extent, "byteoffset");
                        long fileOffset = Number(extent, "fileoffset");
                        long extentLength = Number(extent, "bytecount");
                        long block = Number(extent, "startblock");
                        if (offset >= blockSize || fileOffset != covered || extentLength == 0)
                            throw new InvalidDataException("Invalid, sparse, or overlapping extent.");
                        covered = checked(covered + extentLength);
                        _ = checked(block + (offset + extentLength - 1) / blockSize);
                        extents.Add(new(physical, block, (int)offset, extentLength));
                    }
                    if (covered != length) throw new InvalidDataException("File size and extent coverage differ.");
                    entry = new VolumeFile(Name(child), Time(child), length, extents.AsReadOnly());
                }
                else throw new InvalidDataException($"Unsupported directory entry: {child.Name}");
                if (!names.Add(entry.Name)) throw new InvalidDataException("Names collide under Windows case-insensitive lookup.");
                children.Add(entry);
            }
            return new(name, Time(element), children.AsReadOnly());
        }
        return new(directory.Element("name") is { } label ? DecodeName(label) : "LTFS", ParseDirectory(directory, true, 0));
    }

    private static string Required(XElement element, string name) => element.Element(name)?.Value
        ?? throw new InvalidDataException($"Missing {name}.");
    private static long Number(XElement element, string name)
    {
        if (!long.TryParse(Required(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            throw new InvalidDataException($"Invalid {name}.");
        return number;
    }
    private static DateTimeOffset Time(XElement element)
    {
        var value = element.Element("modifytime")?.Value;
        if (value == null) return DateTimeOffset.UnixEpoch;
        // LTFS timestamps may contain nanoseconds; .NET stores 100 ns ticks.
        value = System.Text.RegularExpressions.Regex.Replace(value, @"(\.\d{7})\d+", "$1");
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) || time.Year < 1601)
            throw new InvalidDataException("Invalid modification timestamp.");
        return time;
    }
    private static string Name(XElement element)
    {
        var node = element.Element("name") ?? throw new InvalidDataException("Missing name.");
        var value = DecodeName(node);
        if (string.IsNullOrEmpty(value) || value.Length > 255 || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ') ||
            value.Any(c => c < 32 || "\\/:*?\"<>|".Contains(c))) throw new InvalidDataException("Unsafe Windows file name.");
        var stem = value.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(stem, "^(COM|LPT)[1-9]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new InvalidDataException("Reserved Windows name.");
        return value;
    }

    private static string DecodeName(XElement node)
    {
        string? encoded = node.Attribute("percentencoded")?.Value;
        if (encoded == null || encoded == "false") return node.Value;
        if (encoded != "true") throw new InvalidDataException("Invalid percentencoded flag.");
        var utf8 = new UTF8Encoding(false, true);
        using var bytes = new MemoryStream();
        var text = node.Value;
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '%')
            {
                if (i + 2 >= text.Length || !byte.TryParse(text.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                    throw new InvalidDataException("Malformed percent-encoded name.");
                bytes.WriteByte(value); i += 3;
            }
            else
            {
                int end = text.IndexOf('%', i);
                if (end < 0) end = text.Length;
                bytes.Write(utf8.GetBytes(text[i..end])); i = end;
            }
        }
        try { return utf8.GetString(bytes.ToArray()); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid UTF-8 in encoded name.", ex); }
    }
}

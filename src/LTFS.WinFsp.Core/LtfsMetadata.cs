using System.Xml;
using System.Xml.Linq;

namespace LTFS.WinFsp.Core;

public sealed record LtfsLabel(Guid VolumeId, string Partition, string IndexPartition, string DataPartition, int BlockSize)
{
    public static LtfsLabel Parse(byte[] xml)
    {
        var root = LtfsMetadata.Xml(xml, "ltfslabel");
        string Part(XElement? element)
        {
            string value = element?.Value ?? "";
            if (value.Length != 1 || value[0] < 'a' || value[0] > 'z') throw new InvalidDataException("Invalid LTFS partition ID.");
            return value;
        }
        if (!Guid.TryParse(root.Element("volumeuuid")?.Value, out var uuid)) throw new InvalidDataException("Invalid label UUID.");
        if (!int.TryParse(root.Element("blocksize")?.Value, out var size) || size < 1 || size > 16 * 1024 * 1024)
            throw new InvalidDataException("Unsupported tape block size.");
        var label = new LtfsLabel(uuid, Part(root.Element("location")?.Element("partition")),
            Part(root.Element("partitions")?.Element("index")), Part(root.Element("partitions")?.Element("data")), size);
        if (label.IndexPartition == label.DataPartition || (label.Partition != label.IndexPartition && label.Partition != label.DataPartition))
            throw new InvalidDataException("Conflicting partition IDs.");
        return label;
    }
}

public sealed record IndexCandidate(byte[] Xml, string Partition, long Block);

public static class LtfsMetadata
{
    internal static XElement Xml(byte[] bytes, string expected)
    {
        using var stream = new MemoryStream(bytes, false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024, IgnoreWhitespace = true });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != expected) throw new InvalidDataException($"Expected {expected}.");
        if (!Version.TryParse(root.Attribute("version")?.Value, out var version) || version.Major != 2 || version.Minor > 4)
            throw new InvalidDataException("Only LTFS 2.0 through 2.4 metadata is supported.");
        return root;
    }

    public static IReadOnlyDictionary<string, byte> ValidateLabels(LtfsLabel first, LtfsLabel second)
    {
        if (first.VolumeId != second.VolumeId || first.BlockSize != second.BlockSize || first.IndexPartition != second.IndexPartition ||
            first.DataPartition != second.DataPartition || first.Partition == second.Partition)
            throw new InvalidDataException("Partition labels do not describe the same LTFS volume.");
        // Windows tape API partitions are numbered 1 and 2, not 0 and 1.
        return new Dictionary<string, byte> { [first.Partition] = 1, [second.Partition] = 2 };
    }

    public static byte[] SelectLatest(LtfsLabel label, IndexCandidate first, IndexCandidate second)
    {
        (long Generation, XElement Directory) Validate(IndexCandidate candidate)
        {
            var root = Xml(candidate.Xml, "ltfsindex");
            if (!Guid.TryParse(root.Element("volumeuuid")?.Value, out var id) || id != label.VolumeId)
                throw new InvalidDataException("Index belongs to a different tape.");
            if (!long.TryParse(root.Element("generationnumber")?.Value, out long generation) || generation < 1)
                throw new InvalidDataException("Invalid index generation.");
            if (root.Element("location")?.Element("partition")?.Value != candidate.Partition ||
                !long.TryParse(root.Element("location")?.Element("startblock")?.Value, out var block) || block != candidate.Block)
                throw new InvalidDataException("Index self-location does not match its tape position.");
            return (generation, root.Element("directory") ?? throw new InvalidDataException("Missing index root directory."));
        }
        if (first.Partition == second.Partition ||
            new[] { first.Partition, second.Partition }.Any(p => p != label.IndexPartition && p != label.DataPartition))
            throw new InvalidDataException("Expected one terminal index from each partition.");
        var a = Validate(first); var b = Validate(second);
        if (a.Generation == b.Generation && !XNode.DeepEquals(a.Directory, b.Directory))
            throw new InvalidDataException("Same-generation indexes disagree; refusing ambiguous mount.");
        return a.Generation >= b.Generation ? first.Xml : second.Xml;
    }
}

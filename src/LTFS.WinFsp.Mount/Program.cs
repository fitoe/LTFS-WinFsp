using Fsp;
using LTFS.WinFsp.Core;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using FileInfo = Fsp.Interop.FileInfo;

return await MountWorker.RunAsync(args);

public sealed class SimulationFileSystem : FileSystemBase, IDisposable
{
    private readonly IReadOnlyTape tape;
    private readonly TapeFileReader reader;
    private readonly Dictionary<string, VolumeEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[] security;
    private readonly string volumeLabel = "LTFS Simulation";
    private readonly ulong volumeSize = 16 * 1024 * 1024 + 100000;
    private readonly bool simulated = true;
    private const int NotFound = unchecked((int)0xC0000034);
    private const int ReadOnly = unchecked((int)0xC00000A2);
    private const int IoError = unchecked((int)0xC0000185);
    public SimulationFileSystem(IReadOnlyTape tape, ReadOnlyVolume volume, int blockSize)
    {
        this.tape = tape;
        reader = new TapeFileReader(tape, blockSize);
        simulated = false;
        volumeLabel = volume.Label.Length > 32 ? volume.Label[..32] : volume.Label;
        ulong total = 0;
        void Add(string path, VolumeEntry entry)
        {
            if (!entries.TryAdd(path, entry)) throw new InvalidDataException("Conflicting file path.");
            if (entry is VolumeDirectory directory)
                foreach (var child in directory.Children) Add(path == "\\" ? path + child.Name : path + "\\" + child.Name, child);
            else if (entry is VolumeFile file) total = checked(total + (ulong)file.Length);
        }
        Add("\\", volume.Root);
        volumeSize = total;
        var descriptor = new RawSecurityDescriptor("O:BAG:BAD:P(A;;FRFX;;;WD)");
        security = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(security, 0);
    }
    public SimulationFileSystem()
    {
        const int blockSize = 65536;
        var blocks = new Dictionary<TapePosition, byte[]>();
        for (int b = 0; b < 256; b++)
        {
            byte[] data = new byte[blockSize];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)((b * 17 + i) % 251);
            blocks.Add(new(0, b), data);
        }
        tape = new SimulatedTape(blocks);
        reader = new TapeFileReader(tape, blockSize);
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var large = new VolumeFile("sample.bin", time, 16 * 1024 * 1024, [new(0, 0, 0, 16 * 1024 * 1024)]);
        var cross = new VolumeFile("cross-block.bin", time, 100000, [new(0, 0, 65000, 100000)]);
        var dir = new VolumeDirectory("samples", time, [large, cross]);
        entries["\\"] = new VolumeDirectory("", time, [dir]);
        entries["\\samples"] = dir;
        entries["\\samples\\sample.bin"] = large;
        entries["\\samples\\cross-block.bin"] = cross;
        var descriptor = new RawSecurityDescriptor("O:BAG:BAD:P(A;;FRFX;;;WD)");
        security = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(security, 0);
    }
    public override int Init(object obj)
    {
        var host = (FileSystemHost)obj;
        host.SectorSize = 512;
        host.SectorsPerAllocationUnit = 8;
        host.MaxComponentLength = 255;
        host.FileSystemName = simulated ? "LTFS-SIM" : "LTFS";
        host.CasePreservedNames = true;
        host.CaseSensitiveSearch = false;
        host.UnicodeOnDisk = true;
        return 0;
    }
    private static FileInfo Info(VolumeEntry entry)
    {
        ulong size = entry is VolumeFile f ? (ulong)f.Length : 0;
        ulong time = (ulong)entry.ModifiedAt.ToFileTime();
        return new FileInfo { FileAttributes = entry is VolumeDirectory ? 0x11u : 0x21u,
            FileSize = size, AllocationSize = (size + 4095) / 4096 * 4096,
            CreationTime = time, LastAccessTime = time, LastWriteTime = time, ChangeTime = time, HardLinks = 1 };
    }
    public override int GetVolumeInfo(out Fsp.Interop.VolumeInfo info)
    { info = default; info.TotalSize = volumeSize; info.SetVolumeLabel(volumeLabel); return 0; }
    public override int GetSecurityByName(string name, out uint attributes, ref byte[] descriptor)
    {
        attributes = 0;
        if (!entries.TryGetValue(name, out var entry)) return NotFound;
        attributes = Info(entry).FileAttributes;
        if (descriptor != null) descriptor = security;
        return 0;
    }
    public override int Open(string name, uint options, uint access, out object node, out object context, out FileInfo info, out string normalized)
    {
        node = context = null; info = default; normalized = null;
        if ((access & (0x2u | 0x4u | 0x10u | 0x100u | 0x10000u | 0x40000u | 0x80000u)) != 0) return ReadOnly;
        if (!entries.TryGetValue(name, out var entry)) return NotFound;
        if ((options & 0x00001000) != 0) return ReadOnly; // FILE_DELETE_ON_CLOSE
        if ((options & 1) != 0 && entry is not VolumeDirectory) return unchecked((int)0xC0000103);
        if ((options & 0x40) != 0 && entry is VolumeDirectory) return unchecked((int)0xC00000BA);
        node = entry; info = Info(entry); return 0;
    }
    public override int GetFileInfo(object node, object context, out FileInfo info) { info = Info((VolumeEntry)node); return 0; }
    public override int GetSecurity(object node, object context, ref byte[] descriptor) { descriptor = security; return 0; }
    public override int ReadDirectory(object node, object context, string pattern, string marker, IntPtr buffer, uint length, out uint transferred)
        => SeekableReadDirectory(node, context, pattern, marker, buffer, length, out transferred);
    public override bool ReadDirectoryEntry(object node, object context, string pattern, string marker, ref object state, out string name, out FileInfo info)
    {
        name = null; info = default;
        if (node is not VolumeDirectory dir) return false;
        if (state == null) state = dir.Children.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Where(e => marker == null || StringComparer.OrdinalIgnoreCase.Compare(e.Name, marker) > 0).GetEnumerator();
        var iterator = (IEnumerator<VolumeEntry>)state;
        if (!iterator.MoveNext()) return false;
        var next = iterator.Current;
        name = next.Name; info = Info(next); return true;
    }
    public override int Read(object node, object context, IntPtr buffer, ulong offset, uint length, out uint transferred)
    {
        transferred = 0;
        if (node is not VolumeFile file || offset > long.MaxValue || length > 16 * 1024 * 1024) return IoError;
        try
        {
            byte[] data = new byte[(int)length];
            int count = reader.ReadAsync(file, (long)offset, data).AsTask().GetAwaiter().GetResult();
            Marshal.Copy(data, 0, buffer, count); transferred = (uint)count; return 0;
        }
        catch (IOException) { return IoError; }
    }
    public override int Create(string name, uint options, uint access, uint attributes, byte[] descriptor, ulong allocation, out object node, out object context, out FileInfo info, out string normalized)
    { node = context = null; info = default; normalized = null; return ReadOnly; }
    public override int Write(object node, object context, IntPtr buffer, ulong offset, uint length, bool eof, bool constrained, out uint transferred, out FileInfo info)
    { transferred = 0; info = default; return ReadOnly; }
    public override int SetFileSize(object node, object context, ulong size, bool allocation, out FileInfo info) { info = default; return ReadOnly; }
    public override int SetBasicInfo(object node, object context, uint attributes, ulong created, ulong accessed, ulong written, ulong changed, out FileInfo info) { info = default; return ReadOnly; }
    public override int Rename(object node, object context, string oldName, string newName, bool replace) => ReadOnly;
    public override int Overwrite(object node, object context, uint attributes, bool replace, ulong allocation, out FileInfo info)
    { info = default; return ReadOnly; }
    public override int SetSecurity(object node, object context, AccessControlSections sections, byte[] descriptor) => ReadOnly;
    public override int SetVolumeLabel(string label, out Fsp.Interop.VolumeInfo info) { info = default; return ReadOnly; }
    public override int CanDelete(object node, object context, string name) => ReadOnly;
    public override int SetDelete(object node, object context, string name, bool delete) => ReadOnly;
    public void Dispose() => tape.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

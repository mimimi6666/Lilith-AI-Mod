// IAT approach derived from SmallYuanSY/LilithCursorGuard (MIT).
// Unlike the upstream scanner, every loaded-image read and scan is bounded.
using System.Buffers.Binary;
using System.Text;

namespace LilithCursorGuard;

internal sealed class PeImportReader
{
    private readonly Func<uint, int, byte[]> _read;
    private readonly uint _mappedSize;
    private uint _imageSize;
    private readonly uint _imports;
    private readonly uint _importSize;

    internal PeImportReader(uint mappedSize, Func<uint, int, byte[]> read)
    {
        _read = read;
        _mappedSize = mappedSize;
        _imageSize = mappedSize;
        if (mappedSize < 0x100 || U16(0) != 0x5a4d) throw Invalid();
        uint nt = U32(0x3c);
        if (nt < 0x40 || U32(nt) != 0x4550 || U16(Add(nt, 4)) != 0x8664) throw Invalid();
        uint optional = Add(nt, 24);
        ushort optionalSize = U16(Add(nt, 20));
        if (optionalSize < 224 || U16(optional) != 0x20b) throw Invalid();
        Read(optional, optionalSize);
        _imageSize = U32(Add(optional, 56));
        if (_imageSize > _mappedSize || _imageSize < Add(optional, optionalSize)) throw Invalid();
        if (U32(Add(optional, 108)) < 2) throw Invalid();
        _imports = U32(Add(optional, 120));
        _importSize = U32(Add(optional, 124));
        if ((_imports == 0) != (_importSize == 0)) throw Invalid();
        if (_imports != 0 && (_importSize < 20 || _importSize > 20 * 4096)) throw Invalid();
        CheckRange(_imports, _importSize);
    }

    internal uint? FindClipCursorSlot()
    {
        if (_imports == 0) return null;
        for (uint offset = 0; offset + 20 <= _importSize; offset += 20)
        {
            uint descriptor = Add(_imports, offset);
            byte[] bytes = Read(descriptor, 20);
            if (Array.TrueForAll(bytes, b => b == 0)) return null;
            uint names = U32(descriptor);
            uint dll = U32(Add(descriptor, 12));
            uint addresses = U32(Add(descriptor, 16));
            if (dll == 0 || addresses == 0) throw Invalid();
            if (!string.Equals(AsciiZ(dll), "user32.dll", StringComparison.OrdinalIgnoreCase)) continue;
            // A resolved loaded IAT is not a name table. Refuse absent OriginalFirstThunk.
            if (names == 0 || names % 8 != 0 || addresses % 8 != 0) throw Invalid();
            uint? found = null;
            for (uint index = 0; index < 65536; index++)
            {
                uint slot = Add(addresses, index * 8);
                CheckRange(slot, 8);
                ulong entry = U64(Add(names, index * 8));
                if (entry == 0) return found;
                if ((entry & 0x8000000000000000UL) != 0) continue;
                if (entry > uint.MaxValue) throw Invalid();
                if (AsciiZ(Add((uint)entry, 2)) != "ClipCursor") continue;
                if (found.HasValue) throw Invalid();
                found = slot;
            }
            throw Invalid();
        }
        throw Invalid(); // No descriptor terminator inside the declared directory.
    }

    private string AsciiZ(uint rva)
    {
        var bytes = new byte[256];
        for (uint i = 0; i < bytes.Length; i++)
        {
            byte value = Read(Add(rva, i), 1)[0];
            if (value == 0) return Encoding.ASCII.GetString(bytes, 0, (int)i);
            if (value > 0x7f) throw Invalid();
            bytes[i] = value;
        }
        throw Invalid();
    }

    private byte[] Read(uint rva, int count)
    {
        if (count < 0) throw Invalid();
        CheckRange(rva, (uint)count);
        byte[] result = _read(rva, count);
        if (result.Length != count) throw Invalid();
        return result;
    }
    private void CheckRange(uint rva, uint count)
    {
        if (rva > _imageSize || count > _imageSize - rva) throw Invalid();
    }
    private ushort U16(uint rva) => BinaryPrimitives.ReadUInt16LittleEndian(Read(rva, 2));
    private uint U32(uint rva) => BinaryPrimitives.ReadUInt32LittleEndian(Read(rva, 4));
    private ulong U64(uint rva) => BinaryPrimitives.ReadUInt64LittleEndian(Read(rva, 8));
    private static uint Add(uint left, uint right) => checked(left + right);
    private static InvalidDataException Invalid() => new("Unsupported or malformed loaded PE image.");
}

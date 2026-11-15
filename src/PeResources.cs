// Lumora Setup - reads the managed resources (app.zip, version.txt) of LumoraClient.exe straight from the file:
// PE headers, CLI header, metadata table ManifestResource (ECMA-335 II.22.24) and the resource blob. The setup never
// loads the downloaded program as an assembly (no Assembly.Load*/ReflectionOnlyLoad*): it only reads bytes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Lumora.Setup
{
    internal static class PeResources
    {
        /// Offset and length in the file of every embedded managed resource, by name. Throws InvalidDataException when
        /// the file is not a .NET program.
        public static Dictionary<string, long[]> List(string file)
        {
            using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var r = new BinaryReader(f);
                f.Position = 0x3C;
                long pe = r.ReadUInt32();
                f.Position = pe;
                if (r.ReadUInt32() != 0x00004550)
                {
                    throw new InvalidDataException("no PE signature");
                }
                f.Position = pe + 6;
                int sections = r.ReadUInt16();
                f.Position = pe + 20;
                int optSize = r.ReadUInt16();
                long opt = pe + 24;
                f.Position = opt;
                int magic = r.ReadUInt16();
                long dirs = opt + (magic == 0x20B ? 112 : 96);
                f.Position = dirs + 14 * 8; // data directory 14: CLI header
                uint cliRva = r.ReadUInt32();
                if (cliRva == 0)
                {
                    throw new InvalidDataException("no CLI header");
                }
                var table = new List<uint[]>();
                for (int i = 0; i < sections; i++)
                {
                    f.Position = opt + optSize + i * 40 + 8;
                    uint vsize = r.ReadUInt32(), va = r.ReadUInt32(), rawSize = r.ReadUInt32(), raw = r.ReadUInt32();
                    table.Add(new[] { va, Math.Max(vsize, rawSize), raw });
                }
                Func<uint, long> at = delegate(uint rva)
                {
                    foreach (uint[] s in table)
                    {
                        if (rva >= s[0] && rva < s[0] + s[1])
                        {
                            return s[2] + (rva - s[0]);
                        }
                    }
                    throw new InvalidDataException("RVA outside the sections");
                };

                long cli = at(cliRva);
                f.Position = cli + 8;
                uint mdRva = r.ReadUInt32();
                r.ReadUInt32();
                f.Position = cli + 24;
                uint resRva = r.ReadUInt32();
                uint resSize = r.ReadUInt32();
                var result = new Dictionary<string, long[]>(StringComparer.Ordinal);
                if (resRva == 0)
                {
                    return result;
                }
                long res = at(resRva);

                // metadata root: streams #~ (or #-) and #Strings
                long md = at(mdRva);
                f.Position = md;
                if (r.ReadUInt32() != 0x424A5342)
                {
                    throw new InvalidDataException("no metadata");
                }
                f.Position = md + 12;
                int verLen = r.ReadInt32();
                f.Position = md + 16 + verLen + 2;
                int streams = r.ReadUInt16();
                long tables = -1, strings = -1;
                for (int i = 0; i < streams; i++)
                {
                    uint off = r.ReadUInt32();
                    r.ReadUInt32();
                    var name = new StringBuilder();
                    int c;
                    while ((c = r.ReadByte()) != 0)
                    {
                        name.Append((char)c);
                    }
                    while (f.Position % 4 != 0)
                    {
                        r.ReadByte();
                    }
                    string n = name.ToString();
                    if (n == "#~" || n == "#-")
                    {
                        tables = md + off;
                    }
                    else if (n == "#Strings")
                    {
                        strings = md + off;
                    }
                }
                if (tables < 0 || strings < 0)
                {
                    throw new InvalidDataException("metadata streams missing");
                }

                f.Position = tables + 6;
                int heaps = r.ReadByte();
                r.ReadByte();
                ulong valid = r.ReadUInt64();
                r.ReadUInt64();
                var rows = new int[64];
                for (int t = 0; t < 64; t++)
                {
                    if ((valid >> t & 1) != 0)
                    {
                        rows[t] = (int)r.ReadUInt32();
                    }
                }
                if ((heaps & 0x40) != 0)
                {
                    r.ReadUInt32(); // extra data
                }
                var layout = new Layout(rows, heaps);
                long pos = f.Position;
                for (int t = 0; t < ManifestResource; t++)
                {
                    pos += (long)rows[t] * layout.RowSize(t);
                }

                // ManifestResource rows: Offset (u4), Flags (u4), Name (string index), Implementation (coded index)
                int impl = layout.Coded(Implementation);
                for (int i = 0; i < rows[ManifestResource]; i++)
                {
                    f.Position = pos;
                    uint offset = r.ReadUInt32();
                    r.ReadUInt32();
                    uint name = layout.Str == 4 ? r.ReadUInt32() : r.ReadUInt16();
                    uint implementation = impl == 4 ? r.ReadUInt32() : r.ReadUInt16();
                    pos = f.Position;
                    if (implementation != 0 || offset + 4L > resSize)
                    {
                        continue; // resource in another file
                    }
                    f.Position = res + offset;
                    long length = r.ReadUInt32();
                    if (offset + 4L + length > resSize)
                    {
                        throw new InvalidDataException("resource outside the resource blob");
                    }
                    result[ReadString(f, strings + name)] = new[] { res + offset + 4, length };
                }
                return result;
            }
        }

        /// A read-only stream over one resource; null if the file has no resource of that name.
        public static Stream Open(string file, string name)
        {
            long[] e;
            if (!List(file).TryGetValue(name, out e))
            {
                return null;
            }
            return new Slice(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read), e[0], e[1]);
        }

        private static string ReadString(FileStream f, long at)
        {
            f.Position = at;
            var bytes = new List<byte>();
            int b;
            while ((b = f.ReadByte()) > 0)
            {
                bytes.Add((byte)b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private const int ManifestResource = 0x28;

        // coded index kinds (ECMA-335 II.24.2.6): tag bits and the tables they point to
        private static readonly int[] TypeDefOrRef = { 2, 0x02, 0x01, 0x1B };
        private static readonly int[] HasConstant = { 2, 0x04, 0x08, 0x17 };
        private static readonly int[] HasCustomAttribute = { 5, 0x06, 0x04, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x00, 0x0E, 0x17, 0x14,
            0x11, 0x1A, 0x1B, 0x20, 0x23, 0x26, 0x27, 0x28, 0x2A, 0x2C, 0x2B };
        private static readonly int[] HasFieldMarshal = { 1, 0x04, 0x08 };
        private static readonly int[] HasDeclSecurity = { 2, 0x02, 0x06, 0x20 };
        private static readonly int[] MemberRefParent = { 3, 0x02, 0x01, 0x1A, 0x06, 0x1B };
        private static readonly int[] HasSemantics = { 1, 0x14, 0x17 };
        private static readonly int[] MethodDefOrRef = { 1, 0x06, 0x0A };
        private static readonly int[] MemberForwarded = { 1, 0x04, 0x06 };
        private static readonly int[] Implementation = { 2, 0x26, 0x23, 0x27 };
        private static readonly int[] CustomAttributeType = { 3, 0x06, 0x0A };
        private static readonly int[] ResolutionScope = { 2, 0x00, 0x1A, 0x23, 0x01 };

        /// Row sizes of the tables before ManifestResource (ECMA-335 II.22).
        private sealed class Layout
        {
            private readonly int[] rows;
            public readonly int Str, Guid, Blob;

            public Layout(int[] rows, int heaps)
            {
                this.rows = rows;
                Str = (heaps & 1) != 0 ? 4 : 2;
                Guid = (heaps & 2) != 0 ? 4 : 2;
                Blob = (heaps & 4) != 0 ? 4 : 2;
            }

            private int Idx(int t)
            {
                return rows[t] > 0xFFFF ? 4 : 2;
            }

            public int Coded(int[] kind)
            {
                int max = 0;
                for (int i = 1; i < kind.Length; i++)
                {
                    max = Math.Max(max, rows[kind[i]]);
                }
                return max < 1 << (16 - kind[0]) ? 2 : 4;
            }

            public int RowSize(int t)
            {
                switch (t)
                {
                    case 0x00: return 2 + Str + 3 * Guid;                                        // Module
                    case 0x01: return Coded(ResolutionScope) + 2 * Str;                          // TypeRef
                    case 0x02: return 4 + 2 * Str + Coded(TypeDefOrRef) + Idx(0x04) + Idx(0x06); // TypeDef
                    case 0x03: return Idx(0x04);                                                 // FieldPtr
                    case 0x04: return 2 + Str + Blob;                                            // Field
                    case 0x05: return Idx(0x06);                                                 // MethodPtr
                    case 0x06: return 8 + Str + Blob + Idx(0x08);                                // MethodDef
                    case 0x07: return Idx(0x08);                                                 // ParamPtr
                    case 0x08: return 4 + Str;                                                   // Param
                    case 0x09: return Idx(0x02) + Coded(TypeDefOrRef);                           // InterfaceImpl
                    case 0x0A: return Coded(MemberRefParent) + Str + Blob;                       // MemberRef
                    case 0x0B: return 2 + Coded(HasConstant) + Blob;                             // Constant
                    case 0x0C: return Coded(HasCustomAttribute) + Coded(CustomAttributeType) + Blob; // CustomAttribute
                    case 0x0D: return Coded(HasFieldMarshal) + Blob;                             // FieldMarshal
                    case 0x0E: return 2 + Coded(HasDeclSecurity) + Blob;                         // DeclSecurity
                    case 0x0F: return 6 + Idx(0x02);                                             // ClassLayout
                    case 0x10: return 4 + Idx(0x04);                                             // FieldLayout
                    case 0x11: return Blob;                                                      // StandAloneSig
                    case 0x12: return Idx(0x02) + Idx(0x14);                                     // EventMap
                    case 0x13: return Idx(0x14);                                                 // EventPtr
                    case 0x14: return 2 + Str + Coded(TypeDefOrRef);                             // Event
                    case 0x15: return Idx(0x02) + Idx(0x17);                                     // PropertyMap
                    case 0x16: return Idx(0x17);                                                 // PropertyPtr
                    case 0x17: return 2 + Str + Blob;                                            // Property
                    case 0x18: return 2 + Idx(0x06) + Coded(HasSemantics);                       // MethodSemantics
                    case 0x19: return Idx(0x02) + 2 * Coded(MethodDefOrRef);                     // MethodImpl
                    case 0x1A: return Str;                                                       // ModuleRef
                    case 0x1B: return Blob;                                                      // TypeSpec
                    case 0x1C: return 2 + Coded(MemberForwarded) + Str + Idx(0x1A);              // ImplMap
                    case 0x1D: return 4 + Idx(0x04);                                             // FieldRVA
                    case 0x1E: return 8;                                                         // EncLog
                    case 0x1F: return 4;                                                         // EncMap
                    case 0x20: return 16 + Blob + 2 * Str;                                       // Assembly
                    case 0x21: return 4;                                                         // AssemblyProcessor
                    case 0x22: return 12;                                                        // AssemblyOS
                    case 0x23: return 12 + 2 * Blob + 2 * Str;                                   // AssemblyRef
                    case 0x24: return 4 + Idx(0x23);                                             // AssemblyRefProcessor
                    case 0x25: return 12 + Idx(0x23);                                            // AssemblyRefOS
                    case 0x26: return 4 + Str + Blob;                                            // File
                    case 0x27: return 8 + 2 * Str + Coded(Implementation);                       // ExportedType
                    default: throw new InvalidDataException("unknown metadata table " + t);
                }
            }
        }

        /// Read-only window [start, start + length) of a file; closes the file with it.
        private sealed class Slice : Stream
        {
            private readonly FileStream file;
            private readonly long start, length;
            private long pos;

            public Slice(FileStream file, long start, long length)
            {
                this.file = file;
                this.start = start;
                this.length = length;
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return length; } }

            public override long Position
            {
                get { return pos; }
                set
                {
                    if (value < 0)
                    {
                        throw new ArgumentOutOfRangeException("value");
                    }
                    pos = value;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                long left = length - pos;
                if (left <= 0)
                {
                    return 0;
                }
                file.Position = start + pos;
                int n = file.Read(buffer, offset, (int)Math.Min(count, left));
                pos += n;
                return n;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                Position = (origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? pos : length) + offset;
                return pos;
            }

            public override void Flush()
            {
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    file.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}

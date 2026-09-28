using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace CATHODE
{
    /// <summary>
    /// Reads and writes Havok <b>tagfiles</b> (TAG0), which is how the mobile and Switch builds store
    /// the collision and physics the PC keeps in classic packfiles.
    ///
    /// A tagfile describes its own schema: one chunk names every type and its members with their byte
    /// offsets, another lists the objects. Nothing here is hard-coded to a Havok version - the offsets
    /// are read out of the file - so a build on a different SDK should still come apart correctly.
    /// The files shipped with the game are SDK 2018.2 where the PC is 2012.2.
    ///
    /// This fills in the same <see cref="HavokPackfile"/> structures the packfile reader produces, so
    /// everything downstream is unaware of which kind it was handed - including the geometry readers,
    /// which walk the tagfile through the same data payload, object list and pointer fixups they use
    /// for a packfile. Previews and bake meshes come out of these builds as they do the PC ones.
    ///
    /// Writing works the same way round: the packfile's own editing calls - adding instances, adding
    /// a box shape, importing a graph from another level - come through here to move item entries and
    /// list pointers rather than to write fixup tables, and the file is re-emitted around whatever
    /// the payload has grown to. Everything that describes the schema is copied through untouched -
    /// until an import adds types or hashes, when TYPE is written from the model - so a file that is
    /// loaded and saved unchanged comes back out byte for byte.
    /// </summary>
    internal sealed class HavokTagfile
    {
        /// <summary>A tagfile opens with a chunk header whose name is TAG0.</summary>
        public static bool IsTagfile(byte[] file)
        {
            return file != null && file.Length >= 8
                && file[4] == 'T' && file[5] == 'A' && file[6] == 'G' && file[7] == '0';
        }

        #region MODEL

        private sealed class TagType
        {
            public string Name;
            public TagType Parent;
            public int Size;
            public int Alignment;

            /// <summary>Its 1-based index in the file's own type table, which PTCH groups are keyed by.</summary>
            public int Index;

            public List<TagMember> Members = new List<TagMember>();

            /* Dozens of types share a name - there are 100 called "T*" and as many called "hkArray" -
             * and only their template arguments tell them apart. Nothing needs them to read a file,
             * but copying objects between two files does: an index only means something in the file
             * it came from, so each type needs an identity both files can agree on. */
            public List<TagTemplate> Templates = new List<TagTemplate>();

            /* Everything else TNA1 and TBDY say about the type, kept exactly as read so the table can
             * be written back out when types are added to it (see IMPORTING TYPES). Nothing reads
             * these to understand a file. */
            public int NameIndex;                     //into TSTR
            public bool HasBody;
            public int ParentIndex;                   //as written - Parent drops a self-reference
            public uint Flags;
            public uint Format, SubType, Version, Extra, Attributes;
            public uint MemberWordHigh;               //the bits above the member count (hkPropertyId: 3)
            public List<uint[]> Interfaces = new List<uint[]>();    //{ type index, value }

            //Both walks are depth-capped: a parent pointer that misparsed into a cycle would
            //otherwise spin here forever rather than showing up as bad data
            private const int MaxDepth = 64;

            public int OffsetOf(string member)
            {
                int depth = 0;
                for (TagType step = this; step != null && depth++ < MaxDepth; step = step.Parent)
                    foreach (TagMember found in step.Members)
                        if (found.Name == member) return found.Offset;

                return -1;
            }

            public bool Is(string name)
            {
                int depth = 0;
                for (TagType step = this; step != null && depth++ < MaxDepth; step = step.Parent)
                    if (step.Name == name) return true;

                return false;
            }

            /// <summary>
            /// How wide one of these is. A primitive has no size of its own - hkInt16 is declared with
            /// nothing but a parent - but it derives from a plain C type that does carry one, so the
            /// answer is up the chain. No table of our own is needed: the file says short is 2.
            /// </summary>
            public int Width
            {
                get
                {
                    int depth = 0;
                    for (TagType step = this; step != null && depth++ < MaxDepth; step = step.Parent)
                        if (step.Size > 0) return step.Size;

                    return 0;
                }
            }
        }

        private sealed class TagMember
        {
            public string Name;
            public int Offset;
            public TagType Type;

            //As written, for re-emitting the table
            public int NameIndex;                     //into FSTR
            public uint Flags;
            public int TypeIndex;
        }

        /// <summary>
        /// One template argument. The parameter's own name says which kind it is - Havok spells type
        /// parameters <c>tSOMETHING</c> and value parameters <c>vSOMETHING</c> - so a type argument
        /// holds a type index and a value argument holds a plain number.
        /// </summary>
        private sealed class TagTemplate
        {
            public string Name;
            public int Value;
            public int NameIndex;                     //into TSTR, for re-emitting the table
            public bool IsType { get { return Name != null && Name.Length != 0 && Name[0] == 't'; } }
        }

        private sealed class TagItem
        {
            public TagType Type;
            public int Offset;
            public int Count;
            public bool IsPointer;

            /// <summary>The type-and-flags word exactly as the file spells it, so a rewrite is faithful.</summary>
            public uint Word;
        }

        /// <summary>
        /// One PTCH group: every place in the data holding a pointer of a given declared type. Which
        /// group a pointer belongs in is decided by the member's own type, not by what it points at.
        /// </summary>
        private sealed class TagPatchGroup
        {
            public int Type;

            //A set, not a list: rewriting a compound retires thousands of these and adds thousands more,
            //and the file wants them sorted anyway, so the order they arrive in is not worth keeping
            public HashSet<int> Offsets = new HashSet<int>();
        }

        private byte[] _file;
        private int _data;
        private int _dataLength;
        private readonly List<TagType> _types = new List<TagType>();
        private readonly List<TagItem> _items = new List<TagItem>();
        private readonly List<TagPatchGroup> _patches = new List<TagPatchGroup>();
        private readonly Dictionary<int, List<HavokPackfile.RigidBodyInfo>> _bodiesBySystem
            = new Dictionary<int, List<HavokPackfile.RigidBodyInfo>>();

        /* The rest of the TYPE chunk, held so it can be written again once types have been appended
         * (see IMPORTING TYPES). Until then TYPE is copied through byte for byte and none of this is
         * consulted. The string tables are without their padding. */
        private readonly List<string> _typeNames = new List<string>();
        private readonly List<string> _memberNames = new List<string>();
        private readonly List<int> _bodyOrder = new List<int>();
        private readonly List<KeyValuePair<int, uint>> _hashes = new List<KeyValuePair<int, uint>>();
        private byte[] _tptr = new byte[0];
        private byte[] _tpad = new byte[0];
        private int _originalTypeCount;

        /* Once this has filled a packfile in, that packfile's payload is the live copy - it grows when
         * instances are added, and reading our own stale copy would miss every edit. */
        private HavokPackfile _owner;
        private byte[] Data { get { return _owner != null ? _owner.DataPayload : _file; } }
        private int DataAt { get { return _owner != null ? 0 : _data; } }

        public string Version { get; private set; }

        #endregion

        #region READING THE FILE

        public bool Read(byte[] file)
        {
            _file = file;
            _types.Clear();
            _items.Clear();
            _typeNames.Clear();
            _memberNames.Clear();
            _bodyOrder.Clear();
            _hashes.Clear();
            _typeModelExact = null;
            _signatures = null;

            Dictionary<string, int[]> chunks = new Dictionary<string, int[]>();
            Walk(chunks, 0, file.Length);

            if (!chunks.TryGetValue("DATA", out int[] data))
                return false;
            _data = data[0];
            _dataLength = data[1];

            if (chunks.TryGetValue("SDKV", out int[] sdkv))
                Version = Encoding.ASCII.GetString(file, sdkv[0], Math.Min(8, sdkv[1]));

            List<string> typeNames = chunks.TryGetValue("TSTR", out int[] tstr) ? Strings(tstr[0], tstr[1]) : new List<string>();
            List<string> memberNames = chunks.TryGetValue("FSTR", out int[] fstr) ? Strings(fstr[0], fstr[1]) : new List<string>();

            if (chunks.TryGetValue("TNA1", out int[] tna1))
                ReadTypeNames(tna1[0], typeNames);
            if (chunks.TryGetValue("TBDY", out int[] tbdy))
                ReadTypeBodies(tbdy[0], tbdy[1], memberNames);
            ReadTypeTableRest(chunks, typeNames, memberNames);
            if (chunks.TryGetValue("ITEM", out int[] item))
                ReadItems(item[0], item[1]);
            if (chunks.TryGetValue("PTCH", out int[] patch))
                ReadPatches(patch[0], patch[1]);

            return _types.Count != 0 && _items.Count != 0;
        }

        /// <summary>
        /// Chunks are 4 bytes of big-endian size-and-flags then a 4 character name. The 0x40000000 bit
        /// means the chunk holds raw data; without it, it holds more chunks. The size counts the header.
        /// </summary>
        private void Walk(Dictionary<string, int[]> chunks, int start, int end)
        {
            int at = start;
            while (at + 8 <= end)
            {
                uint header = (uint)((_file[at] << 24) | (_file[at + 1] << 16) | (_file[at + 2] << 8) | _file[at + 3]);
                string name = Encoding.ASCII.GetString(_file, at + 4, 4);
                int size = (int)(header & 0x3FFFFFFF);
                if (size < 8 || at + size > end)
                    return;

                if ((header & 0x40000000) != 0) chunks[name] = new int[] { at + 8, size - 8 };
                else Walk(chunks, at + 8, at + size);

                at += size;
            }
        }

        /* TNA1 - a count, then per type a name and its template arguments. Types are 1-based. */
        private void ReadTypeNames(int at, List<string> names)
        {
            int cursor = at;
            int count = (int)Packed(ref cursor);

            for (int i = 1; i < count; i++)
            {
                TagType type = new TagType() { Index = i };
                int nameIndex = (int)Packed(ref cursor);
                type.Name = nameIndex >= 0 && nameIndex < names.Count ? names[nameIndex] : "";
                type.NameIndex = nameIndex;

                /* Each argument is a name and a value. The name is a plain index into the same string
                 * table the type names come from - it is NOT shifted or flagged, whatever the shape of
                 * the number suggests. */
                int templates = (int)Packed(ref cursor);
                for (int t = 0; t < templates; t++)
                {
                    int argName = (int)Packed(ref cursor);
                    int argValue = (int)Packed(ref cursor);
                    type.Templates.Add(new TagTemplate()
                    {
                        Name = argName >= 0 && argName < names.Count ? names[argName] : "",
                        Value = argValue,
                        NameIndex = argName,
                    });
                }

                _types.Add(type);
            }
        }

        /* TBDY - per entry: self, parent, flags, then sections the flags select. Entries are NOT in
         * type order, so nothing here may assume a sequence. */
        private void ReadTypeBodies(int at, int length, List<string> names)
        {
            int cursor = at;
            while (cursor < at + length)
            {
                int selfIndex = (int)Packed(ref cursor);
                if (selfIndex == 0) break;

                TagType type = TypeAt(selfIndex);
                if (type == null) break;

                _bodyOrder.Add(selfIndex);
                type.HasBody = true;
                type.ParentIndex = (int)Packed(ref cursor);
                TagType parent = TypeAt(type.ParentIndex);
                type.Parent = ReferenceEquals(parent, type) ? null : parent;
                uint flags = Packed(ref cursor);
                type.Flags = flags;

                /* The sub type is a type index - the element of an array, the target of a pointer -
                 * and equals the tT argument wherever there is one (every shipped type). */
                if ((flags & 0x1) != 0) type.Format = Packed(ref cursor);
                if ((flags & 0x2) != 0) type.SubType = Packed(ref cursor);
                if ((flags & 0x4) != 0) type.Version = Packed(ref cursor);
                if ((flags & 0x8) != 0)
                {
                    type.Size = (int)Packed(ref cursor);
                    type.Alignment = (int)Packed(ref cursor);
                }
                if ((flags & 0x10) != 0) type.Extra = Packed(ref cursor);

                if ((flags & 0x20) != 0)
                {
                    /* The member count shares its word with flags above bit 16 - hkPropertyId's reads
                     * 0x30001, one member. Taking the whole word as the count used to throw the reader
                     * off: in an animation file it lost more than half the table, including
                     * hkaSplineCompressedAnimation. Masked, every shipped tagfile reads each type once. */
                    uint memberWord = Packed(ref cursor);
                    type.MemberWordHigh = memberWord >> 16;
                    int members = (int)(memberWord & 0xFFFF);
                    if (members > 4096)
                        return;

                    for (int m = 0; m < members; m++)
                    {
                        TagMember member = new TagMember();
                        int nameIndex = (int)Packed(ref cursor);
                        member.Name = nameIndex >= 0 && nameIndex < names.Count ? names[nameIndex] : "";
                        member.NameIndex = nameIndex;
                        member.Flags = Packed(ref cursor);
                        member.Offset = (int)Packed(ref cursor);
                        member.TypeIndex = (int)Packed(ref cursor);
                        member.Type = TypeAt(member.TypeIndex);
                        type.Members.Add(member);

                        if (cursor > at + length) return;
                    }
                }

                //An interface is a type index and (in every shipped one) the offset of its vtable
                if ((flags & 0x40) != 0)
                {
                    int interfaces = (int)Packed(ref cursor);
                    for (int n = 0; n < interfaces && cursor < at + length; n++)
                        type.Interfaces.Add(new uint[] { Packed(ref cursor), Packed(ref cursor) });
                }
                if ((flags & 0x80) != 0) type.Attributes = Packed(ref cursor);

                if (cursor > at + length) return;
            }
        }

        /// <summary>
        /// The parts of TYPE nothing needs for reading, kept so the table can be written out again:
        /// the string tables without their padding, THSH, and TPTR/TPAD as they are.
        /// </summary>
        private void ReadTypeTableRest(Dictionary<string, int[]> chunks, List<string> typeNames, List<string> memberNames)
        {
            _originalTypeCount = _types.Count;
            _typeNames.AddRange(typeNames);
            _memberNames.AddRange(memberNames);
            if (chunks.TryGetValue("TSTR", out int[] tstr)) Unpad(_typeNames, tstr[1]);
            if (chunks.TryGetValue("FSTR", out int[] fstr)) Unpad(_memberNames, fstr[1]);

            _tptr = Raw(chunks, "TPTR");
            _tpad = Raw(chunks, "TPAD");

            /* THSH - a count, then that many (type index, 32-bit little-endian hash). Every shipped
             * file hashes exactly the types its items and PTCH groups use, and a type's hash is the
             * same in every file that carries one. */
            if (chunks.TryGetValue("THSH", out int[] thsh) && thsh[1] > 0)
            {
                int cursor = thsh[0], end = thsh[0] + thsh[1];
                int count = (int)Packed(ref cursor);
                for (int i = 0; i < count && cursor < end; i++)
                {
                    int type = (int)Packed(ref cursor);
                    if (cursor + 4 > end) break;
                    _hashes.Add(new KeyValuePair<int, uint>(type, BitConverter.ToUInt32(_file, cursor)));
                    cursor += 4;
                }
            }
        }

        private byte[] Raw(Dictionary<string, int[]> chunks, string name)
        {
            if (!chunks.TryGetValue(name, out int[] at)) return new byte[0];
            byte[] copy = new byte[at[1]];
            Buffer.BlockCopy(_file, at[0], copy, 0, at[1]);
            return copy;
        }

        /* A string table is padded to four bytes with zeros, and each zero reads as another empty
         * string. Drop exactly those: the ones the table's length does not need. */
        private static void Unpad(List<string> strings, int length)
        {
            int bytes = 0;
            foreach (string s in strings) bytes += s.Length + 1;

            while (strings.Count != 0 && strings[strings.Count - 1].Length == 0 && ((bytes - 1 + 3) & ~3) == length)
            {
                strings.RemoveAt(strings.Count - 1);
                bytes--;
            }
        }

        private void ReadItems(int at, int length)
        {
            for (int cursor = at; cursor + 12 <= at + length; cursor += 12)
            {
                uint typeAndFlags = BitConverter.ToUInt32(_file, cursor);
                _items.Add(new TagItem()
                {
                    Word = typeAndFlags,
                    Type = TypeAt((int)(typeAndFlags & 0xFFFFFF)),
                    IsPointer = (typeAndFlags & 0x10000000) != 0,
                    Offset = BitConverter.ToInt32(_file, cursor + 4),
                    Count = BitConverter.ToInt32(_file, cursor + 8),
                });
            }
        }

        /* PTCH is grouped by the declared type of the pointer: an index, a count, then that many
         * offsets into the data, each of which holds an item index. */
        private void ReadPatches(int at, int length)
        {
            int cursor = at, end = at + length;
            while (cursor + 8 <= end)
            {
                TagPatchGroup group = new TagPatchGroup() { Type = BitConverter.ToInt32(_file, cursor) };
                int count = BitConverter.ToInt32(_file, cursor + 4);
                cursor += 8;
                if (count < 0 || cursor + count * 4 > end)
                    return;

                for (int i = 0; i < count; i++, cursor += 4)
                    group.Offsets.Add(BitConverter.ToInt32(_file, cursor));

                _patches.Add(group);
            }
        }

        private TagType TypeAt(int index)
        {
            return index >= 1 && index <= _types.Count ? _types[index - 1] : null;
        }

        /// <summary>
        /// The tagfile's variable-length integer: leading 1 bits in the first byte give the width.
        /// </summary>
        private uint Packed(ref int at)
        {
            byte first = _file[at];

            if ((first & 0x80) == 0) { at += 1; return (uint)(first & 0x7F); }
            if ((first & 0xC0) == 0x80) { uint v = (uint)(((first & 0x3F) << 8) | _file[at + 1]); at += 2; return v; }
            if ((first & 0xE0) == 0xC0) { uint v = (uint)(((first & 0x1F) << 16) | (_file[at + 1] << 8) | _file[at + 2]); at += 3; return v; }
            if ((first & 0xF0) == 0xE0)
            {
                uint v = (uint)(((first & 0x0F) << 24) | (_file[at + 1] << 16) | (_file[at + 2] << 8) | _file[at + 3]);
                at += 4; return v;
            }

            uint wide = (uint)((_file[at + 1] << 24) | (_file[at + 2] << 16) | (_file[at + 3] << 8) | _file[at + 4]);
            at += 5;
            return wide;
        }

        private List<string> Strings(int at, int length)
        {
            List<string> found = new List<string>();
            int start = at;
            for (int i = at; i < at + length; i++)
            {
                if (_file[i] != 0) continue;
                found.Add(Encoding.ASCII.GetString(_file, start, i - start));
                start = i + 1;
            }
            return found;
        }

        #endregion

        #region READING OBJECTS

        /* A pointer stored in the data is an index into the item table, not an address. */
        private int Follow(int objectOffset, int memberOffset)
        {
            if (objectOffset < 0 || memberOffset < 0) return -1;

            int index = (int)BitConverter.ToUInt64(Data, DataAt + objectOffset + memberOffset);
            return index > 0 && index < _items.Count ? _items[index].Offset : -1;
        }

        /* An hkArray's m_size is left at zero in the file - the real count is on the item its m_data
         * names, which is where the loader fills it from. */
        private TagItem ArrayItem(int objectOffset, int memberOffset)
        {
            if (objectOffset < 0 || memberOffset < 0) return null;

            int index = (int)BitConverter.ToUInt64(Data, DataAt + objectOffset + memberOffset);
            return index > 0 && index < _items.Count ? _items[index] : null;
        }

        /// <summary>The offsets of an array's elements: item indices when it holds pointers, and the
        /// element stride when it holds structs laid out in place.</summary>
        private List<int> Elements(TagItem array)
        {
            List<int> found = new List<int>();
            if (array == null) return found;

            bool pointers = array.IsPointer || (array.Type != null && array.Type.Name == "T*");
            int stride = pointers ? 8 : (array.Type == null ? 0 : array.Type.Width);

            //An element size of zero means the type carried no body, and walking it would step one
            //byte at a time through however much the count claims
            if (stride <= 0) return found;

            //A count is only trustworthy as far as the data actually reaches
            long room = (Data.Length - DataAt - (long)array.Offset) / stride;
            int count = (int)Math.Max(0, Math.Min(array.Count, room));

            for (int i = 0; i < count; i++)
            {
                int at = array.Offset + i * stride;

                if (!pointers) { found.Add(at); continue; }

                int index = (int)BitConverter.ToUInt64(Data, DataAt + at);
                found.Add(index > 0 && index < _items.Count ? _items[index].Offset : -1);
            }

            return found;
        }

        private string Text(int objectOffset, int memberOffset)
        {
            int at = Follow(objectOffset, memberOffset);
            if (at < 0) return null;

            byte[] data = Data;
            int start = DataAt + at;
            int end = start;
            while (end < data.Length && data[end] != 0) end++;
            return Encoding.UTF8.GetString(data, start, end - start);
        }

        private TagType Find(string name)
        {
            return _types.FirstOrDefault(o => o.Name == name);
        }

        #endregion

        #region FILLING IN THE PACKFILE

        public void Populate(HavokPackfile target)
        {
            /* The geometry readers work off the packfile's own three lookups - the data payload, the
             * objects in it and the pointer fixups - so rather than duplicate every shape decoder,
             * fill those in from the tagfile and let the existing code run. */
            target.DataPayload = new byte[Math.Max(0, Math.Min(_dataLength, _file.Length - _data))];
            Buffer.BlockCopy(_file, _data, target.DataPayload, 0, target.DataPayload.Length);

            //From here on that payload is the live one - ours is only kept for re-emitting the file
            _owner = target;

            foreach (TagItem item in _items)
                if (item.Type != null)
                    target.Objects.Add(new HavokPackfile.PackfileObject()
                    {
                        DataOffset = (uint)item.Offset,
                        ClassName = item.Type.Name,

                        //The readers dispatch on this, not on the name - leaving it Unknown means
                        //every shape silently declines to decode
                        Class = Classify(item.Type.Name),
                    });

            ReadFixups(target);
            ReadLayout(target.Layout);

            ReadPhysics(target);
            ReadCollision(target);
        }

        /// <summary>
        /// PTCH says which places in the data hold a pointer, grouped by type: an index, a count, then
        /// that many offsets. Each of those offsets holds an item index, so turning them into the
        /// packfile's src-to-dst fixups is what makes the shape graph walkable.
        /// </summary>
        private void ReadFixups(HavokPackfile target)
        {
            byte[] data = Data;
            int start = DataAt;

            foreach (TagPatchGroup group in _patches)
                foreach (int source in group.Offsets)
                {
                    if (source < 0 || start + source + 8 > data.Length) continue;

                    int index = (int)BitConverter.ToUInt64(data, start + source);
                    if (index <= 0 || index >= _items.Count) continue;

                    target.GlobalFixups.Add(new HavokPackfile.GlobalFixup()
                    {
                        Src = (uint)source,
                        Dst = (uint)_items[index].Offset,
                    });
                }
        }

        /// <summary>
        /// Follow a pointer stored at <paramref name="at"/> in the data. Tagfile pointers are item
        /// indices, and the item also carries the element count - which is where an hkArray's real
        /// size lives, because m_size in the data itself is left at zero.
        /// </summary>
        public bool TryResolvePointer(uint at, out uint target, out int count)
        {
            target = 0;
            count = 0;
            if (DataAt + at + 8 > Data.Length) return false;

            int index = (int)BitConverter.ToUInt64(Data, DataAt + (int)at);

            /* A null pointer is an empty array, not a failure - the packfile reader says so too, and
             * callers treat a false here as "this shape is unreadable" and give up on the whole mesh. */
            if (index == 0) return true;
            if (index < 0 || index >= _items.Count) return false;

            target = (uint)_items[index].Offset;
            count = _items[index].Count;
            return true;
        }

        /// <summary>Tell the geometry readers where this file's shape fields actually are.</summary>
        private void ReadLayout(HavokPackfile.ShapeLayout layout)
        {
            TagType convex = Find("hkpConvexVerticesShape");
            if (convex != null)
            {
                Set(convex, "rotatedVertices", ref layout.ConvexRotatedVertices);
                Set(convex, "numVertices", ref layout.ConvexNumVertices);
                Set(convex, "planeEquations", ref layout.ConvexPlaneEquations);
                Set(convex, "connectivity", ref layout.ConvexConnectivity);
                Set(convex, "aabbHalfExtents", ref layout.ConvexAabbHalfExtents);
                Set(convex, "aabbCenter", ref layout.ConvexAabbCentre);
            }

            TagType connectivity = Find("hkpConvexVerticesConnectivity");
            if (connectivity != null)
            {
                Set(connectivity, "vertexIndices", ref layout.ConnectivityVertexIndices);
                Set(connectivity, "numVerticesPerFace", ref layout.ConnectivityFacesPerVertex);
            }

            TagType list = Find("hkpListShape");
            if (list != null)
                Set(list, "childInfo", ref layout.ListChildInfo);

            TagType child = Find("hkpListShape::ChildInfo");
            if (child != null && child.Size > 0)
                layout.ListChildStride = child.Size;

            TagType worldObject = Find("hkpWorldObject");
            if (worldObject != null)
                Set(worldObject, "collidable", ref layout.WorldObjectCollidable);
        }

        private Dictionary<int, string> _classAt;

        /// <summary>The Havok class of whatever object sits at a data offset.</summary>
        private string ClassAt(int offset)
        {
            if (_classAt == null)
            {
                _classAt = new Dictionary<int, string>();
                foreach (TagItem item in _items)
                    if (item.Type != null && !_classAt.ContainsKey(item.Offset))
                        _classAt[item.Offset] = item.Type.Name;
            }

            return _classAt.TryGetValue(offset, out string name) ? name : null;
        }

        /// <summary>The kinds of object the packfile readers know how to walk, by Havok class name.</summary>
        private static HavokPackfile.ObjectClass Classify(string name)
        {
            switch (name)
            {
                case "hkRootLevelContainer": return HavokPackfile.ObjectClass.RootLevelContainer;
                case "hkpPhysicsData": return HavokPackfile.ObjectClass.PhysicsData;
                case "hkpPhysicsSystem": return HavokPackfile.ObjectClass.PhysicsSystem;
                case "hkpWorldCinfo": return HavokPackfile.ObjectClass.WorldCinfo;
                case "hkpGroupFilter": return HavokPackfile.ObjectClass.GroupFilter;
                case "hkpDefaultConvexListFilter": return HavokPackfile.ObjectClass.DefaultConvexListFilter;
                case "hkpRigidBody": return HavokPackfile.ObjectClass.RigidBody;
                case "hkpListShape": return HavokPackfile.ObjectClass.ListShape;
                case "hkpStaticCompoundShape": return HavokPackfile.ObjectClass.StaticCompoundShape;
                case "hkpBvCompressedMeshShape": return HavokPackfile.ObjectClass.BvCompressedMeshShape;
                case "hkpBoxShape": return HavokPackfile.ObjectClass.BoxShape;
                default: return HavokPackfile.ObjectClass.Unknown;
            }
        }

        private static void Set(TagType type, string member, ref int target)
        {
            int at = type.OffsetOf(member);
            if (at >= 0) target = at;
        }

        /// <summary>The rigid bodies of a system, read when the system was, so this is a lookup.</summary>
        public List<HavokPackfile.RigidBodyInfo> RigidBodies(HavokPackfile.PhysicsSystem system)
        {
            return system != null && _bodiesBySystem.TryGetValue(system.SystemIndex, out List<HavokPackfile.RigidBodyInfo> found)
                ? found : new List<HavokPackfile.RigidBodyInfo>();
        }

        private void ReadPhysics(HavokPackfile target)
        {
            TagType dataType = Find("hkpPhysicsData");
            TagType systemType = Find("hkpPhysicsSystem");
            TagType worldObject = Find("hkpWorldObject");
            if (dataType == null || systemType == null) return;

            TagItem root = _items.FirstOrDefault(o => o.Type != null && o.Type.Is("hkpPhysicsData"));
            if (root == null) return;

            List<int> systems = Elements(ArrayItem(root.Offset, dataType.OffsetOf("systems")));
            int nameAt = systemType.OffsetOf("name");
            int bodiesAt = systemType.OffsetOf("rigidBodies");

            for (int i = 0; i < systems.Count; i++)
            {
                int at = systems[i];
                if (at < 0) continue;

                HavokPackfile.PhysicsSystem system = new HavokPackfile.PhysicsSystem()
                {
                    SystemIndex = i,
                    DataOffset = (uint)at,
                    Name = Text(at, nameAt),
                };
                target.PhysicsSystems.Add(system);

                List<HavokPackfile.RigidBodyInfo> bodies = new List<HavokPackfile.RigidBodyInfo>();
                foreach (int body in Elements(ArrayItem(at, bodiesAt)))
                    if (body >= 0)
                        bodies.Add(ReadRigidBody(body, worldObject));

                _bodiesBySystem[i] = bodies;
            }
        }

        private HavokPackfile.RigidBodyInfo ReadRigidBody(int at, TagType worldObject)
        {
            byte[] data = Data;
            int start = DataAt;
            HavokPackfile.RigidBodyInfo info = new HavokPackfile.RigidBodyInfo()
            {
                DataOffset = (uint)at,
                Name = worldObject == null ? null : Text(at, worldObject.OffsetOf("name")),
            };

            TagType motion = Find("hkpMotion");
            TagType entity = Find("hkpEntity");
            if (motion != null && entity != null)
            {
                //A rigid body carries its motion inline rather than by pointer
                int motionAt = entity.OffsetOf("motion");
                if (motionAt >= 0)
                {
                    int typeAt = motion.OffsetOf("type");
                    if (typeAt >= 0 && start + at + motionAt + typeAt < data.Length)
                    {
                        info.MotionType = data[start + at + motionAt + typeAt];
                        info.MotionTypeName = HavokPackfile.DescribeMotionType(info.MotionType);
                    }

                    int inertiaAt = motion.OffsetOf("inertiaAndMassInv");
                    if (inertiaAt >= 0 && start + at + motionAt + inertiaAt + 16 <= data.Length)
                    {
                        int w = start + at + motionAt + inertiaAt;
                        info.InertiaInvLocal = new Vector3(
                            BitConverter.ToSingle(data, w), BitConverter.ToSingle(data, w + 4), BitConverter.ToSingle(data, w + 8));
                        info.MassInv = BitConverter.ToSingle(data, w + 12);
                        info.Mass = info.MassInv > 1e-12f ? 1.0f / info.MassInv : float.PositiveInfinity;
                    }

                    int gravityAt = motion.OffsetOf("gravityFactor");
                    if (gravityAt >= 0 && start + at + motionAt + gravityAt + 2 <= data.Length)
                        info.GravityFactor = Half(start + at + motionAt + gravityAt);
                }
            }

            //The rest of what the packfile reader fills in, from wherever this file puts it
            int shapeAt = OffsetOfPath("hkpRigidBody", "collidable", "shape");
            int shape = Follow(at, shapeAt);
            info.ShapeClassName = shape < 0 ? "" : (ClassAt(shape) ?? "");

            int frictionAt = OffsetOfPath("hkpRigidBody", "material", "friction");
            if (frictionAt >= 0 && start + at + frictionAt + 4 <= data.Length)
                info.Friction = BitConverter.ToSingle(data, start + at + frictionAt);
            int restitutionAt = OffsetOfPath("hkpRigidBody", "material", "restitution");
            if (restitutionAt >= 0 && start + at + restitutionAt + 4 <= data.Length)
                info.Restitution = BitConverter.ToSingle(data, start + at + restitutionAt);
            int filterAt = OffsetOfPath("hkpRigidBody", "collidable", "broadPhaseHandle", "collisionFilterInfo");
            if (filterAt >= 0 && start + at + filterAt + 4 <= data.Length)
                info.CollisionFilterInfo = BitConverter.ToUInt32(data, start + at + filterAt);

            int radiusAt = OffsetOfPath("hkpRigidBody", "motion", "motionState", "objectRadius");
            if (radiusAt >= 0 && start + at + radiusAt + 4 <= data.Length)
                info.ObjectRadius = BitConverter.ToSingle(data, start + at + radiusAt);
            int linearAt = OffsetOfPath("hkpRigidBody", "motion", "motionState", "linearDamping");
            if (linearAt >= 0 && start + at + linearAt + 2 <= data.Length)
                info.LinearDamping = Half(start + at + linearAt);
            int angularAt = OffsetOfPath("hkpRigidBody", "motion", "motionState", "angularDamping");
            if (angularAt >= 0 && start + at + angularAt + 2 <= data.Length)
                info.AngularDamping = Half(start + at + angularAt);
            int maxLinearAt = OffsetOfPath("hkpRigidBody", "motion", "motionState", "maxLinearVelocity");
            if (maxLinearAt >= 0 && start + at + maxLinearAt < data.Length)
                info.MaxLinearVelocity = data[start + at + maxLinearAt];

            return info;
        }

        private void ReadCollision(HavokPackfile target)
        {
            TagType compound = Find("hkpStaticCompoundShape");
            TagType instance = Find("hkpStaticCompoundShape::Instance");
            if (compound == null || instance == null) return;

            int instancesAt = compound.OffsetOf("instances");
            int transformAt = instance.OffsetOf("transform");
            int shapeAt = instance.OffsetOf("shape");
            int filterAt = instance.OffsetOf("filterInfo");
            int childMaskAt = instance.OffsetOf("childFilterInfoMask");
            int userDataAt = instance.OffsetOf("userData");

            byte[] data = Data;
            int start = DataAt;

            /* A COLLISION.MAP row names its proxy by where it sits in the proxy list - the hkpListShape
             * the first rigid body carries - not by where it sits in the file. A tagfile puts the
             * three world hosts first, so numbering by item order put every template three past the
             * row that means it (0 of 27,439 iOS rows bound right; 100% by the list). The hosts are
             * not in the list, so they follow it, in item order - ballistic, spare, walkable - as the
             * PC's own come last. */
            Dictionary<int, int> listed = new Dictionary<int, int>();
            List<int> children = ProxyListChildren();
            for (int i = 0; i < children.Count; i++)
                if (children[i] >= 0 && !listed.ContainsKey(children[i]))
                    listed[children[i]] = i;
            int unlisted = children.Count;

            List<HavokPackfile.StaticCompoundShape> read = new List<HavokPackfile.StaticCompoundShape>();
            foreach (TagItem item in _items)
            {
                if (item.Type == null || !item.IsPointer || !item.Type.Is("hkpStaticCompoundShape")) continue;

                HavokPackfile.StaticCompoundShape shape = new HavokPackfile.StaticCompoundShape()
                {
                    ProxyIndex = listed.TryGetValue(item.Offset, out int index) ? index : unlisted++,
                    DataOffset = (uint)item.Offset,
                };

                /* The tree's domain, as the packfile reader fills it. Left at zero, every compound looked
                 * too small for its own mesh, and Save & Build rebuilt the tree of every template. */
                int domainAt = Compound().Domain;
                if (domainAt >= 0 && start + item.Offset + domainAt + 32 <= data.Length)
                {
                    shape.DomainMin = Vector(start + item.Offset + domainAt);
                    shape.DomainMax = Vector(start + item.Offset + domainAt + 16);
                }

                foreach (int at in Elements(ArrayItem(item.Offset, instancesAt)))
                {
                    if (at < 0 || start + at + instance.Size > data.Length) continue;

                    //hkQsTransform: translation, then a quaternion, then scale
                    int t = start + at + transformAt;
                    HavokPackfile.CompoundInstance carried = new HavokPackfile.CompoundInstance()
                    {
                        //Where this instance sits, so an edit to it can be written straight back
                        DataOffset = (uint)at,

                        Translation = new Vector4(BitConverter.ToSingle(data, t), BitConverter.ToSingle(data, t + 4),
                                                  BitConverter.ToSingle(data, t + 8), BitConverter.ToSingle(data, t + 12)),
                        Rotation = new Quaternion(BitConverter.ToSingle(data, t + 16), BitConverter.ToSingle(data, t + 20),
                                                  BitConverter.ToSingle(data, t + 24), BitConverter.ToSingle(data, t + 28)),
                        Scale = new Vector4(BitConverter.ToSingle(data, t + 32), BitConverter.ToSingle(data, t + 36),
                                            BitConverter.ToSingle(data, t + 40), BitConverter.ToSingle(data, t + 44)),
                        FilterInfo = filterAt < 0 ? 0 : BitConverter.ToUInt32(data, start + at + filterAt),
                        ChildFilterInfoMask = childMaskAt < 0 ? 0 : BitConverter.ToUInt32(data, start + at + childMaskAt),
                        UserData = userDataAt < 0 ? 0 : BitConverter.ToUInt64(data, start + at + userDataAt),
                    };

                    /* The preview dispatches on the instance's own record of what its shape is, not on
                     * a lookup - leave it blank and every shape declines to decode and falls back to
                     * the compound's domain box. */
                    int child = Follow(at, shapeAt);
                    carried.ShapeDataOffset = child < 0 ? 0 : (uint)child;
                    carried.ShapeClassName = child < 0 ? null : ClassAt(child);

                    shape.AddInstance(carried);
                }

                read.Add(shape);
            }

            target.StaticCompoundShapes.AddRange(read.OrderBy(o => o.ProxyIndex));
        }

        /// <summary>
        /// The proxy list: the hkpListShape a rigid body carries, whose children are the per-mesh
        /// compounds. Falls back to the first list in the file, which is the same object in every
        /// shipped collision file. -1 when there is none - a physics file, say.
        /// </summary>
        public int ProxyListOffset()
        {
            int shapeAt = OffsetOfPath("hkpRigidBody", "collidable", "shape");
            if (shapeAt >= 0)
                foreach (TagItem item in _items)
                {
                    if (item.Type == null || !item.IsPointer || !item.Type.Is("hkpRigidBody")) continue;
                    int shape = Follow(item.Offset, shapeAt);
                    if (shape >= 0 && ClassAt(shape) == "hkpListShape")
                        return shape;
                }

            TagItem first = _items.FirstOrDefault(o => o.Type != null && o.IsPointer && o.Type.Name == "hkpListShape");
            return first == null ? -1 : first.Offset;
        }

        /// <summary>What each slot of the proxy list points at, in list order (-1 for an empty slot).</summary>
        public List<int> ProxyListChildren()
        {
            List<int> found = new List<int>();
            int list = ProxyListOffset();
            TagType listType = Find("hkpListShape");
            TagType childType = Find("hkpListShape::ChildInfo");
            if (list < 0 || listType == null || childType == null || childType.Size <= 0)
                return found;

            TagItem array = ArrayItem(list, listType.OffsetOf("childInfo"));
            int shapeAt = childType.OffsetOf("shape");
            if (array == null || shapeAt < 0)
                return found;

            for (int i = 0; i < array.Count; i++)
            {
                int slot = array.Offset + i * childType.Size;
                if (DataAt + slot + shapeAt + 8 > Data.Length) break;
                found.Add(Follow(slot, shapeAt));
            }
            return found;
        }

        /// <summary>
        /// Fill in a skeleton from the one hkaSkeleton this file holds. The mobile and Switch builds
        /// ship these as tagfiles where the PC ships packfiles; the skeleton itself is the same.
        /// </summary>
        public bool ReadSkeleton(Skeleton target)
        {
            TagType skeletonType = Find("hkaSkeleton");
            TagItem root = _items.FirstOrDefault(o => o.Type != null && o.Type.Is("hkaSkeleton"));
            if (skeletonType == null || root == null) return false;

            target.Name = Text(root.Offset, skeletonType.OffsetOf("name")) ?? "";

            TagItem parents = ArrayItem(root.Offset, skeletonType.OffsetOf("parentIndices"));
            TagItem bones = ArrayItem(root.Offset, skeletonType.OffsetOf("bones"));
            TagItem pose = ArrayItem(root.Offset, skeletonType.OffsetOf("referencePose"));
            if (parents == null || bones == null || pose == null) return false;

            TagType boneType = Find("hkaBone");
            int stride = boneType != null && boneType.Size > 0 ? boneType.Size : 16;
            int nameAt = boneType == null ? 0 : Math.Max(0, boneType.OffsetOf("name"));
            int lockAt = boneType == null ? 8 : Math.Max(0, boneType.OffsetOf("lockTranslation"));

            //A reference pose entry is an hkQsTransform: translation, rotation, scale
            const int poseStride = 48;
            int count = Math.Min(parents.Count, Math.Min(bones.Count, pose.Count));

            for (int i = 0; i < count; i++)
            {
                int bone = bones.Offset + i * stride;
                int transform = DataAt + pose.Offset + i * poseStride;
                int parent = DataAt + parents.Offset + i * 2;

                if (transform + poseStride > Data.Length || parent + 2 > Data.Length
                    || DataAt + bone + stride > Data.Length)
                    break;

                target.Bones.Add(new Skeleton.Bone()
                {
                    Name = Text(bone, nameAt) ?? "",
                    ParentIndex = BitConverter.ToInt16(Data, parent),
                    LockTranslation = Data[DataAt + bone + lockAt] != 0,
                    Translation = Vector(transform),
                    Rotation = new Quaternion(BitConverter.ToSingle(Data, transform + 16), BitConverter.ToSingle(Data, transform + 20),
                                              BitConverter.ToSingle(Data, transform + 24), BitConverter.ToSingle(Data, transform + 28)),
                    Scale = Vector(transform + 32),
                });
            }

            return target.Bones.Count != 0;
        }

        private Vector4 Vector(int at)
        {
            return new Vector4(BitConverter.ToSingle(Data, at), BitConverter.ToSingle(Data, at + 4),
                               BitConverter.ToSingle(Data, at + 8), BitConverter.ToSingle(Data, at + 12));
        }

        #endregion

        #region WRITING THE FILE

        /* Writing a tagfile is the same trick as reading one, in reverse. Nothing about the container
         * needs re-deriving: the chunks that describe the schema are copied through byte for byte (until
         * types or hashes are added - see IMPORTING TYPES), and only the three that describe the data change - DATA itself, the item table that says where
         * each object lives, and the patch table that says which words hold pointers.
         *
         * The two things a packfile does not have to think about:
         *   - an array's real length lives on its item, not in the data, so growing an array means
         *     moving that item rather than writing a new m_size;
         *   - a pointer in the data is an item index, so a new pointer has to be given the index of
         *     the item whose object it means, and be listed in PTCH under the pointer's declared type. */

        /// <summary>
        /// Everything a static compound rewrite needs, taken from the file's own type table rather
        /// than assumed: where each array pointer and the domain sit, and which PTCH group each of
        /// those pointers belongs in.
        /// </summary>
        public sealed class CompoundLayout
        {
            public int Instances = -1;
            public int InstancesGroup = -1;
            public int Nodes = -1;
            public int NodesGroup = -1;
            public int Domain = -1;
            public int InstanceStride;
            public int Shape = -1;
            public int ShapeGroup = -1;

            public bool Complete
            {
                get
                {
                    return Instances >= 0 && InstancesGroup > 0 && Nodes >= 0 && NodesGroup > 0
                        && Domain >= 0 && InstanceStride > 0 && Shape >= 0 && ShapeGroup > 0;
                }
            }
        }

        private CompoundLayout _compound;

        public CompoundLayout Compound()
        {
            if (_compound != null)
                return _compound;

            _compound = new CompoundLayout();
            TagType shape = Find("hkpStaticCompoundShape");
            TagType instance = Find("hkpStaticCompoundShape::Instance");
            if (shape == null || instance == null)
                return _compound;

            _compound.Instances = shape.OffsetOf("instances");
            _compound.InstancesGroup = GroupOf(shape, "instances");
            _compound.InstanceStride = instance.Size;
            _compound.Shape = instance.OffsetOf("shape");
            _compound.ShapeGroup = GroupOf(instance, "shape");

            /* The tree is held inline, so its own members are relative to where it starts - and the
             * node array's PTCH group is named by the tree's member, not the compound's. */
            TagMember tree = MemberOf(shape, "tree");
            if (tree != null && tree.Type != null)
            {
                _compound.Nodes = tree.Offset + tree.Type.OffsetOf("nodes");
                _compound.NodesGroup = GroupOf(tree.Type, "nodes");
                _compound.Domain = tree.Offset + tree.Type.OffsetOf("domain");
            }

            return _compound;
        }

        private static TagMember MemberOf(TagType type, string member)
        {
            int depth = 0;
            for (TagType step = type; step != null && depth++ < 64; step = step.Parent)
                foreach (TagMember found in step.Members)
                    if (found.Name == member) return found;

            return null;
        }

        /// <summary>
        /// The PTCH group a pointer stored in this member belongs to. Groups are keyed by the member's
        /// declared type - <c>T*</c> for an object pointer, the particular <c>hkArray</c> for an array -
        /// not by what the pointer happens to point at.
        /// </summary>
        private static int GroupOf(TagType type, string member)
        {
            TagMember found = MemberOf(type, member);
            return found == null || found.Type == null ? -1 : found.Type.Index;
        }

        /// <summary>
        /// Point an array member at <paramref name="count"/> elements starting at
        /// <paramref name="elementOffset"/>. The item the member names is what carries both, so this
        /// moves that item rather than writing anything into the array header.
        /// <para>
        /// Only for a word that owns its item. On a word copied from another object it moves the
        /// other object's array - use <see cref="NewArray"/> there.
        /// </para>
        /// </summary>
        public bool SetArray(int fieldOffset, int elementOffset, int count, int patchGroup)
        {
            byte[] data = Data;
            int start = DataAt;
            if (fieldOffset < 0 || start + fieldOffset + 8 > data.Length)
                return false;

            int index = (int)BitConverter.ToUInt64(data, start + fieldOffset);
            if (index <= 0 || index >= _items.Count)
            {
                //An array that was empty in the file has no item to move, so it needs one making
                index = CloneItemFor(patchGroup);
                if (index <= 0)
                    return false;

                WriteIndex(data, start + fieldOffset, index);
                AddPatch(patchGroup, fieldOffset);
            }

            int movedFrom = _items[index].Offset;
            _items[index].Offset = elementOffset;
            _items[index].Count = count;

            /* The packfile's object list has an entry per item, and a port walks it to find where each
             * object's bytes stop. Left at the old address, the entry before the moved array claimed
             * the whole array - the proxy list, or hkpPhysicsData.systems - and a port from a level
             * edited in the same session dragged every compound or system in it across. */
            if (movedFrom != elementOffset && _owner != null && _items[index].Type != null)
            {
                string name = _items[index].Type.Name;
                foreach (HavokPackfile.PackfileObject entry in _owner.Objects)
                {
                    if (entry.DataOffset != (uint)movedFrom || entry.ClassName != name) continue;
                    entry.DataOffset = (uint)elementOffset;
                    break;
                }
                _classAt = null;
            }

            //And its fixup, which a port follows to find the array: left at the old address, a compound
            //rebuilt by Save & Build ported across with no instances
            if (_owner != null)
            {
                HavokPackfile.GlobalFixup live = new HavokPackfile.GlobalFixup() { Src = (uint)fieldOffset, Dst = (uint)elementOffset };
                int at = _owner.GlobalFixups.FindIndex(o => o.Src == (uint)fieldOffset);
                if (at >= 0) _owner.GlobalFixups[at] = live;
                else _owner.GlobalFixups.Add(live);
            }
            return true;
        }

        /// <summary>
        /// Point an object pointer member at an object that already exists in the data. Returns false
        /// if nothing in the item table claims that offset, which would leave a dangling pointer.
        /// </summary>
        public bool SetPointer(int fieldOffset, uint targetOffset, int patchGroup)
        {
            byte[] data = Data;
            int start = DataAt;
            if (fieldOffset < 0 || start + fieldOffset + 8 > data.Length)
                return false;

            int index = ItemIndexAt((int)targetOffset);
            if (index <= 0)
                return false;

            WriteIndex(data, start + fieldOffset, index);
            AddPatch(patchGroup, fieldOffset);
            return true;
        }

        /* Copying objects from another tagfile.
         *
         * An item names its type by index and a pointer names its item by index, so bytes copied from
         * another file only mean anything if both files number their types the same way. They usually
         * do - the shipped levels are built from one schema - but "usually" is not something to write
         * a file on, so it is checked rather than assumed, and an import into a file with a different
         * schema is refused instead of quietly producing rubbish. */

        private string[] _signatures;

        /// <summary>
        /// A name for a type that means the same thing in any file: its own name plus its template
        /// arguments, with type arguments written out the same way rather than left as indices. Every
        /// type in the shipped files comes out distinct under this, which is what a copy between two
        /// files needs - the two never number their types the same way.
        /// </summary>
        private string Signature(TagType type, int depth)
        {
            if (type == null) return "void";
            if (depth > 16) return type.Name ?? "";
            if (type.Templates.Count == 0) return type.Name ?? "";

            StringBuilder written = new StringBuilder(type.Name ?? "").Append('<');
            for (int i = 0; i < type.Templates.Count; i++)
            {
                TagTemplate argument = type.Templates[i];
                if (i != 0) written.Append(',');
                written.Append(argument.Name).Append('=');
                written.Append(argument.IsType
                    ? Signature(TypeAt(argument.Value), depth + 1)
                    : argument.Value.ToString());
            }
            return written.Append('>').ToString();
        }

        private string[] Signatures()
        {
            if (_signatures != null)
                return _signatures;

            _signatures = new string[_types.Count + 1];
            for (int i = 1; i <= _types.Count; i++)
                _signatures[i] = Signature(_types[i - 1], 0);

            return _signatures;
        }

        /// <summary>
        /// Match this file's types to another's by that portable name. Returns one entry per type index
        /// in <paramref name="source"/>, giving our own index for it or -1 if we have no such type -
        /// which is a real answer, not a failure: a level with no box shapes does not declare one.
        /// </summary>
        public int[] MapTypesFrom(HavokTagfile source)
        {
            string[] theirs = source.Signatures();
            string[] ours = Signatures();

            Dictionary<string, int> byName = new Dictionary<string, int>(ours.Length);
            for (int i = 1; i < ours.Length; i++)
                if (!byName.ContainsKey(ours[i]))
                    byName[ours[i]] = i;

            int[] map = new int[theirs.Length];
            for (int i = 1; i < theirs.Length; i++)
                map[i] = byName.TryGetValue(theirs[i], out int found) ? found : -1;

            return map;
        }

        /// <summary>What a source type is called, for saying which one an import could not find.</summary>
        public string SignatureOf(int index)
        {
            string[] all = Signatures();
            return index >= 1 && index < all.Length ? all[index] : "type " + index;
        }

        /// <summary>An item, as the raw pieces a copy needs: what it is, where it is and how many.</summary>
        public struct Item
        {
            public uint Word;
            public int Offset;
            public int Count;
        }

        public List<Item> Items()
        {
            return _items.Select(o => new Item() { Word = o.Word, Offset = o.Offset, Count = o.Count }).ToList();
        }

        /// <summary>Add an item and return its index, which is what a pointer to it holds.</summary>
        public int AddItem(uint word, int offset, int count)
        {
            TagItem added = new TagItem()
            {
                Word = word,
                Type = TypeAt((int)(word & 0xFFFFFF)),
                IsPointer = (word & 0x10000000) != 0,
                Offset = offset,
                Count = count,
            };
            _items.Add(added);

            if (_itemAt != null && added.IsPointer && !_itemAt.ContainsKey(offset))
                _itemAt[offset] = _items.Count - 1;
            if (_classAt != null && added.Type != null && !_classAt.ContainsKey(offset))
                _classAt[offset] = added.Type.Name;

            return _items.Count - 1;
        }

        /// <summary>How many items there are, so a writer can tell which ones it added.</summary>
        public int ItemCount { get { return _items.Count; } }

        /// <summary>Every place holding a pointer, with the group it belongs to.</summary>
        public List<KeyValuePair<int, int>> Patches()
        {
            List<KeyValuePair<int, int>> found = new List<KeyValuePair<int, int>>();
            foreach (TagPatchGroup group in _patches)
                foreach (int offset in group.Offsets)
                    found.Add(new KeyValuePair<int, int>(group.Type, offset));

            return found;
        }

        public void SetPatch(int patchGroup, int offset)
        {
            AddPatch(patchGroup, offset);
        }

        /// <summary>The item index a pointer would need to hold to name the object at a data offset.</summary>
        public int IndexOfObjectAt(int offset)
        {
            return ItemIndexAt(offset);
        }

        /// <summary>The item index stored in a pointer word, for translating one file's into another's.</summary>
        public int ReadIndex(int fieldOffset)
        {
            byte[] data = Data;
            int start = DataAt;
            if (fieldOffset < 0 || start + fieldOffset + 8 > data.Length) return -1;
            return (int)BitConverter.ToUInt64(data, start + fieldOffset);
        }

        public bool WriteIndexAt(int fieldOffset, int index)
        {
            byte[] data = Data;
            int start = DataAt;
            if (fieldOffset < 0 || start + fieldOffset + 8 > data.Length) return false;
            WriteIndex(data, start + fieldOffset, index);
            return true;
        }

        /// <summary>Where the item at an index lives, so a copy can be followed back to its source.</summary>
        public bool TryGetItem(int index, out int offset, out int count)
        {
            offset = 0;
            count = 0;
            if (index <= 0 || index >= _items.Count) return false;
            offset = _items[index].Offset;
            count = _items[index].Count;
            return true;
        }

        /// <summary>
        /// Read the physics systems and collision compounds again, after objects have been added.
        /// The packfile's own rebuild goes through class names, which a tagfile does not have.
        /// </summary>
        public void RereadTypedViews(HavokPackfile target)
        {
            target.Objects.Clear();
            target.GlobalFixups.Clear();
            target.StaticCompoundShapes.Clear();
            target.PhysicsSystems.Clear();
            _bodiesBySystem.Clear();
            _classAt = null;

            foreach (TagItem item in _items)
                if (item.Type != null)
                    target.Objects.Add(new HavokPackfile.PackfileObject()
                    {
                        DataOffset = (uint)item.Offset,
                        ClassName = item.Type.Name,
                        Class = Classify(item.Type.Name),
                    });

            ReadFixups(target);
            ReadPhysics(target);
            ReadCollision(target);
        }

        /// <summary>
        /// The arrays an object points at, as where the elements start, how many there are and how
        /// wide each one is. A packfile finds these by looking for fixups inside the object; a tagfile
        /// has none, so this walks the patch table instead - and the element size comes off the item's
        /// own type rather than being guessed from the gap to the next thing.
        ///
        /// Pointers to other objects are left out: those are not arrays.
        /// </summary>
        public List<int[]> ArraysIn(int start, int end)
        {
            List<int[]> found = new List<int[]>();
            byte[] data = Data;
            int at = DataAt;

            foreach (TagPatchGroup group in _patches)
                foreach (int source in group.Offsets)
                {
                    if (source < start || source >= end || at + source + 8 > data.Length) continue;

                    int index = (int)BitConverter.ToUInt64(data, at + source);
                    if (index <= 0 || index >= _items.Count) continue;

                    TagItem item = _items[index];
                    if (item.IsPointer || item.Type == null || item.Count <= 0) continue;

                    int width = item.Type.Width;
                    if (width <= 0) continue;

                    found.Add(new int[] { item.Offset, item.Count, width });
                }

            return found;
        }

        /// <summary>The PTCH group a pointer stored in this member belongs to.</summary>
        public int PatchGroupOf(string type, string member)
        {
            TagType found = Find(type);
            return found == null ? -1 : GroupOf(found, member);
        }

        /// <summary>
        /// Which group already claims a word. Some pointers - the elements of a pointer array - are not
        /// named by any member, so the only honest way to place a new one is to see where its
        /// neighbours were filed.
        /// </summary>
        public int GroupContaining(int offset)
        {
            foreach (TagPatchGroup group in _patches)
                if (group.Offsets.Contains(offset)) return group.Type;

            return -1;
        }

        /// <summary>How big a type is, and where one of its members sits, as the file itself says.</summary>
        public int SizeOf(string type)
        {
            TagType found = Find(type);
            return found == null ? -1 : found.Size;
        }

        public int OffsetOf(string type, string member)
        {
            TagType found = Find(type);
            return found == null ? -1 : found.OffsetOf(member);
        }

        /// <summary>
        /// Where a member of an inline member sits, counted from the start of the outer object - a
        /// rigid body's friction is <c>material.friction</c>, its shape <c>collidable.shape</c>.
        /// -1 if any step is missing.
        /// </summary>
        public int OffsetOfPath(string type, params string[] path)
        {
            TagType step = Find(type);
            int offset = 0;
            foreach (string name in path)
            {
                TagMember member = step == null ? null : MemberOf(step, name);
                if (member == null) return -1;
                offset += member.Offset;
                step = member.Type;
            }
            return offset;
        }

        /// <summary>A type's index in this file's table, or -1. Indices are per file: never carry one over.</summary>
        public int TypeIndex(string type)
        {
            TagType found = Find(type);
            return found == null ? -1 : found.Index;
        }

        public bool HasType(string type)
        {
            TagType found = Find(type);
            return found != null && found.Size > 0;
        }

        /// <summary>How a type wants aligning in the data, as the file says (walking up to a base that says).</summary>
        public int AlignOf(string type)
        {
            int depth = 0;
            for (TagType step = Find(type); step != null && depth++ < 64; step = step.Parent)
                if (step.Alignment > 0) return step.Alignment;

            return 0;
        }

        /// <summary>
        /// What an array or pointer member holds: the <c>tT</c> argument of its declared type -
        /// <c>hkArray&lt;hkInt16&gt;</c> gives hkInt16. -1 if the member is not a template.
        /// </summary>
        public int ElementTypeOf(string type, string member)
        {
            TagType owner = Find(type);
            TagMember found = owner == null ? null : MemberOf(owner, member);
            if (found == null || found.Type == null) return -1;

            foreach (TagTemplate argument in found.Type.Templates)
                if (argument.IsType && argument.Name == "tT") return argument.Value;

            return -1;
        }

        /// <summary>The ITEM word for an object of this type: the object flag and the type's index.</summary>
        public uint ObjectWord(string type)
        {
            int index = TypeIndex(type);
            return index <= 0 ? 0 : 0x10000000u | (uint)index;
        }

        /// <summary>The ITEM word for what an array member points at: the array flag and the element type.</summary>
        public uint ArrayWord(string type, string member)
        {
            int element = ElementTypeOf(type, member);
            return element <= 0 ? 0 : 0x20000000u | (uint)element;
        }

        /// <summary>The ITEM word for a string's characters, which are an array of char.</summary>
        public uint StringWord()
        {
            int index = TypeIndex("char");
            return index <= 0 ? 0 : 0x20000000u | (uint)index;
        }

        /// <summary>
        /// Point a member at a brand new array (or string) item, never at an existing one. This is the
        /// call for a copied object: its bytes still carry the template's item indices, and
        /// <see cref="SetArray"/> would MOVE the template's own array to wherever the copy's goes.
        /// An empty array is a null word with no item and no patch entry, as retail writes it.
        /// </summary>
        public bool NewArray(int fieldOffset, int elementOffset, int count, uint word, int patchGroup)
        {
            byte[] data = Data;
            int start = DataAt;
            if (fieldOffset < 0 || start + fieldOffset + 8 > data.Length)
                return false;

            if (count <= 0)
            {
                WriteIndex(data, start + fieldOffset, 0);
                Group(patchGroup, false)?.Offsets.Remove(fieldOffset);
                return true;
            }

            if (word == 0 || patchGroup <= 0)
                return false;

            int index = AddItem(word, elementOffset, count);
            WriteIndex(data, start + fieldOffset, index);
            AddPatch(patchGroup, fieldOffset);
            return true;
        }

        /// <summary>A string member, followed to its characters. Null when the pointer is.</summary>
        public string ReadString(int fieldOffset)
        {
            return Text(fieldOffset, 0);
        }

        /// <summary>
        /// Everything an edit can change outside the data: the items (whose offsets and counts are
        /// moved in place), the patch groups and the bodies read per system. A writer that fails half
        /// way puts this back along with the payload, or the file keeps items and pointers into data
        /// that no longer exists - which reloads with no physics systems at all.
        /// </summary>
        public object Snapshot()
        {
            return new TagState()
            {
                Items = _items.Select(o => new TagItem() { Type = o.Type, Offset = o.Offset, Count = o.Count, IsPointer = o.IsPointer, Word = o.Word }).ToList(),
                Patches = _patches.Select(o => new TagPatchGroup() { Type = o.Type, Offsets = new HashSet<int>(o.Offsets) }).ToList(),
                Bodies = _bodiesBySystem.ToDictionary(o => o.Key, o => new List<HavokPackfile.RigidBodyInfo>(o.Value)),

                /* The type table only ever grows, and a type is never changed once it is in it, so
                 * copies of the lists are a complete record of it. */
                Types = new List<TagType>(_types),
                TypeNames = new List<string>(_typeNames),
                MemberNames = new List<string>(_memberNames),
                BodyOrder = new List<int>(_bodyOrder),
                Hashes = new List<KeyValuePair<int, uint>>(_hashes),
                HashesChanged = _hashesChanged,
                Layout = CopyLayout(_owner?.Layout, null),
            };
        }

        public void Restore(object snapshot)
        {
            if (!(snapshot is TagState state))
                return;

            _items.Clear();
            _items.AddRange(state.Items.Select(o => new TagItem() { Type = o.Type, Offset = o.Offset, Count = o.Count, IsPointer = o.IsPointer, Word = o.Word }));
            _patches.Clear();
            _patches.AddRange(state.Patches.Select(o => new TagPatchGroup() { Type = o.Type, Offsets = new HashSet<int>(o.Offsets) }));
            _bodiesBySystem.Clear();
            foreach (KeyValuePair<int, List<HavokPackfile.RigidBodyInfo>> bodies in state.Bodies)
                _bodiesBySystem[bodies.Key] = new List<HavokPackfile.RigidBodyInfo>(bodies.Value);

            if (state.Types != null && _types.Count != state.Types.Count)
            {
                _types.Clear();
                _types.AddRange(state.Types);
                _typeNames.Clear();
                _typeNames.AddRange(state.TypeNames);
                _memberNames.Clear();
                _memberNames.AddRange(state.MemberNames);
                _bodyOrder.Clear();
                _bodyOrder.AddRange(state.BodyOrder);
                _hashes.Clear();
                _hashes.AddRange(state.Hashes);
                _signatures = null;
                _compound = null;
                if (_owner != null && state.Layout != null)
                    CopyLayout(state.Layout, _owner.Layout);
            }

            //Hashes can change without a type being added, so they are put back regardless
            if (state.Hashes != null)
            {
                _hashes.Clear();
                _hashes.AddRange(state.Hashes);
                _hashesChanged = state.HashesChanged;
            }

            _itemAt = null;
            _classAt = null;
        }

        private sealed class TagState
        {
            public List<TagItem> Items;
            public List<TagPatchGroup> Patches;
            public Dictionary<int, List<HavokPackfile.RigidBodyInfo>> Bodies;

            public List<TagType> Types;
            public List<string> TypeNames;
            public List<string> MemberNames;
            public List<int> BodyOrder;
            public List<KeyValuePair<int, uint>> Hashes;
            public bool HashesChanged;
            public HavokPackfile.ShapeLayout Layout;
        }

        /// <summary>
        /// Copy the packfile's shape offsets, which adding a type re-reads - every field, so one added
        /// to the layout later is kept too. Copies into a new layout when <paramref name="to"/> is null.
        /// </summary>
        private static HavokPackfile.ShapeLayout CopyLayout(HavokPackfile.ShapeLayout from, HavokPackfile.ShapeLayout to)
        {
            if (from == null) return null;
            to = to ?? new HavokPackfile.ShapeLayout();
            foreach (System.Reflection.FieldInfo field in typeof(HavokPackfile.ShapeLayout).GetFields())
                if (!field.IsStatic && !field.IsInitOnly) field.SetValue(to, field.GetValue(from));
            return to;
        }

        /// <summary>
        /// Forget every item and pointer, keeping the schema - for writing a file's whole object graph
        /// from scratch against its own type table. Item 0 is the null item every file opens with.
        /// </summary>
        public void ResetIndex()
        {
            _items.Clear();
            _items.Add(new TagItem() { Word = 0, Offset = 0, Count = 0 });
            _patches.Clear();
            _bodiesBySystem.Clear();
            _itemAt = null;
            _classAt = null;
        }

        /// <summary>
        /// Let the packfile's own views see items added since <paramref name="firstNewItem"/> without
        /// re-reading everything: an object entry per new item, and the pointer fixups rebuilt from
        /// the patch table. A full re-read would replace every PhysicsSystem and compound view,
        /// breaking whatever already holds one (PHYSICS.MAP rows, pickers, the importer itself).
        /// </summary>
        public void RegisterNewItems(HavokPackfile target, int firstNewItem)
        {
            for (int i = Math.Max(1, firstNewItem); i < _items.Count; i++)
                if (_items[i].Type != null)
                    target.Objects.Add(new HavokPackfile.PackfileObject()
                    {
                        DataOffset = (uint)_items[i].Offset,
                        ClassName = _items[i].Type.Name,
                        Class = Classify(_items[i].Type.Name),
                    });

            target.GlobalFixups.Clear();
            ReadFixups(target);
            _classAt = null;
        }

        /// <summary>
        /// A view of a physics system appended at <paramref name="systemOffset"/>, with its bodies
        /// read so <see cref="RigidBodies"/> answers for it like for a retail one.
        /// </summary>
        public HavokPackfile.PhysicsSystem AddSystemView(int systemIndex, int systemOffset)
        {
            TagType systemType = Find("hkpPhysicsSystem");
            TagType worldObject = Find("hkpWorldObject");
            if (systemType == null) return null;

            _classAt = null;
            List<HavokPackfile.RigidBodyInfo> bodies = new List<HavokPackfile.RigidBodyInfo>();
            foreach (int body in Elements(ArrayItem(systemOffset, systemType.OffsetOf("rigidBodies"))))
                if (body >= 0)
                    bodies.Add(ReadRigidBody(body, worldObject));
            _bodiesBySystem[systemIndex] = bodies;

            return new HavokPackfile.PhysicsSystem()
            {
                SystemIndex = systemIndex,
                DataOffset = (uint)systemOffset,
                Name = Text(systemOffset, systemType.OffsetOf("name")),
            };
        }

        /// <summary>
        /// Register a copy of an existing object that has already been written into the data. An object
        /// only exists as far as a tagfile is concerned if an item claims it, and any pointer inside it
        /// has to be listed again at its new address - otherwise the copy loads with null members.
        /// <para>
        /// The copy's pointer words still hold the template's item indices. Object pointers may share,
        /// but an array or string item has one owner: point the copy's at fresh ones with
        /// <see cref="NewArray"/>, never <see cref="SetArray"/>, which would move the template's.
        /// </para>
        /// </summary>
        public bool CloneObject(uint sourceOffset, uint destOffset, int size)
        {
            int index = ItemIndexAt((int)sourceOffset);
            if (index <= 0)
                return false;

            TagItem template = _items[index];
            _items.Add(new TagItem()
            {
                Word = template.Word,
                Type = template.Type,
                IsPointer = template.IsPointer,
                Offset = (int)destOffset,
                Count = template.Count,
            });

            if (_itemAt != null && !_itemAt.ContainsKey((int)destOffset))
                _itemAt[(int)destOffset] = _items.Count - 1;
            if (_classAt != null && template.Type != null && !_classAt.ContainsKey((int)destOffset))
                _classAt[(int)destOffset] = template.Type.Name;

            foreach (TagPatchGroup group in _patches)
            {
                List<int> inside = null;
                foreach (int offset in group.Offsets)
                    if (offset >= sourceOffset && offset < sourceOffset + size)
                        (inside ?? (inside = new List<int>())).Add(offset);

                if (inside == null) continue;
                foreach (int offset in inside)
                    group.Offsets.Add((int)destOffset + (offset - (int)sourceOffset));
            }

            return true;
        }

        /// <summary>
        /// Forget that a word held a pointer. Called when the instances it belonged to are about to be
        /// orphaned, so repeated edits do not leave the patch table growing with dead entries.
        /// </summary>
        public void ClearPointer(int fieldOffset, int patchGroup)
        {
            Group(patchGroup, false)?.Offsets.Remove(fieldOffset);
        }

        private TagPatchGroup Group(int patchGroup, bool create)
        {
            foreach (TagPatchGroup group in _patches)
                if (group.Type == patchGroup) return group;

            if (!create)
                return null;

            TagPatchGroup added = new TagPatchGroup() { Type = patchGroup };
            _patches.Add(added);
            return added;
        }

        private static void WriteIndex(byte[] data, int at, int index)
        {
            byte[] written = BitConverter.GetBytes((ulong)index);
            Buffer.BlockCopy(written, 0, data, at, 8);
        }

        private Dictionary<int, int> _itemAt;

        /// <summary>The item that owns an object at a data offset. Only objects reached by pointer are
        /// indexed, because those are the only ones a pointer can name - and unlike array items, they
        /// never move.</summary>
        private int ItemIndexAt(int offset)
        {
            if (_itemAt == null)
            {
                _itemAt = new Dictionary<int, int>();
                for (int i = 1; i < _items.Count; i++)
                    if (_items[i].IsPointer && !_itemAt.ContainsKey(_items[i].Offset))
                        _itemAt[_items[i].Offset] = i;
            }

            if (_itemAt.TryGetValue(offset, out int found))
                return found;

            for (int i = 1; i < _items.Count; i++)
                if (_items[i].Offset == offset)
                    return i;

            return -1;
        }

        /// <summary>Add an item shaped like the ones an existing group already points at.</summary>
        private int CloneItemFor(int patchGroup)
        {
            byte[] data = Data;
            int start = DataAt;

            foreach (TagPatchGroup group in _patches)
            {
                if (group.Type != patchGroup) continue;

                foreach (int source in group.Offsets)
                {
                    if (source < 0 || start + source + 8 > data.Length) continue;

                    int index = (int)BitConverter.ToUInt64(data, start + source);
                    if (index <= 0 || index >= _items.Count) continue;

                    TagItem template = _items[index];
                    _items.Add(new TagItem()
                    {
                        Word = template.Word,
                        Type = template.Type,
                        IsPointer = template.IsPointer,
                    });
                    return _items.Count - 1;
                }
            }

            return -1;
        }

        private void AddPatch(int patchGroup, int offset)
        {
            Group(patchGroup, true).Offsets.Add(offset);
        }

        /// <summary>
        /// Re-emit the file around a data payload that may have grown. Every chunk that describes the
        /// schema is copied through unchanged unless types or hashes were added (then TYPE is written
        /// from the model); DATA, ITEM and PTCH are written from what we hold.
        /// </summary>
        public byte[] ToBytes(byte[] payload)
        {
            if (_file == null || payload == null)
                return null;

            //The objects in the data are aligned, so the payload has to stay a whole number of blocks
            if ((payload.Length & 15) != 0)
            {
                byte[] padded = new byte[(payload.Length + 15) & ~15];
                Buffer.BlockCopy(payload, 0, padded, 0, payload.Length);
                payload = padded;
            }

            return EmitChunk(0, payload);
        }

        private byte[] EmitChunk(int at, byte[] payload)
        {
            uint header = (uint)((_file[at] << 24) | (_file[at + 1] << 16) | (_file[at + 2] << 8) | _file[at + 3]);
            string name = Encoding.ASCII.GetString(_file, at + 4, 4);
            int size = (int)(header & 0x3FFFFFFF);

            byte[] body;
            if ((header & 0x40000000) != 0)
            {
                switch (name)
                {
                    case "DATA": body = payload; break;
                    case "ITEM": body = ItemBytes(); break;
                    case "PTCH": body = PatchBytes(); break;
                    default:
                        //The type table is written out again only once types or hashes have been added to it
                        body = TypesAdded || _hashesChanged ? TypeChunk(name) : null;
                        if (body != null) break;

                        body = new byte[size - 8];
                        Buffer.BlockCopy(_file, at + 8, body, 0, body.Length);
                        break;
                }
            }
            else
            {
                List<byte[]> children = new List<byte[]>();
                int child = at + 8, end = at + size, total = 0;
                while (child + 8 <= end)
                {
                    int childSize = (int)((uint)((_file[child] << 24) | (_file[child + 1] << 16)
                        | (_file[child + 2] << 8) | _file[child + 3]) & 0x3FFFFFFF);
                    if (childSize < 8 || child + childSize > end)
                        break;

                    byte[] written = EmitChunk(child, payload);
                    children.Add(written);
                    total += written.Length;
                    child += childSize;
                }

                body = new byte[total];
                int cursor = 0;
                foreach (byte[] written in children)
                {
                    Buffer.BlockCopy(written, 0, body, cursor, written.Length);
                    cursor += written.Length;
                }
            }

            byte[] chunk = new byte[body.Length + 8];
            uint length = (uint)chunk.Length | (header & 0xC0000000u);
            chunk[0] = (byte)(length >> 24);
            chunk[1] = (byte)(length >> 16);
            chunk[2] = (byte)(length >> 8);
            chunk[3] = (byte)length;
            Encoding.ASCII.GetBytes(name, 0, 4, chunk, 4);
            Buffer.BlockCopy(body, 0, chunk, 8, body.Length);
            return chunk;
        }

        private byte[] ItemBytes()
        {
            byte[] written = new byte[_items.Count * 12];
            for (int i = 0; i < _items.Count; i++)
            {
                int at = i * 12;
                Buffer.BlockCopy(BitConverter.GetBytes(_items[i].Word), 0, written, at, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(_items[i].Offset), 0, written, at + 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(_items[i].Count), 0, written, at + 8, 4);
            }
            return written;
        }

        private byte[] PatchBytes()
        {
            //Retail writes the groups in type order with each group's offsets ascending - and never an
            //empty one, which an edit can leave behind by retiring every pointer a group held
            List<TagPatchGroup> ordered = _patches.Where(o => o.Offsets.Count != 0).OrderBy(o => o.Type).ToList();

            int length = 0;
            foreach (TagPatchGroup group in ordered)
                length += 8 + group.Offsets.Count * 4;

            byte[] written = new byte[length];
            int cursor = 0;
            foreach (TagPatchGroup group in ordered)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(group.Type), 0, written, cursor, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(group.Offsets.Count), 0, written, cursor + 4, 4);
                cursor += 8;

                List<int> offsets = group.Offsets.ToList();
                offsets.Sort();
                foreach (int offset in offsets)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(offset), 0, written, cursor, 4);
                    cursor += 4;
                }
            }

            return written;
        }

        #endregion

        #region IMPORTING TYPES

        /* Each level declares only the types its own objects use, so an object copied from another
         * level can need a type the destination has never heard of - frontend's physics declares no
         * hkpListShape, torrens' none of airport's capsules. Those definitions are copied across from
         * the source file, appended to our table so no existing index moves.
         *
         * How the TYPE chunk is laid out, measured over every shipped tagfile (6 iOS + 72 Switch level
         * files, 11,657 animation sections, 653 skeleton-folder blobs - all hold, no exceptions):
         *   - sub-chunks TPTR TSTR TNA1 FSTR TBDY THSH TPAD, each a whole number of 4 bytes, padded
         *     with the fewest zeros that gets there; every varint in the smallest width that holds it
         *   - TPTR is 8 zero bytes per TNA1 entry, the null type included: 8 * (types + 1)
         *   - TSTR and FSTR hold each string once, and in the order the bodies first use them: walk
         *     TBDY in its own order, taking a type's name then its argument names (TSTR), its member
         *     names (FSTR). Nothing unused is kept
         *   - TNA1 is types + 1, then per type its name and arguments; a type argument may name a
         *     later index as freely as an earlier one, and so may a parent or a member
         *   - TBDY has exactly one body per type, nearly in index order (one or two early swaps)
         *   - a sub type (flag 0x2) and an interface's first value are type indices; the sub type
         *     equals the tT argument wherever there is one. Flag 0x80 never occurs
         *   - THSH hashes exactly the types the ITEM and PTCH tables use, one hash per type that is
         *     the same in every file carrying it; TPAD is always empty
         * Appended types follow the same rules: new indices at the end in the source's own order,
         * their bodies after the existing ones, strings the table lacks after its existing strings,
         * the source's hash for each one it hashes. */

        //Whether writing our table back out from the model reproduces the file's own bytes; null until asked
        private bool? _typeModelExact;

        /// <summary>
        /// True once types have been added. Until then the TYPE chunk is copied through byte for byte;
        /// after, its sub-chunks are written from the model.
        /// </summary>
        public bool TypesAdded { get { return _types.Count > _originalTypeCount; } }

        /// <summary>How many types this file declares.</summary>
        public int TypeCount { get { return _types.Count; } }

        private sealed class TypePlan
        {
            public string Refusal;
            public int[] Map;                           //source index -> ours, -1 where we have none yet
            public List<int> Added = new List<int>();   //source indices to append, in the order they will be
        }

        /// <summary>
        /// Why <paramref name="sourceTypes"/> (indices in <paramref name="source"/>) could not be
        /// brought across into this file, or null when they can - including everything they depend on.
        /// Changes nothing.
        /// </summary>
        public string CanImportTypes(HavokTagfile source, IEnumerable<int> sourceTypes)
        {
            return Plan(source, sourceTypes).Refusal;
        }

        /// <summary>
        /// Append to this file's type table every type in <paramref name="sourceTypes"/> that it lacks,
        /// along with whatever those depend on - parents, member types, template arguments, sub types,
        /// interfaces - copied from <paramref name="source"/> with every index translated. Existing
        /// types keep their indices. Returns the translation of every source type index into ours
        /// (-1 for those still missing, which are the ones nothing asked for).
        /// Everything is checked before anything changes: a refusal leaves the table untouched.
        /// </summary>
        public int[] ImportTypes(HavokTagfile source, IEnumerable<int> sourceTypes)
        {
            TypePlan plan = Plan(source, sourceTypes);
            if (plan.Refusal != null)
                throw new InvalidOperationException(plan.Refusal);
            if (plan.Added.Count == 0)
                return plan.Map;

            int[] map = (int[])plan.Map.Clone();
            int next = _types.Count + 1;
            foreach (int theirs in plan.Added)
                map[theirs] = next++;

            Dictionary<string, int> typeNameAt = FirstIndexOf(_typeNames);
            Dictionary<string, int> memberNameAt = FirstIndexOf(_memberNames);
            Dictionary<int, uint> hashes = new Dictionary<int, uint>();
            foreach (KeyValuePair<int, uint> hash in source._hashes)
                if (!hashes.ContainsKey(hash.Key)) hashes[hash.Key] = hash.Value;

            //Strings are taken in body order - name, argument names, then member names - as retail does
            List<TagType> added = new List<TagType>();
            foreach (int theirs in plan.Added)
            {
                TagType from = source._types[theirs - 1];
                TagType copy = new TagType()
                {
                    Index = map[theirs],
                    Name = from.Name,
                    NameIndex = Intern(_typeNames, typeNameAt, from.Name),
                    HasBody = true,
                    ParentIndex = Translate(map, from.ParentIndex),
                    Flags = from.Flags,
                    Format = from.Format,
                    SubType = (from.Flags & 0x2) != 0 ? (uint)Translate(map, (int)from.SubType) : from.SubType,
                    Version = from.Version,
                    Size = from.Size,
                    Alignment = from.Alignment,
                    Extra = from.Extra,
                    Attributes = from.Attributes,
                    MemberWordHigh = from.MemberWordHigh,
                };

                foreach (TagTemplate argument in from.Templates)
                    copy.Templates.Add(new TagTemplate()
                    {
                        Name = argument.Name,
                        NameIndex = Intern(_typeNames, typeNameAt, argument.Name),
                        Value = argument.IsType ? Translate(map, argument.Value) : argument.Value,
                    });

                foreach (TagMember member in from.Members)
                    copy.Members.Add(new TagMember()
                    {
                        Name = member.Name,
                        NameIndex = Intern(_memberNames, memberNameAt, member.Name),
                        Flags = member.Flags,
                        Offset = member.Offset,
                        TypeIndex = Translate(map, member.TypeIndex),
                    });

                foreach (uint[] implemented in from.Interfaces)
                    copy.Interfaces.Add(new uint[] { (uint)Translate(map, (int)implemented[0]), implemented[1] });

                added.Add(copy);
            }

            //Only now that every index exists can the references be joined up
            _types.AddRange(added);
            foreach (TagType copy in added)
            {
                TagType parent = TypeAt(copy.ParentIndex);
                copy.Parent = ReferenceEquals(parent, copy) ? null : parent;
                foreach (TagMember member in copy.Members)
                    member.Type = TypeAt(member.TypeIndex);

                _bodyOrder.Add(copy.Index);
            }
            foreach (int theirs in plan.Added)
                if (hashes.TryGetValue(theirs, out uint hash))
                    _hashes.Add(new KeyValuePair<int, uint>(map[theirs], hash));

            TypesChanged();
            return map;
        }

        private static int Translate(int[] map, int theirs)
        {
            return theirs <= 0 ? theirs : map[theirs];
        }

        /* THSH holds a hash for exactly the types the ITEM and PTCH tables use - every retail tagfile
         * (12,388 of them) - and a type's hash is the same in every file. An edit can break that from
         * either side: a port can start using a type this file declared but never used (Switch
         * tech_rnd_hzdlab's swinging signs bring T*<hkpConstraintMotor> into files that declare it
         * unhashed), and a section rebuilt from a template drops the template's extracted motion or
         * annotations while their hashes stay. Once hashes change, TYPE is written from the model. */
        private bool _hashesChanged;

        /// <summary>The types the item and patch tables use: what THSH must hash.</summary>
        private HashSet<int> UsedTypes()
        {
            HashSet<int> used = new HashSet<int>();
            for (int i = 1; i < _items.Count; i++)
                if ((_items[i].Word & 0xFFFFFF) != 0) used.Add((int)(_items[i].Word & 0xFFFFFF));
            foreach (TagPatchGroup group in _patches)
                if (group.Offsets.Count != 0) used.Add(group.Type);
            return used;
        }

        /// <summary>
        /// Give every used type that has no hash the hash <paramref name="donor"/> carries for it,
        /// <paramref name="donorToOurs"/> translating the donor's indices into ours (as
        /// <see cref="MapTypesFrom"/> or <see cref="ImportTypes"/> return). Returns how many were added;
        /// a type the donor does not hash stays as it is.
        /// </summary>
        public int HashUsedTypesFrom(HavokTagfile donor, int[] donorToOurs)
        {
            if (donor == null || donorToOurs == null || !TypeModelExact())
                return 0;

            HashSet<int> hashed = new HashSet<int>(_hashes.Select(o => o.Key));
            HashSet<int> missing = new HashSet<int>(UsedTypes().Where(o => !hashed.Contains(o)));
            if (missing.Count == 0)
                return 0;

            int added = 0;
            foreach (KeyValuePair<int, uint> theirs in donor._hashes)
            {
                if (theirs.Key <= 0 || theirs.Key >= donorToOurs.Length) continue;
                int ours = donorToOurs[theirs.Key];
                if (ours <= 0 || !missing.Remove(ours)) continue;
                _hashes.Add(new KeyValuePair<int, uint>(ours, theirs.Value));
                added++;
            }
            if (added != 0)
                _hashesChanged = true;
            return added;
        }

        /// <summary>Drop the hashes of types nothing uses any more. Returns how many went.</summary>
        public int DropUnusedHashes()
        {
            if (!TypeModelExact())
                return 0;

            HashSet<int> used = UsedTypes();
            int removed = _hashes.RemoveAll(o => !used.Contains(o.Key));
            if (removed != 0)
                _hashesChanged = true;
            return removed;
        }

        private static Dictionary<string, int> FirstIndexOf(List<string> strings)
        {
            Dictionary<string, int> at = new Dictionary<string, int>(strings.Count, StringComparer.Ordinal);
            for (int i = 0; i < strings.Count; i++)
                if (!at.ContainsKey(strings[i])) at[strings[i]] = i;
            return at;
        }

        private static int Intern(List<string> strings, Dictionary<string, int> at, string value)
        {
            value = value ?? "";
            if (at.TryGetValue(value, out int found)) return found;
            strings.Add(value);
            at[value] = strings.Count - 1;
            return strings.Count - 1;
        }

        /// <summary>Everything cached off the type table, dropped after it changes.</summary>
        private void TypesChanged()
        {
            _signatures = null;
            _compound = null;
            if (_owner != null)
                ReadLayout(_owner.Layout);
        }

        /// <summary>
        /// Work out which source types have to be appended, and whether they can be. The closure is
        /// walked over everything a body names, since a table with a dangling index would not load.
        /// </summary>
        private TypePlan Plan(HavokTagfile source, IEnumerable<int> sourceTypes)
        {
            TypePlan plan = new TypePlan();
            if (source == null || sourceTypes == null)
            {
                plan.Refusal = "there is no source type table to import from.";
                return plan;
            }
            plan.Map = MapTypesFrom(source);

            HashSet<int> needed = new HashSet<int>();
            Stack<int> pending = new Stack<int>();
            foreach (int theirs in sourceTypes)
            {
                if (theirs < 1 || theirs > source._types.Count)
                {
                    plan.Refusal = "the source names type " + theirs + ", which it does not declare.";
                    return plan;
                }
                pending.Push(theirs);
            }

            while (pending.Count != 0)
            {
                int theirs = pending.Pop();
                if (plan.Map[theirs] >= 0 || !needed.Add(theirs))
                    continue;

                TagType from = source._types[theirs - 1];
                string problem = CannotCopy(source, from);
                if (problem != null)
                {
                    plan.Refusal = "the destination has no " + source.SignatureOf(theirs) + ", and it cannot be added: " + problem;
                    return plan;
                }

                foreach (int dependency in Dependencies(from))
                {
                    if (dependency == 0 || dependency == theirs) continue;
                    if (dependency < 0 || dependency > source._types.Count)
                    {
                        plan.Refusal = "the destination has no " + source.SignatureOf(theirs) + ", and the source's own definition of it names type "
                            + dependency + ", which the source does not declare.";
                        return plan;
                    }
                    pending.Push(dependency);
                }
            }

            if (needed.Count == 0)
                return plan;

            /* Only a table we can write back out exactly may be extended - otherwise the types that
             * were already there would come out different. The same goes for the source, whose
             * bodies are what gets copied. */
            if (!TypeModelExact())
                plan.Refusal = "the destination's type table could not be read completely enough to add to it.";
            else if (!source.TypeModelExact())
                plan.Refusal = "the source's type table could not be read completely enough to copy from it.";
            else if (Version != source.Version)
                plan.Refusal = "the two files were written by different Havok versions (" + source.Version + " into " + Version + ").";
            else if (_types.Count + needed.Count >= 0xFFFFFF)
                plan.Refusal = "the destination's type table would outgrow the 24 bits an item has for a type index.";
            if (plan.Refusal != null)
                return plan;

            plan.Added = needed.OrderBy(o => o).ToList();
            return plan;
        }

        private static IEnumerable<int> Dependencies(TagType type)
        {
            yield return type.ParentIndex;
            foreach (TagTemplate argument in type.Templates)
                if (argument.IsType) yield return argument.Value;
            if ((type.Flags & 0x2) != 0) yield return (int)type.SubType;
            foreach (TagMember member in type.Members)
                yield return member.TypeIndex;
            foreach (uint[] implemented in type.Interfaces)
                yield return (int)implemented[0];
        }

        /// <summary>Why a source type's definition could not be copied faithfully, or null.</summary>
        private static string CannotCopy(HavokTagfile source, TagType type)
        {
            if (!type.HasBody)
                return "the source carries no body for it.";

            //Names are copied as strings, so each has to be one the source actually holds
            bool named = type.NameIndex >= 0 && type.NameIndex < source._typeNames.Count
                && type.Templates.All(o => o.NameIndex >= 0 && o.NameIndex < source._typeNames.Count)
                && type.Members.All(o => o.NameIndex >= 0 && o.NameIndex < source._memberNames.Count);
            if (!named)
                return "its definition names a string the source's tables do not hold.";

            //0x80 (attributes) never occurs in a shipped file, so what its value refers to is unknown
            if ((type.Flags & ~0x7Fu) != 0)
                return "its definition uses flags 0x" + type.Flags.ToString("X") + ", which are not understood well enough to copy.";

            //Every value has to fit the widths the files are measured to use (up to 28 bits)
            const uint limit = 1u << 28;
            if (type.Format >= limit || type.SubType >= limit || type.Version >= limit || type.Extra >= limit
                || (uint)type.Size >= limit || (uint)type.Alignment >= limit || type.MemberWordHigh >= (limit >> 16)
                || type.Members.Count > 0xFFFF)
                return "its definition holds a value too large to write back.";
            foreach (TagTemplate argument in type.Templates)
                if ((uint)argument.Value >= limit) return "its template argument " + argument.Name + " is too large to write back.";
            foreach (TagMember member in type.Members)
                if (member.Flags >= limit || (uint)member.Offset >= limit) return "its member " + member.Name + " is too large to write back.";
            foreach (uint[] implemented in type.Interfaces)
                if (implemented[1] >= limit) return "its interface list holds a value too large to write back.";

            return null;
        }

        /// <summary>
        /// Whether the model holds everything the TYPE chunk says: writing every sub-chunk back out
        /// from it reproduces the file's own bytes. Asked once, before the first type is added.
        /// </summary>
        private bool TypeModelExact()
        {
            if (_typeModelExact.HasValue)
                return _typeModelExact.Value;
            if (_file == null || TypesAdded)
                return false;

            bool exact = true;
            int typeChunks = 0;
            try
            {
                foreach (int[] child in TypeChildren())
                {
                    string name = Encoding.ASCII.GetString(_file, child[0] + 4, 4);
                    byte[] written = TypeChunk(name);
                    if (written == null || written.Length != child[1] - 8) { exact = false; break; }
                    for (int i = 0; i < written.Length && exact; i++)
                        if (written[i] != _file[child[0] + 8 + i]) exact = false;
                    if (!exact) break;
                    typeChunks++;
                }
            }
            catch (InvalidOperationException)
            {
                exact = false;      //a value too wide to write back
            }

            _typeModelExact = exact && typeChunks != 0;
            return _typeModelExact.Value;
        }

        /// <summary>The chunks inside TYPE, each as { header offset, size including the header }.</summary>
        private List<int[]> TypeChildren()
        {
            List<int[]> found = new List<int[]>();
            int at = 8, end = Math.Min(_file.Length, (int)(ChunkHeader(0) & 0x3FFFFFFF));
            while (at + 8 <= end)
            {
                uint header = ChunkHeader(at);
                int size = (int)(header & 0x3FFFFFFF);
                if (size < 8 || at + size > end) break;

                if ((header & 0x40000000) == 0 && Encoding.ASCII.GetString(_file, at + 4, 4) == "TYPE")
                {
                    for (int child = at + 8; child + 8 <= at + size;)
                    {
                        int childSize = (int)(ChunkHeader(child) & 0x3FFFFFFF);
                        if (childSize < 8 || child + childSize > at + size) break;
                        found.Add(new int[] { child, childSize });
                        child += childSize;
                    }
                }
                at += size;
            }
            return found;
        }

        private uint ChunkHeader(int at)
        {
            return (uint)((_file[at] << 24) | (_file[at + 1] << 16) | (_file[at + 2] << 8) | _file[at + 3]);
        }

        /// <summary>
        /// A TYPE sub-chunk's body written from the model, or null for any other chunk. Only used once
        /// types have been added (and to prove, before that, that it would reproduce the file).
        /// </summary>
        private byte[] TypeChunk(string name)
        {
            List<byte> written = new List<byte>();
            switch (name)
            {
                case "TPTR":
                    //Room for a runtime pointer per type, null type included - zeros in every shipped file
                    written.AddRange(_tptr);
                    written.AddRange(new byte[8 * Math.Max(0, _types.Count - _originalTypeCount)]);
                    return written.ToArray();

                case "TSTR":
                    return StringTable(_typeNames);

                case "FSTR":
                    return StringTable(_memberNames);

                case "TNA1":
                    Pack(written, (uint)_types.Count + 1);
                    foreach (TagType type in _types)
                    {
                        Pack(written, (uint)type.NameIndex);
                        Pack(written, (uint)type.Templates.Count);
                        foreach (TagTemplate argument in type.Templates)
                        {
                            Pack(written, (uint)argument.NameIndex);
                            Pack(written, (uint)argument.Value);
                        }
                    }
                    return Padded(written);

                case "TBDY":
                    foreach (int self in _bodyOrder)
                    {
                        TagType type = TypeAt(self);
                        if (type == null) continue;

                        Pack(written, (uint)self);
                        Pack(written, (uint)type.ParentIndex);
                        Pack(written, type.Flags);
                        if ((type.Flags & 0x1) != 0) Pack(written, type.Format);
                        if ((type.Flags & 0x2) != 0) Pack(written, type.SubType);
                        if ((type.Flags & 0x4) != 0) Pack(written, type.Version);
                        if ((type.Flags & 0x8) != 0) { Pack(written, (uint)type.Size); Pack(written, (uint)type.Alignment); }
                        if ((type.Flags & 0x10) != 0) Pack(written, type.Extra);
                        if ((type.Flags & 0x20) != 0)
                        {
                            Pack(written, (uint)type.Members.Count | (type.MemberWordHigh << 16));
                            foreach (TagMember member in type.Members)
                            {
                                Pack(written, (uint)member.NameIndex);
                                Pack(written, member.Flags);
                                Pack(written, (uint)member.Offset);
                                Pack(written, (uint)member.TypeIndex);
                            }
                        }
                        if ((type.Flags & 0x40) != 0)
                        {
                            Pack(written, (uint)type.Interfaces.Count);
                            foreach (uint[] implemented in type.Interfaces) { Pack(written, implemented[0]); Pack(written, implemented[1]); }
                        }
                        if ((type.Flags & 0x80) != 0) Pack(written, type.Attributes);
                    }
                    return Padded(written);

                case "THSH":
                    Pack(written, (uint)_hashes.Count);
                    foreach (KeyValuePair<int, uint> hash in _hashes)
                    {
                        Pack(written, (uint)hash.Key);
                        written.AddRange(BitConverter.GetBytes(hash.Value));
                    }
                    return Padded(written);

                case "TPAD":
                    return (byte[])_tpad.Clone();

                default:
                    return null;
            }
        }

        private static byte[] StringTable(List<string> strings)
        {
            List<byte> written = new List<byte>();
            foreach (string s in strings)
            {
                written.AddRange(Encoding.ASCII.GetBytes(s ?? ""));
                written.Add(0);
            }
            return Padded(written);
        }

        private static byte[] Padded(List<byte> written)
        {
            while ((written.Count & 3) != 0) written.Add(0);
            return written.ToArray();
        }

        /// <summary>The varint <see cref="Packed"/> reads, in the smallest width that holds it, as retail writes them.</summary>
        private static void Pack(List<byte> to, uint value)
        {
            if (value < 0x80) { to.Add((byte)value); return; }
            if (value < 0x4000) { to.Add((byte)(0x80 | (value >> 8))); to.Add((byte)value); return; }
            if (value < 0x200000) { to.Add((byte)(0xC0 | (value >> 16))); to.Add((byte)(value >> 8)); to.Add((byte)value); return; }
            if (value < 0x10000000) { to.Add((byte)(0xE0 | (value >> 24))); to.Add((byte)(value >> 16)); to.Add((byte)(value >> 8)); to.Add((byte)value); return; }

            //No shipped file needs the five byte form, so its first byte is unmeasured - never guess it
            throw new InvalidOperationException("A Havok tagfile value of " + value + " is too large to write.");
        }

        #endregion

        #region SHARED

        /// <summary>
        /// gravityFactor, the dampings and friends are hkHalf16s - which, despite the name, are not
        /// IEEE halves but the top sixteen bits of a float, exactly as the PC's 2012 hkHalf: 0x3F80 is
        /// 1.0 (read as an IEEE half it came out 1.875). Every retail body's bits equal its PC twin's.
        /// </summary>
        private float Half(int at)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes((uint)BitConverter.ToUInt16(Data, at) << 16), 0);
        }

        #endregion
    }
}

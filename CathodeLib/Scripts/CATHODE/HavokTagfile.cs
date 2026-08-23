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
    /// the payload has grown to. Everything that describes the schema is copied through untouched, so
    /// a file that is loaded and saved unchanged comes back out byte for byte.
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

            /// <summary>Its 1-based index in the file's own type table, which PTCH groups are keyed by.</summary>
            public int Index;

            public List<TagMember> Members = new List<TagMember>();

            /* Dozens of types share a name - there are 100 called "T*" and as many called "hkArray" -
             * and only their template arguments tell them apart. Nothing needs them to read a file,
             * but copying objects between two files does: an index only means something in the file
             * it came from, so each type needs an identity both files can agree on. */
            public List<TagTemplate> Templates = new List<TagTemplate>();

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

                TagType parent = TypeAt((int)Packed(ref cursor));
                type.Parent = ReferenceEquals(parent, type) ? null : parent;
                uint flags = Packed(ref cursor);

                if ((flags & 0x1) != 0) Packed(ref cursor);   //format
                if ((flags & 0x2) != 0) Packed(ref cursor);   //sub type
                if ((flags & 0x4) != 0) Packed(ref cursor);   //version
                if ((flags & 0x8) != 0)
                {
                    type.Size = (int)Packed(ref cursor);
                    Packed(ref cursor);                       //alignment
                }
                if ((flags & 0x10) != 0) Packed(ref cursor);  //unknown

                if ((flags & 0x20) != 0)
                {
                    /* At least one type (hkPropertyId in the shipped files) carries a field here that
                     * the flags do not announce, which makes the count read as nonsense. A member count
                     * is always small, so step over anything that plainly is not one. */
                    int members = (int)Packed(ref cursor);
                    while (members > 256 && cursor < at + length)
                        members = (int)Packed(ref cursor);
                    if (members > 256)
                        return;

                    for (int m = 0; m < members; m++)
                    {
                        TagMember member = new TagMember();
                        int nameIndex = (int)Packed(ref cursor);
                        member.Name = nameIndex >= 0 && nameIndex < names.Count ? names[nameIndex] : "";
                        Packed(ref cursor);                    //member flags
                        member.Offset = (int)Packed(ref cursor);
                        member.Type = TypeAt((int)Packed(ref cursor));
                        type.Members.Add(member);

                        if (cursor > at + length) return;
                    }
                }

                if ((flags & 0x40) != 0)
                {
                    int interfaces = (int)Packed(ref cursor);
                    for (int n = 0; n < interfaces && cursor < at + length; n++) { Packed(ref cursor); Packed(ref cursor); }
                }
                if ((flags & 0x80) != 0) Packed(ref cursor);  //attributes

                if (cursor > at + length) return;
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
                        info.MotionType = data[start + at + motionAt + typeAt];

                    int inertiaAt = motion.OffsetOf("inertiaAndMassInv");
                    if (inertiaAt >= 0 && start + at + motionAt + inertiaAt + 16 <= data.Length)
                    {
                        int w = start + at + motionAt + inertiaAt;
                        info.InertiaInvLocal = new Vector3(
                            BitConverter.ToSingle(data, w), BitConverter.ToSingle(data, w + 4), BitConverter.ToSingle(data, w + 8));
                        info.MassInv = BitConverter.ToSingle(data, w + 12);
                        info.Mass = info.MassInv == 0 ? float.PositiveInfinity : 1.0f / info.MassInv;
                    }

                    int gravityAt = motion.OffsetOf("gravityFactor");
                    if (gravityAt >= 0 && start + at + motionAt + gravityAt + 2 <= data.Length)
                        info.GravityFactor = Half(start + at + motionAt + gravityAt);
                }
            }

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

            int proxy = 0;
            foreach (TagItem item in _items)
            {
                if (item.Type == null || !item.Type.Is("hkpStaticCompoundShape")) continue;

                HavokPackfile.StaticCompoundShape shape = new HavokPackfile.StaticCompoundShape()
                {
                    ProxyIndex = proxy++,
                    DataOffset = (uint)item.Offset,
                };

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

                target.StaticCompoundShapes.Add(shape);
            }
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
         * needs re-deriving: the chunks that describe the schema are copied through byte for byte, and
         * only the three that describe the data change - DATA itself, the item table that says where
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

            _items[index].Offset = elementOffset;
            _items[index].Count = count;
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

        public void AddItem(uint word, int offset, int count)
        {
            _items.Add(new TagItem()
            {
                Word = word,
                Type = TypeAt((int)(word & 0xFFFFFF)),
                IsPointer = (word & 0x10000000) != 0,
                Offset = offset,
                Count = count,
            });

            if (_itemAt != null && (word & 0x10000000) != 0 && !_itemAt.ContainsKey(offset))
                _itemAt[offset] = _items.Count - 1;
        }

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
        /// Register a copy of an existing object that has already been written into the data. An object
        /// only exists as far as a tagfile is concerned if an item claims it, and any pointer inside it
        /// has to be listed again at its new address - otherwise the copy loads with null members.
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
        /// schema is copied through unchanged; DATA, ITEM and PTCH are written from what we hold.
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
            //Retail writes the groups in type order with each group's offsets ascending
            List<TagPatchGroup> ordered = _patches.OrderBy(o => o.Type).ToList();

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

        #region SHARED

        /// <summary>gravityFactor and friends are stored as half floats.</summary>
        private float Half(int at)
        {
            ushort bits = BitConverter.ToUInt16(Data, at);
            int sign = (bits >> 15) & 0x1, exponent = (bits >> 10) & 0x1F, mantissa = bits & 0x3FF;

            if (exponent == 0) return (sign == 1 ? -1f : 1f) * (mantissa / 1024f) * (float)Math.Pow(2, -14);
            if (exponent == 31) return mantissa == 0 ? (sign == 1 ? float.NegativeInfinity : float.PositiveInfinity) : float.NaN;

            return (sign == 1 ? -1f : 1f) * (1 + mantissa / 1024f) * (float)Math.Pow(2, exponent - 15);
        }

        #endregion
    }
}

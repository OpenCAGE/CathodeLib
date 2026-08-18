using CATHODE.Animations;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> x_ANIM_CLIP_DB.BIN
    ///
    /// One character's animation clips, grouped into contexts. The file is named after the
    /// hash of <see cref="Character"/>.
    /// </summary>
    public class AnimClipDB : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE | Implementation.SAVE;

        public string Character = "";
        public List<AnimClip> Animations = new List<AnimClip>();
        public List<BlendSet> BlendSets = new List<BlendSet>();
        public List<Context> Contexts = new List<Context>();

        public AnimClipDB(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public AnimClipDB(MemoryStream stream, AnimationStrings strings, string path = "") : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public AnimClipDB(byte[] data, AnimationStrings strings, string path = "") : base(data, path)
        {
            _strings = strings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private AnimationStrings _strings;

        /* Reserved words around the header and at the end of the file. Zero in all 400 retail
         * files, so they carry nothing - preserved anyway in case a modded file uses them. */
        private int _reserved0, _reserved1, _reserved2, _reserved3;

        /* The context name table lists names in an order the contexts themselves don't follow */
        private List<KeyValuePair<string, uint>> _contextNames = new List<KeyValuePair<string, uint>>();

        /* Lookup pair order per table, in the order the tables appear. A handful of files list the
         * same clip name twice, and which copy comes first isn't derivable from the names. */
        private List<List<int>> _slotOrders = new List<List<int>>();
        private int _slotCursor;

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null)
                return false;

            Animations.Clear();
            BlendSets.Clear();
            Contexts.Clear();
            _slotOrders.Clear();

            using (BinaryReader reader = new BinaryReader(stream))
            {
                _reserved0 = reader.ReadInt32();
                _reserved1 = reader.ReadInt32();
                Character = _strings.GetString(reader.ReadUInt32());
                _reserved2 = reader.ReadInt32();

                Animations = ReadClips(reader);
                BlendSets = ReadBlendSets(reader);

                //Context names come first as one table, then each context's own clips and blend sets
                _contextNames = HashTable.Read(reader, (r, n) => new KeyValuePair<string, uint>(n, r.ReadUInt32()), _strings, NextSlotOrder());
                for (int i = 0; i < _contextNames.Count; i++)
                {
                    Contexts.Add(new Context
                    {
                        //The table indexes contexts by value, so find the name pointing at this slot
                        Name = _contextNames.FirstOrDefault(o => o.Value == i).Key,
                        Animations = ReadClips(reader),
                        BlendSets = ReadBlendSets(reader),
                    });
                }

                _reserved3 = reader.ReadInt32();

                return true;
            }
        }

        private List<int> NextSlotOrder()
        {
            List<int> order = new List<int>();
            _slotOrders.Add(order);
            return order;
        }

        private List<int> TakeSlotOrder()
        {
            return _slotCursor < _slotOrders.Count ? _slotOrders[_slotCursor++] : null;
        }

        private List<AnimClip> ReadClips(BinaryReader reader)
        {
            return HashTable.Read(reader, (r, n) => new AnimClip
            {
                Name = n,
                Path = _strings.GetString(r.ReadUInt32()),
                MetadataInstance = r.ReadInt32(),
            }, _strings, NextSlotOrder());
        }

        private List<BlendSet> ReadBlendSets(BinaryReader reader)
        {
            return HashTable.Read(reader, (r, n) => new BlendSet
            {
                Name = n,
                Filename = _strings.GetString(r.ReadUInt32()),
            }, _strings, NextSlotOrder());
        }

        override protected bool SaveInternal()
        {
            byte[] content = ToBytes();
            if (content == null) return false;
            File.WriteAllBytes(_filepath, content);
            return true;
        }

        /// <summary>
        /// Serialise back to the format stored in ANIMATION.PAK.
        /// </summary>
        public byte[] ToBytes()
        {
            _slotCursor = 0;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(_reserved0);
                writer.Write(_reserved1);
                writer.Write(_strings.GetID(Character));
                writer.Write(_reserved2);

                WriteClips(writer, Animations);
                WriteBlendSets(writer, BlendSets);

                //Rebuild the context name table from the load order where it still lines up
                List<KeyValuePair<string, uint>> names = _contextNames.Count == Contexts.Count
                    ? _contextNames
                    : Contexts.Select((x, i) => new KeyValuePair<string, uint>(x.Name, (uint)i)).ToList();
                HashTable.Write(writer, names, x => x.Key, (w, x) => w.Write(x.Value), _strings, TakeSlotOrder());

                for (int i = 0; i < Contexts.Count; i++)
                {
                    WriteClips(writer, Contexts[i].Animations);
                    WriteBlendSets(writer, Contexts[i].BlendSets);
                }

                writer.Write(_reserved3);
                return stream.ToArray();
            }
        }

        private void WriteClips(BinaryWriter writer, List<AnimClip> clips)
        {
            HashTable.Write(writer, clips, x => x.Name, (w, x) =>
            {
                w.Write(_strings.GetID(x.Path));
                w.Write(x.MetadataInstance);
            }, _strings, TakeSlotOrder());
        }

        private void WriteBlendSets(BinaryWriter writer, List<BlendSet> blendSets)
        {
            HashTable.Write(writer, blendSets, x => x.Name, (w, x) => w.Write(_strings.GetID(x.Filename)), _strings, TakeSlotOrder());
        }
        #endregion

        #region STRUCTURES
        public class AnimClip
        {
            public string Name = "";
            public string Path = "";

            /// <summary>Index into the metadata DB in the matching ANIM_CLIP_DB_SEC file.</summary>
            public int MetadataInstance;

            public override string ToString() => Name;
        }

        public class BlendSet
        {
            public string Name = "";
            public string Filename = "";

            public override string ToString() => Name;
        }

        public class Context
        {
            public string Name = "";
            public List<AnimClip> Animations = new List<AnimClip>();
            public List<BlendSet> BlendSets = new List<BlendSet>();

            public override string ToString() => Name;
        }
        #endregion
    }
}

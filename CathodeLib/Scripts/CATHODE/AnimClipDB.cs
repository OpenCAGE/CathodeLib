using CATHODE.Animations;
using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using static System.Collections.Specialized.BitVector32;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> x_ANIM_CLIP_DB.BIN
    /// </summary>
    public class AnimClipDB : CathodeFile
    {
        public List<AnimClip> Animations = new List<AnimClip>();
        public List<Tuple<string, string>> BlendSets = new List<Tuple<string, string>>();
        public List<Context> Contexts = new List<Context>();
        
        public string Character { get; set; }
        
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE;

        public AnimClipDB(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public AnimClipDB(MemoryStream stream, AnimationStrings strings, string path) : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public AnimClipDB(byte[] data, AnimationStrings strings, string path) : base(data, path)
        {
            _strings = strings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private AnimationStrings _strings;

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null || _filepath == null || _filepath == "")
                return false;

            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 8;
                Character = _strings.GetString(reader.ReadUInt32()); //note - the name of the file is this hashed string, followed by _ANIM_CLIP_DB.BIN
                reader.BaseStream.Position += 4;

                //anim_db.cpp line 4482

                Animations = ReadAnimationHashTable(reader);

                BlendSets = HashTable.Read(reader, (r, n) =>
                    new Tuple<string, string>(n, _strings.GetString(r.ReadUInt32()))
                , _strings);

                List<Tuple<string, uint>> contextNames = ReadStringHashTable(reader);
                for (int i = 0; i < contextNames.Count; i++)
                {
                    Context context = new Context();
                    context.Name = contextNames.FirstOrDefault(o => o.Item2 == i)?.Item1; //sometimes this is null - when it's null, the index of the string seems to be a hash?
                    context.Animations = ReadAnimationHashTable(reader);
                    context.BlendSets = HashTable.Read(reader, (r, n) =>
                        new Tuple<string, string>(n, _strings.GetString(r.ReadUInt32()))
                    , _strings);
                    Contexts.Add(context);
                }

                reader.BaseStream.Position += 4;
                return true;
            }
        }

        private List<AnimClip> ReadAnimationHashTable(BinaryReader reader)
        {
            return HashTable.Read(reader, (r, n) => new AnimClip
            {
                Name = n,
                Path = _strings.GetString(r.ReadUInt32()),
                MetadataInstance = r.ReadInt32() //what does this map to
            }, _strings);
        }

        private List<Tuple<string, uint>> ReadStringHashTable(BinaryReader reader)
        {
            return HashTable.Read(reader, (r, n) => 
                new Tuple<string, uint>(n, r.ReadUInt32())
            , _strings);
        }

        override protected bool SaveInternal()
        {
            return false;

            /*
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                writer.Write(new byte[8]); 
                writer.Write(_strings.GetID(Character));
                writer.Write(0);

                WriteHashTable(writer, Animations, (w, item) => {
                    w.Write(_strings.GetID(item.PathLabel));
                    w.Write(item.MetadataInstance);
                });
               
                WriteHashTable(writer, BlendSets, (w, item) => {
                    w.Write(_strings.GetID(item));
                });
               
                WriteHashTable(writer, PatchNames, (w, item) => {
                    w.Write(_strings.GetID(item));
                });
               
                foreach (var context in Contexts)
                {
                    WriteHashTable(writer, context.Animations, (w, item) => {
                        w.Write(_strings.GetID(item.Path));
                        w.Write(item.MetadataInstance);
                    });
                    WriteHashTable(writer, context.BlendSets, (w, item) => {
                        w.Write(_strings.GetID(item));
                    });
                }

                // Write termination
                writer.Write(0);

                return true;
            }
            */
        }

        private void WriteHashTable<T>(BinaryWriter writer, List<T> data, Action<BinaryWriter, T> itemWriter)
        {
            writer.Write(data.Count);
            writer.Write(data.Count);

            // Write hash table entries
            for (int i = 0; i < data.Count; i++)
            {
                writer.Write(Utilities.AnimationHashedString($"item_{i}")); // Generate hash
                writer.Write(i);
            }

            // Write data entries
            foreach (var item in data)
            {
                itemWriter(writer, item);
            }
        }
        #endregion

        #region STRUCTURES
        public struct AnimClip
        {
            public string Name;
            public string Path;
            public int MetadataInstance;
        }

        public class Context
        {
            public string Name;
            public List<AnimClip> Animations = new List<AnimClip>();
            public List<Tuple<string, string>> BlendSets = new List<Tuple<string, string>>();
        }
        #endregion
    }
}

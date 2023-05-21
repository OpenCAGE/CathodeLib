using CATHODE.Scripting;
using CathodeLib;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE.EXPERIMENTAL
{
    /* *.AIS */
    public class MissionSave : CathodeFile
    {
        public static new Implementation Implementation = Implementation.NONE;
        public MissionSave(string path) : base(path) { }

        private Header _header;

        // From the iOS decomp: the saves work with a "leaf and node" system, where you have
        // "node" names saved with their connected "leafs" which acts like a "system" and
        // "parameter" to apply to the system

        // Check CATHODE::SaveState::save_leaf!

        #region FILE_IO
        /* Load the file */
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                _header = Utilities.Consume<Header>(reader);
                switch (_header.VersionNum)
                {
                    case AISType.SAVE:
                        string levelName = Utilities.ReadString(reader.ReadBytes(128));
                        Console.WriteLine("Level Name: " + levelName);
                        string saveName = Utilities.ReadStringAlternating(reader.ReadBytes(256));
                        Console.WriteLine("Save Name: " + levelName);
                        string levelSaveDescriptor = Utilities.ReadString(reader.ReadBytes(160));
                        Console.WriteLine("Localised Save Descriptor: " + levelName);
                        reader.BaseStream.Position += 8;
                        string playlist = Utilities.ReadString(reader.ReadBytes(64));
                        Console.WriteLine("Playlist: " + levelName);

                        reader.BaseStream.Position = 1208;
                        while (true)
                        {
                            if (!ReadEntry(reader)) break;
                        }
                        break;
                }

                reader.BaseStream.Position = _header.save_root_offset;
                while (true)
                {
                    string id = ReadNode(reader);
                    if (_header.VersionNum == AISType.SAVE && id == "temp_entities") break; //This seems to be the last resolvable in SAVE?
                    if (id == null) break;
                }
                Console.WriteLine("Finished root nodes at " + reader.BaseStream.Position);
            }
            return true;
        }

        /* Save the file */
        override protected bool SaveInternal()
        {
            using (BinaryWriter stream = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                stream.BaseStream.SetLength(0);
                Utilities.Write<Header>(stream, _header);
            }
            return true;
        }
        #endregion

        #region HELPERS
        private bool ReadEntry(BinaryReader stream)
        {
            UInt32 type = stream.ReadUInt32();
            if (type == 0) return false;
            switch (type)
            {
                case 55762421:
                    stream.BaseStream.Position += 8;
                break;
                default:
                    UInt32 id = stream.ReadUInt32();
                    int val = stream.ReadInt32();
                    Console.WriteLine(type + ": " + id + " -> " + val);
                    break;
            }
            return true;
        }

        private string ReadNode(BinaryReader stream)
        {
            //Read leaf name
            ShortGuid id = Utilities.Consume<ShortGuid>(stream);
            if (id.ToUInt32() == 0) return null;

            string id_str = id.ToString();
            Console.WriteLine("Reading " + id_str);

            //The root nodes are always 8 in length
            if (id_str == "save_root" || id_str == "progression_root")
            {
                stream.BaseStream.Position += 8;
                return id_str;
            }

            //Read entry header
            byte type = stream.ReadByte();

            if (type == 0x01)
            {
                stream.BaseStream.Position += 1;
                return id_str;
            }

            int offset = stream.ReadInt16();
            byte unk = stream.ReadByte();
            if (unk == 0x01)
                throw new Exception("Unhandled");

            //Read entry contents
            int length = 0;
            switch (type)
            {
                case 0x02:
                    length = 0;
                    break;
                case 0x04:
                    length = 1;
                    break;
                case 0x40:
                case 0x0D:
                    length = offset;
                    break;
                case 0x4D:
                    length = offset + 3;
                    break;
                default:
                    throw new Exception("Unhandled");
            }
            byte[] content = stream.ReadBytes(length);

            switch (id_str)
            {
                case "m_last_saved_level":
                    string lvl = Utilities.ReadString(content);
                    Console.WriteLine("\t" + lvl);
                    break;
            }
            return id_str;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Header
        {
            public fourcc FourCC;
            public AISType VersionNum;

            public ShortGuid unk1;
            public ShortGuid unk2;
            public ShortGuid unk3;

            public int Offset1;
            public int save_root_offset;
            public int Offset3;
        };
        public enum AISType : Int32
        {
            SAVE = 18,
            PROGRESSION = 4
        }
        #endregion
    }
}
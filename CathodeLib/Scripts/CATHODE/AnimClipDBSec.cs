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
using System.Security.Cryptography;
using System.Text;
using static System.Collections.Specialized.BitVector32;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> ANIM_CLIP_DB_SEC_*.BIN
    /// </summary>
    public class AnimClipDBSec : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE;

        public AnimClipDBSec(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public AnimClipDBSec(MemoryStream stream, AnimationStrings strings, string path) : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public AnimClipDBSec(byte[] data, AnimationStrings strings, string path) : base(data, path)
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

            List<string> skeletonDepends = new List<string>();

            using (BinaryReader reader = new BinaryReader(stream))
            {
                int dependsCount = reader.ReadInt32();
                for (int x = 0; x < dependsCount; x++)
                    skeletonDepends.Add(_strings.GetString(reader.ReadUInt32()));

                // Havok PAK buffer
                int hkt_length = reader.ReadInt32();
                byte[] hkt = reader.ReadBytes(hkt_length);
                //m_all_anims_in_section = (hkaAnimationContainer*)hkNativePackfileUtils::loadInPlace(m_loaded_data->get_raw_ptr(), m_buffer_size);

                // ANIMATION_METADATA_DB (void ANIMATION_METADATA_DB::loadInPlace ( char * bin, card32 size ))
                int mddb_length = reader.ReadInt32();
                //byte[] mddb = reader.ReadBytes(mddb_length);
                string mddb = ParseMetadata(new BinaryReader(new MemoryStream(reader.ReadBytes(mddb_length))));
                Console.WriteLine(mddb);

                long position = reader.BaseStream.Position;
                long length = reader.BaseStream.Length ;
                if (position != length)
                    throw new Exception("");

                return true;
            }
        }

        private string ParseMetadata(BinaryReader reader)
        {
            string str = "";
            try
            {
                reader.BaseStream.Position += 4; //MDDB magic

                int count_offsets = reader.ReadInt32();
                List<int> offsets = new List<int>();
                for (int i = 0; i < count_offsets; i++)
                    offsets.Add((int)reader.ReadInt64());

                for (int i = 0; i < offsets.Count; i++)
                {
                    reader.BaseStream.Position = offsets[i];

                    int offset0 = (int)reader.ReadInt64(); //always 0?
                    int offset1 = (int)reader.ReadInt64();
                    int offset2 = (int)reader.ReadInt64();

                    int val1 = reader.ReadInt32(); //usually 0,1,2
                    float val2 = reader.ReadSingle(); //set if val1 isnt 0

                    int position_plus_40 = (int)reader.ReadInt64();
                    int another_position = (int)reader.ReadInt64();

                    int tag_count = reader.ReadInt32();
                    //there is sometimes a number here too
                    reader.BaseStream.Position += 28;
                    for (int C = 0; C < tag_count; C++)
                    {
                        reader.BaseStream.Position += 8;

                        uint tagID = reader.ReadUInt32();
                        string tag = _strings.Entries[tagID];

                        reader.BaseStream.Position += 28;

                        //shouldn't this match AnimTreeDB? AnimationMetadataValue
                        MetadataValueType type = (MetadataValueType)reader.ReadInt32();
                        short requires_convert = reader.ReadInt16();
                        byte can_mirror = reader.ReadByte();
                        byte can_modulate_by_playspeed = reader.ReadByte();

                        reader.BaseStream.Position -= 24;

                        switch (type)
                        {
                            case MetadataValueType.UINT32:
                            case MetadataValueType.INT32:
                                int v = reader.ReadInt32();
                                reader.BaseStream.Position += 12;
                                str += tag + " = " + v + "\n";
                                break;
                            case MetadataValueType.FLOAT32:
                                float f = reader.ReadSingle();
                                reader.BaseStream.Position += 12;
                                str += tag + " = " + f + "\n";
                                break;
                            case MetadataValueType.STRING:
                                uint strHash = reader.ReadUInt32();
                                reader.BaseStream.Position += 12;
                                str += tag + " = " + ((int)strHash != -1 ? _strings.Entries[strHash] : "NONE") + "\n";
                                break;
                            case MetadataValueType.BOOL:
                                bool b = reader.ReadInt32() == 1;
                                reader.BaseStream.Position += 12;
                                str += tag + " = " + b + "\n";
                                break;
                            case MetadataValueType.VECTOR:
                                float x = reader.ReadSingle();
                                float y = reader.ReadSingle();
                                float z = reader.ReadSingle();
                                reader.BaseStream.Position += 4;
                                str += tag + " = (" + x + ", " + y + ", " + z + ")\n";
                                break;
                            default:
                                throw new Exception("Unhandled type!");
                        }

                        reader.BaseStream.Position += 8;
                    }

                    str += "\n";

                    //perhaps m_timeline? (see animation_metadata.cpp line 633/533)
                    // int someOtherOffset1 = (int)reader.ReadInt64(); //this is often the current position, but sometimes zero
                    // int someOtherOffset2 = (int)reader.ReadInt64();
                    // int someOtherOffset3 = (int)reader.ReadInt64();
                }
            }
            catch { }

            return str;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);



                return true;
            }
        }
        #endregion

        #region STRUCTURES

        #endregion
    }
}

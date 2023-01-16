using CATHODE.Assets;
using CathodeLib;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* Loads and/or creates Cathode PAK2 files: ANIMATION and UI */
    public class PAK2 : CathodeFile
    {
        private List<EntryPAK2> _entries = new List<EntryPAK2>();

        public PAK2(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                //Read the header info
                string MagicValidation = "";
                for (int i = 0; i < 4; i++) { MagicValidation += reader.ReadChar(); }
                if (MagicValidation != "PAK2") { reader.Close(); return false; }
                int offsetListBegin = reader.ReadInt32() + 16;
                int entryCount = reader.ReadInt32();
                reader.BaseStream.Position += 4; //Skip "4"

                //Read all file names and create entries
                for (int i = 0; i < entryCount; i++)
                {
                    string ThisFileName = "";
                    for (byte b; (b = reader.ReadByte()) != 0x00;)
                    {
                        ThisFileName += (char)b;
                    }

                    EntryPAK2 NewPakFile = new EntryPAK2();
                    NewPakFile.Filename = ThisFileName;
                    _entries.Add(NewPakFile);
                }

                //Read all file offsets
                reader.BaseStream.Position = offsetListBegin;
                List<int> FileOffsets = new List<int>();
                FileOffsets.Add(offsetListBegin + (entryCount * 4));
                for (int i = 0; i < entryCount; i++)
                {
                    FileOffsets.Add(reader.ReadInt32());
                    _entries[i].Offset = FileOffsets[i];
                }

                //Read in the files to entries
                for (int i = 0; i < entryCount; i++)
                {
                    //Must pass to RemoveLeadingNulls as each file starts with 0-3 null bytes to align files to a 4-byte block reader
                    _entries[i].Content = ExtraBinaryUtils.RemoveLeadingNulls(reader.ReadBytes(FileOffsets[i + 1] - FileOffsets[i]));
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                //Write header
                ExtraBinaryUtils.WriteString("PAK2", writer);
                int OffsetListBegin_New = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    OffsetListBegin_New += _entries[i].Filename.Length + 1;
                }
                writer.Write(OffsetListBegin_New);
                writer.Write(_entries.Count);
                writer.Write(4);

                //Write filenames
                for (int i = 0; i < _entries.Count; i++)
                {
                    ExtraBinaryUtils.WriteString(_entries[i].Filename.Replace("\\", "/"), writer);
                    writer.Write((byte)0x00);
                }

                //Write placeholder offsets for now, we'll correct them after writing the content
                int offsetListBegin = (int)writer.BaseStream.Position;
                for (int i = 0; i < _entries.Count; i++)
                {
                    writer.Write(0);
                }

                //Write files
                for (int i = 0; i < _entries.Count; i++)
                {
                    while (writer.BaseStream.Position % 4 != 0)
                    {
                        writer.Write((byte)0x00);
                    }
                    writer.Write(_entries[i].Content);
                    _entries[i].Offset = (int)writer.BaseStream.Position;
                }

                //Re-write offsets with correct values
                writer.BaseStream.Position = offsetListBegin;
                for (int i = 0; i < _entries.Count; i++)
                {
                    writer.Write(_entries[i].Offset);
                }
            }
            return true;
        }
        #endregion
    }
}
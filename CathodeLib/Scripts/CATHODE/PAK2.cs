using CATHODE.LEGACY.Assets;
using CathodeLib;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* Loads and/or creates Cathode PAK2 files: ANIMATION and UI */
    public class PAK2 : CathodeFile
    {
        //Files within the PAK2 archive
        public List<Entry> Entries = new List<Entry>();

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

                    Entry NewPakFile = new Entry();
                    NewPakFile.Filename = ThisFileName;
                    Entries.Add(NewPakFile);
                }

                //Read all file offsets
                reader.BaseStream.Position = offsetListBegin;
                List<int> FileOffsets = new List<int>();
                FileOffsets.Add(offsetListBegin + (entryCount * 4));
                for (int i = 0; i < entryCount; i++)
                {
                    FileOffsets.Add(reader.ReadInt32());
                    Entries[i].Offset = FileOffsets[i];
                }

                //Read in the files to entries
                for (int i = 0; i < entryCount; i++)
                {
                    //Must pass to RemoveLeadingNulls as each file starts with 0-3 null bytes to align files to a 4-byte block reader
                    Entries[i].Content = Utilities.RemoveLeadingNulls(reader.ReadBytes(FileOffsets[i + 1] - FileOffsets[i]));
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
                Utilities.WriteString("PAK2", writer);
                int OffsetListBegin_New = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    OffsetListBegin_New += Entries[i].Filename.Length + 1;
                }
                writer.Write(OffsetListBegin_New);
                writer.Write(Entries.Count);
                writer.Write(4);

                //Write filenames
                for (int i = 0; i < Entries.Count; i++)
                    Utilities.WriteString(Entries[i].Filename.Replace("\\", "/"), writer, true);

                //Write placeholder offsets for now, we'll correct them after writing the content
                int offsetListBegin = (int)writer.BaseStream.Position;
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(0);
                }

                //Write files
                for (int i = 0; i < Entries.Count; i++)
                {
                    while (writer.BaseStream.Position % 4 != 0)
                    {
                        writer.Write((byte)0x00);
                    }
                    writer.Write(Entries[i].Content);
                    Entries[i].Offset = (int)writer.BaseStream.Position;
                }

                //Re-write offsets with correct values
                writer.BaseStream.Position = offsetListBegin;
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].Offset);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public string Filename = "";
            public int Offset = 0;
            public byte[] Content; //better than a byte list, initialise it with the calculated size
        }
        #endregion
    }
}
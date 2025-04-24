using CathodeLib;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* DATA/UI.PAK & DATA/CHR_INFO.PAK & DATA/GLOBAL/ANIMATION.PAK & DATA/GLOBAL/UI.PAK */
    /* Note: Also supports the PAK2 files in Viking: Battle for Asgard */
    public class PAK2 : CathodeFile
    {
        public List<File> Entries = new List<File>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public PAK2(string path) : base(path) { }

        ~PAK2()
        {
            Entries.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(System.IO.File.OpenRead(_filepath)))
            {
                //Read the header info
                string MagicValidation = "";
                for (int i = 0; i < 4; i++) { MagicValidation += reader.ReadChar(); }
                if (MagicValidation != "PAK2") { reader.Close(); return false; }
                int offsetListBegin = reader.ReadInt32() + 16;
                int entryCount = reader.ReadInt32();
                int alignment = reader.ReadInt32();

                //Read all file names and create entries
                for (int i = 0; i < entryCount; i++)
                {
                    File NewPakFile = new File();
                    NewPakFile.Filename = Utilities.ReadString(reader);
                    Entries.Add(NewPakFile);
                }

                //Read all file offsets
                reader.BaseStream.Position = offsetListBegin;
                List<int> FileOffsets = new List<int>();
                FileOffsets.Add(offsetListBegin + (entryCount * 4));
                for (int i = 0; i < entryCount; i++) FileOffsets.Add(reader.ReadInt32());

                //Read in the files to entries
                for (int i = 0; i < entryCount; i++)
                {
                    Utilities.Align(reader, alignment);
                    Entries[i].Content = reader.ReadBytes(FileOffsets[i + 1] - (int)reader.BaseStream.Position);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(System.IO.File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                Utilities.WriteString("PAK2", writer);
                writer.Write(0);
                writer.Write(Entries.Count);
                writer.Write(4);

                //Write filenames
                for (int i = 0; i < Entries.Count; i++) Utilities.WriteString(Entries[i].Filename, writer, true);

                //Write placeholder offsets for now, we'll correct them after writing the content
                int offsetListBegin = (int)writer.BaseStream.Position;
                for (int i = 0; i < Entries.Count; i++) writer.Write(0);

                //Write files
                List<int> offsets = new List<int>();
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Align(writer, 4);
                    writer.Write(Entries[i].Content);
                    offsets.Add((int)writer.BaseStream.Position);
                }
                Utilities.Align(writer, 4);

                //Re-write offsets with correct values
                writer.BaseStream.Position = 4;
                writer.Write(offsetListBegin - 16);
                writer.BaseStream.Position = offsetListBegin;
                for (int i = 0; i < Entries.Count; i++) writer.Write(offsets[i]);
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class File
        {
            ~File()
            {
                Content = null;
            }

            public string Filename = "";
            public byte[] Content;
        }
        #endregion
    }
}
using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/SOUNDDIALOGUELOOKUPS.DAT */
    public class SoundDialogueLookups : CathodeFile
    {
        //This stores the associated sound and animation hashes for soundbank entries

        public List<Soundbank> Entries = new List<Soundbank>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;
        public SoundDialogueLookups(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4; //version
                int soundbankCount = reader.ReadInt32();
                for (int i = 0; i < soundbankCount; i++)
                {
                    Soundbank bank = new Soundbank()
                    {
                        id = reader.ReadUInt32()
                    };
                    int hashCount = reader.ReadInt32();
                    for (int x = 0;x < hashCount; x++)
                    {
                        Soundbank.ResourceHashes hashes = new Soundbank.ResourceHashes()
                        {
                            SoundHash = reader.ReadUInt32(),
                            AnimationHash = reader.ReadUInt32(),
                        };
                        bank.hashes.Add(hashes);
                    }
                    Entries.Add(bank);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(1);
                writer.Write(Entries.Count);
                foreach (Soundbank soundbank in Entries)
                {
                    writer.Write(soundbank.id);
                    writer.Write(soundbank.hashes.Count);
                    foreach (Soundbank.ResourceHashes hashes in soundbank.hashes)
                    {
                        writer.Write(hashes.SoundHash);
                        writer.Write(hashes.AnimationHash);
                    }
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Soundbank
        {
            public uint id; //This is the hashed soundbank string name (hashed via Utilities.SoundHashedString)
            public List<ResourceHashes> hashes = new List<ResourceHashes>();
            public class ResourceHashes
            {
                public uint SoundHash; //Generated from the wav filename
                public uint AnimationHash; //Can be looked up using animation string table
            }
        };
        #endregion
    }
}
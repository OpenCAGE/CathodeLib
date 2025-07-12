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
    /* DATA/ENV/PRODUCTION/x/WORLD/SOUNDEVENTDATA.DAT */
    public class SoundEventData : CathodeFile
    {
        //This stores all available sound events within soundbanks, along with their associated max attenuation and metadata (parameters)

        public List<Soundbank> Entries = new List<Soundbank>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public SoundEventData(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4; //version
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Soundbank.Event e = new Soundbank.Event();
                    int length = reader.ReadInt32();
                    for (int x = 0; x < length; x++) e.name += reader.ReadChar();
                    length = reader.ReadInt32();
                    for (int x = 0; x < length; x++) e.metadata += reader.ReadChar();

                    e.max_attenuation = reader.ReadSingle();

                    uint soundbankID = reader.ReadUInt32();
                    Soundbank soundbank = Entries.FirstOrDefault(o => o.id == soundbankID);
                    if (soundbank == null)
                    {
                        soundbank = new Soundbank() { id = soundbankID };
                        Entries.Add(soundbank);
                    }
                    soundbank.events.Add(e);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(0);
                writer.Write(0);
                int count = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < Entries[i].events.Count; x++)
                    {
                        writer.Write(Entries[i].events[x].name.Length);
                        Utilities.WriteString(Entries[i].events[x].name, writer);
                        writer.Write(Entries[i].events[x].metadata.Length);
                        Utilities.WriteString(Entries[i].events[x].metadata, writer);
                        writer.Write(Entries[i].events[x].max_attenuation);
                        writer.Write(Entries[i].id);
                        count++;
                    }
                }
                writer.BaseStream.Position = 4;
                writer.Write(count);
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Soundbank
        {
            public uint id; //This is the hashed soundbank string name (hashed via Utilities.SoundHashedString)
            public List<Event> events = new List<Event>();

            public class Event
            {
                public string name = "";
                public string metadata = "";
                public float max_attenuation = 1024.0f;
            }
        };
        #endregion
    }
}
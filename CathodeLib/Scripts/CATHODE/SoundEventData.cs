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
        public List<Soundbank> Entries = new List<Soundbank>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public SoundEventData(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Soundbank.Event e = new Soundbank.Event();
                    int length = reader.ReadInt32();
                    for (int x = 0; x < length; x++) e.name += reader.ReadChar();
                    length = reader.ReadInt32();
                    for (int x = 0; x < length; x++) e.args += reader.ReadChar();
                    reader.BaseStream.Position += 2;
                    e.unknown = reader.ReadInt16();

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
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < Entries[i].events.Count; x++)
                    {
                        writer.Write(Entries[i].events[x].name.Length);
                        Utilities.WriteString(Entries[i].events[x].name, writer);
                        writer.Write(Entries[i].events[x].args.Length);
                        Utilities.WriteString(Entries[i].events[x].args, writer);
                        writer.Write((Int16)0);
                        writer.Write(Entries[i].events[x].unknown);
                        writer.Write(Entries[i].id);
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
            public List<Event> events = new List<Event>();

            public class Event
            {
                public string name;
                public string args;

                public int unknown;
            }
        };
        #endregion
    }
}
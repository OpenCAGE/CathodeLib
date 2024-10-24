using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE.EXPERIMENTAL
{
    /* DATA/ENV/PRODUCTION/x/WORLD/LIGHTS.BIN */
    public class Lights : CathodeFile
    {
        //NOTE: we can read this file in/out, but we don't really know the data yet.

        public List<int> Indexes = new List<int>();
        public List<short[]> Values = new List<short[]>();

        public int UnknownValue0;
        public int UnknownValue1;
        public int UnknownValue2;
        public int UnknownValue3;
        public int UnknownValue4;
        public int UnknownValue5;
        public int UnknownValue6;
        public int UnknownValue7;

        public static new Implementation Implementation = Implementation.NONE;
        public Lights(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 8;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Indexes.Add(reader.ReadInt32()); //I think this is MVR index
                }
                int nextCount = reader.ReadInt16();

                //Assertions
                if (nextCount != (entryCount * 2) - 1)
                {
                    string gdsfdsf = "";
                }

                for (int i = 0; i < nextCount; i++)
                {
                    short[] array = new short[18];
                    for (int x = 0; x < 18; x++)
                        array[x] = reader.ReadInt16();
                    Values.Add(array);

                    //Assertions
                    if (array[17] != 0)
                    {
                        string sdsdfd = "";
                    }
                    if (array[16] != 0 && array[16] != 1)
                    {
                        string sdsdfd = "";
                    }
                    if (array[15] < 0)
                    {
                        string sdsdfd = "";
                    }
                    if (array[14] < 0)
                    {
                        //i think this is an index 
                        string sdsdfd = "";
                    }
                }

                //Additional unknowns
                UnknownValue0 = reader.ReadChar();
                UnknownValue1 = reader.ReadInt32();
                UnknownValue2 = reader.ReadInt32();
                UnknownValue3 = reader.ReadInt32();
                UnknownValue4 = reader.ReadInt32();
                UnknownValue5 = reader.ReadInt32();
                UnknownValue6 = reader.ReadInt32();
                UnknownValue7 = reader.ReadInt16();

                //Assertions
                if (UnknownValue0 != 0 || 
                    UnknownValue1 != 0 || 
                    UnknownValue2 != 0 || 
                    UnknownValue3 != 0 || 
                    UnknownValue4 != 0 || 
                    UnknownValue5 != 0 || 
                    UnknownValue6 != 0 ||
                    UnknownValue7 != 0)
                {
                    string dfdf = "";
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                //TODO: this data is ordered by array entry -4 and then -3 i think

                writer.BaseStream.SetLength(0);
                Utilities.WriteString("ligt", writer);
                writer.Write(4);
                writer.Write(Indexes.Count);
                for (int i = 0; i < Indexes.Count; i++)
                    writer.Write(Indexes[i]);
                writer.Write((Int16)Values.Count);
                for (int i = 0; i < Values.Count; i++)
                {
                    if (Values[i].Length != 18)
                        throw new Exception("Entry was of unexpected length.");

                    for (int x = 0; x < Values[i].Length; x++)
                        writer.Write((Int16)Values[i][x]);
                }

                writer.Write((char)UnknownValue0);
                writer.Write(UnknownValue1);
                writer.Write(UnknownValue2);
                writer.Write(UnknownValue3);
                writer.Write(UnknownValue4);
                writer.Write(UnknownValue5);
                writer.Write(UnknownValue6);
                writer.Write((Int16)UnknownValue7);

            }
            return true;
        }
        #endregion

        #region STRUCTURES

        #endregion
    }
}
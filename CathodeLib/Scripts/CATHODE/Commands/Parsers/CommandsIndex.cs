using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /// <summary>
    /// A composite as the index reader sees it: just its ID and name.
    /// </summary>
    public struct CompositeIndexEntry
    {
        public ShortGuid ID;
        public string Name;

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Reads only the composite table of a COMMANDS.PAK: every composite's ID and name, nothing else.
    /// A full parse of a shipped level takes seconds and gigabytes of graph; this walks the header,
    /// the composite offset table and one string per composite, so a browser can list what a level
    /// holds without loading it.
    /// </summary>
    internal static class CommandsIndex
    {
        public static List<CompositeIndexEntry> Read(byte[] content)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(content)))
            {
                //Three entry point IDs (root, GLOBAL, PAUSEMENU), then the parameter table position and
                //count, then the composite table position and count
                ShortGuid rootId = new ShortGuid(reader);
                reader.BaseStream.Position = 12 + 8;
                int compositeOffsetPos = reader.ReadInt32() * 4;
                int compositeCount = reader.ReadInt32();

                reader.BaseStream.Position = compositeOffsetPos;
                int[] compositeOffsets = Utilities.ConsumeArray<int>(reader, compositeCount);

                List<CompositeIndexEntry> entries = new List<CompositeIndexEntry>(compositeCount);
                for (int i = 0; i < compositeCount; i++)
                {
                    //Each composite block: 4 zero bytes, then the script start offset (top byte is a flag),
                    //the first offset pair, and the ID - the same first steps as the full reader
                    reader.BaseStream.Position = (compositeOffsets[i] * 4) + 4;
                    byte[] startOffsetRaw = reader.ReadBytes(4);
                    startOffsetRaw[3] = 0x00;
                    int scriptStartOffset = BitConverter.ToInt32(startOffsetRaw, 0);
                    reader.BaseStream.Position += 8;
                    ShortGuid id = new ShortGuid(reader);

                    reader.BaseStream.Position = (scriptStartOffset * 4) + 4;
                    string name = Utilities.ReadString(reader);

                    //The same tidy-up the full loader applies (CommandsUtils.SetPrettyNames and the root
                    //rename): a shipped PAK stores names upper-cased and the root as a build machine path,
                    //while the vanilla path table knows the proper-cased name for every shipped composite.
                    //A level OpenCAGE has saved already holds tidy names, and the table lookup then agrees.
                    string prettyPath = CustomTable.Vanilla.CompositePaths.GetPrettyPath(id);
                    if (prettyPath != "") name = prettyPath;
                    name = name.Replace("/", "\\");
                    if (id == rootId)
                    {
                        string[] nameSplit = name.Split('\\');
                        name = nameSplit[nameSplit.Length - 1];
                    }

                    entries.Add(new CompositeIndexEntry() { ID = id, Name = name });
                }
                return entries;
            }
        }
    }
}

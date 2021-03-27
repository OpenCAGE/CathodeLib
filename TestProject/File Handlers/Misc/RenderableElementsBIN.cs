using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestProject.File_Handlers.Misc
{
    //REnDerable elementS
    public class RenderableElementsBIN
    {
        public static alien_reds_bin Load(string FullFilePath)
        {
            alien_reds_bin Result = new alien_reds_bin();
            BinaryReader Stream = new BinaryReader(File.OpenRead(FullFilePath));

            Result.Header = Utilities.Consume<alien_reds_header>(ref Stream);
            // TODO: Seems to be varying length or something weirder.
            Result.Entries = Utilities.ConsumeArray<alien_reds_entry>(ref Stream, Result.Header.EntryCount);

            return Result;
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_reds_header
{
    public int EntryCount;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_reds_entry
{
    public int UnknownZeros0_;
    public int ModelIndex;
    public byte UnknownZeroByte0_;
    public int UnknownZeros1_;
    public int MaterialLibraryIndex;
    public byte UnknownZeroByte1_;
    public int ModelLODIndex; // NOTE: Not sure, looks like it.
    public byte ModelLODPrimitiveCount; // NOTE: Sure it is primitive count, not sure about the ModelLOD part.
};

public struct alien_reds_bin
{
    public alien_reds_header Header;
    public List<alien_reds_entry> Entries;
};
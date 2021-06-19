using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Textures
{
    public class TextureBIN
    {
        public static alien_texture_bin Load(string FullFilePath)
        {
            alien_texture_bin Result = new alien_texture_bin();
            BinaryReader Stream = new BinaryReader(File.OpenRead(FullFilePath));

            alien_texture_bin_header Header = Utilities.Consume<alien_texture_bin_header>(ref Stream);

            int StringsStartCount = Stream.ReadInt32();
            byte[] StringsStart  = Stream.ReadBytes(StringsStartCount);

            List<alien_texture_bin_texture> Textures = Utilities.ConsumeArray<alien_texture_bin_texture>(ref Stream, Header.EntryCount);

            Result.TextureFilePaths = new List<string>(Header.EntryCount);
            for (int EntryIndex = 0; EntryIndex < Header.EntryCount; ++EntryIndex)
            {
                Result.TextureFilePaths.Add(Utilities.ReadString(StringsStart, Textures[EntryIndex].FileNameOffset).Replace('\\', '/'));
            }

            Result.Header = Header;
            Result.Textures = Textures;

            return Result;
        }
    }
}

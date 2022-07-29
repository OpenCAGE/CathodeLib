using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

/*
namespace CATHODE.LEGACY
{
    public class IDXRemap : CathodePAK
    {
        public List<alien_shader_idx_remap_data> Datas;
        public void Load(string FullFilePath)
        {
            LoadPAK(FullFilePath, false);

            Datas = new List<alien_shader_idx_remap_data>(PAKHeader.EntryCount);

            for (int EntryIndex = 0; EntryIndex < PAKHeader.EntryCount; ++EntryIndex)
            {
                Datas.Add(Utilities.Consume<alien_shader_idx_remap_data>(EntryDatas[EntryIndex]));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_shader_idx_remap_data
    {
        public int Index;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public int[] Unknown0_; //3
    };
}
*/
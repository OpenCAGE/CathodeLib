using System.IO;
using System.Runtime.InteropServices;
using CathodeLib;

namespace CATHODE
{
    /* Handles Cathode PROGRESSION.AIS files */
    public class ProgressionSave : CathodeFile
    {
        public Progression Content;
        public static new Impl Implementation = Impl.LOAD | Impl.SAVE;
        public ProgressionSave(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                Content = Utilities.Consume<Progression>(reader);
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                Utilities.Write<Progression>(writer, Content);
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Progression
        {
            public fourcc FourCC;
            public int VersionNum;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)] public byte[] unk1;

            public byte gamepad_ControlScheme; //44

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public byte[] unk2;

            public float gamepad_ControllerSensitivity; //48

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] unk3;

            public byte InvertX; //56
            public byte InvertY; //57

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)] public byte[] unk4;

            public byte gamepad_Vibration; //59

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public byte[] unk5;

            public byte aimAssist;
        };
        #endregion
    }
}
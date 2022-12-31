using System.IO;
using System.Runtime.InteropServices;
using CathodeLib;

namespace CATHODE
{
    /* Handles Cathode PROGRESSION.AIS files */
    public class ProgressionSave : CathodeFile
    {
        private Progression _content;
        public Progression Content { get { return _content; } }

        public ProgressionSave(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        protected override bool Load()
        {
            if (!File.Exists(_filepath)) return false;

            BinaryReader Stream = new BinaryReader(File.OpenRead(_filepath));
            try
            {
                _content = Utilities.Consume<Progression>(Stream);
            }
            catch
            {
                Stream.Close();
                return false;
            }
            Stream.Close();
            return true;
        }

        /* Save the file */
        override public bool Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(_filepath));
            try
            {
                Utilities.Write<Progression>(stream, _content);
            }
            catch
            {
                stream.Close();
                return false;
            }
            stream.Close();
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
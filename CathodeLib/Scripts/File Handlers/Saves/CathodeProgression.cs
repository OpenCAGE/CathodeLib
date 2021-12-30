using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Saves
{
    /* Handles Cathode PROGRESSION.AIS files */
    public class CathodeProgression
    {
        private string filepath;
        private alien_progression_ais content;

        /* Load the file */
        public CathodeProgression(string pathToMVR)
        {
            filepath = pathToMVR;

            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));
            content = Utilities.Consume<alien_progression_ais>(Stream);
            Stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<alien_progression_ais>(stream, content);
            stream.Close();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_progression_ais
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
}
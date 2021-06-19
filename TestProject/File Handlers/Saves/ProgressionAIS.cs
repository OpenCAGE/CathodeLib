﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Saves
{
    /* Handles CATHODE PROGRESSION.AIS files */
    public class ProgressionAIS
    {
        private string filepath;
        private alien_progression_ais content;

        /* Load the file */
        public ProgressionAIS(string pathToMVR)
        {
            filepath = pathToMVR;

            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));
            content = Utilities.Consume<alien_progression_ais>(ref Stream);
            Stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            FileStream stream = new FileStream(filepath, FileMode.Create);
            Utilities.Write<alien_progression_ais>(ref stream, content);
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
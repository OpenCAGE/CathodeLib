using CATHODE.Scripting;
using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using static CATHODE.Movers;
using static CATHODE.Resources;

namespace CATHODE
{
    /// <summary>
    /// DATA/UI/SELECTIONOVERLAYPARAMS.BIN
    /// </summary>
    public class SelectionOverlayParams : CathodeFile
    {
        public Vector4 Colour; //R,G,B,A
        public float Rate;
        public float Power;

        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public SelectionOverlayParams(string path) : base(path) { }
        public SelectionOverlayParams(MemoryStream stream, string path = "") : base(stream, path) { }
        public SelectionOverlayParams(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                Colour = new Vector4();
                ReadValue(reader, ref Colour.X, 15);
                ReadValue(reader, ref Colour.Y, 36);
                ReadValue(reader, ref Colour.Z, 56);
                ReadValue(reader, ref Colour.W, 77);
                ReadValue(reader, ref Rate, 90);
                ReadValue(reader, ref Power, 104);
            }
            return true;
        }

        private void ReadValue(BinaryReader reader, ref float value, int offset)
        {
            reader.BaseStream.Position = offset;
            value = reader.ReadSingle();
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                WriteValue(writer, "m_colour.red", Colour.X);
                WriteValue(writer, "m_colour.green", Colour.Y);
                WriteValue(writer, "m_colour.blue", Colour.Z);
                WriteValue(writer, "m_colour.alpha", Colour.W);
                WriteValue(writer, "m_rate", Rate);
                WriteValue(writer, "m_power", Power);
            }
            return true;
        }

        private void WriteValue(BinaryWriter writer, string name, float val)
        {
            Utilities.WriteString(name, writer);
            writer.Write(new byte[] { 0x0A, 0x34, 0x0A });
            writer.Write(val);
        }
        #endregion
    }
}
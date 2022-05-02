using CATHODE.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    /* Handles Cathode PROGRESSION.AIS files */
    public class MissionSave
    {
        private string filepath;
        private alien_mission_ais header;

        // From the iOS decomp: the saves work with a "leaf and node" system, where you have
        // "node" names saved with their connected "leafs" which acts like a "system" and
        // "parameter" to apply to the system

        /* Load the file */
        public MissionSave(string pathToAIS)
        {
            filepath = pathToAIS;

            BinaryReader reader = new BinaryReader(File.OpenRead(filepath));

            header = Utilities.Consume<alien_mission_ais>(reader);
            string levelname = Utilities.ReadString(reader.ReadBytes(128));

            reader.BaseStream.Position = header.save_root_offset;

            ValidateGuid(reader, "save_root");
            reader.BaseStream.Position += 8;

            ValidateGuid(reader, "trigger");
            ParseHeaderAndSkip(reader);

            ValidateGuid(reader, "pause_context_trigger");
            ParseHeaderAndSkip(reader);

            ValidateGuid(reader, "forward_triggers");
            ParseHeaderAndSkip(reader);

            ValidateGuid(reader, "m_broadcast_messages");
            ParseHeaderAndSkip(reader);

            ValidateGuid(reader, "player_pos");
            ParseHeaderAndSkip(reader);

            ValidateGuid(reader, "player_pos_valid");
            reader.BaseStream.Position += 2;

            ValidateGuid(reader, "filter_object");
            ParseHeaderAndSkip(reader);

            ValidateGuid(reader, "next_temporary_guid");
            ParseHeaderAndSkip(reader);

            ValidateGuid(reader, "trigger_object");
            ParseHeaderAndSkip(reader);

            ValidateGuid(reader, "temp_entities_data");
            ParseHeaderAndSkip(reader);

            ValidateGuid(reader, "temp_entities");
            ParseHeaderAndSkip(reader);

            //TODO: What do we reach after this point? Can't find the ShortGuid!

            int pos = (int)reader.BaseStream.Position;

            /*
            List<string> dump = new List<string>();
            int prevPos = (int)Stream.BaseStream.Position;
            while (true)
            {
                if (Stream.BaseStream.Position + 4 >= Stream.BaseStream.Length) break;

                ShortGuid consumed_guid = Utilities.Consume<ShortGuid>(Stream);
                if (consumed_guid.ToString() == "00-00-00-00") continue;

                string match = ShortGuidUtils.FindString(consumed_guid);
                if (match != consumed_guid.ToString())
                {
                    dump.Add((Stream.BaseStream.Position - 4) + " => [ + " + ((Stream.BaseStream.Position - 4) - prevPos) + "] => " + match);
                    prevPos = (int)Stream.BaseStream.Position;
                }

                Stream.BaseStream.Position -= 3;
            }
            File.WriteAllLines(Path.GetFileNameWithoutExtension(pathToMVR) + "_dump.txt", dump);
            */

            reader.Close();
        }

        private void ParseHeaderAndSkip(BinaryReader stream)
        {
            byte type = stream.ReadByte();
            int offset = stream.ReadInt16();
            byte unk = stream.ReadByte();
            if (unk == 0x01)
                throw new Exception("Unhandled");

            switch (type)
            {
                case 0x40:
                case 0x0D:
                    stream.BaseStream.Position += offset;
                    break;
                case 0x04:
                    stream.BaseStream.Position += 1;
                    break;
                default:
                    throw new Exception("Unhandled");
            }
        }

        private void ValidateGuid(BinaryReader Stream, string str)
        {
            ShortGuid consumed_guid = Utilities.Consume<ShortGuid>(Stream);
            if (consumed_guid != ShortGuidUtils.Generate(str)) 
                throw new Exception(str + " mismatch");
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<alien_mission_ais>(stream, header);
            stream.Close();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_mission_ais
    {
        public fourcc FourCC;
        public int VersionNum;

        public ShortGuid unk1;
        public ShortGuid unk2;
        public ShortGuid unk3;

        public int Offset1;
        public int save_root_offset;
        public int Offset3;
    };
}
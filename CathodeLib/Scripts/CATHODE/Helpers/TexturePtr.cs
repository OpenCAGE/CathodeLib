using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CATHODE
{
    public class TexturePtr
    {
        public int Index = -1;
        public Source Location = Source.NONE;

        public enum Source
        {
            GLOBAL = 2, //Texture comes from ENV/GLOBAL
            LEVEL = 0,  //Texture comes from the level (in ENV/PRODUCTION)
            NONE = -1,  //Texture is located nowhere
        }

        public TexturePtr(BinaryReader reader)
        {
            Index = reader.ReadInt16();
            int source = reader.ReadInt16();
            if (source == -1)
            {
                Index = -1;
                return;
            }
            Location = (Source)source;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write((Int16)Index);
            writer.Write((Int16)Location);
        }
    };
}

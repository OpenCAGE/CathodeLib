using CATHODE;
using System;
using System.Collections.Generic;
using System.Text;

namespace CathodeLib
{
    public class Level
    {
        private Models models;
        private RenderableElementsDatabase reds;

        public Level(string path)
        {
            models = new Models(path + "/RENDERABLE/LEVEL_MODELS.PAK");
            reds = new RenderableElementsDatabase(path + "/WORLD/REDS.BIN");

            List<RemappedRenderableElementData> remappedReds = new List<RemappedRenderableElementData>();
            for (int i = 0; i < reds.Entries.Count; i++)
            {
                RemappedRenderableElementData red = new RemappedRenderableElementData();
                if (reds.Entries[i].ModelIndex != -1)
                    red.Model = models.GetSubmeshForWriteIndex(reds.Entries[i].ModelIndex);
                //COMMENTING THIS OUT AS IT GOES OUTSIDE THE BOUNDS OF THE ARRAY!
                //if (reds.Entries[i].ModelLODIndex != -1)
                //    red.ModelLOD = models.GetSubmeshForWriteIndex(reds.Entries[i].ModelLODIndex);
                remappedReds.Add(red);
            }

            models.Save();
            for (int i = 0; i < reds.Entries.Count; i++)
            {
                if (reds.Entries[i].ModelIndex != -1)
                    reds.Entries[i].ModelIndex = models.GetWriteIndexForSubmesh(remappedReds[i].Model);
                //if (reds.Entries[i].ModelLODIndex != -1)
                //    reds.Entries[i].ModelLODIndex = models.GetWriteIndexForSubmesh(remappedReds[i].ModelLOD);
            }
            reds.Save();
        }

        private class RemappedRenderableElementData
        {
            public Models.CS2.Submesh Model = null;
            public Models.CS2.Submesh ModelLOD = null;
        }
    }
}

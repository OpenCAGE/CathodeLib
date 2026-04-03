using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CathodeLib
{
    //Function that returns 1:
    //  Steam: 0x00ea70f0
    //  EGS: 0x006f18e0
    //  GOG: 0x0085d960

    //Function that returns 0:
    //  Steam: 0x00d0fb70
    //  EGS: 0x00db7d40
    //  GOG: 0x007ef2e0

    public class PatchManager
    {
        public enum Platform
        {
            STEAM,
            EPIC_GAMES_STORE,
            GOG,
            WINDOWS_STORE,

            UNKNOWN
        }

        /// <summary>
        /// Figure out the current platform for the game
        /// </summary>
        public static Platform GetPlatform(string pathToAI)
        {
            if (File.Exists(pathToAI + "/STEAM_API.DLL")) 
                return Platform.STEAM;

            else if (File.Exists(pathToAI + "/EOSSDK-Win32-Shipping.dll"))
                return Platform.EPIC_GAMES_STORE;

            else if (File.Exists(pathToAI + "/GALAXY.DLL"))
                return Platform.GOG;

            else if (File.Exists(pathToAI + "/MicrosoftGame.config"))
                return Platform.WINDOWS_STORE;

            else
                return Platform.UNKNOWN;
        }

        /// <summary>
        /// It's recommended to always call this for CathodeLib edit functionality to operate!
        /// </summary>
        public static void PerformRecommendedPatches(Platform platform, string pathToAI)
        {
            PatchFileIntegrityCheck(platform, pathToAI);
            //PatchPatchLevels(platform, pathToAI);
            DisableCurrentGenOptimisations(platform, pathToAI, true);
            PatchPopupMessage(platform, pathToAI);
            UpdateLevelListInPackages(platform, pathToAI);
        }

        /// <summary>
        /// Patch the AI binary to circumvent FILE_HASHES::verify_integrity
        /// </summary>
        public static bool PatchFileIntegrityCheck(Platform platform, string pathToAI)
        {
            List<PatchBytes> hashPatches = new List<PatchBytes>();
            switch (platform)
            {
                case Platform.STEAM:
                    hashPatches.Add(new PatchBytes(4043685, new byte[] { 0xf7, 0x8c, 0xf9, 0xff }, new byte[] { 0x67, 0x28, 0x25, 0x00 }));
                    hashPatches.Add(new PatchBytes(6193062, new byte[] { 0xf6, 0xc0, 0xd8, 0xff }, new byte[] { 0x66, 0x5c, 0x04, 0x00 }));
                    break;
                case Platform.EPIC_GAMES_STORE:
                    hashPatches.Add(new PatchBytes(4113398, new byte[] { 0x2d, 0x19, 0x00 }, new byte[] { 0x2d, 0x19, 0x00 }));
                    hashPatches.Add(new PatchBytes(5481335, new byte[] { 0xa9, 0xe4, 0xff }, new byte[] { 0x4e, 0x04, 0x00 }));
                    break;
                case Platform.GOG:
                    hashPatches.Add(new PatchBytes(4043525, new byte[] { 0x97, 0x90, 0xf9, 0xff }, new byte[] { 0xb7, 0x29, 0x25, 0x00 }));
                    hashPatches.Add(new PatchBytes(6193443, new byte[] { 0x79, 0xc2, 0xd8, 0xff }, new byte[] { 0x99, 0x5b, 0x04, 0x00 }));
                    break;
            }
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(pathToAI + "/AI.exe")))
                {
                    for (int i = 0; i < hashPatches.Count; i++)
                    {
                        writer.BaseStream.Position = hashPatches[i].offset;
                        writer.Write(hashPatches[i].patched);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchFileIntegrityCheck - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Patch the game binary to disable the _PATCH directory for pre-order DLCs
        /// </summary>
        /*
        public static bool PatchPatchLevels(Platform platform, string pathToAI)
        {
            byte[] patchRemover = { 0x5C, 0x5C, 0x57, 0x4F, 0x52, 0x4C, 0x44, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(pathToAI + "/AI.exe")))
                {
                    switch (platform)
                    {
                        case Platform.STEAM:
                            writer.BaseStream.Position = 15673322;
                            break;
                        case Platform.EPIC_GAMES_STORE:
                            //writer.BaseStream.Position = ; TODO look for %WORLD offset (the offset here is after the second %s)
                            break;
                        case Platform.GOG:
                            //writer.BaseStream.Position = ; TODO
                            break;
                    }
                    if (writer.BaseStream.Position != 0)
                        writer.Write(patchRemover);
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchLaunchMode - " + e.ToString());
                return false;
            }
        }
        */

        /// <summary>
        /// Patch the no_ui option in game binary
        /// </summary>
        public static bool PatchNoUIFlag(Platform platform, string pathToAI, bool noUI)
        {
            List<PatchBytes> noUIPatches = new List<PatchBytes>();
            switch (platform)
            {
                case Platform.STEAM:
                    noUIPatches.Add(new PatchBytes(3842753, new byte[] { 0xab, 0x4c, 0x56 }, new byte[] { 0x2b, 0xc2, 0x6f }));
                    break;
                case Platform.EPIC_GAMES_STORE:
                    noUIPatches.Add(new PatchBytes(3912033, new byte[] { 0xdb, 0xbf, 0x5f, 0x00 }, new byte[] { 0x7b, 0x5b, 0xf3, 0xff }));
                    break;
                case Platform.GOG:
                    noUIPatches.Add(new PatchBytes(3842929, new byte[] { 0x6b, 0x43, 0x04 }, new byte[] { 0xeb, 0x29, 0x0b }));
                    break;
            }
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(pathToAI + "/AI.exe")))
                {
                    for (int i = 0; i < noUIPatches.Count; i++)
                    {
                        writer.BaseStream.Position = noUIPatches[i].offset;
                        if (noUI) writer.Write(noUIPatches[i].patched);
                        else writer.Write(noUIPatches[i].original);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchNoUIFlag - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Patch the skip_frontend option in game binary
        /// </summary>
        public static bool PatchSkipFrontendFlag(Platform platform, string pathToAI, bool skipFrontend)
        {
            List<PatchBytes> skipFEPatches = new List<PatchBytes>();
            switch (platform)
            {
                case Platform.STEAM:
                    skipFEPatches.Add(new PatchBytes(3842846, new byte[] { 0x4e, 0x4c, 0x56 }, new byte[] { 0xce, 0xc1, 0x6f }));
                    skipFEPatches.Add(new PatchBytes(4047697, new byte[] { 0x1b, 0x2c, 0x53 }, new byte[] { 0x9b, 0xa1, 0x6c }));
                    break;
                case Platform.EPIC_GAMES_STORE:
                    skipFEPatches.Add(new PatchBytes(3912126, new byte[] { 0x7e, 0xbf, 0x5f, 0x00 }, new byte[] { 0x1e, 0x5b, 0xf3, 0xff }));
                    skipFEPatches.Add(new PatchBytes(4117408, new byte[] { 0x9c, 0x9d, 0x5c, 0x00 }, new byte[] { 0x3c, 0x39, 0xf0, 0xff }));
                    break;
                case Platform.GOG:
                    skipFEPatches.Add(new PatchBytes(3843022, new byte[] { 0x0e, 0x43, 0x04 }, new byte[] { 0x8e, 0x29, 0x0b }));
                    skipFEPatches.Add(new PatchBytes(4047514, new byte[] { 0x42, 0x24, 0x01 }, new byte[] { 0xc2, 0x0a, 0x08 }));
                    break;
            }
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(pathToAI + "/AI.exe")))
                {
                    for (int i = 0; i < skipFEPatches.Count; i++)
                    {
                        writer.BaseStream.Position = skipFEPatches[i].offset;
                        if (skipFrontend) writer.Write(skipFEPatches[i].patched);
                        else writer.Write(skipFEPatches[i].original);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchSkipFrontendFlag - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Patch the instruction to set Mem_Replay_Logs logging in game binary
        /// </summary>
        public static bool PatchMemReplayLogFlag(Platform platform, string pathToAI, bool shouldLog)
        {
            List<PatchBytes> memReplayPatches = new List<PatchBytes>();
            switch (platform)
            {
                case Platform.STEAM:
                    memReplayPatches.Add(new PatchBytes(4039327, new byte[] { 0xcd, 0x4c, 0x53 }, new byte[] { 0x4d, 0xc2, 0x6c }));
                    break;
                case Platform.EPIC_GAMES_STORE:
                    memReplayPatches.Add(new PatchBytes(4109007, new byte[] { 0x6d, 0xbe, 0x5c, 0x00 }, new byte[] { 0x0d, 0x5a, 0xf0, 0xff }));
                    break;
                case Platform.GOG:
                    memReplayPatches.Add(new PatchBytes(4039167, new byte[] { 0xdd, 0x44, 0x01 }, new byte[] { 0x5d, 0x2b, 0x08 }));
                    break;
            }
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(pathToAI + "/AI.exe")))
                {
                    for (int i = 0; i < memReplayPatches.Count; i++)
                    {
                        writer.BaseStream.Position = memReplayPatches[i].offset;
                        if (shouldLog) writer.Write(memReplayPatches[i].patched);
                        else writer.Write(memReplayPatches[i].original);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchMemReplayLogFlag - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Patch the instruction to make requires_script_for_current_gen always return true in game binary
        /// </summary>
        public static bool DisableCurrentGenOptimisations(Platform platform, string pathToAI, bool disable)
        {
            List<PatchBytes> memReplayPatches = new List<PatchBytes>();
            switch (platform)
            {
                case Platform.STEAM:
                    memReplayPatches.Add(new PatchBytes(5074902, new byte[] { 0xb6, 0x89, 0xd5, 0xff }, new byte[] { 0x16, 0xf5, 0x5c, 0x00 }));
                    break;
                case Platform.EPIC_GAMES_STORE:
                    memReplayPatches.Add(new PatchBytes(6156998, new byte[] { 0x36, 0x0c, 0xc6 }, new byte[] { 0x16, 0x1a, 0xd1 }));
                    break;
                case Platform.GOG:
                    memReplayPatches.Add(new PatchBytes(5074918, new byte[] { 0xb6, 0x8c, 0xd5 }, new byte[] { 0x76, 0x5d, 0xf8 }));
                    break;
            }
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(pathToAI + "/AI.exe")))
                {
                    for (int i = 0; i < memReplayPatches.Count; i++)
                    {
                        writer.BaseStream.Position = memReplayPatches[i].offset;
                        if (disable) writer.Write(memReplayPatches[i].patched);
                        else writer.Write(memReplayPatches[i].original);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::DisableCurrentGenOptimisations - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Patch the cUI UI perf stats flag in the game binary
        /// </summary>
        public static bool PatchUIPerfFlag(Platform platform, string pathToAI, bool shouldShow)
        {
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(pathToAI + "/AI.exe")))
                {
                    switch (platform)
                    {
                        case Platform.STEAM:
                            writer.BaseStream.Position = 4430526;
                            break;
                        case Platform.EPIC_GAMES_STORE:
                            writer.BaseStream.Position = 4500590;
                            break;
                        case Platform.GOG:
                            writer.BaseStream.Position = 4431006;
                            break;
                    }
                    writer.Write((shouldShow) ? (byte)0x01 : (byte)0x00);
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchUIPerfFlag - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Patch the game binary to remove the seemingly debug "TEXT_" prefix for PopupMessage texts
        /// </summary>
        public static bool PatchPopupMessage(Platform platform, string pathToAI)
        {
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(pathToAI + "/AI.exe")))
                {
                    switch (platform)
                    {
                        case Platform.STEAM:
                            writer.BaseStream.Position = 14687164;
                            break;
                        case Platform.EPIC_GAMES_STORE:
                            writer.BaseStream.Position = 14783204;
                            break;
                        case Platform.GOG:
                            writer.BaseStream.Position = 14784108;
                            break;
                    }

                    //%s, replacing TEXT_%s
                    writer.Write(new byte[] { 0x25, 0x73, 0x00, 0x00, 0x00, 0x00, 0x00 });
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchPopupMessage - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Patch the game binary to allow us to launch directly to a map
        /// </summary>
        public static bool PatchLaunchMode(Platform platform, string pathToAI, string MapName = "Production/Frontend")
        {
            //This is the level the benchmark function loads into - we can overwrite it to change
            byte[] mapStringByteArray = { 0x50, 0x72, 0x6F, 0x64, 0x75, 0x63, 0x74, 0x69, 0x6F, 0x6E, 0x2F, 0x54, 0x45, 0x43, 0x48, 0x5F, 0x52, 0x4E, 0x44, 0x5F, 0x48, 0x5A, 0x44, 0x4C, 0x41, 0x42, 0x00, 0x00, 0x65, 0x6E, 0x67, 0x69, 0x6E, 0x65, 0x5F, 0x73, 0x65, 0x74, 0x74, 0x69, 0x6E, 0x67, 0x73 };

            //These are the original/edited setters in the benchmark function to enable benchmark mode - if we're just loading a level, we want to change them
            List<PatchBytes> benchmarkPatches = new List<PatchBytes>();
            switch (platform)
            {
                case Platform.STEAM:
                    benchmarkPatches.Add(new PatchBytes(3842041, new byte[] { 0xe3, 0x48, 0x26 }, new byte[] { 0x13, 0x3c, 0x28 }));
                    benchmarkPatches.Add(new PatchBytes(3842068, new byte[] { 0xce, 0x0c, 0x6f }, new byte[] { 0x26, 0x0f, 0x64 }));
                    benchmarkPatches.Add(new PatchBytes(3842146, new byte[] { 0xcb, 0x0c, 0x6f }, new byte[] { 0x26, 0x0f, 0x64 }));
                    //benchmarkPatches.Add(new PatchBytes(3842846, new byte[] { 0x4e, 0x4c, 0x56 }, new byte[] { 0xce, 0xc1, 0x6f })); //skip_frontend
                    //benchmarkPatches.Add(new PatchBytes(4047697, new byte[] { 0x1b, 0x2c, 0x53 }, new byte[] { 0x9b, 0xa1, 0x6c })); //skip_frontend
                    break;
                case Platform.EPIC_GAMES_STORE:
                    benchmarkPatches.Add(new PatchBytes(3911321, new byte[] { 0x13, 0x5f, 0x1a }, new byte[] { 0x23, 0x43, 0x1c }));
                    benchmarkPatches.Add(new PatchBytes(3911348, new byte[] { 0xee, 0xd1, 0x70 }, new byte[] { 0xe6, 0xce, 0x65 }));
                    benchmarkPatches.Add(new PatchBytes(3911426, new byte[] { 0xeb, 0xd1, 0x70 }, new byte[] { 0xe6, 0xce, 0x65 }));
                    //benchmarkPatches.Add(new PatchBytes(3912126, new byte[] { 0x7e, 0xbf, 0x5f, 0x00 }, new byte[] { 0x1e, 0x5b, 0xf3, 0xff })); //skip_frontend
                    //benchmarkPatches.Add(new PatchBytes(4117408, new byte[] { 0x9c, 0x9d, 0x5c, 0x00 }, new byte[] { 0x3c, 0x39, 0xf0, 0xff })); //skip_frontend
                    break;
                case Platform.GOG:
                    benchmarkPatches.Add(new PatchBytes(3842217, new byte[] { 0x33, 0x4b, 0x26 }, new byte[] { 0x13, 0x3c, 0x28 }));
                    benchmarkPatches.Add(new PatchBytes(3842244, new byte[] { 0x0e, 0xaf, 0x70 }, new byte[] { 0x26, 0xaf, 0x65 }));
                    benchmarkPatches.Add(new PatchBytes(3842322, new byte[] { 0x0b, 0xaf, 0x70 }, new byte[] { 0x26, 0xaf, 0x65 }));
                    //benchmarkPatches.Add(new PatchBytes(3843022, new byte[] { 0x0e, 0x43, 0x04 }, new byte[] { 0x8e, 0x29, 0x0b })); //skip_frontend
                    //benchmarkPatches.Add(new PatchBytes(4047514, new byte[] { 0x42, 0x24, 0x01 }, new byte[] { 0xc2, 0x0a, 0x08 })); //skip_frontend
                    break;
            }

            //Frontend acts as a reset
            bool shouldPatch = true;
            if (MapName.ToUpper() == "FRONTEND")
            {
                MapName = "Tech_RnD_HzdLab";
                shouldPatch = false;
            }

            //Update vanilla byte array with selection
            for (int i = 0; i < MapName.Length; i++)
            {
                mapStringByteArray[i] = (byte)MapName[i];
            }
            mapStringByteArray[MapName.Length] = 0x00;

            //Edit game EXE with selected option & hack out the benchmark mode
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(pathToAI + "/AI.exe")))
                {
                    for (int i = 0; i < benchmarkPatches.Count; i++)
                    {
                        writer.BaseStream.Position = benchmarkPatches[i].offset;
                        if (shouldPatch) writer.Write(benchmarkPatches[i].patched);
                        else writer.Write(benchmarkPatches[i].original);
                    }
                    switch (platform)
                    {
                        case Platform.STEAM:
                            writer.BaseStream.Position = 15676264;
                            break;
                        case Platform.EPIC_GAMES_STORE:
                            writer.BaseStream.Position = 15773400;
                            break;
                        case Platform.GOG:
                            writer.BaseStream.Position = 15773440;
                            break;
                    }
                    if (writer.BaseStream.Position != 0)
                        writer.Write(mapStringByteArray);
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchLaunchMode - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Update the list of levels in PACKAGES MAIN.PKG to account for any custom levels
        /// </summary>
        public static bool UpdateLevelListInPackages(Platform platform, string pathToAI)
        {
            try
            {
                string pathToPackages = pathToAI + "/DATA/PACKAGES/MAIN.PKG";
                XDocument packagesXML = XDocument.Load(pathToPackages);
                XElement levelsRootNode = packagesXML.XPathSelectElement("//package/game_data/levels");
                levelsRootNode.RemoveNodes();

                List<string> levels = Level.GetLevels(pathToAI);
                foreach (string level in levels)
                {
                    //Ignore maps included in the base game or other PKGs
                    if (level.ToUpper() == "BSP_LV426_PT01" ||
                        level.ToUpper() == "BSP_LV426_PT02" ||
                        level.ToUpper() == "BSP_TORRENS" ||
                        level.ToUpper() == @"DLC\BSPNOSTROMO_RIPLEY" ||
                        level.ToUpper() == @"DLC\BSPNOSTROMO_RIPLEY_PATCH" ||
                        level.ToUpper() == @"DLC\BSPNOSTROMO_TWOTEAMS" ||
                        level.ToUpper() == @"DLC\BSPNOSTROMO_TWOTEAMS_PATCH" ||
                        level.ToUpper() == @"DLC\CHALLENGEMAP1" ||
                        level.ToUpper() == @"DLC\CHALLENGEMAP11" ||
                        level.ToUpper() == @"DLC\CHALLENGEMAP12" ||
                        level.ToUpper() == @"DLC\CHALLENGEMAP14" ||
                        level.ToUpper() == @"DLC\CHALLENGEMAP3" ||
                        level.ToUpper() == @"DLC\CHALLENGEMAP4" ||
                        level.ToUpper() == @"DLC\CHALLENGEMAP5" ||
                        level.ToUpper() == @"DLC\CHALLENGEMAP7" ||
                        level.ToUpper() == @"DLC\CHALLENGEMAP9" ||
                        level.ToUpper() == @"DLC\SALVAGEMODE1" ||
                        level.ToUpper() == @"DLC\SALVAGEMODE2" ||
                        level.ToUpper() == "ENG_ALIEN_NEST" ||
                        level.ToUpper() == "ENG_REACTORCORE" ||
                        level.ToUpper() == "ENG_TOWPLATFORM" ||
                        level.ToUpper() == "HAB_AIRPORT" ||
                        level.ToUpper() == "HAB_CORPORATEPENT" ||
                        level.ToUpper() == "HAB_SHOPPINGCENTRE" ||
                        level.ToUpper() == "SCI_ANDROIDLAB" ||
                        level.ToUpper() == "SCI_HOSPITALLOWER" ||
                        level.ToUpper() == "SCI_HOSPITALUPPER" ||
                        level.ToUpper() == "SCI_HUB" ||
                        level.ToUpper() == "SOLACE" ||
                        level.ToUpper() == "TECH_COMMS" ||
                        level.ToUpper() == "TECH_HUB" ||
                        level.ToUpper() == "TECH_MUTHRCORE" ||
                        level.ToUpper() == "TECH_RND" ||
                        level.ToUpper() == "TECH_RND_HZDLAB")
                        continue;

                    levelsRootNode.Add(XElement.Parse("<level id=\"Production\\" + level + "\" path=\"data\\ENV\\Production\\" + level + "\" />"));
                }
                File.WriteAllText(pathToPackages, packagesXML.ToString());
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::UpdateLevelListInPackages - " + e.ToString());
                return false;
            }
        }

        struct PatchBytes
        {
            public PatchBytes(int _o, byte[] _orig, byte[] _patch)
            {
                offset = _o;
                original = _orig;
                patched = _patch;
            }
            public int offset;
            public byte[] original;
            public byte[] patched;
        }
    }
}

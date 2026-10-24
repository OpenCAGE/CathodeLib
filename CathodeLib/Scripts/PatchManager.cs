using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
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
            //Windows
            STEAM,
            EPIC_GAMES_STORE,
            GOG,
            WINDOWS_STORE,

            //Others
            SWITCH,
            IOS_ANDROID,
            MAC_LINUX,

            UNKNOWN

            //This is used like a flag in some instances, so only append values.
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

            else if (File.Exists(pathToAI + "/data/global/animation_switch.pak"))
                return Platform.SWITCH;

            else if (File.Exists(pathToAI + "/data/font_config_switch.xml"))
                return Platform.IOS_ANDROID;

            else if (Directory.Exists(pathToAI + "/data/havok"))
                return Platform.MAC_LINUX;

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
                default:
                    return false;
            }
            try
            {
                WritePatches(pathToAI, hashPatches, true);
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
                default:
                    return false;
            }
            try
            {
                WritePatches(pathToAI, noUIPatches, noUI);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchNoUIFlag - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Patch the game binary so the DebugText and DebugTextStacking script entities are created at runtime.
        /// </summary>
        public static bool PatchDebugTextEntities(Platform platform, string pathToAI, bool enable)
        {
            List<PatchBytes> debugTextPatches = new List<PatchBytes>();
            switch (platform)
            {
                case Platform.STEAM:
                    debugTextPatches.Add(new PatchBytes(15031656, new byte[] { 0x80, 0x91, 0x99, 0x00 }, new byte[] { 0x10, 0x30, 0x8c, 0x00 })); 
                    debugTextPatches.Add(new PatchBytes(15030400, new byte[] { 0x80, 0x91, 0x99, 0x00 }, new byte[] { 0x10, 0x30, 0x8c, 0x00 })); 
                    break;
                case Platform.EPIC_GAMES_STORE:
                    debugTextPatches.Add(new PatchBytes(15128272, new byte[] { 0xd0, 0x7f, 0xfd, 0x00 }, new byte[] { 0xf0, 0xb2, 0x9c, 0x00 }));
                    debugTextPatches.Add(new PatchBytes(15127016, new byte[] { 0xd0, 0x7f, 0xfd, 0x00 }, new byte[] { 0xf0, 0xb2, 0x9c, 0x00 }));
                    break;
                case Platform.GOG:
                    debugTextPatches.Add(new PatchBytes(15128880, new byte[] { 0xe0, 0xb9, 0xd7, 0x00 }, new byte[] { 0x10, 0x30, 0x8c, 0x00 }));
                    debugTextPatches.Add(new PatchBytes(15127624, new byte[] { 0xe0, 0xb9, 0xd7, 0x00 }, new byte[] { 0x10, 0x30, 0x8c, 0x00 }));
                    break;
                default:
                    return false;
            }
            try
            {
                WritePatches(pathToAI, debugTextPatches, enable);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchDebugTextEntities - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Patch the render_constant_ambient option in game binary
        /// </summary>
        public static bool PatchRenderConstantAmbientFlag(Platform platform, string pathToAI, bool constantAmbient)
        {
            List<PatchBytes> constantAmbPatches = new List<PatchBytes>();
            switch (platform)
            {
                case Platform.STEAM:
                    constantAmbPatches.Add(new PatchBytes(6158004, new byte[] { 0x08, 0x06, 0xc7, 0xff }, new byte[] { 0x38, 0x6e, 0x4c, 0x00 }));
                    break;
                case Platform.EPIC_GAMES_STORE:
                    constantAmbPatches.Add(new PatchBytes(5445908, new byte[] { 0xb8, 0xb9, 0xc8 }, new byte[] { 0xc8, 0xf3, 0xdb }));
                    break;
                case Platform.GOG:
                    constantAmbPatches.Add(new PatchBytes(6158500, new byte[] { 0x08, 0x6c, 0xbc }, new byte[] { 0xb8, 0xd4, 0xe7 }));
                    break;
                default:
                    return false;
            }
            try
            {
                WritePatches(pathToAI, constantAmbPatches, constantAmbient);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchRenderConstantAmbientFlag - " + e.ToString());
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
                default:
                    return false;
            }
            try
            {
                WritePatches(pathToAI, skipFEPatches, skipFrontend);
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
                default:
                    return false;
            }
            try
            {
                WritePatches(pathToAI, memReplayPatches, shouldLog);
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
                default:
                    return false;
            }
            try
            {
                WritePatches(pathToAI, memReplayPatches, disable);
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
            int offset = 0;
            switch (platform)
            {
                case Platform.STEAM:
                    offset = 4430526;
                    break;
                case Platform.EPIC_GAMES_STORE:
                    offset = 4500590;
                    break;
                case Platform.GOG:
                    offset = 4431006;
                    break;
                default:
                    return false;
            }

            try
            {
                WriteToBinary(pathToAI, new List<KeyValuePair<int, byte[]>>() { new KeyValuePair<int, byte[]>(offset, new byte[] { (shouldShow) ? (byte)0x01 : (byte)0x00 }) });
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
            int offset = 0;
            switch (platform)
            {
                case Platform.STEAM:
                    offset = 14687164;
                    break;
                case Platform.EPIC_GAMES_STORE:
                    offset = 14783204;
                    break;
                case Platform.GOG:
                    offset = 14784108;
                    break;
                default:
                    return false;
            }

            try
            {
                WriteToBinary(pathToAI, new List<KeyValuePair<int, byte[]>>() { new KeyValuePair<int, byte[]>(offset, new byte[] { 0x25, 0x73, 0x00, 0x00, 0x00, 0x00, 0x00 }) }); //%s, replacing TEXT_%s
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchPopupMessage - " + e.ToString());
                return false;
            }
        }

        /// <summary>
        /// The longest "Production/NAME" string <see cref="PatchLaunchMode"/> will write. The name lands in
        /// the benchmark's command-line switch table: the retail level name, then "engine_settings\0" at +28,
        /// then "benchmark\0" at +44. Names of 28 characters and up overwrite the "engine_settings" switch, which
        /// the game does not miss (a 29-character DLC name boots straight into its level - checked 3 Sep 2026).
        /// The patch writes bytes +0 to +42, so a 42-character name puts its terminator at +42 and leaves the
        /// switch's own terminator at +43 and "benchmark" untouched - long enough for every retail level,
        /// Production/DLC/BSPNOSTROMO_TWOTEAMS (35) the longest. "benchmark" must never be reached.
        /// </summary>
        public const int MaxLaunchMapNameLength = 42;

        /// <summary>
        /// Patch the game binary to allow us to launch directly to a map
        /// </summary>
        public static bool PatchLaunchMode(Platform platform, string pathToAI, string MapName = "Production/Frontend")
        {
            //A longer name would overwrite the "engine_settings" string that follows it in the executable
            if (MapName == null || MapName.Length > MaxLaunchMapNameLength)
                return false;

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
                default:
                    return false;
            }

            //Frontend acts as a reset: the benchmark bytes go back to retail and the game boots normally,
            //into its own menu. Level pickers name it PRODUCTION/FRONTEND, so that spelling counts too.
            bool shouldPatch = true;
            if (MapName.Replace('\\', '/').Trim('/').ToUpper() == "PRODUCTION/FRONTEND")
            {
                MapName = "Production/Tech_RnD_HzdLab";
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
                List<KeyValuePair<int, byte[]>> writes = new List<KeyValuePair<int, byte[]>>();
                for (int i = 0; i < benchmarkPatches.Count; i++)
                    writes.Add(new KeyValuePair<int, byte[]>(benchmarkPatches[i].offset, shouldPatch ? benchmarkPatches[i].patched : benchmarkPatches[i].original));
                int mapNameOffset = 0;
                switch (platform)
                {
                    case Platform.STEAM:
                        mapNameOffset = 15676264;
                        break;
                    case Platform.EPIC_GAMES_STORE:
                        mapNameOffset = 15773400;
                        break;
                    case Platform.GOG:
                        mapNameOffset = 15773440;
                        break;
                }
                if (mapNameOffset != 0)
                    writes.Add(new KeyValuePair<int, byte[]>(mapNameOffset, mapStringByteArray));
                WriteToBinary(pathToAI, writes);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchLaunchMode - " + e.ToString());
                return false;
            }
        }

        //The <level> entries MAIN.PKG ships with on PC (Steam's and Epic's files are the same), in their shipped order and spelling.
        //ChallengeMap16's is the only place that level's name is given; BSP_Torens is the shipped file's own misspelling (there is
        //no such level).
        private static readonly string[] _shippedPackageLevelsPC = new string[]
        {
            "<level id=\"Production\\Frontend\" path=\"data\\ENV\\Production\\Frontend\" />",
            "<level id=\"Production\\BSP_Torens\" path=\"data\\ENV\\Production\\BSP_Torens\" />",
            "<level id=\"Production\\DLC\\ChallengeMap16\" name=\"AI_UI_CH_TITLE_0\" path=\"Data\\ENV\\Production\\DLC\\ChallengeMap16\" />",
        };
        private static readonly HashSet<string> _shippedPackageLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PRODUCTION\\FRONTEND", "PRODUCTION\\BSP_TORENS", "PRODUCTION\\DLC\\CHALLENGEMAP16" };

        //Maps included in the base game or other PKGs: never listed in MAIN.PKG by us
        private static readonly HashSet<string> _otherPackageLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PRODUCTION\\BSP_LV426_PT01", "PRODUCTION\\BSP_LV426_PT02", "PRODUCTION\\BSP_TORRENS",
            "PRODUCTION\\DLC\\BSPNOSTROMO_RIPLEY", "PRODUCTION\\DLC\\BSPNOSTROMO_RIPLEY_PATCH", "PRODUCTION\\DLC\\BSPNOSTROMO_TWOTEAMS", "PRODUCTION\\DLC\\BSPNOSTROMO_TWOTEAMS_PATCH",
            "PRODUCTION\\DLC\\CHALLENGEMAP1", "PRODUCTION\\DLC\\CHALLENGEMAP11", "PRODUCTION\\DLC\\CHALLENGEMAP12", "PRODUCTION\\DLC\\CHALLENGEMAP14",
            "PRODUCTION\\DLC\\CHALLENGEMAP3", "PRODUCTION\\DLC\\CHALLENGEMAP4", "PRODUCTION\\DLC\\CHALLENGEMAP5", "PRODUCTION\\DLC\\CHALLENGEMAP7",
            "PRODUCTION\\DLC\\CHALLENGEMAP9", "PRODUCTION\\DLC\\SALVAGEMODE1", "PRODUCTION\\DLC\\SALVAGEMODE2",
            "PRODUCTION\\ENG_ALIEN_NEST", "PRODUCTION\\ENG_REACTORCORE", "PRODUCTION\\ENG_TOWPLATFORM",
            "PRODUCTION\\HAB_AIRPORT", "PRODUCTION\\HAB_CORPORATEPENT", "PRODUCTION\\HAB_SHOPPINGCENTRE",
            "PRODUCTION\\SCI_ANDROIDLAB", "PRODUCTION\\SCI_HOSPITALLOWER", "PRODUCTION\\SCI_HOSPITALUPPER", "PRODUCTION\\SCI_HUB", "PRODUCTION\\SOLACE",
            "PRODUCTION\\TECH_COMMS", "PRODUCTION\\TECH_HUB", "PRODUCTION\\TECH_MUTHRCORE", "PRODUCTION\\TECH_RND", "PRODUCTION\\TECH_RND_HZDLAB",
        };

        /// <summary>
        /// Update the list of levels in PACKAGES MAIN.PKG to account for any custom levels
        /// </summary>
        public static bool UpdateLevelListInPackages(Platform platform, string pathToAI)
        {
            try
            {
                string pathToPackages = pathToAI + "/DATA/PACKAGES/MAIN.PKG";

                /* Only the <levels> block changes: it is written back into the file's own text in place of the old one, so the rest of
                   the file keeps its bytes (formatting, line endings, encoding, byte order mark and any XML declaration). */
                byte[] bytes = File.ReadAllBytes(pathToPackages);
                Encoding encoding = PackageEncoding(bytes, out int preamble);
                string original = encoding.GetString(bytes, preamble, bytes.Length - preamble);
                XDocument packagesXML = XDocument.Parse(original, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                XElement levelsRootNode = packagesXML.XPathSelectElement("//package/game_data/levels");
                FindElementText(original, levelsRootNode, out int start, out int end);
                string block = original.Substring(start, end - start);
                string newLine = block.Contains("\r\n") ? "\r\n" : null;
                if (WriteElement(XElement.Parse(block, LoadOptions.PreserveWhitespace), newLine) != WriteElement(levelsRootNode, newLine))
                    throw new InvalidDataException("The <levels> block could not be found in the file's text");
                string indent = EntryIndent(levelsRootNode);

                bool pc = platform == Platform.STEAM || platform == Platform.EPIC_GAMES_STORE || platform == Platform.GOG;
                List<string> onDisk = Level.GetLevels(pathToAI).Select(o => o.Replace("/", "\\").ToUpper()).ToList();

                /* An entry we wrote is one whose path is DATA\ENV\ and its id (the shipped entries spell it "data\ENV" and "Data\ENV").
                   Those are levels made with OpenCAGE, listed while the level is there. An older OpenCAGE also removed the shipped
                   entries and wrote Frontend and ChallengeMap16 back in its own spelling, without ChallengeMap16's name: on PC those
                   go, and the shipped entries are put back as they ship below. Every other entry is left as it is. */
                HashSet<string> listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (XElement entry in new List<XElement>(levelsRootNode.Elements("level")))
                {
                    string id = entry.Attribute("id")?.Value ?? "";
                    string key = id.Replace("/", "\\").ToUpper();
                    bool ours = entry.Attribute("path")?.Value == "DATA\\ENV\\" + id;
                    bool remove;
                    if (_shippedPackageLevels.Contains(key))
                        remove = pc && (ours || listed.Contains(key));
                    else if (ours && !_otherPackageLevels.Contains(key))
                        remove = !onDisk.Contains(key) || listed.Contains(key);
                    else
                        remove = false;

                    if (remove)
                        RemoveEntry(entry);
                    else
                        listed.Add(key);
                }
                if (pc)
                {
                    XElement previous = null;
                    foreach (string shipped in _shippedPackageLevelsPC)
                    {
                        XElement entry = XElement.Parse(shipped);
                        string key = entry.Attribute("id").Value.ToUpper();
                        XElement existing = levelsRootNode.Elements("level").FirstOrDefault(o => string.Equals((o.Attribute("id")?.Value ?? "").Replace("/", "\\"), key, StringComparison.OrdinalIgnoreCase));
                        if (existing == null)
                        {
                            AddEntry(levelsRootNode, previous, entry, indent);
                            listed.Add(key);
                            existing = entry;
                        }
                        previous = existing;
                    }
                }

                //Levels made with OpenCAGE that aren't listed yet go at the end
                foreach (string key in onDisk)
                {
                    if (listed.Contains(key) || _shippedPackageLevels.Contains(key) || _otherPackageLevels.Contains(key))
                        continue;
                    string lvlName = key;
                    if (lvlName.StartsWith("PRODUCTION\\"))
                        lvlName = "Production" + lvlName.Substring(10);
                    AddEntry(levelsRootNode, levelsRootNode.Elements("level").LastOrDefault(), new XElement("level", new XAttribute("id", lvlName), new XAttribute("path", "DATA\\ENV\\" + lvlName)), indent);
                    listed.Add(key);
                }

                //Written only when it changes, and whole: to a file beside it first, which then replaces it
                string updated = original.Substring(0, start) + WriteElement(levelsRootNode, newLine) + original.Substring(end);
                byte[] updatedBytes = new byte[preamble + encoding.GetByteCount(updated)];
                Array.Copy(bytes, updatedBytes, preamble);
                encoding.GetBytes(updated, 0, updated.Length, updatedBytes, preamble);
                if (!SameBytes(updatedBytes, bytes))
                {
                    string written = pathToPackages + ".new";
                    File.WriteAllBytes(written, updatedBytes);
                    File.Replace(written, pathToPackages, null, true);
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::UpdateLevelListInPackages - " + e.ToString());
                return false;
            }
        }

        /* The encoding of a package file and the length of its byte order mark. Without a mark it is UTF-8, or Latin-1 when it
           isn't valid UTF-8 (every byte then reads and writes back as itself). */
        private static Encoding PackageEncoding(byte[] bytes, out int preamble)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                preamble = 3;
                return new UTF8Encoding(false);
            }
            if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
            {
                preamble = 2;
                return new UnicodeEncoding(bytes[0] == 0xFE, false);
            }
            preamble = 0;
            try
            {
                new UTF8Encoding(false, true).GetCharCount(bytes);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(28591);
            }
        }

        /* Where an element's text starts and ends in the text it was parsed from (parsed with LoadOptions.SetLineInfo) */
        private static void FindElementText(string text, XElement element, out int start, out int end)
        {
            IXmlLineInfo info = element;
            int index = 0;
            for (int line = 1; line < info.LineNumber && index < text.Length; index++)
            {
                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                if (text[index] == '\n' || text[index] == '\r')
                    line++;
            }
            start = text.LastIndexOf('<', Math.Min(text.Length - 1, index + info.LinePosition - 1));
            int tagEnd = text.IndexOf('>', start);
            if (text[tagEnd - 1] == '/')
                end = tagEnd + 1;
            else
                end = text.IndexOf('>', text.IndexOf("</" + element.Name.LocalName, tagEnd, StringComparison.Ordinal)) + 1;
        }

        /* An element as text, its whitespace exactly as it is (newLine: the line ending to write, or null to keep "\n") */
        private static string WriteElement(XElement element, string newLine)
        {
            StringBuilder text = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings()
            {
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment,
                NewLineHandling = newLine == null ? NewLineHandling.None : NewLineHandling.Replace,
                NewLineChars = newLine ?? "\n",
            };
            using (XmlWriter writer = XmlWriter.Create(text, settings))
                element.WriteTo(writer);
            return text.ToString();
        }

        /* The whitespace the file puts before each <level> entry: as before its first one, or one step in from the <levels> line */
        private static string EntryIndent(XElement levels)
        {
            XElement first = levels.Elements("level").FirstOrDefault();
            string space = (first != null ? first.PreviousNode : levels.PreviousNode) is XText text && string.IsNullOrWhiteSpace(text.Value) ? text.Value : "";
            if (space.Contains("\n"))
                space = "\n" + space.Substring(space.LastIndexOf('\n') + 1);
            if (first == null && space.Length != 0)
                space += space.Contains("\t") || space == "\n" ? "\t" : "  ";
            return space;
        }

        /* An entry added after another (or first in the block), on its own line */
        private static void AddEntry(XElement levels, XElement after, XElement entry, string indent)
        {
            XText space = indent.Length != 0 ? new XText(indent) : null;
            if (after != null)
                after.AddAfterSelf(space, entry);
            else
                levels.AddFirst(space, entry);
        }

        /* An entry removed with the whitespace before it, so adding then removing one gives back the same text */
        private static void RemoveEntry(XElement entry)
        {
            if (entry.PreviousNode is XText space && string.IsNullOrWhiteSpace(space.Value))
                space.Remove();
            entry.Remove();
        }

        /// <summary>
        /// Point one of the game's hard-coded file paths at another file of the same length, or back at the original. The UI
        /// movies are opened by paths held in the binary ("DATA/UI/PAUSEMENU.GFX" and the like), so pointing one at a new entry
        /// in UI.PAK changes which movie the game plays without touching the shipped entry.
        /// </summary>
        public static bool PatchFilePath(string pathToAI, string originalPath, string replacementPath, bool redirect)
        {
            if (string.IsNullOrEmpty(originalPath) || replacementPath == null || originalPath.Length != replacementPath.Length)
                return false;
            byte[] from = System.Text.Encoding.ASCII.GetBytes(redirect ? originalPath : replacementPath);
            byte[] to = System.Text.Encoding.ASCII.GetBytes(redirect ? replacementPath : originalPath);
            try
            {
                string exe = pathToAI + "/AI.exe";
                byte[] binary = File.ReadAllBytes(exe);
                List<int> found = FindWholeString(binary, from);
                if (found.Count == 0)
                    return FindWholeString(binary, to).Count == 1;
                if (found.Count != 1)
                    return false;
                WriteToBinary(pathToAI, new List<KeyValuePair<int, byte[]>>() { new KeyValuePair<int, byte[]>(found[0], to) });
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("PatchManager::PatchFilePath - " + e.ToString());
                return false;
            }
        }

        /// <summary>Whether the game's binary currently has <paramref name="path"/> as one of its strings, once.</summary>
        public static bool HasFilePath(string pathToAI, string path)
        {
            try
            {
                return FindWholeString(File.ReadAllBytes(pathToAI + "/AI.exe"), System.Text.Encoding.ASCII.GetBytes(path)).Count == 1;
            }
            catch
            {
                return false;
            }
        }

        private static List<int> FindWholeString(byte[] binary, byte[] text)
        {
            List<int> found = new List<int>();
            for (int i = 1; i + text.Length < binary.Length; i++)
            {
                if (binary[i] != text[0] || binary[i - 1] != 0 || binary[i + text.Length] != 0)
                    continue;
                int j = 1;
                while (j < text.Length && binary[i + j] == text[j]) j++;
                if (j == text.Length)
                    found.Add(i);
            }
            return found;
        }

        /// <summary>
        /// Write each patch's patched (or original) bytes into the game binary.
        /// </summary>
        private static void WritePatches(string pathToAI, List<PatchBytes> patches, bool patched)
        {
            List<KeyValuePair<int, byte[]>> writes = new List<KeyValuePair<int, byte[]>>();
            for (int i = 0; i < patches.Count; i++)
                writes.Add(new KeyValuePair<int, byte[]>(patches[i].offset, patched ? patches[i].patched : patches[i].original));
            WriteToBinary(pathToAI, writes);
        }

        /// <summary>
        /// Write bytes into the game binary at the given offsets. What is already there is read first: bytes that already
        /// match are not written again, and when they all match AI.exe isn't opened for writing at all, so an already patched
        /// binary keeps its timestamp (and a running game's binary can still be checked).
        /// </summary>
        private static void WriteToBinary(string pathToAI, List<KeyValuePair<int, byte[]>> writes)
        {
            string exe = pathToAI + "/AI.exe";
            List<KeyValuePair<int, byte[]>> changed = new List<KeyValuePair<int, byte[]>>();
            using (FileStream reader = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                for (int i = 0; i < writes.Count; i++)
                {
                    byte[] current = new byte[writes[i].Value.Length];
                    reader.Position = writes[i].Key;
                    int read = 0;
                    while (read < current.Length)
                    {
                        int count = reader.Read(current, read, current.Length - read);
                        if (count <= 0) break;
                        read += count;
                    }
                    if (read != current.Length || !SameBytes(current, writes[i].Value))
                        changed.Add(writes[i]);
                }
            }
            if (changed.Count == 0)
                return;

            using (BinaryWriter writer = new BinaryWriter(new FileStream(exe, FileMode.Open, FileAccess.Write, FileShare.None)))
            {
                for (int i = 0; i < changed.Count; i++)
                {
                    writer.BaseStream.Position = changed[i].Key;
                    writer.Write(changed[i].Value);
                }
            }
        }

        private static bool SameBytes(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
                return false;
            for (int i = 0; i < first.Length; i++)
                if (first[i] != second[i])
                    return false;
            return true;
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

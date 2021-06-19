using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TestProject
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            string pathToEnv = @"G:\SteamLibrary\steamapps\common\Alien Isolation\DATA\ENV";

            //Load global assets
            alien_textures GlobalTextures = CATHODE.Textures.TexturePAK.Load(pathToEnv + "/GLOBAL/WORLD/GLOBAL_TEXTURES.ALL.PAK", pathToEnv + "/GLOBAL/WORLD/GLOBAL_TEXTURES_HEADERS.ALL.BIN");
            //alien_pak2 GlobalAnimations;
            //alien_anim_string_db GlobalAnimationsStrings;

            //Load level assets
            alien_level level = CATHODE.AlienLevel.Load("BSP_TORRENS", pathToEnv);
        }
    }
}
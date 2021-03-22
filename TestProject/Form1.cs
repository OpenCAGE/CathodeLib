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

            CATHODE.TexturePAK test = new CATHODE.TexturePAK(@"G:\SteamLibrary\steamapps\common\Alien Isolation\DATA\ENV\PRODUCTION\FRONTEND\RENDERABLE\LEVEL_TEXTURES.ALL.PAK");
            //test.Load();

            alien_level level = File_Handlers.AlienLevel.Load(@"G:\SteamLibrary\steamapps\common\Alien Isolation\DATA\ENV\PRODUCTION\FRONTEND");
        }
    }
}
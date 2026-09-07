using CATHODE.Enums;

namespace CATHODE
{
    /// <summary>
    /// Converts an authored CHARACTER_CLASS_COMBINATION into the NAVIGATION_CHARACTER_CLASS_COMBINATION
    /// the navigation data stores.
    /// </summary>
    /// <remarks>
    /// These are two different enums and the values do NOT carry across. Scripting works in the game's
    /// ten character classes (PLAYER, ALIEN, ANDROID, CIVILIAN, SECURITY, FACEHUGGER, INNOCENT,
    /// ANDROID_HEAVY, MOTION_TRACKER, MELEE_HUMAN; ALL = 1023). Navigation works in the five Detour
    /// cares about (PLAYER, ALIEN, ANDROID, HUMAN_NPC, FACEHUGGER; ALL = 31), and the field it goes in
    /// is five bits wide - so writing the authored number through unchanged both means the wrong
    /// classes and overflows.
    ///
    /// Verified against retail TECH_COMMS: a barrier authored MOTION_TRACKER (256) ships as NONE,
    /// because navigation has no such class; one authored PLAYER_AND_ALIEN (35, which is
    /// PLAYER|ALIEN|FACEHUGGER in game classes) ships as 19, which is exactly PLAYER|ALIEN|FACEHUGGER
    /// in navigation classes. ALL (1023) maps to ALL (31).
    /// </remarks>
    public static class NavigationCharacterClasses
    {
        //Game class bits, in CHARACTER_CLASS order
        private const int PLAYER = 1 << 0;
        private const int ALIEN = 1 << 1;
        private const int ANDROID = 1 << 2;
        private const int CIVILIAN = 1 << 3;
        private const int SECURITY = 1 << 4;
        private const int FACEHUGGER = 1 << 5;
        private const int INNOCENT = 1 << 6;
        private const int ANDROID_HEAVY = 1 << 7;
        private const int MOTION_TRACKER = 1 << 8;
        private const int MELEE_HUMAN = 1 << 9;

        /// <summary>
        /// Fold the game's ten classes onto navigation's five. Anything navigation has no class for -
        /// the motion tracker - contributes nothing.
        /// </summary>
        public static NAVIGATION_CHARACTER_CLASS_COMBINATION FromCharacterClasses(int authored)
        {
            //-1 is the enum's UNKNOWN, which is "nothing was chosen" rather than a set of classes
            if (authored <= 0)
                return NAVIGATION_CHARACTER_CLASS_COMBINATION.NONE;

            NAVIGATION_CHARACTER_CLASS_COMBINATION result = NAVIGATION_CHARACTER_CLASS_COMBINATION.NONE;

            if ((authored & PLAYER) != 0)
                result |= NAVIGATION_CHARACTER_CLASS_COMBINATION.PLAYER;
            if ((authored & ALIEN) != 0)
                result |= NAVIGATION_CHARACTER_CLASS_COMBINATION.ALIEN;
            if ((authored & (ANDROID | ANDROID_HEAVY)) != 0)
                result |= NAVIGATION_CHARACTER_CLASS_COMBINATION.ANDROID;
            if ((authored & (CIVILIAN | SECURITY | INNOCENT | MELEE_HUMAN)) != 0)
                result |= NAVIGATION_CHARACTER_CLASS_COMBINATION.HUMAN_NPC;
            if ((authored & FACEHUGGER) != 0)
                result |= NAVIGATION_CHARACTER_CLASS_COMBINATION.FACEHUGGER;

            /* The motion tracker is not a character that navigates, and there is no navigation class
               for it - but retail does not drop it either. Every shipped barrier whose selected value
               is MOTION_TRACKER stores PLAYER: 15 rows across ENG_TowPlatform, Tech_RnD and Tech_Hub,
               in both the open and closed roles. It is the player's own device, so being folded onto
               the player is the reading that makes sense of that. */
            if ((authored & MOTION_TRACKER) != 0)
                result |= NAVIGATION_CHARACTER_CLASS_COMBINATION.PLAYER;

            return result;
        }

        /// <summary>Everything, as the scripting enum spells it - the default for an open barrier.</summary>
        public const int AllCharacterClasses = PLAYER | ALIEN | ANDROID | CIVILIAN | SECURITY
            | FACEHUGGER | INNOCENT | ANDROID_HEAVY | MOTION_TRACKER | MELEE_HUMAN;
    }
}

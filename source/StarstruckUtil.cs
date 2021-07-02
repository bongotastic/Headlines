using KSP.UI.Screens;


namespace RPStoryteller.source
{
    public class StarStruckUtil
    {
        public static void Report(int significance, string message, string title="Starstruck")
        {
            switch (significance)
            {
                case 1:
                    KSPLog.print(message);
                    break;
                case 3:
                    CreateMessage(message, title);
                    break;
                case 2:
                    ScreenMessage(message);
                    break;
            }
            
        }

        public static void CreateMessage(string message, string title)
        {
            // TODO implement notification generation
            KSPLog.print("[[" + message + "]]");
        }
        
        public static void ScreenMessage(string message)
        {
            var messageUI = new ScreenMessage(message, 4f, ScreenMessageStyle.UPPER_CENTER);
            ScreenMessages.PostScreenMessage(messageUI);
        }
        
        /// <summary>
        /// Avoid some weird quirk from KCT editor revert (taken from RP-0)
        /// </summary>
        /// <returns>UT time in seconds</returns>
        public static double GetUT()
        {
            return HighLogic.LoadedSceneIsEditor ? HighLogic.CurrentGame.UniversalTime : Planetarium.GetUniversalTime();
        }
    }
}
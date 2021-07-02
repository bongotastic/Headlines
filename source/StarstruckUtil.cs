namespace RPStoryteller.source
{
    public class StarStruckUtil
    {
        public static void Report(int significance, string message)
        {
            switch (significance)
            {
                case 1:
                    KSPLog.print(message);
                    break;
                case 2:
                    CreateNotification(message);
                    break;
                case 3:
                    ScreenInterrupt(message);
                    break;
            }
            
        }

        private static void CreateNotification(string message)
        {
            // TODO implement notification generation
            KSPLog.print("[[" + message + "]]");
        }
        
        private static void ScreenInterrupt(string message)
        {
            // TODO implement notification generation
            KSPLog.print("!!" + message + "!!");
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
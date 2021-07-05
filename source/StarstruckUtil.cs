using KSP.UI.Screens;


namespace RPStoryteller.source
{
    public class StarStruckUtil
    {
        /// <summary>
        /// Output textual information to the user. Significance has a related level of intrusion. 
        /// </summary>
        /// <param name="significance">1: Log, 2: Screen update, 3: Message</param>
        /// <param name="message">Test to display</param>
        /// <param name="title">Piece of categorical information</param>
        public static void Report(int significance, string message, string title="Starstruck")
        {
            switch (significance)
            {
                case 1:
                    KSPLog.print($"[{title}] " + message);
                    break;
                case 3:
                    CreateMessage(message, title);
                    break;
                case 2:
                    ScreenMessage(message);
                    break;
            }
            
        }
    
        /// <summary>
        /// Yet to be implemented way to post messages in the message inbox, with the ability to dismiss.
        /// Include a duplicate in the log as well for tracktability. 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="title"></param>
        public static void CreateMessage(string message, string title)
        {
            // TODO implement notification generation
            KSPLog.print("[[" + message + "]]");
        }
        
        /// <summary>
        /// Flashes on the screen for 4 seconds. This method does not uses the title parameter.
        /// </summary>
        /// <param name="message">Keep it short</param>
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
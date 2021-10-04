using CommNet.Network;
using KSP.UI.Screens;
using Headlines.source.Emissions;


namespace Headlines.source
{
    public class HeadlinesUtil
    {
        /// <summary>
        /// Output textual information to the user. Significance has a related level of intrusion. 
        /// </summary>
        /// <param name="significance">1: Log, 2: Screen update, 3: Message</param>
        /// <param name="message">Test to display</param>
        /// <param name="title">Piece of categorical information</param>
        public static void Report(int significance, string message, string title="Headlines")
        {
            NewsStory ns = new NewsStory((HeadlineScope) significance - 1, Headline: title, Story: message);
            Report(ns, HeadlineScope.FEATURE);
        }

        public static void Report(NewsStory newStory, HeadlineScope notificationThreshold = HeadlineScope.FEATURE, bool fileMessage = true)
        {
            switch (newStory.scope)
            {
                case HeadlineScope.DEBUG:
                    KSPLog.print($"[Headlines] " + newStory.story);
                    break;
                case HeadlineScope.SCREEN:
                    KSPLog.print($"[Headlines] " + newStory.story);
                    ScreenMessage(newStory.story);
                    break;
                case HeadlineScope.NEWSLETTER:
                case HeadlineScope.FEATURE:
                case HeadlineScope.FRONTPAGE:
                    KSPLog.print($"[Headlines][{newStory.scope}] {newStory.headline}");
                    if (notificationThreshold <= newStory.scope & fileMessage)
                    {
                        CreateMessage(newStory.story, newStory.headline);
                    }
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
            KSPLog.print("[[[" + message + "]]]");
            MessageSystem ms = MessageSystem.Instance;
            ms.AddMessage(new MessageSystem.Message(title, message,
                MessageSystemButton.MessageButtonColor.GREEN, MessageSystemButton.ButtonIcons.MESSAGE));
        }
        
        /// <summary>
        /// Flashes on the screen for 4 seconds. This method does not uses the title parameter.
        /// </summary>
        /// <param name="message">Keep it short</param>
        public static void ScreenMessage(string message)
        {
            KSPLog.print("[[" + message + "]]");
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

        public static void SafeString(string stringName, ref string dataHolder, ConfigNode cfg)
        {
            if (cfg.HasValue(stringName))
            {
                dataHolder = cfg.GetValue(stringName);
            }
        }
        
        public static void SafeBool(string stringName, ref bool dataHolder, ConfigNode cfg)
        {
            if (cfg.HasValue(stringName))
            {
                dataHolder = bool.Parse(cfg.GetValue(stringName));
            }
        }
        
        public static void SafeInt(string stringName, ref int dataHolder, ConfigNode cfg)
        {
            if (cfg.HasValue(stringName))
            {
                dataHolder = int.Parse(cfg.GetValue(stringName));
            }
        }
        
        public static void SafeDouble(string stringName, ref double dataHolder, ConfigNode cfg)
        {
            if (cfg.HasValue(stringName))
            {
                dataHolder = double.Parse(cfg.GetValue(stringName));
            }
        }
    }
}

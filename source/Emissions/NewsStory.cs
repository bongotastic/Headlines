using System.Collections.Generic;
using UniLinq;

namespace RPStoryteller.source.Emissions
{
    public enum HeadlineScope
    {
        DEBUG, SCREEN, NEWSLETTER, FEATURE, FRONTPAGE   
    }

    public class NewsStory :IConfigNode
    {
        // scope level
        [KSPField]
        public HeadlineScope scope = HeadlineScope.DEBUG;
        
        // header or digest
        [KSPField]
        public string headline = "";
        
        // Full text
        [KSPField]
        public string story = "";
        
        // time
        [KSPField]
        public double timestamp = 0;
        
        // Implicated actors
        [KSPField]
        public List<string> actors;

        #region Kitchen sink

        public NewsStory(HeadlineScope Scope, string Headline = "", string Story = "")
        {
            scope = Scope;
            timestamp = HeadlinesUtil.GetUT();
            headline = Headline;
            story = Story;
        }

        /// <summary>
        /// Create a story from an emissions instance.
        /// </summary>
        /// <param name="emission"></param>
        /// <param name="Headline">the headline</param>
        /// <param name="generateStory">Trigger generate story without waiting for localVariables to be input</param>
        public NewsStory(Emissions emission, string Headline = "", bool generateStory = false)
        {
            scope = emission.scope;
            timestamp = HeadlinesUtil.GetUT();
            headline = Headline;
            
            if (generateStory) AddToStory(emission.GenerateStory());
        }

        public void Load(ConfigNode node)
        {
            ConfigNode.LoadObjectFromConfig(this, node);
        }

        public void Save(ConfigNode node)
        {
            ConfigNode.CreateConfigFromObject(node);
        }

        #endregion
       

        #region Modifiers

        /// <summary>
        /// Add an actor to a story so that it may be used in language generation OR filtering.
        /// </summary>
        /// <param name="actor"></param>
        public void AttachActor(string actor)
        {
            if (!actors.Contains(actor)) actors.Add(actor);
        }

        /// <summary>
        /// Check for membership of a given kerbal to actor set
        /// </summary>
        /// <param name="actorName"></param>
        /// <returns></returns>
        public bool HasActor(string actorName)
        {
            return actors.Contains(actorName);
        }
        
        /// <summary>
        /// Use language generation from emission object to write to the story
        /// </summary>
        /// <param name="emitData"></param>
        public void AddToStory(string fragment, bool newline = true)
        {
            story += fragment;
            if (newline) story += "\n";
        }

        #endregion
       

    }
}
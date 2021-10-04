using System.Collections.Generic;
using UniLinq;

namespace Headlines.source.Emissions
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
        
        // Reputation value
        [KSPField] 
        public float reputationValue = 0f;
        
        // Implicated actors
        public List<string> actors = new List<string>();

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

        public NewsStory(ConfigNode node)
        {
            FromConfigNode(node);
        }

        public ConfigNode AsConfigNode()
        {
            ConfigNode output = new ConfigNode();
            
            output.AddValue("timestamp", timestamp);
            output.AddValue("scope", (int)scope);
            output.AddValue("headline", headline);
            output.AddValue("story", story);
            output.AddValue("reputationValue", reputationValue);

            ConfigNode act = new ConfigNode();
            bool addAct = false;
            foreach (string actor in actors)
            {
                act.AddValue(actor, "actor");
                addAct = true;
            }

            if (addAct)
            {
                output.AddNode("actors", act);
            }

            return output;
        }

        public void FromConfigNode(ConfigNode node)
        {
            HeadlinesUtil.SafeDouble("timestamp", ref timestamp, node);
            if (node.HasValue("scope"))
            {
                scope = (HeadlineScope) int.Parse(node.GetValue("scope"));
            }
            HeadlinesUtil.SafeString("headline", ref headline, node);
            HeadlinesUtil.SafeString("story", ref story, node);

            HeadlinesUtil.SafeFloat("reputationValue", ref reputationValue, node);

            ConfigNode act = node.GetNode("actors");
            if (act != null)
            {
                foreach (ConfigNode.Value actor in act.values)
                {
                    actors.Add(actor.name);
                }
            }
        }
        
        public void Load(ConfigNode node)
        {
            //ConfigNode.LoadObjectFromConfig(this, node);
        }

        public void Save(ConfigNode node)
        {
            //ConfigNode.CreateConfigFromObject(node);
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

        public void SpecifyMainActor(string actorName, Emissions emission = null)
        {
            AttachActor(actorName);

            if (emission != null)
            {
                emission.AddStoryElement("actor_name", actorName);
            }
        }

        public void SpecifyOtherCrew(string crewName, Emissions emission = null)
        {
            AttachActor(crewName);

            if (emission != null)
            {
                emission.AddStoryElement("other_crew", crewName);
            }
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

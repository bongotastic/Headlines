using System.Collections.Generic;
using System.Configuration;
using System.Security.AccessControl;
using System.Text.RegularExpressions;
using KerbalConstructionTime;
using UniLinq;
using UnityEngine;

namespace Headlines.source.Emissions
{
    public class Emissions
    {
        // Generator
        private static System.Random _random = new System.Random();
        
        //Public/Private attributes
        public string nodeName;
        public HeadlineScope scope;
        
        private ConfigNode _node;
        private Dictionary<string, string> localVariable = new Dictionary<string, string>();
        private int _recursionDepth = 0;

        /// <summary>
        /// Fetch an load an emission node from the object database. 
        /// </summary>
        /// <param name="nodeName"></param>
        public Emissions(string nodeName)
        {
            this.nodeName = nodeName;

            foreach (ConfigNode dbNode in GameDatabase.Instance.GetConfigNodes("HIDDENMARKOVMODELS"))
            {
                ConfigNode hmm_emissions = dbNode.GetNode("HMM_EMISSIONS");

                if (hmm_emissions.HasNode(nodeName))
                {
                    FromConfigNode(hmm_emissions.GetNode(nodeName));
                }
            }
            if (_node == null) HeadlinesUtil.Report(1,$"Emission {nodeName} not found in config files.");
        }

        #region API

        /// <summary>
        /// Uses the patterns from the config nodes to generate a story
        /// </summary>
        /// <param name="localValues"></param>
        /// <returns></returns>
        public string GenerateStory()
        {
            _recursionDepth = 0;

            return ResolveLabel("event_text");
            /*
            string story = "";
            
            ConfigNode template = GetRandomNodeOfType("event_text");
            if (template == null) return story;

            story = Strip(template.GetValue("text"));
            
            // Cause
            if (story.Contains("[cause]"))
            {
                story = story.Replace("[cause]", Strip(RandomCause()));
            }
            
            // iterate over localVariable
            foreach (KeyValuePair<string, string> kvp in localVariable)
            {
                if (story.Contains("[" + kvp.Key + "]"))
                {
                    story = story.Replace("[" + kvp.Key + "]", kvp.Value);
                }
            }

            return story;
            */
        }
        
        /// <summary>
        /// Feed the emission instance data specific to a situation
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void AddStoryElement(string key, string value)
        {
            localVariable.Add(key, value);
        }
        
        /// <summary>
        /// Attempts to obtain a value already stored in the instance.
        /// </summary>
        /// <param name="keyName"></param>
        /// <returns>Content or empty string</returns>
        public string GetStoryElement(string keyName)
        {
            if (_node.HasValue(keyName) == true)
            {
                return _node.GetValue(keyName);
            }

            return "";
        }
        
        public bool IsOngoingTask()
        {
            if (_node.HasValue("takesTime") == true)
            {
                return bool.Parse(_node.GetValue("takesTime"));
            }

            return false;
        }
        
        #endregion
        
        #region Internal
        /// <summary>
        /// Connect to a ConfigNode.
        /// </summary>
        /// <param name="node"></param>
        private void FromConfigNode(ConfigNode node)
        {
            _node = node;
            this.scope = (HeadlineScope)int.Parse(_node.GetValue("significance"));
        }
        
        /// <summary>
        /// Removes quotation marks from the config file
        /// </summary>
        /// <param name="entry"></param>
        /// <returns></returns>
        private static string Strip(string entry)
        {
            if (entry.StartsWith("\""))
            {
                return entry.Substring(1, entry.Length - 2);
            }

            return entry;
        }

        #endregion

        #region Language generation

        /// <summary>
        /// Returns one of many nodes of a certain type, if many, pick one randomly.
        /// </summary>
        /// <param name="nodeName"></param>
        /// <returns>A node or null</returns>
        private ConfigNode GetRandomNodeOfType(string nodeName)
        {
            List<ConfigNode> nodeSet = new List<ConfigNode>();
            
            foreach (ConfigNode cfg in _node.GetNodes(nodeName))
            {
                nodeSet.Add(cfg);
            }

            if (nodeSet.Count != 0)
            {
                int randomInt = _random.Next() % nodeSet.Count;
                return nodeSet[randomInt];
            }

            return null;
        }

        /// <summary>
        /// Recursive resolution of a label. A label can either be event_text itself, or [label] within another text fragment.
        /// </summary>
        /// <remarks>Recursion depth is there to prevent cfg designers to cause stack overflow</remarks>
        /// <param name="label"></param>
        /// <returns>expanded text for this label</returns>
        private string ResolveLabel(string label)
        {
            HeadlinesUtil.Report(1, $"Generating label {label} from emission {nodeName}");
            _recursionDepth += 1;
            
            string outputText = $"[{label}]";
            ConfigNode labelNode = GetRandomNodeOfType(label);

            if (labelNode == null | _recursionDepth > 10)
            {
                HeadlinesUtil.Report(1,$"Recursion depth met with {label}", "Emission");
                _recursionDepth -= 1;
                return outputText;
            }

            if (labelNode.HasValue("text"))
            {
                outputText = Strip(labelNode.GetValue("text"));
            }
            else
            {
                _recursionDepth -= 1;
                HeadlinesUtil.Report(1,$"Text node for {label} does not exist", "Emission");
                return outputText;
            }
            
            // Resolve labels within the new text
            string subLabel = "";
            string expandedLabel = "";
            Match m = Regex.Match(outputText, @"\[\w+\]");
            while (m.Success)
            {
                subLabel = m.Value.Substring(1, m.Value.Length - 2);

                if (localVariable.ContainsKey(subLabel))
                {
                    // Side effect: sub-nodes can be overriden in-code by providing a value in localVariable
                    expandedLabel = localVariable[subLabel];
                }
                else if (subLabel != label)
                {
                    expandedLabel = ResolveLabel(subLabel);
                }
                else
                {
                    expandedLabel = label;
                }
                
                outputText = outputText.Replace(m.Value, expandedLabel);
                m = m.NextMatch();
            }

            _recursionDepth -= 1;
            
            return outputText;
        }

        #endregion
    }
}

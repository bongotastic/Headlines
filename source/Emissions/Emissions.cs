using System.Collections.Generic;
using System.Configuration;
using System.Security.AccessControl;
using UnityEngine;

namespace RPStoryteller.source.Emissions
{
    public class Emissions
    {
        private static System.Random _random = new System.Random();
        
        public string nodeName;
        public int significance; 
        
        private ConfigNode _node;

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

        /// <summary>
        /// Connect to a ConfigNode.
        /// </summary>
        /// <param name="node"></param>
        private void FromConfigNode(ConfigNode node)
        {
            _node = node;
            this.significance = int.Parse(_node.GetValue("significance"));
        }

        /// <summary>
        /// Attempts to obtain a value from a node.
        /// </summary>
        /// <param name="keyName"></param>
        /// <returns>Content or empty string</returns>
        public string GetValue(string keyName)
        {
            if (_node.HasValue(keyName) == true)
            {
                return _node.GetValue(keyName);
            }

            return "";
        }

        /// <summary>
        /// Returns one of many nodes of a certain type, if many, pic one randomly.
        /// </summary>
        /// <param name="nodeName"></param>
        /// <returns>A node or null</returns>
        public ConfigNode GetRandomNodeOfType(string nodeName)
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
        /// Convenience method that selects 1 of 1+ causes in the config node and returns as string.
        /// </summary>
        /// <returns></returns>
        public string RandomCause()
        {
            ConfigNode cfg = GetRandomNodeOfType("cause");
            if (cfg != null)
            {
                return cfg.GetValue("text");
            }
            return "";
        }

        public bool OngoingTask()
        {
            if (_node.HasValue("takesTime") == true)
            {
                return bool.Parse(_node.GetValue("takesTime"));
            }

            return false;
        }
    }
}
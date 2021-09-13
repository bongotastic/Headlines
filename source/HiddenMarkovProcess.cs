using System;
using System.Collections.Generic;
using RPStoryteller.source;
using UniLinq;
using UnityEngine;

namespace HiddenMarkovProcess
{
    public class HiddenState : MonoBehaviour
    {
        
        private static readonly System.Random _stateRandom = new System.Random();
        
        // Unique identifier for this Hidden State
        public string templateStateName;

        // Number of days between triggers
        public double period;
        
        // Anchor point for HMM associated with kerbals
        public string kerbalName;
        
        // Transitions
        private readonly Dictionary<string, float> _transitions = new Dictionary<string, float>();
        
        // Emissions
        private readonly Dictionary<string, float> _emissions = new Dictionary<string, float>();

        // Housekeeping
        private bool _dirty = true;
        private List<string> _appliedFilters = new List<string>(); 

        public HiddenState(string templateStateIdentity, string kerbalName="")
        {
            this.templateStateName = templateStateIdentity;
            this.kerbalName = kerbalName;
            
            LoadTemplate();
        }

        #region Meta

        /// <summary>
        /// Debug method that may not be needed in the long run.
        /// </summary>
        public void PrintHMM()
        {
            HeadlinesUtil.Report(1,$"[HMM] {this.templateStateName}");

            foreach (KeyValuePair<string, float> kvp in _transitions)
            {
                HeadlinesUtil.Report(1,$"[HMM][Transition] {kvp.Key} : {kvp.Value}");
            }
            
            foreach (KeyValuePair<string, float> kvp in _emissions)
            {
                HeadlinesUtil.Report(1,$"[HMM][Emission] {kvp.Key} : {kvp.Value}");
            }
        }

        /// <summary>
        /// Accounts for the fact that a HMM is registered under a different name that its template name
        /// </summary>
        /// <returns></returns>
        public string TemplateStateName()
        {
            return this.templateStateName;
        }

        /// <summary>
        /// Use this property to register to a live process/scheduler
        /// </summary>
        /// <returns></returns>
        public string RegisteredName()
        {
            if (kerbalName != "") return kerbalName + "@" + templateStateName;
            
            return templateStateName;
        }

        public List<string> ListEmissions()
        {
            List<string> output = new List<string>();
            foreach (KeyValuePair<string, float> kvp in _emissions)
            {
                if (kvp.Key != "") output.Add(kvp.Key);
            }

            return output;
        }

        public void LoadTemplate()
        {
            _appliedFilters.Clear();
            _emissions.Clear();
            _transitions.Clear();
            
            // Default emission-less permanent state
            _transitions.Add("", 1.0f);
            _emissions.Add("", 1.0f);

            ConfigNode thisdefinition = null;
            
            foreach (ConfigNode cfg in GameDatabase.Instance.GetConfigNodes("HIDDENMARKOVMODELS"))
            {
                if (cfg.TryGetNode(templateStateName, ref thisdefinition))
                {
                    FromConfigNode(thisdefinition);
                    break;
                }
            }
            Recompute();
        }
        #endregion

        #region KSP
        
        /// <summary>
        /// Logic to read from the config files to define transition and emission probabilities.
        /// </summary>
        /// <param name="node">The config node specific to this stateName</param>
        public void FromConfigNode(ConfigNode node)
        {
            // Basic properties
            this.templateStateName = node.GetValue("stateName");
            this.period = double.Parse(node.GetValue("period"));
            
            ConfigNode transitionNode = node.GetNode("Transitions");
            ConfigNode emissionNode = node.GetNode("Emissions");

            // Transitions
            if (transitionNode != null)
            {
                foreach (ConfigNode.Value item in transitionNode.values)
                {
                    if (_transitions.ContainsKey(item.name) == true) continue;
                    
                    _transitions.Add(item.name, float.Parse(item.value));
                }
            }

            // Emissions
            if (emissionNode != null)
            {
                foreach (ConfigNode.Value item in emissionNode.values)
                {
                    if (_emissions.ContainsKey(item.name) == true) continue;
                    
                    _emissions.Add(item.name, float.Parse(item.value));
                }
            }
        }

        #endregion

        #region HMMlogic
        /// <summary>
        /// Determines the next state to transition to.
        /// </summary>
        /// <returns>(string)The unique identifier of a state.</returns>
        public string Transition()
        {
            if (_dirty) Recompute();
            
            string outputState = RandomSelect(_transitions);
            return outputState == "" ? this.templateStateName : outputState;
        }
        
        /// <summary>
        /// Determines the event to emit.
        /// </summary>
        /// <returns>(string)The identifier of an event.</returns>
        public string Emission()
        {
            if (_dirty) Recompute();

            return RandomSelect(_emissions);
        }

        /// <summary>
        /// General purpose selection of one item within a collection of items. 
        /// </summary>
        /// <param name="target">the collection to perform the selection on.</param>
        /// <returns>The selected unique identifier.</returns>
        private string RandomSelect(Dictionary<string, float> target)
        {
            float randomVal = (float)_stateRandom.NextDouble();
            string lastKey = "";

            foreach (KeyValuePair<string, float> kvp in target)
            {
                if (randomVal <= kvp.Value)
                {
                    return kvp.Key;
                }
                else
                {
                    randomVal -= kvp.Value;
                    lastKey = kvp.Key;
                }
            }
            
            // Odd case where randomVal is so high that there is a precision issue
            return lastKey;
        }
            
        /// <summary>
        /// Recompute the probabilities of an internal dictionary to balance to 1.0. Will refuse to
        /// set the probability such that it overflows.
        /// </summary>
        /// <param name="target">Either _transitions or _emissions</param>
        private void RecomputeProbabilities(Dictionary<string, float> target)
        {
            // Tally the sum of probabilities
            float runningSum = 0f;

            foreach (KeyValuePair<string, float> kvp in target)
            {
                if (kvp.Key != "") runningSum += kvp.Value;
            }

            // Rescale, using "" as the proportionally flexible element.
            if (runningSum > 1.0)
            {
                //foreach (KeyValuePair<string, float> kvp in target)
                foreach (string emitKey in target.Keys.ToList())
                {
                    if (emitKey != "")
                    {
                        target[emitKey] /= runningSum;
                    }
                    else target[emitKey] = 0f;
                }
            }
            else
            {
                target[""] = 1f - runningSum;
            }
        }
        
        /// <summary>
        /// Ensures that the Hidden Model is ready for use.
        /// </summary>
        public void Recompute()
        {
            RecomputeProbabilities(_transitions);
            RecomputeProbabilities(_emissions);
            _dirty = false;
        }
        
        /// <summary>
        /// Safe method to add a transition to the model. This method is NOT rescaling probabilities, this
        /// must be done separately so as to avoid unnecessary work when building models.
        /// </summary>
        /// <param name="key">The transition unique identifier</param>
        /// <param name="value">The transition probability. If set to 0, delete the key.</param>
        public void SpecifyTransition(string key, float value)
        {
            SpecifyProbability(_transitions, key, value);
            _dirty = true;
        }
        
        /// <summary>
        /// Safe method to add an emission to the model. This method is NOT rescaling probabilities, this
        /// must be done separately so as to avoid unnecessary work when building models.
        /// </summary>
        /// <param name="key">The transition unique identifier</param>
        /// <param name="value">The transition probability. If set to 0, delete the key.</param>
        public void SpecifyEmission(string key, float value)
        {
            SpecifyProbability(_emissions, key, value);
            _dirty = true;
        }

        /// <summary>
        /// Returns the emission probability, or 0 when it is not in the list
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public float GetEmissionProbability(string key)
        {
            if (_emissions.ContainsKey(key)) return _emissions[key];
            return 0;
        }
        
        
        /// <summary>
        /// Safe method to add or remove an element to the model. This method is NOT rescaling probabilities, this
        /// must be done separately so as to avoid unnecessary work when building models.
        /// </summary>
        /// <param name="key">The unique identifier</param>
        /// <param name="value">The raw probability. If set to 0, it deletes the key.</param>
        private void SpecifyProbability(Dictionary<string, float> itemset, string key, float value)
        {
            _dirty = true;
            
            if (itemset.ContainsKey(key))
            {
                // Deletion of a key if zeroes and existing
                if (value == 0f) itemset.Remove(key);
                
                // Change the value
                itemset[key] = value;
            }
            else
            {
                // Adding the key to the collection
                itemset.Add(key, value);
            }
            
        }
        #endregion

        #region Filters
        
        /// <summary>
        /// Modify one emission probability by a factor. 
        /// </summary>
        /// <remarks>Must be used in conjuction with setting a filter label with RegisterFilter().</remarks>
        /// <param name="emissionName"></param>
        /// <param name="factor"></param>
        public void AdjustEmission(string emissionName, float factor = 1)
        {
            if (_emissions.ContainsKey(emissionName))
            {
                float newProbability = _emissions[emissionName];
                newProbability *= factor;
                SpecifyEmission(emissionName, newProbability);
            }
        }

        /// <summary>
        /// Keep a record of modifications to the emission probabilities
        /// </summary>
        /// <param name="filterName"></param>
        public void RegisterFilter(string filterName)
        {
            if (!_appliedFilters.Contains(filterName))
            {
                _appliedFilters.Add(filterName);
            }
        }

        #endregion

    }
}
using System.Collections.Generic;
using UnityEngine;

namespace HiddenMarkovProcess
{
    public class HiddenState : MonoBehaviour
    {
        
        private static readonly System.Random _stateRandom = new System.Random();
        
        // Unique identifier for this Hidden State
        public string stateName;
        
        // Transitions
        private readonly Dictionary<string, float> _transitions = new Dictionary<string, float>();
        
        // Emissions
        private readonly Dictionary<string, float> _emissions = new Dictionary<string, float>();

        // Housekeeping
        private bool _dirty = true;

        public HiddenState(string stateIdentity)
        {
            this.stateName = stateIdentity;
            
            // TODO Load HMM data from config
        }

        #region UnityStuff
    
        public void Start()
        {
            // Default emission-less permanent state
            _transitions.Add("", 1.0f);
            _emissions.Add("", 1.0f);
            
        }
        
        public ConfigNode AsConfigNode()
        {
            ConfigNode outputNode = new ConfigNode(this.stateName);
            
            // TODO actually write the HMM state
            
            return outputNode;
        }

        public void FromConfigNode(ConfigNode node)
        {
            
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
            return outputState == "" ? this.stateName : outputState;
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
                // TODO this expression should throw an exception
                foreach (KeyValuePair<string, float> kvp in target)
                {
                    if (kvp.Key != "")
                    {
                        target[kvp.Key] /= runningSum;
                    }
                    else target[kvp.Key] = 0f;
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
        private void Recompute()
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
        

    }
}
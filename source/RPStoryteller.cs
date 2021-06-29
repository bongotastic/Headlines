using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HiddenMarkovProcess;
using UnityEngine;


namespace RPStoryteller
{
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames, GameScenes.SPACECENTER)]
    public class RPStoryteller : ScenarioModule
    {

        // HMM data structures and parameters
        private Dictionary<string, HiddenState> _liveProcesses = new Dictionary<string, HiddenState>();
        private Dictionary<string, double> _hmmScheduler = new Dictionary<string, double>();
        private double _periodBase = 3600 * 24 * 10;
        
        #region UnityStuff
        
        public void Start()
        {
            LogLevel1("Initializing Starstruck");
            
            // Default HMM
            InitializeHMM("space_craze");
            
        }
        #endregion

        #region KSP
        
        /// <summary>
        /// Specialized serialization
        /// </summary>
        /// <param name="node"></param>
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            
            // Record all of the time triggers for Hidden models
            ConfigNode hmmSet = new ConfigNode("HIDDENMODELS");
            
            foreach (KeyValuePair<string, double> kvp in _hmmScheduler)
            {
                hmmSet.AddValue(kvp.Key, kvp.Value);
            }

            node.AddNode(hmmSet);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            
            // Read Hidden models and build set
            double transientTime;
            string modelName;
            
            ConfigNode hmNode = node.GetNode("HIDDENMODELS");
            if (hmNode != null)
            {
                foreach (ConfigNode.Value nodeVal in hmNode.values)
                {
                    modelName = nodeVal.name;
                    transientTime = double.Parse(nodeVal.value);

                    if (_hmmScheduler.ContainsKey(modelName))
                    {
                        _hmmScheduler[modelName] = transientTime;
                    }
                    else
                    {
                        InitializeHMM(modelName, transientTime);
                    }
                }
            }
        }

        /// <summary>
        /// Level 1 write to console. 
        /// </summary>
        /// <param name="message">The text to be logged.</param>
        public void LogLevel1(string message)
        {
            KSPLog.print($"[Starstruck] {message}");
        }

        /// <summary>
        /// Avoid some weird quirk from KCT editor revert (taken from RP-0)
        /// </summary>
        /// <returns>UT time in seconds</returns>
        public static double GetUT()
        {
            return HighLogic.LoadedSceneIsEditor ? HighLogic.CurrentGame.UniversalTime : Planetarium.GetUniversalTime();
        }
        
        #endregion

        #region HMM Logic

        /// <summary>
        /// Create a HMM and place it to be triggered at a later time.
        /// </summary>
        /// <param name="stateIdentity">The identifier in the config files.</param>
        private void InitializeHMM(string stateIdentity, double timestamp = 0)
        {
            // Avoid duplications
            if (_liveProcesses.ContainsKey(stateIdentity) == false)
            {
                HiddenState newState = new HiddenState(stateIdentity);
                
                _liveProcesses.Add(stateIdentity, newState);
                
                if (timestamp == 0) _hmmScheduler.Add(stateIdentity, GetUT() + _periodBase);
                else _hmmScheduler.Add(stateIdentity, timestamp);
                
                LogLevel1($"HMM {stateIdentity} launched to be triggered at {_hmmScheduler[stateIdentity]}.");
            }
        }

        /// <summary>
        /// Safely disable a HMM.
        /// </summary>
        /// <param name="stateIdentity">The identifier for this HMM</param>
        private void RemoveHMM(string stateIdentity)
        {
            if (_hmmScheduler.ContainsKey(stateIdentity)) _hmmScheduler.Remove(stateIdentity);
            if (_liveProcesses.ContainsKey(stateIdentity)) _liveProcesses.Remove(stateIdentity);
        }

        /// <summary>
        /// Execute a HMM transition from one hidden state to another.
        /// </summary>
        /// <param name="initialState">Identifier of the existing state to be discarded</param>
        /// <param name="finalState">Identifier of the state to initialize</param>
        private void TransitionHMM(string initialState, string finalState)
        {
            if (initialState != finalState)
            {
                LogLevel1($"State {initialState} transitions to state {finalState}.");
                InitializeHMM(finalState);
                RemoveHMM(initialState);
            }
        }

        #endregion

    }
}

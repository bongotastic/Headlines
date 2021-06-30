using System;
using System.Collections.Generic;
using System.Linq;
using HiddenMarkovProcess;



namespace RPStoryteller
{
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames, GameScenes.SPACECENTER)]
    public class RPStoryteller : ScenarioModule
    {
        // Random number generator
        private static System.Random storytellerRand = new System.Random();
        
        // HMM data structures and parameters
        private Dictionary<string, HiddenState> _liveProcesses = new Dictionary<string, HiddenState>();
        private Dictionary<string, double> _hmmScheduler = new Dictionary<string, double>();

        // Cached value for the next trigger so the Scheduler doesn't have to scan constantly
        private double _nextUpdate = -1;
        private double _assumedPeriod = 60;

        
        [KSPField(isPersistant = true)] public double attentionSpanFactor = 1;
        [KSPField(isPersistant = true)] public double programHypeFactor = 1;
        
        #region UnityStuff


        public void Start()
        {
            LogLevel1("Initializing Starstruck");
            
            // Default HMM
            InitializeHMM("space_craze");

            SchedulerCacheNextTime();
        }
        
        /// <summary>
        /// Heartbeat of the Starstruck mod. 
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void Update()
        {
            // Minimizing the profile of this method's call.
            if (_nextUpdate <= GetUT()) SchedulerUpdate(GetUT());
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

            ConfigNode hmNode = node.GetNode("HIDDENMODELS");
            if (hmNode != null)
            {
                // Read Hidden models and build set
                double transientTime;
                string modelName;
                
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
            // TODO figure out why a new state has the same transitions as original
            // Avoid duplications
            if (_liveProcesses.ContainsKey(stateIdentity) == false)
            {
                HiddenState newState = new HiddenState(stateIdentity);
                
                _liveProcesses.Add(stateIdentity, newState);
                
                if (timestamp == 0) _hmmScheduler.Add(stateIdentity, GetUT() + GeneratePeriod( newState.period ));
                else _hmmScheduler.Add(stateIdentity, timestamp);
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
                LogLevel1($"[HMM] Entering hidden state {finalState}.");
                InitializeHMM(finalState);
                RemoveHMM(initialState);
            }
        }

        /// <summary>
        /// Generate a nearly normal random number of days for a HMM triggering event. Box Muller transform
        /// taken from https://stackoverflow.com/questions/218060/random-gaussian-variables
        /// Guarantees that the period is at least 1/20 of the baseValue.
        /// </summary>
        /// <param name="baseValue">The base value specified in the config node of a HMM</param>
        /// <returns></returns>
        private double GeneratePeriod(double baseValue)
        {
            double stdDev = baseValue / 3;
            double u1 = 1.0- storytellerRand.NextDouble(); 
            double u2 = 1.0- storytellerRand.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                                   Math.Sin(2.0 * Math.PI * u2);
            double returnedVal = baseValue + stdDev * randStdNormal;
            double floorVal = baseValue / 20;
            double outValue = Math.Max(returnedVal * attentionSpanFactor, floorVal);

            // Convert to seconds based on hardcoded period
            return outValue * _assumedPeriod; 
        }
        #endregion

        #region Scheduling

        /// <summary>
        /// Update the cached value for nextUpdate by finding the smallest value in the scheduler.
        /// </summary>
        private void SchedulerCacheNextTime()
        {
            _nextUpdate = GetUT();
            
            double minVal = 60;
            foreach (KeyValuePair<string, double> kvp in _hmmScheduler)
            {
                if (minVal == 60) minVal = kvp.Value;
                else
                {
                    if (kvp.Value < minVal) minVal = kvp.Value;
                }
            }
            _nextUpdate = minVal;
        }

        private void SchedulerUpdate(double currentTime)
        {
            string emittedString = "";
            string transitionString = "";
            
            // Make a list of states to trigger
            List<string> triggerStates = new List<string>();
            foreach (KeyValuePair<string, double> kvp in _hmmScheduler)
            {
                if (kvp.Value <= currentTime)
                {
                    triggerStates.Add(kvp.Key);
                }
            }

            // Do the deed
            foreach (string stateName in triggerStates)
            {
                emittedString = _liveProcesses[stateName].Emission();
                if (emittedString != "")
                {
                    EmitEvent(emittedString);
                }

                transitionString = _liveProcesses[stateName].Transition();
                if (transitionString != stateName)
                {
                    TransitionHMM(stateName, transitionString);
                }
                else
                {
                    _hmmScheduler[stateName] = currentTime + GeneratePeriod(_liveProcesses[stateName].period);
                    //LogLevel1($"[HMM] State {stateName} will trigger at time {KSPUtil.PrintDate(_hmmScheduler[stateName], true, false)}.");
                } 
            }
            SchedulerCacheNextTime();
        }


        #endregion

        #region Events

        /// <summary>
        /// Primary handler for the emission of events.
        /// </summary>
        /// <param name="eventName"></param>
        public void EmitEvent(string eventName)
        {
            LogLevel1($"[Emission] {eventName} at time { KSPUtil.PrintDate(GetUT(), true, false) }");

            switch (eventName)
            {
                case "attention_span_long":
                    AdjustAttentionSpan(1);
                    break;
                case "attention_span_short":
                    AdjustAttentionSpan(-1);
                    break;
                default:
                    LogLevel1($"[Emission] Event {eventName} is not implemented yet.");
                    break;
            }
        }

        public void AdjustAttentionSpan(double increment)
        {
            double power = 1.618;
            if (increment < 0)
            {
                power = 1 / power;
            }

            attentionSpanFactor *= power;
            
            LogLevel1($"New attentionSpanFactor = {attentionSpanFactor}");
        }
        #endregion
    }
}

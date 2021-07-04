using System;
using System.Collections.Generic;
using System.Linq;
using HiddenMarkovProcess;
using RPStoryteller.source;


namespace RPStoryteller
{
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames, GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class RPStoryteller : ScenarioModule
    {
        #region Declarations
        
        // Random number generator
        private static System.Random storytellerRand = new System.Random();
        
        // Mod data structures
        private RPPeopleManager _peopleManager;

        // HMM data structures and parameters
        private Dictionary<string, HiddenState> _liveProcesses = new Dictionary<string, HiddenState>();
        private Dictionary<string, double> _hmmScheduler = new Dictionary<string, double>();

        // Cached value for the next trigger so the Scheduler doesn't have to scan constantly
        private double _nextUpdate = -1;
        private double _assumedPeriod = 60;

        
        [KSPField(isPersistant = true)] public double attentionSpanFactor = 1;
        [KSPField(isPersistant = true)] public double programHypeFactor = 1;
        [KSPField(isPersistant = true)] public float programHype = 1;
        [KSPField(isPersistant = true)] public float programLastKnownReputation = 0;
        #endregion
        
        #region UnityStuff
        
        public void Start()
        {
            StarStruckUtil.Report(1,"Initializing Starstruck");
            
            // Default HMM
            InitializeHMM("space_craze");
            SchedulerCacheNextTime();

            InitializePeopleManager();
            
            // Event Catching
            GameEvents.OnReputationChanged.Add(ReputationChanged);
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

            
            double triggertime;
            HiddenState stateToSave;
            ConfigNode temporaryNode = new ConfigNode();

            foreach (KeyValuePair<string, double> kvp in _hmmScheduler)
            {
                triggertime = kvp.Value;
                stateToSave = _liveProcesses[kvp.Key];

                temporaryNode = new ConfigNode();
                temporaryNode.AddValue("stateName", stateToSave.RealStateName());
                temporaryNode.AddValue("nextTrigger", triggertime);

                if (stateToSave.kerbalName != "") temporaryNode.AddValue("kerbalName", stateToSave.kerbalName);


                hmmSet.AddNode("hmm", temporaryNode);
            }

            node.AddNode(hmmSet);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            
            _liveProcesses.Clear();
            _hmmScheduler.Clear();

            ConfigNode hmNode = node.GetNode("HIDDENMODELS");
            if (hmNode != null)
            {
                // Read Hidden models and build set
                double transientTime;
                string modelName;
                string kerbalName;
                
                foreach (ConfigNode itemNode in hmNode.GetNodes())
                {
                    
                    modelName = itemNode.GetValue("stateName");
                    transientTime = double.Parse(itemNode.GetValue("nextTrigger"));
                    if (itemNode.HasValue("kerbalName"))
                    {
                        kerbalName = itemNode.GetValue("kerbalName");
                    }
                    else
                    {
                        kerbalName = "";
                    }

                    if (_hmmScheduler.ContainsKey(modelName))
                    {
                        _hmmScheduler[modelName] = transientTime;
                    }
                    else
                    {
                        InitializeHMM(modelName, transientTime, kerbalName);
                    }
                }
            }
        }

        /// <summary>
        /// Avoid some weird quirk from KCT editor revert (taken from RP-0)
        /// </summary>
        /// <returns>UT time in seconds</returns>
        public static double GetUT()
        {
            return StarStruckUtil.GetUT();
        }

        /// <summary>
        /// Highjacks all increase in reputation to ensure that they are capped at the program hype level.
        /// </summary>
        /// <param name="newReputation">The new, unmodified reputation</param>
        /// <param name="reason">Transactionreason item</param>
        private void ReputationChanged(float newReputation, TransactionReasons reason)
        {
            float deltaReputation = newReputation - this.programLastKnownReputation;
            float actualDeltaReputation = deltaReputation;

            // Avoid processing recursively the adjustment
            if ( reason == TransactionReasons.None) return;
            
            StarStruckUtil.Report(1,$"New delta: {deltaReputation} whilst hype is {this.programHype}.");
            if (deltaReputation <= this.programHype)
            {
                this.programHype -= deltaReputation;
                this.programLastKnownReputation = newReputation;
            }
            else
            {
                // Retroactively cap the reputation gain to the active hype
                this.programLastKnownReputation += this.programHype;
                Reputation.Instance.SetReputation(this.programLastKnownReputation, TransactionReasons.None);
                StarStruckUtil.Report(1,$"Reputation was capped at {this.programHype} due to insufficient hype.");
                StarStruckUtil.ScreenMessage($"Underrated! Your achievement's impact is limited.\n({(this.programHype/deltaReputation).ToString("P1")})");
                
                // Surplus reputation goes as additional hype
                this.programHype = deltaReputation - this.programHype;
            }
            StarStruckUtil.Report(1,$"Program hype is now {this.programHype}.");
            
        }
        #endregion

        #region kerbals

        /// <summary>
        /// Register all kerbal HMM into event generation structures.
        /// </summary>
        private void InitializePeopleManager()
        {
            _peopleManager = new RPPeopleManager();
            _peopleManager.RefreshPersonnelFolder();

            foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.personnelFolders)
            {
                // Productivity 
                InitializeHMM("kerbal_" + kvp.Value.kerbalProductiveState, kerbalName: kvp.Value.UniqueName());

                // Task
                InitializeHMM("kerbal_" + kvp.Value.Specialty(), kerbalName: kvp.Value.UniqueName());
            }
        }

        /// <summary>
        /// Determines the outcome of a check based on a mashup of Pendragon and VOID.
        /// </summary>
        /// <param name="skillLevel">0+ arbitrary unit</param>
        /// <param name="difficulty">0+ arbitrary unit</param>
        /// <returns>FUMBLE|FAILURE|SUCCESS|CRITICAL</returns>
        static string SkillCheck(int skillLevel, int difficulty)
        {
            int upperlimit = 3 * skillLevel;
            int lowerlimit = 3 * difficulty;

            string outcome = "FAILURE";

            int die = storytellerRand.Next(1, 20);

            if (upperlimit > 20) die += (upperlimit - 20);
            else if (die == 20) outcome = "FUMBLE";

            if (die == upperlimit || (upperlimit >= 20 && die >= 20)) outcome = "CRITICAL";
            else if (die >= lowerlimit && die < upperlimit) outcome = "SUCCESS";

            return outcome;
        }
        #endregion

        #region HMM Logic

        /// <summary>
        /// Create a HMM and place it to be triggered at a later time.
        /// </summary>
        /// <param name="stateIdentity">The identifier in the config files.</param>
        private void InitializeHMM(string stateIdentity, double timestamp = 0, string kerbalName = "")
        {
            // TODO update personnel file on the new state
            
            string templateStateIdentity = stateIdentity;
            
            // Split template and kerbal parts when initialized from a save node
            int splitter = stateIdentity.IndexOf("@");
            if (splitter != -1)
            {
                templateStateIdentity = stateIdentity.Substring(splitter + 1);
                kerbalName = stateIdentity.Substring(0, splitter);
            }
            
            // Avoid duplications
            if (_liveProcesses.ContainsKey(stateIdentity) == false)
            {
                HiddenState newState = new HiddenState(templateStateIdentity, kerbalName);
                StarStruckUtil.Report(1, $"Registering {templateStateIdentity} for {kerbalName} as {newState.RealStateName()}.");

                _liveProcesses.Add(newState.RegisteredName(), newState);
                
                if (timestamp == 0) _hmmScheduler.Add(newState.RegisteredName(), GetUT() + GeneratePeriod( newState.period ));
                else _hmmScheduler.Add(newState.RegisteredName(), timestamp);
                
                // Record new state into personnel file
                if (kerbalName != "")
                {
                    _peopleManager.GetFile(kerbalName).EnterNewState(templateStateIdentity);
                }
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
        /// Execute a HMM transition from one hidden state to another. Assumes that 
        /// </summary>
        /// <param name="initialState">Identifier of the existing state to be discarded as registered in _liveProcess</param>
        /// <param name="finalState">Identifier of the state to initialize without the kerbal name if applicable</param>
        private void TransitionHMM(string initialState, string finalState)
        {
            InitializeHMM(finalState, kerbalName:_liveProcesses[initialState].kerbalName);
            RemoveHMM(initialState);
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

        /// <summary>
        /// Set/Reset the next trigger time for Registered state name. Assumes that it is in the system.
        /// </summary>
        /// <param name="registeredStateName">State as registered</param>
        /// <param name="baseTime">Depending on the state itself.</param>
        public void ReScheduleHMM(string registeredStateName, double baseTime)
        {
            _hmmScheduler[registeredStateName] = StarStruckUtil.GetUT() + GeneratePeriod(_liveProcesses[registeredStateName].period);
        }

        private void SchedulerUpdate(double currentTime)
        {
            string emittedEvent = "";
            string nextTransitionState = "";
            
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
            foreach (string registeredStateName in triggerStates)
            {
                emittedEvent = _liveProcesses[registeredStateName].Emission();

                if (emittedEvent != "")
                {
                    if (_liveProcesses[registeredStateName].kerbalName != "")
                    {
                        PersonnelFile personnelFile = _peopleManager.GetFile(_liveProcesses[registeredStateName].kerbalName);
                        EmitEvent(emittedEvent, personnelFile);
                    }
                    else
                    {
                        EmitEvent(emittedEvent);
                    }
                }

                nextTransitionState = _liveProcesses[registeredStateName].Transition();
                if (nextTransitionState != _liveProcesses[registeredStateName].RealStateName())
                {
                    TransitionHMM(registeredStateName, nextTransitionState);
                }
                else
                {
                    ReScheduleHMM(registeredStateName, _liveProcesses[registeredStateName].period);
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
            StarStruckUtil.Report(1,$"[Emission] {eventName} at time { KSPUtil.PrintDate(GetUT(), true, false) }");

            switch (eventName)
            {
                case "attention_span_long":
                    AdjustAttentionSpan(1);
                    break;
                case "attention_span_short":
                    AdjustAttentionSpan(-1);
                    break;
                case "hype_boost":
                    AdjustHype(1f);
                    break;
                case "hype_dampened":
                    AdjustHype(-1f);
                    break;
                default:
                    StarStruckUtil.Report(1,$"[Emission] {eventName} is not implemented yet.");
                    break;
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="eventName"></param>
        /// <param name="personnelFile"></param>
        public void EmitEvent(string eventName, PersonnelFile personnelFile)
        {
            personnelFile.TrackCurrentActivity(eventName);
            
            switch (eventName)
            {
                case "bogus":
                    break;
                default:
                    StarStruckUtil.Report(1,$"[Emission] Event {eventName} is not implemented yet.");
                    break;
            }
        }

        /// <summary>
        /// Adjust the time it takes to trigger Reputation Decay. The golden ratio here is about just right
        /// for the purpose.
        /// </summary>
        /// <param name="increment">either -1 or 1</param>
        public void AdjustAttentionSpan(double increment)
        {
            double power = 1.618;
            if (increment < 0)
            {
                power = 1 / power;
            }

            attentionSpanFactor *= power;
            
            // Clamp this factor within reasonable boundaries
            attentionSpanFactor = Math.Max(Math.Pow(power, -5), attentionSpanFactor); // That's a 3.24-day span
            attentionSpanFactor = Math.Min(Math.Pow(power, 3), attentionSpanFactor); // That's a 152-day span
            
            StarStruckUtil.Report(1,$"New attentionSpanFactor = {attentionSpanFactor}");
        }

        /// <summary>
        /// Adjust hype in either direction in increments of 5.
        /// </summary>
        /// <param name="increment">(float)the number of increment unit to apply.</param>
        public void AdjustHype(float increment)
        {
            // Simplistic model ignoring reasonable targets
            this.programHype += increment * 5f;
            this.programHype = Math.Max(0f, this.programHype);
            
            StarStruckUtil.Report(1,$"Hype on the program changed by {increment*5f} to now be {this.programHype}.");
            StarStruckUtil.ScreenMessage($"Program Hype: {string.Format("{0:0}", this.programHype)}");
        }
        
        #endregion
    }
}

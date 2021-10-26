using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Contracts;
using Expansions.Missions.Editor;
using FinePrint;
using HiddenMarkovProcess;
using KerbalConstructionTime;
using Renamer;
using Headlines.source;
using Headlines.source.Emissions;
using KSP.IO;
using UnityEngine;
using UnityEngine.Serialization;


namespace Headlines
{
    public enum ImpactType
    {
        NEGATIVE,
        PASSIVE,
        TRANSIENT,
        LASTING
    }

    /// <summary>
    /// As per PENDRAGON system 
    /// </summary>
    public enum SkillCheckOutcome
    {
        FUMBLE, FAILURE, SUCCESS, CRITICAL
    }

    //[KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames,
    //    GameScenes.SPACECENTER | GameScenes.FLIGHT )]
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, new GameScenes[] { GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION })]
    public class StoryEngine : ScenarioModule
    {
        #region Declarations

        public static StoryEngine Instance = null;

        // Random number generator
        private static System.Random storytellerRand = new System.Random();

        // Mod data structures
        private PeopleManager _peopleManager;

        public ReputationManager _reputationManager = new ReputationManager();

        public ProgramManager _programManager = new ProgramManager();
        
        // Terrible hack
        private int updateIndex = 0;

        // HMM data structures and parameters
        private Dictionary<string, HiddenState> _liveProcesses = new Dictionary<string, HiddenState>();
        public Dictionary<string, double> _hmmScheduler = new Dictionary<string, double>();

        // Cached value for the next trigger so the Scheduler doesn't have to scan constantly
        private double _nextUpdate = -1;

        // Master switch of the mod's tempo in second. Should be 36 days for a real run. 
        private double _assumedPeriod = 3600 * 24;

        // Prevent infinite recursion
        private bool _scienceManipultation = false;

        // Multiplier to the _assumedPeriod when it comes to HMM triggering
        [KSPField(isPersistant = true)] public double attentionSpanFactor = 1;

        // Maximum earning in reputation in a single increase call.
        [KSPField(isPersistant = true)] public float programHype = 10;
        
        // To appeal to the optimizers
        [KSPField(isPersistant = true)] public double headlinesScore = 0;
        [KSPField(isPersistant = true)] public double lastScoreTimestamp = 0;

        // Pledged hype when inviting the public
        [KSPField(isPersistant = true)] public bool mediaSpotlight = false;
        [KSPField(isPersistant = true)] public double endSpotlight = 0;
        [KSPField(isPersistant = true)] public double wageredReputation = 0;

        // Cache of the last time we manipulated repuation
        [KSPField(isPersistant = true)] public float programLastKnownReputation = 0;

        // Highest reputation achieved (including overvaluation)
        [KSPField(isPersistant = true)] public float programHighestValuation = 0;
        
        // Hiring payroll rebate
        [KSPField(isPersistant = true)] public int programPayrollRebate = 1;

        // Antagonized potential capital campaign donors
        [KSPField(isPersistant = true)] public bool fundraisingBlackout = false;

        // sum of raised funds
        [KSPField(isPersistant = true)] public double fundraisingTally = 0;

        // Visiting scholars
        public List<double> visitingScholarEndTimes = new List<double>();
        [KSPField(isPersistant = true)] public bool visitingScholar = false;
        [KSPField(isPersistant = true)] public float programLastKnownScience = 0;
        [KSPField(isPersistant = true)] public float visitingScienceTally = 0;
        [KSPField(isPersistant = true)] public float totalScience = 0;
        [KSPField(isPersistant = true)] public string visitingScholarName = "";

        // Inquiry
        [KSPField(isPersistant = true)] public bool ongoingInquiry = false;
        private List<string> newDeath = new List<string>();

        // Launch detection
        private List<Vessel> newLaunch = new List<Vessel>();
        [KSPField(isPersistant = true)] public bool highDramaReported = false;
        [KSPField(isPersistant = true)] public bool overUrbanReported = false;

        // New Game flag
        [KSPField(isPersistant = true)] public bool hasnotvisitedAstronautComplex = true;
        public bool inAstronautComplex = false;
        
        // Logging
        [KSPField(isPersistant = true)] public HeadlineScope notificationThreshold = HeadlineScope.NEWSLETTER;
        [KSPField(isPersistant = true)] public HeadlineScope feedThreshold = HeadlineScope.FEATURE;
        [KSPField(isPersistant = true)] public bool logDebug = true;
        public Queue<NewsStory> headlines = new Queue<NewsStory>();
         
        // UI states
        public ConfigNode UIStates = null;
        
        // HMM to remove
        private List<string> _hmmToRemove = new List<string>();
        
        #endregion

        #region UnityStuff

        public override void OnAwake()
        {
            base.OnAwake();
        }

        /// <summary>
        /// Unity method with some basics stuff that needs to run once inside a the scene.
        /// </summary>
        public void Start()
        {
            Instance = this;

            Debug( "Initializing Storyteller");

            // Default HMM
            InitializeHMM("space_craze");
            InitializeHMM("reputation_decay");
            InitializeHMM("position_search");
            InitializeHMM("program_manager");

            InitializePeopleManager();
            SchedulerCacheNextTime();

            // Event Catching
            GameEvents.OnReputationChanged.Add(EventReputationChanged);
            GameEvents.OnScienceChanged.Add(EventScienceChanged);
            GameEvents.OnCrewmemberSacked.Add(EventCrewSacked);
            GameEvents.OnCrewmemberHired.Add(EventCrewHired);
            GameEvents.onCrewKilled.Add(EventCrewKilled);
            GameEvents.onKerbalAddComplete.Add(EventNewKerbalInRoster);
            GameEvents.onVesselSituationChange.Add(EventRegisterLaunch);
            GameEvents.onGUIAstronautComplexSpawn.Add(EventAstronautComplexSpawn);
            GameEvents.onGUIAstronautComplexDespawn.Add(EventAstronautComplexDespawn);
            GameEvents.Contract.onCompleted.Add(EventContractCompleted);
            GameEvents.Contract.onAccepted.Add(EventContractAccepted);
        }

        /// <summary>
        /// Heartbeat of the Starstruck mod. Using a cached _nextupdate so as to
        /// avoid checking the collection's values all the time.
        /// </summary>
        public void Update()
        {
            // Do not run Headlines outside of career
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER) return;

            // Shameless hack.
            if (updateIndex < 10)
            {
                _programManager.SetStoryEngine(this);
                
                _reputationManager.ReattemptLoadContracts();

                try
                {
                    KACWrapper.InitKACWrapper();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }

                if (updateIndex == 9)
                {
                    // if the mod is installed in a new career (less than an hour), randomize crew specialty
                    if (HeadlinesUtil.GetUT() < 3600)
                    {
                        Debug("New career detected, randomizing crew specialty");
                        _peopleManager.RandomizeStartingCrew();
                    }
                    AssertRoleHMM();
                    _peopleManager.RefreshPersonnelFolder();
                    _peopleManager.initialized = true;
                }
                updateIndex += 1;
                
                // todo delete 0.6.1 backward compatibility in a while 
                _programManager.SetInitialReputation(_reputationManager.CurrentReputation());
            }
            
            _reputationManager.SetLastKnownCredibility(Reputation.CurrentRep);

            // Unfortunate addition to the Update look to get around weirdness in Event Firing.
            if (newDeath.Count != 0) DeathRoutine();
            if (newLaunch.Count != 0) CrewedLaunchReputation();
            
            // Kill impact if a kerbal is inactive
            CancelInfluenceForInactiveCrew();
            
            // Never allow 0 applicants
            if (_peopleManager.applicantFolders.Count == 0) NewRandomApplicant();

            // Another dumb hack
            if (ongoingInquiry && !_liveProcesses.ContainsKey("death_inquiry")) InitializeHMM("death_inquiry");
            
            // End of Media spotlight?
            MediaEventUpdate();
            
            // Minimizing the profile of this method's call.
            if (_nextUpdate <= GetUT()) SchedulerUpdate(GetUT());
        }

        #endregion

        #region KSP

        /// <summary>
        /// Specialized serialization. Handles serialization of the Scheduler. 
        /// </summary>
        /// <param name="node">Unity passes this one.</param>
        public override void OnSave(ConfigNode node)
        {

            _peopleManager.RefreshPersonnelFolder();
            
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
                temporaryNode.AddValue("stateName", stateToSave.TemplateStateName());
                temporaryNode.AddValue("nextTrigger", triggertime);

                if (stateToSave.kerbalName != "") temporaryNode.AddValue("kerbalName", stateToSave.kerbalName);


                hmmSet.AddNode("hmm", temporaryNode);
            }
            node.AddNode(hmmSet);

            ConfigNode headlineFeed = new ConfigNode("HEADLINESFEED");
            foreach (NewsStory ns in headlines)
            {
                headlineFeed.AddNode("headline", ns.AsConfigNode());
            }
            node.AddNode(headlineFeed);

            ConfigNode visitingEndTimes = new ConfigNode("VISITINGSCHOLAR");
            foreach (double time in visitingScholarEndTimes)
            {
                visitingEndTimes.AddValue("time", time);
            }
            node.AddNode(visitingEndTimes);
            
            node.AddNode(_reputationManager.AsConfigNode());
            node.AddNode(_programManager.AsConfigNode());

            if (UIStates != null)
            {
                node.AddNode("UIStates", UIStates);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            Debug("Loading Storyteller");
            base.OnLoad(node);

            _liveProcesses.Clear();
            _hmmScheduler.Clear();
            
            ConfigNode hlNode = node.GetNode("HEADLINESFEED");
            if (hlNode != null)
            {
                foreach (ConfigNode headline in hlNode.GetNodes("headline"))
                {
                    headlines.Enqueue(new NewsStory(headline));
                }
            }
            
            foreach (ConfigNode rmNode in node.GetNodes("REPUTATIONMANAGER"))
            {
                _reputationManager.FromConfigNode(rmNode);
            }

            ConfigNode pmNode = node.GetNode("PROGRAMMANAGER");
            if (pmNode != null)
            {
                _programManager.FromConfigNode(pmNode);
            }
            
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
            
            ConfigNode vsNode = node.GetNode("VISITINGSCHOLAR");
            if (vsNode != null)
            {
                foreach (ConfigNode.Value cfV in vsNode.values)
                {
                    visitingScholarEndTimes.Add(double.Parse(cfV.value));
                }
            }
            visitingScholarEndTimes.Sort();

            if (node.HasNode("UIStates"))
            {
                UIStates = node.GetNode("UIStates");
            }
            
        }

        private void OnDestroy()
        {
            GameEvents.OnReputationChanged.Remove(EventReputationChanged);
            GameEvents.OnScienceChanged.Remove(EventScienceChanged);
            GameEvents.OnCrewmemberSacked.Remove(EventCrewSacked);
            GameEvents.OnCrewmemberHired.Remove(EventCrewHired);
            GameEvents.onCrewKilled.Remove(EventCrewKilled);
            GameEvents.onKerbalAddComplete.Remove(EventNewKerbalInRoster);
            GameEvents.onVesselSituationChange.Remove(EventRegisterLaunch);
            GameEvents.onGUIAstronautComplexSpawn.Remove(EventAstronautComplexSpawn);
            GameEvents.onGUIAstronautComplexDespawn.Remove(EventAstronautComplexDespawn);
            GameEvents.Contract.onCompleted.Remove(EventContractCompleted);
            GameEvents.Contract.onAccepted.Remove(EventContractAccepted);
        }

        /// <summary>
        /// Avoid some weird quirk from KCT editor revert (taken from RP-0)
        /// </summary>
        /// <returns>UT time in seconds</returns>
        public static double GetUT()
        {
            return HeadlinesUtil.GetUT();
        }

        public double GetFunds()
        {
            return Funding.Instance.Funds;
        }

        public void AdjustFunds(double deltaFund)
        {
            Debug( $"Adjusting funds by {deltaFund}", "FUNDS");
            Funding.Instance.AddFunds(deltaFund, TransactionReasons.None);
        }

        /// <summary>
        /// Highjacks all increase in reputation to ensure that they are capped at the program hype level.
        /// </summary>
        /// <param name="newReputation">The new, unmodified reputation</param>
        /// <param name="reason">Transactionreason item</param>
        private void EventReputationChanged(float newReputation, TransactionReasons reason)
        {
            // Avoid processing recursively the adjustment
            if (reason == TransactionReasons.None)
            {
                _reputationManager.UpdatePeakReputation();
                return;
            }

            // Don't grant reputation for losing a vessel
            if (reason == TransactionReasons.VesselLoss)
            {
                _reputationManager.IgnoreLastCredibilityChange();
                return;
            }
            
            _reputationManager.HighjackCredibility(newReputation, reason);

            if ( newReputation - _reputationManager.Credibility() >= 1 && _reputationManager.currentMode != MediaRelationMode.LOWPROFILE)
            {
                HeadlinesUtil.Report(2, $"{Math.Round(newReputation - _reputationManager.Credibility(),MidpointRounding.AwayFromZero)} reputation spoiled");
            }
        }

        /// <summary>
        /// Can do so much more interesting things than this, but this is a stub where visiting scholars are boosting science
        /// output of experiments by 20%. Maybe a visiting scholar could be a "tourist" or a Scientist.
        /// </summary>
        /// <param name="newScience"></param>
        /// <param name="reason"></param>
        private void EventScienceChanged(float newScience, TransactionReasons reason)
        {
            // Kills recursion
            if (_scienceManipultation)
            {
                _scienceManipultation = false;
                return;
            }
            
            float deltaScience = (newScience - programLastKnownScience);
            if (deltaScience > 0) totalScience += deltaScience;
            Debug( $"Adding {deltaScience} science", "Science");
            
            if (visitingScholarEndTimes.Count != 0)
            {
                deltaScience *= VisitingScienceBonus();
                if (deltaScience >= 0)
                {
                    visitingScienceTally += deltaScience;

                    _scienceManipultation = true;
                    ResearchAndDevelopment.Instance.CheatAddScience(deltaScience);
                    HeadlinesUtil.Report(3, $"Visiting scholar bonus: {deltaScience}", "Science");
                }
            }

            this.programLastKnownScience = newScience;
        }

        /// <summary>
        /// Event handler for sacking a kerbal
        /// </summary>
        /// <param name="pcm"></param>
        /// <param name="count"></param>
        public void EventCrewSacked(ProtoCrewMember pcm, int count)
        {
            Debug($"{pcm.name} sacked", "HR");
            if (pcm.type == ProtoCrewMember.KerbalType.Crew)
            {
                KerbalResignation(_peopleManager.GetFile(pcm.name), new Emissions("quit"));
            }
            else if (pcm.type == ProtoCrewMember.KerbalType.Applicant &
                     _peopleManager.personnelFolders.ContainsKey(pcm.name))
            {
                // Recently sacked crewmember
                KerbalSacked(_peopleManager.GetFile(pcm.name));
            }
        }

        /// <summary>
        /// Catches new hires from the kerbonaut centre and add to Headlines' _peopleManager
        /// </summary>
        /// <param name="pcm"></param>
        /// <param name="count"></param>
        public void EventCrewHired(ProtoCrewMember pcm, int count)
        {
            if (programPayrollRebate > 0)
            {
                Funding.Instance.AddFunds(HiringRebate(), TransactionReasons.None);
                programPayrollRebate -= 1;
            }
            PersonnelFile newCrew = _peopleManager.GetFile(pcm.name);
            _peopleManager.HireApplicant(newCrew);
            InitializeCrewHMM(newCrew);
            
            // Add to credibility
            _reputationManager.AdjustCredibility(newCrew.Effectiveness(deterministic:true));
            
            while (_peopleManager.applicantFolders.Count <= 1)
            {
                _peopleManager.GenerateRandomApplicant();
            }
        }

        public void EventCrewKilled(EventReport data)
        {
            if (!newDeath.Contains(data.sender))
            {
                newDeath.Add(data.sender);
            }
        }

        /// <summary>
        /// Delayed event handler that is likely no longer necessary and can just as well be moved back to CrewKilled
        /// </summary>
        public void DeathRoutine()
        {
            List<string> alreadyProcessed = new List<string>();
            
            foreach (string crewName in newDeath)
            {
                if (alreadyProcessed.Contains(crewName))
                {
                    continue;
                }
                alreadyProcessed.Add(crewName);
                
                NewsStory ns = new NewsStory(HeadlineScope.FRONTPAGE, $"Death inquiry for {crewName} launched.",
                    "Public Inquiry");
                FileHeadline(ns);

                PersonnelFile personnelFile = _peopleManager.GetFile(crewName);
                if (personnelFile == null) PrintScreen("null pfile");
                
                // Credibility loss
                double repLoss = -2 * personnelFile.Effectiveness(deterministic: true);
                HeadlinesUtil.Report(2,$"Initial shock at {personnelFile.DisplayName()}'s death. Credibility decreased by {repLoss}");
                _reputationManager.AdjustCredibility(repLoss);
                
                // Kill all Hype
                _reputationManager.ResetHype();
            
                // Make crew members a bit more discontent
                _peopleManager.OperationalDeathShock(crewName);
                
                // Paralyze the KCS
                CancelAllInfluence();
            
                // inquiry
                InitializeHMM("death_inquiry");
                ongoingInquiry = true;

                // Remove influence
                PrintScreen( "Canceling influence");
                CancelInfluence(personnelFile, leaveKSC: true);

                // HMMs
                RemoveKerbalHMM(personnelFile.UniqueName());

                // Make it happen
                _peopleManager.RemoveKerbal(personnelFile);
            }
            
            newDeath.Clear();
        }

        /// <summary>
        /// Whenever a kerbal enters the roster (all type), this is triggered and thus creates a file.
        /// </summary>
        /// <param name="pcm"></param>
        public void EventNewKerbalInRoster(ProtoCrewMember pcm)
        {
            if (pcm.type == ProtoCrewMember.KerbalType.Applicant)
            {
                PersonnelFile pf = _peopleManager.GetFile(pcm.name);
                Debug( $"Adding {pf.UniqueName()} to as {pf.Specialty()}");
            }
        }

        /// <summary>
        /// Check to see if a change begins with Pre-launch and is the active vessel.
        /// </summary>
        /// <param name="ev"></param>
        public void EventRegisterLaunch(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> ev)
        {
            if (ev.from == Vessel.Situations.PRELAUNCH && ev.host == FlightGlobals.ActiveVessel)
            {
                // newLaunch might as well be a pointer
                if (newLaunch.Count == 0)
                {
                    newLaunch.Add(ev.host);
                }
            }
        }

        /// <summary>
        /// Reputation and hype gained by launching a kerbal. 
        /// </summary>
        public void CrewedLaunchReputation()
        {
            // automatically begin a live event if in the media event window
            if (_reputationManager.currentMode == MediaRelationMode.CAMPAIGN)
            {
                double now = HeadlinesUtil.GetUT();
                if (now >= _reputationManager.airTimeStarts && now <= _reputationManager.airTimeEnds)
                {
                    _reputationManager.GoLIVE();
                }
            }
            
            highDramaReported = false;
            overUrbanReported = false;
            
            foreach (Vessel vessel in newLaunch)
            {
                _programManager.RegisterLaunch(vessel);
                
                // get crew
                List<ProtoCrewMember> inFlight = vessel.GetVesselCrew();

                float onboardHype = 0f;
                float individualHype = 0f;
            
                PersonnelFile pf;
                foreach (ProtoCrewMember pcm in inFlight)
                {
                    pf = _peopleManager.GetFile(pcm.name);
                    individualHype = (float)_reputationManager.AdjustHype(pf.Effectiveness());
                    onboardHype += individualHype;
                    pf.AddLifetimeHype((int)individualHype);
                    pf.AdjustDiscontent(-1);
                    CancelInfluence(pf);
                }

                if (onboardHype != 0)
                {
                    HeadlinesUtil.Report(3, $"Hype and rep increased by {onboardHype} due to the crew.", "Crew in Flight");
                    _reputationManager.AdjustCredibility(onboardHype, reason: TransactionReasons.None);
                }
            }
            newLaunch.Clear();
            
            // Program manager burn-out 
            // Slowly happens over time
            double pmBurnout = 0;
            if (_programManager.ManagerIsTired())
            {
                // Campaigns are hard
                pmBurnout = _reputationManager.currentMode == MediaRelationMode.CAMPAIGN ? -2 : -1;
            }
            // Waning star effect
            if (_programManager.ManagerInitialCredibility() / _reputationManager.CurrentReputation() <= 0.8)
                pmBurnout -= 1;
            // Success high
            if (_reputationManager.CurrentReputation() - _programManager.ManagerInitialCredibility() >= 50)
                pmBurnout += 0.5;
            _programManager.AdjustRemainingLaunches(pmBurnout);

        }

        /// <summary>
        /// Flag to determine what to display on the Recruitment UI tab when there is no applicants to show.
        /// </summary>
        /// <remarks>New Refresh routines may be negating the need for hasnotvisited...</remarks>
        private void EventAstronautComplexSpawn()
        {
            // Clean up the KSP stuff that happened meanwhile
            _peopleManager.RefreshPersonnelFolder();
            
            inAstronautComplex = true;
            if (hasnotvisitedAstronautComplex)
            {
                LaunchSearch();
                hasnotvisitedAstronautComplex = false;
            }
        }

        /// <summary>
        /// Needed to generate the files when a new career starts.
        /// </summary>
        private void EventAstronautComplexDespawn()
        {
            inAstronautComplex = false;
            _peopleManager.RefreshPersonnelFolder();
        }

        #endregion

        #region kerbals

        /// <summary>
        /// Register all kerbal HMM into event generation structures.
        /// </summary>
        private void InitializePeopleManager()
        {
            Debug( "Initializing PeopleManager");
            _peopleManager = PeopleManager.Instance;
            _peopleManager.RefreshPersonnelFolder();

            foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.personnelFolders)
            {
                InitializeCrewHMM(kvp.Value);
            }
        }

        /// <summary>
        /// A cache into PersonnelFile so it can be serialized.
        /// </summary>
        /// <param name="kerbalName"></param>
        /// <param name="newStateIdentity"></param>
        private void UpdateProductiveStateOf(string kerbalName, string newStateIdentity)
        {
            // bother if only a productivity state
            if (newStateIdentity.StartsWith("kerbal_") == true)
            {
                PersonnelFile pf = _peopleManager.GetFile(kerbalName);
                pf.UpdateProductiveState(newStateIdentity);
            }
        }

        /// <summary>
        /// Rather specific method to get productivity state of a specific kerbal crew member.
        /// </summary>
        /// <param name="kerbalFile">kerbal crew member</param>
        /// <returns></returns>
        private string KerbalStateOf(PersonnelFile kerbalFile)
        {
            foreach (KeyValuePair<string, HiddenState> kvp in _liveProcesses)
            {
                if (kvp.Value.kerbalName == kerbalFile.UniqueName() &&
                    kvp.Value.TemplateStateName().StartsWith("kerbal_"))
                {
                    return kvp.Key;
                }
            }

            // This should never happen
            Debug( $"Failed attempt to fetch productive state of {kerbalFile.DisplayName()}.");
            return "";
        }

        public void RetrainKerbal(PersonnelFile crewmember, string newRole)
        {
            PrintScreen( $"Retraining {crewmember.DisplayName()} from {crewmember.Specialty()} to {newRole}");
            KerbalRoster.SetExperienceTrait(crewmember.GetKSPData(), newRole);

            // Delete role HMM
            RemoveHMM(GetRoleHMM(crewmember).RegisteredName());

            // Create role HMM
            InitializeHMM("role_"+crewmember.Specialty(), kerbalName:crewmember.UniqueName());
        }

        /// <summary>
        /// Kerbal staff will alter the state of the KSC or have an effect on a media_blitz.
        /// </summary>
        /// <param name="kerbalFile">the actor</param>
        /// <param name="emitData">the event</param>
        /// <param name="legacyMode">legacy impact possible</param>
        /// <param name="isMedia">Indicate a media task for non-pilot</param>
        public void KerbalImpact(PersonnelFile kerbalFile, Emissions emitData, bool legacyMode = false,
            bool isMedia = false)
        {
            // Check for valid activities
            List<string> validTask = new List<string>() {"media_blitz", "accelerate_research", "accelerate_assembly"};
            if (validTask.Contains(kerbalFile.kerbalTask) == false)
            {
                return;
            }

            int skillLevel = kerbalFile.Effectiveness(isMedia);
            int difficulty = GetProgramComplexity();
            if (_programManager.ControlLevel() == ProgramControlLevel.HIGH)
            {
                difficulty -= 1;
            }
            else if (_programManager.ControlLevel() == ProgramControlLevel.CHAOS)
            {
                difficulty += 1;
            }
            
            SkillCheckOutcome successLevel = SkillCheck(skillLevel, difficulty);
            HeadlinesUtil.Report(1, $"Skill check (impact)-{skillLevel} diff:{difficulty}, outcome: {successLevel}","SKILLCHECK");

            // In case of inquiry, immediate impact vanishes.
            if (ongoingInquiry &
                (successLevel == SkillCheckOutcome.SUCCESS | successLevel == SkillCheckOutcome.CRITICAL))
            {
                successLevel = SkillCheckOutcome.FAILURE;
            }

            ImpactType impactType = ImpactType.TRANSIENT;
            switch (successLevel)
            {
                case SkillCheckOutcome.FAILURE:
                    impactType = ImpactType.PASSIVE;
                    break;
                case SkillCheckOutcome.FUMBLE:
                    impactType = ImpactType.NEGATIVE;
                    break;
                case SkillCheckOutcome.CRITICAL:
                    KerbalRegisterSuccess(kerbalFile, true);
                    impactType = ImpactType.LASTING;
                    break;
                default:
                    KerbalRegisterSuccess(kerbalFile);
                    break;
            }

            switch (kerbalFile.Specialty())
            {
                case "Pilot":
                    PilotImpact(impactType, kerbalFile);
                    break;
                case "Engineer":
                    EngineerImpact(impactType, kerbalFile, legacyMode);
                    break;
                case "Scientist":
                    ScientistImpact(impactType, kerbalFile, legacyMode);
                    break;
            }

        }

        /// <summary>
        /// A kerbal is in the spotlight and is either playing up the program, or down by accident.
        /// </summary>
        /// <param name="impactType">Level of success</param>
        /// <param name="kerbalFile">The actor in this emission</param>
        public void PilotImpact(ImpactType impactType, PersonnelFile kerbalFile)
        {
            float multiplier = 1;
            switch (impactType)
            {
                case ImpactType.NEGATIVE:
                    multiplier *= -1f;
                    break;
                case ImpactType.LASTING:
                    multiplier *= 2f;
                    break;
                case ImpactType.PASSIVE:
                    multiplier *= 0.5f;
                    break;
            }

            string adjective = " effective ";
            int effectiveness = kerbalFile.Effectiveness();
            float deltaHype = effectiveness * multiplier;
            if (deltaHype < 0f)
            {
                adjective = " poorly representing the program ";
            }
            else if (deltaHype < 1f)
            {
                adjective = ", however, ineffectively ";
                deltaHype = 1f;
            }

            Emissions em = new Emissions("media_blitz");
            NewsStory ns = new NewsStory(em);
            ns.headline = "Media appearance";
            ns.SpecifyMainActor(kerbalFile.DisplayName(), em);
            ns.AddToStory(em.GenerateStory());
            ns.AddToStory($"They are{adjective}in the public eye. Hype gain is {deltaHype}.");
            FileHeadline(ns);
            kerbalFile.AddLifetimeHype((int)deltaHype);
            AdjustHype(deltaHype);
        }

        /// <summary>
        /// A scientist leads a research and development team to affect research rates.
        /// </summary>
        /// <param name="impactType">Skill check outcome</param>
        /// <param name="kerbalFile">The actor for this scence</param>
        /// <param name="legacyMode">Whether this is a legacy event or not</param>
        public void ScientistImpact(ImpactType impactType, PersonnelFile kerbalFile, bool legacyMode = false)
        {
            if (legacyMode == true && impactType == ImpactType.NEGATIVE) return;
            if (impactType == ImpactType.PASSIVE) return;

            // Define the magnitude of the change.
            int pointsRandD = GetRnDPoints();

            // 2% of R&D or 1 point
            int deltaRandD = (int) Math.Ceiling((double) pointsRandD * 0.02);
            if (deltaRandD == 0) return;

            int notification_level = 2;

            string message = "";
            if (legacyMode == true)
            {
                switch (impactType)
                {
                    case ImpactType.TRANSIENT:
                        kerbalFile.teamInfluence += deltaRandD;
                        message = $"{kerbalFile.DisplayName()}'s team is taking it to the next level.";
                        break;
                    case ImpactType.LASTING:
                        kerbalFile.legacy += deltaRandD;
                        message = $"{kerbalFile.DisplayName()} legacy is growing.";
                        notification_level = 3;
                        break;
                }
            }
            else
            {
                switch (impactType)
                {
                    case ImpactType.NEGATIVE:
                        deltaRandD *= -1;
                        message = $"{kerbalFile.DisplayName()} sows confusion in the R&D complex.";
                        kerbalFile.influence += deltaRandD;
                        break;
                    case ImpactType.LASTING:
                        kerbalFile.teamInfluence += deltaRandD;
                        message = $"{kerbalFile.DisplayName()}'s team is taking it to the next level.";
                        break;
                    case ImpactType.TRANSIENT:
                        kerbalFile.influence += deltaRandD;
                        message = $"{kerbalFile.DisplayName()} earns their pay at the R&D complex.";
                        notification_level--;
                        break;
                }
            }

            AdjustRnD(deltaRandD);
            
            FileHeadline(new NewsStory((HeadlineScope)notification_level, "R&D Productivity report",$"{message} ({deltaRandD})") );
            
        }

        /// <summary>
        /// An engineer does wonders in the VAB with vehicle assembly
        /// </summary>
        /// <param name="impactType">Skill check outcome</param>
        /// <param name="kerbalFile">The actor</param>
        /// <param name="legacyMode">Whether this change is lasting or not</param>
        public void EngineerImpact(ImpactType impactType, PersonnelFile kerbalFile, bool legacyMode = false)
        {
            if (legacyMode == true && impactType == ImpactType.NEGATIVE) return;
            if (impactType == ImpactType.PASSIVE) return;

            // Define the magnitude of the change.
            // TODO Get from RP1 the total number of points in R&D
            int pointsVAB = GetVABPoints();

            // 2% of R&D or 1 point
            int deltaVAB = (int) Math.Ceiling((double) pointsVAB * 0.02);
            if (deltaVAB == 0) return;
            
            int notification_level = 2;

            string message = "";
            if (legacyMode == true)
            {
                switch (impactType)
                {
                    case ImpactType.TRANSIENT:
                        kerbalFile.teamInfluence += deltaVAB;
                        message = $"{kerbalFile.DisplayName()}'s team is taking it to the next level.";
                        break;
                    case ImpactType.LASTING:
                        kerbalFile.legacy += deltaVAB;
                        message = $"{kerbalFile.DisplayName()} legacy is growing.";
                        notification_level++;
                        break;
                }
            }
            else
            {
                switch (impactType)
                {
                    case ImpactType.NEGATIVE:
                        deltaVAB *= -1;
                        message = $"{kerbalFile.DisplayName()} sows confusion in the VAB.";
                        kerbalFile.influence += deltaVAB;
                        break;
                    case ImpactType.LASTING:
                        kerbalFile.teamInfluence += deltaVAB;
                        message = $"{kerbalFile.DisplayName()}'s team is taking it to the next level.";
                        break;
                    case ImpactType.TRANSIENT:
                        kerbalFile.influence += deltaVAB;
                        message = $"{kerbalFile.DisplayName()} earns their pay at the VAB.";
                        notification_level--;
                        break;
                }
            }

            AdjustVAB(deltaVAB);
            
            FileHeadline(new NewsStory((HeadlineScope)notification_level,"VAB productivity report", $"{message} ({deltaVAB})"));
        }

        /// <summary>
        /// At this time, does not much more than to change the personelFile. This is redundant with the more general
        /// task updating code in EmitEvent, but may differ at a later time.
        /// </summary>
        /// <param name="personnelFile">The Actor</param>
        /// <param name="emitData">The event</param>
        public void KerbalAccelerate(PersonnelFile personnelFile, Emissions emitData)
        {
            personnelFile.TrackCurrentActivity(emitData.nodeName);
        }

        /// <summary>
        /// Stub for the kerbal's AI to enter a charm campaign. Currently, does nothing special.
        /// </summary>
        /// <param name="personnelFile">The actor</param>
        /// <param name="emitData">The event</param>
        public void KerbalMediaBlitz(PersonnelFile personnelFile, Emissions emitData)
        {
            personnelFile.TrackCurrentActivity(emitData.nodeName);
        }

        /// <summary>
        /// Kerbal taking time to get professional development. It caps skill improvement to an
        /// effectiveness of 5, which is plenty. 
        /// </summary>
        /// <param name="personnelFile">the actor</param>
        /// <param name="emitData">the event</param>
        public void KerbalStudyLeave(PersonnelFile personnelFile, Emissions emitData)
        {
            CancelInfluence(personnelFile);

            int difficulty = personnelFile.Effectiveness();
            if (personnelFile.coercedTask) difficulty += 2;
            
            NewsStory ns = new NewsStory(emitData,$"Study leave: {personnelFile.UniqueName()}", false);
            ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
            ns.AddToStory(emitData.GenerateStory());
            
            // A leave is always good for the soul.
            personnelFile.AdjustDiscontent(-1);

            SkillCheckOutcome outcome = SkillCheck(5, difficulty);

            if (outcome == SkillCheckOutcome.SUCCESS)
            {
                personnelFile.trainingLevel += 1;
                KerbalRegisterSuccess(personnelFile);
                ns.AddToStory($" {personnelFile.DisplayName()} matures.");
            }
            else if (outcome == SkillCheckOutcome.CRITICAL)
            {
                personnelFile.trainingLevel += 2;
                KerbalRegisterSuccess(personnelFile, true);
                ns.AddToStory($" {personnelFile.DisplayName()} has a breakthrough.");
            }
            else if (outcome == SkillCheckOutcome.FUMBLE)
            {
                personnelFile.trainingLevel -= 1;
                personnelFile.AdjustDiscontent(1);
                ns.AddToStory($" {personnelFile.DisplayName()} goes down a misguided rabbit hole during their study leave.");
            }
            else if (outcome == SkillCheckOutcome.FAILURE)
            {
                ns.AddToStory($" The only thing to show for this leave will be a postcard or some beach glass.");
            }
            
            // New passion
            PartCategories oldPassion = personnelFile.passion;
            personnelFile.RandomizePassion();
            if (personnelFile.passion != oldPassion && personnelFile.Specialty() == "Scientist")
            {
                ns.AddToStory($"They have developed and affinity to research in the field of {personnelFile.passion.ToString()}.");
            }
            
            FileHeadline(ns);
        }

        /// <summary>
        /// Determines whether a Kerbal decides to walk away.
        /// </summary>
        /// <param name="personnelFile"></param>
        /// <param name="emitData"></param>
        public void KerbalConsiderResignation(PersonnelFile personnelFile, Emissions emitData)
        {
            // Assert resignation
            double effectiveness = Math.Floor((double) personnelFile.Effectiveness());
            double peer_effectiveness = Math.Floor(_peopleManager.ProgramAverageEffectiveness());
            int netDiscontent = (int) effectiveness - (int) peer_effectiveness + personnelFile.GetDiscontent();

            SkillCheckOutcome outcome = SkillCheck(netDiscontent);
            switch (outcome)
            {
                case SkillCheckOutcome.FUMBLE:
                    personnelFile.AdjustDiscontent(-1 * personnelFile.GetDiscontent());
                    break;
                case SkillCheckOutcome.FAILURE:
                    personnelFile.AdjustDiscontent(1);
                    break;
                default:
                    KerbalResignation(personnelFile, emitData);
                    return;
            }
        }

        /// <summary>
        /// Two kerbals are discovering new ways to work together than enhances their productivity.
        /// </summary>
        /// <param name="personnelFile">subject triggering the event</param>
        /// <param name="emitData"></param>
        public void KerbalSynergy(PersonnelFile personnelFile, Emissions emitData)
        {
            NewsStory ns = new NewsStory(emitData, "New collaboration");
            ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
            
            PersonnelFile collaborator = _peopleManager.GetRandomKerbal(personnelFile);
            if (collaborator == null)
            {
                return;
            }
            ns.SpecifyOtherCrew(collaborator.DisplayName(), emitData);
            
            if (personnelFile.IsFeuding(collaborator.UniqueName()))
            {
                if (personnelFile.UnsetFeuding(collaborator))
                {
                    collaborator.UnsetFeuding(personnelFile);
                    ns.AddToStory($"{personnelFile.DisplayName()} and {collaborator.DisplayName()} have found a way to make peace, somehow.");
                    ns.headline = "Reconciliation";
                }
            }
            else if (personnelFile.IsCollaborator(collaborator.UniqueName()) == false)
            {
                if (personnelFile.SetCollaborator(collaborator))
                {
                    collaborator.SetCollaborator(personnelFile);
                    ns.AddToStory( emitData.GenerateStory());
                }
            }
            if (ns.story != "") FileHeadline(ns);
        }

        public void KerbalFeud(PersonnelFile personnelFile, Emissions emitData)
        {
            // todo continue here and down
            PersonnelFile candidate = _peopleManager.GetRandomKerbal(personnelFile);
            if (candidate == null) return;

            if (personnelFile.SetFeuding(candidate))
            {
                candidate.SetFeuding(personnelFile);

                NewsStory ns = new NewsStory(emitData, "Feud at KSC");
                ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
                ns.SpecifyOtherCrew(candidate.DisplayName(), emitData);
                ns.AddToStory(emitData.GenerateStory());
                FileHeadline(ns);
            }
        }

        public void KerbalReconcile(PersonnelFile personnelFile, Emissions emitData)
        {
            PersonnelFile candidate = _peopleManager.GetRandomKerbal(personnelFile, personnelFile.feuds);
            if (candidate == null) return;

            // Scrapper are less likely to reconcile
            if (personnelFile.HasAttribute("scrapper"))
            {
                if (storytellerRand.NextDouble() < 0.66) return;
            }
            
            if (personnelFile.UnsetFeuding(candidate))
            {
                candidate.UnsetFeuding(personnelFile);
                
                NewsStory ns = new NewsStory(emitData, "Reconciliation");
                ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
                ns.SpecifyOtherCrew(candidate.DisplayName(), emitData);
                ns.AddToStory(emitData.GenerateStory());
                FileHeadline(ns);
            }
        }

        /// <summary>
        /// Execute a resignation from the space program
        /// </summary>
        /// <param name="personnelFile">resiging kerbal</param>
        /// <param name="emitData"></param>
        public void KerbalResignation(PersonnelFile personnelFile, Emissions emitData, bool trajedy = false)
        {
            Debug($"Kerbal resignation for {personnelFile.DisplayName()}");
            HeadlinesUtil.Report(2, $"BREAKING: {personnelFile.DisplayName()} resigns!");
            TimeWarp.SetRate(0,false);
            
            // Message
            if (!trajedy)
            {
                NewsStory ns = new NewsStory(emitData);
                ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
                ns.headline = "Resignation";
                ns.AddToStory(emitData.GenerateStory());
                FileHeadline(ns);
            }
            else
            {
                emitData = new Emissions("dumb_accident");
                NewsStory ns = new NewsStory(emitData);
                ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
                ns.headline = "Trajedy";
                ns.AddToStory(emitData.GenerateStory());
                FileHeadline(ns);
            }
            
            // Remove influence
            CancelInfluence(personnelFile, leaveKSC: true);

            // HMMs
            RemoveKerbalHMM(personnelFile.UniqueName());

            // Reputation
            _reputationManager.AdjustCredibility(-1 * personnelFile.Effectiveness(deterministic:true));
            
            // Make it happen
            _peopleManager.RemoveKerbal(personnelFile);
        }

        public void KerbalSacked(PersonnelFile personnelFile)
        {
            // Remove influence
            CancelInfluence(personnelFile, leaveKSC: true);

            // HMMs
            RemoveKerbalHMM(personnelFile.UniqueName());
            
            // reputation
            _reputationManager.AdjustCredibility(-1*personnelFile.Effectiveness());

            // Make it happen
            _peopleManager.ReturnToApplicantPool(personnelFile);
        }

        /// <summary>
        /// Helps a peer to gain training with a probability related to the difference in training level. There is a
        /// very low probability that a less trained peer can somehow make a difference.
        /// </summary>
        /// <param name="personnelFile">the actor</param>
        /// <param name="emitData">the event</param>
        public void KerbalMentorPeer(PersonnelFile personnelFile, Emissions emitData)
        {
            NewsStory ns = new NewsStory(emitData, Headline:"Mentorship");
            ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
            
            List<string> excludeList = new List<string>() {personnelFile.UniqueName()};
            foreach (string feudingbuddy in personnelFile.feuds)
            {
                excludeList.Add(feudingbuddy);
            }

            PersonnelFile peer = _peopleManager.GetRandomKerbal(excludeList);

            if (peer != null)
            {
                ns.SpecifyOtherCrew(peer.DisplayName(), emitData);
                ns.AddToStory(emitData.GenerateStory());
                
                int deltaSkill = personnelFile.trainingLevel - peer.trainingLevel;
                if (personnelFile.HasAttribute("inspiring")) deltaSkill++;
                
                string message = "";
                if (deltaSkill < 0) message = "Although unlikely, ";

                SkillCheckOutcome outcome = SkillCheck((int) Math.Max(1, deltaSkill));

                switch (outcome)
                {
                    case SkillCheckOutcome.FUMBLE:
                        message +=
                            $"{personnelFile.DisplayName()} introduces superstitious ideas taken up by {peer.DisplayName()}.";
                        peer.trainingLevel -= 1;
                        break;
                    case SkillCheckOutcome.SUCCESS:
                        message += $"{personnelFile.DisplayName()} mentors {peer.DisplayName()}.";
                        KerbalRegisterSuccess(personnelFile);
                        peer.trainingLevel += 1;
                        break;
                    case SkillCheckOutcome.CRITICAL:
                        message +=
                            $"{personnelFile.DisplayName()} and {peer.DisplayName()} mutually benefits from each other's company.";
                        KerbalRegisterSuccess(personnelFile, true);
                        peer.trainingLevel += 1;
                        personnelFile.trainingLevel += 1;
                        break;
                    default:
                        return;
                }

                ns.AddToStory(message);
                FileHeadline(ns);
            }
        }

        /// <summary>
        /// Keep this one simple for now and treat the donation as a KCT point. It could be a career-saving move just as well.
        /// </summary>
        /// <remarks>
        /// This should be handled proportionally to where a career is as 1 point in mid/late career is pretty meaningless and the money
        /// should be stashed for KSC upgrades.
        /// </remarks>
        /// <param name="personnelFile">the fundraiser</param>
        /// <param name="emitData">the event</param>
        public void KerbalFundRaise(PersonnelFile personnelFile, Emissions emitData)
        {
            NewsStory ns;
            if (fundraisingBlackout)
            {
                ns = new NewsStory(HeadlineScope.NEWSLETTER, "Fundraising possible again",
                    "Relationship mended with potential capital campaign donors. Fundraising is possible again.");
                FileHeadline(ns);
                fundraisingBlackout = false;
                return;
            }

            ns = new NewsStory(emitData);
            ns.headline = "Capital funding raised";
            ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);

            SkillCheckOutcome outcome = SkillCheck(personnelFile.Effectiveness());

            double funds = 0;

            switch (outcome)
            {
                case SkillCheckOutcome.SUCCESS:
                    KerbalRegisterSuccess(personnelFile);
                    funds = 20000;
                    break;
                case SkillCheckOutcome.CRITICAL:
                    KerbalRegisterSuccess(personnelFile, true);
                    funds = 100000;
                    break;
                case SkillCheckOutcome.FUMBLE:
                    fundraisingBlackout = true;
                    ns.AddToStory("Unfortunately, commits a blunder and forces the program into damage control.");
                    break;
            }

            if (funds > 0)
            {
                ns.AddToStory($"{personnelFile.DisplayName()} finalizes a gift agreement for ${(int) (funds / 1000)}K.");
                FileHeadline(ns);
                
                Funding.Instance.AddFunds(funds, TransactionReasons.Any);
                this.fundraisingTally += funds;
                personnelFile.fundRaised += (int)funds;
            }

        }

        /// <summary>
        /// Stupidity kills the cat. 
        /// </summary>
        /// <param name="personnelFile">the actor</param>
        /// <param name="emitData"></param>
        public void KerbalAccident(PersonnelFile personnelFile, Emissions emitData)
        {
            double stupidity = personnelFile.Stupidity() * 10;
            stupidity = Math.Min(4, stupidity);

            NewsStory ns = new NewsStory(emitData, Headline:$"{personnelFile.DisplayName()} injured");
            ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
            
            SkillCheckOutcome outcome = SkillCheck((int) stupidity);
            switch (outcome)
            {
                case SkillCheckOutcome.CRITICAL:
                    KerbalResignation(personnelFile, emitData, trajedy: true);
                    break;
                case SkillCheckOutcome.SUCCESS:
                    ns.AddToStory(emitData.GenerateStory());
                    FileHeadline(ns);
                    TransitionHMM(KerbalStateOf(personnelFile), "kerbal_injured");
                    CancelInfluence(personnelFile);
                    break;
            }
        }

        /// <summary>
        /// Add one new kerbal to the talent pool with a better Profile that expected.
        /// </summary>
        /// <param name="personnelFile"></param>
        /// <param name="emitData"></param>
        public void KerbalScoutTalent(PersonnelFile personnelFile, Emissions emitData)
        {
            programPayrollRebate += 1;
            personnelFile.numberScout += 1;
            personnelFile.fundRaised += 40000;
            
            PersonnelFile newApplicant = _peopleManager.GenerateRandomApplicant(GetValuationLevel() + 2);

            NewsStory ns = new NewsStory(emitData);
            ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
            emitData.AddStoryElement("recruit_name", newApplicant.DisplayName());
            ns.AddToStory(emitData.GenerateStory());
            ns.AddToStory($"{newApplicant.DisplayName()} is a  {_peopleManager.QualitativeEffectiveness(newApplicant.Effectiveness())} {newApplicant.Specialty()}.");
            if (newApplicant.personality != "")
            {
                ns.AddToStory($"They are reported to be somewhat {newApplicant.personality}.");
            }
            FileHeadline(ns);
            
            if (_peopleManager.ShouldEndWarp(newApplicant))
            {
                HeadlinesUtil.Report(2, $"{personnelFile.DisplayName()} scouted a new applicant to review: {newApplicant.DisplayName()}");
                TimeWarp.SetRate(0,false);
            }
        }

        /// <summary>
        /// A visiting scholar allows to do more with the science units that are in the bank. 
        /// </summary>
        /// <param name="personnelFile"></param>
        /// <param name="emitData"></param>
        public void KerbalBringScholar(PersonnelFile personnelFile, Emissions emitData)
        {
            // New visiting scholar for 3-6 months
            double expiryDate = HeadlinesUtil.GetUT() + storytellerRand.Next(3, 7) * (3600 * 24 * 30);
            visitingScholarEndTimes.Add(expiryDate);
            
            this.visitingScholar = true;
            ProtoCrewMember.Gender gender = ProtoCrewMember.Gender.Female;
            if (storytellerRand.NextDouble() < 0.5) gender = ProtoCrewMember.Gender.Male;
            
            //visitingScholarName = CrewGenerator.GetRandomName(gender);
            string cultureName = "";
            KerbalRenamer.RandomName(gender, ref cultureName, ref visitingScholarName);

            if (KACWrapper.APIReady)
            {
                KACWrapper.KAC.CreateAlarm(KACWrapper.KACAPI.AlarmTypeEnum.ScienceLab, $"{visitingScholarName} leaves.", expiryDate);
            }

            NewsStory ns = new NewsStory(emitData);
            ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
            emitData.AddStoryElement("visiting_name", visitingScholarName);
            ns.headline = "New visiting scientist";
            ns.AddToStory(emitData.GenerateStory());
            ns.AddToStory($" {visitingScholarName} ({cultureName}) is expected to be in-residence until {KSPUtil.PrintDate(expiryDate,false, false)}.");
            FileHeadline(ns);
        }

        /// <summary>
        /// Called buy the UI to force a task change.
        /// </summary>
        /// <param name="personnelFile"></param>
        /// <param name="newTask"></param>
        public void KerbalOrderTask(PersonnelFile personnelFile, string newTask)
        {
            CancelInfluence(personnelFile);
            personnelFile.OrderTask(newTask);
        }
        /// <summary>
        /// Occurs after a player tells a kerbal what to do. Determines whether the kerbal loses the coercedFlag and whether
        /// it makes them unhappy.
        /// </summary>
        /// <param name="personnelFile">the actor</param>
        public void KerbalCoercedTask(PersonnelFile personnelFile)
        {
            double stubbornFactor = personnelFile.HasAttribute("stubborn") ? 1.5 : 1;
            
            string message = $"{personnelFile.DisplayName()} is told to {personnelFile.kerbalTask} ";

            bool justDoingMyJob = false;
            if (personnelFile.Specialty() == "Pilot" && 
                _reputationManager.currentMode == MediaRelationMode.CAMPAIGN &&
                _reputationManager.CampaignHype() < _reputationManager.GetMediaEventWager() &&
                personnelFile.personality != "stubborn")
            {
                message += "and will keep on going ";
                justDoingMyJob = true;
            }
            else if (storytellerRand.NextDouble() < 0.5 * stubbornFactor)
            {
                message += "one last time ";
                personnelFile.coercedTask = false;
            }

            if (!justDoingMyJob && storytellerRand.NextDouble() < 0.20 * stubbornFactor)
            {
                message += "and isn't happy";
                personnelFile.AdjustDiscontent(1);
            }

            NewsStory ns = new NewsStory(HeadlineScope.SCREEN, Story: message + ".");
            FileHeadline(ns);

            if (personnelFile.kerbalTask.StartsWith("accelerate_") || personnelFile.kerbalTask.EndsWith("_blitz"))
            {
                EmitEvent("impact", personnelFile);
            }
            else
            {
                EmitEvent(personnelFile.kerbalTask, personnelFile);
            }
        }

        /// <summary>
        /// Workplace satisfaction when successful (with a probability)
        /// </summary>
        /// <param name="personnelFile">the actor</param>
        /// <param name="critical">force adjustment</param>
        public void KerbalRegisterSuccess(PersonnelFile personnelFile, bool critical = false)
        {
            if (critical | storytellerRand.NextDouble() < 0.15)
            {
                personnelFile.AdjustDiscontent(-1);
            }
        }

        public void KerbalAppointProgramManager(PersonnelFile newManager)
        {
            _programManager.PerformIntegrityCheckonRecord();
            
            string initialName = _programManager.ManagerName();
            if (newManager != null)
            {
                _programManager.AssignProgramManager(newManager, _reputationManager.CurrentReputation());
                
                // Set manager as inactive until next program check
                newManager.SetInactive(_hmmScheduler["program_manager"]);
            }
            else
            {
                // revert to default PM
                _programManager.RevertToDefaultProgramManager();
            }
            string finalName = _programManager.ManagerName();
            NewsStory ns = new NewsStory(HeadlineScope.FRONTPAGE, $"{finalName} as Program Manager");
            ns.AddToStory($"{finalName} replaces {initialName} as program manager.");
            if (_programManager.ManagerBackground() != "Neutral")
            {
                ns.AddToStory($" The news comes as a delight to {_programManager.ManagerBackground()}s.");
            }
            FileHeadline(ns);
            
            _peopleManager.MarkEffectivenessCacheDirty();
            
        }
        #endregion

        #region HMM Logic

        /// <summary>
        /// Unfortunate method to catch cases where the role was misread because RP1 changd it after initializing this object.
        /// </summary>
        public void AssertRoleHMM()
        {
            foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.personnelFolders)
            {
                HiddenState roleHMM = GetRoleHMM(kvp.Value);
                if (roleHMM.templateStateName.Contains(kvp.Value.Specialty()) == false)
                {
                    RemoveHMM(roleHMM.RegisteredName());
                    InitializeHMM("role_"+kvp.Value.Specialty(),kerbalName:kvp.Value.UniqueName());
                }
            }  
        }
        
        /// <summary>
        /// Create a HMM and place it to be triggered at a later time.
        /// </summary>
        /// <param name="registeredStateIdentity">The identifier as registered in the scheduler</param>
        private void InitializeHMM(string registeredStateIdentity, double timestamp = 0, string kerbalName = "")
        {
            string templateStateIdentity = registeredStateIdentity;

            // Split template and kerbal parts when initialized from a save node
            int splitter = registeredStateIdentity.IndexOf("@");
            if (splitter != -1)
            {
                templateStateIdentity = registeredStateIdentity.Substring(splitter + 1);
                kerbalName = registeredStateIdentity.Substring(0, splitter);
            }

            HiddenState newState = new HiddenState(templateStateIdentity, kerbalName);

            // Odd case where people manager isn't loaded yet
            //ApplyPersonality(newState);
            
            // Avoid duplications
            if (_liveProcesses.ContainsKey(newState.RegisteredName()) == false)
            {
                _liveProcesses.Add(newState.RegisteredName(), newState);
            }

            timestamp = timestamp != 0 ? timestamp : HeadlinesUtil.GetUT() + GeneratePeriod(newState.period, newState.RegisteredName().Contains("_decay"));

            if (_hmmScheduler.ContainsKey(newState.RegisteredName()) == false)
            {
                _hmmScheduler.Add(newState.RegisteredName(), timestamp);
            }
            else
            {
                _hmmScheduler[newState.RegisteredName()] = timestamp;
            }

        }

        public void ApplyCrewPersonality(HiddenState newState)
        {
            // People manager may not be loaded
            if (_peopleManager.applicantFolders.Count + _peopleManager.personnelFolders.Count == 0) return;
            
            // Personality
            if (newState.kerbalName != "")
            {
                PersonnelFile pf = _peopleManager.GetFile(newState.kerbalName);

                // This crew member is a social butterfly
                if (pf.HasAttribute("genial"))
                {
                    newState.AdjustEmission("synergy", 1.5f);
                    newState.AdjustEmission("reconcile", 1.5f);
                    newState.AdjustEmission("feud", 0.5f);
                }

                // This one is not
                if (pf.HasAttribute("scrapper"))
                {
                    newState.AdjustEmission("reconcile", 0.5f);
                    newState.AdjustEmission("feud", 1.5f);
                }
                
                // Passion
                if (GetPartCategoriesUnderResearch().Contains(pf.passion))
                {
                    newState.AdjustTransition("kerbal_inspired", 2);
                }
            }
        }
        
        /// <summary>
        /// Launch both role and productivity HMM for a kerbal, if they don't exist.
        /// </summary>
        /// <param name="personnelFile"></param>
        public void InitializeCrewHMM(PersonnelFile personnelFile)
        {
            string registeredStateName = personnelFile.UniqueName() + "@role_" + personnelFile.Specialty();
            if (_liveProcesses.ContainsKey(registeredStateName) == false)
            {
                InitializeHMM("role_" + personnelFile.Specialty(), kerbalName:personnelFile.UniqueName());
            }
            registeredStateName = personnelFile.UniqueName() + "@kerbal_" + personnelFile.kerbalProductiveState;
            if (_liveProcesses.ContainsKey(registeredStateName) == false)
            {
                InitializeHMM("kerbal_"+personnelFile.kerbalProductiveState, kerbalName:personnelFile.UniqueName());
            }
        }

        /// <summary>
        /// Makes sure that the emission probabilities of a HMM reflect the current cirsumstances.
        /// </summary>
        /// <param name="hmm"></param>
        public void ContextualizeHMM(HiddenState hmm)
        {
            // Clean slate
            hmm.LoadTemplate();
            
            // Program Manager
            _programManager.ModifyEmissionProgramManager(hmm);
            
            // Media mode
            _programManager.ModifyEmissionMediaMode(hmm, _reputationManager.currentMode);
            
            // Control status
            _programManager.ModifyEmissionControl(hmm);
            
            // Priorities
            _programManager.ModifyEmissionPriority(hmm);
            
            
            // Personality
            ApplyCrewPersonality(hmm);
        }

        /// <summary>
        /// Safely disable a HMM.
        /// </summary>
        /// <param name="registeredStateIdentity">The identifier for this HMM</param>
        private void RemoveHMM(string registeredStateIdentity)
        {
            if (_hmmScheduler.ContainsKey(registeredStateIdentity)) _hmmScheduler.Remove(registeredStateIdentity);
            if (_liveProcesses.ContainsKey(registeredStateIdentity)) _liveProcesses.Remove(registeredStateIdentity);
        }
        
        
        /// <summary>
        /// Delete all HMM associated to a certain name
        /// </summary>
        /// <param name="kerbalName">a unique name</param>
        private void RemoveKerbalHMM(string kerbalName)
        {
            List<string> choppingBlock = new List<string>();
            foreach (KeyValuePair<string, HiddenState> kvp in _liveProcesses)
            {
                if (kvp.Value.kerbalName == kerbalName)
                {
                    choppingBlock.Add(kvp.Key);
                }
            }

            foreach (string registeredName in choppingBlock)
            {
                _liveProcesses.Remove(registeredName);
                if (_hmmScheduler.ContainsKey(registeredName))
                {
                    _hmmScheduler.Remove(registeredName);
                }
            }
        }

        /// <summary>
        /// Execute a HMM transition from one hidden state to another. Assumes that 
        /// </summary>
        /// <param name="registeredInitialState">Identifier of the existing state to be discarded as registered in _liveProcess</param>
        /// <param name="templateFinalState">Identifier of the state to initialize without the kerbal name if applicable</param>
        private void TransitionHMM(string registeredInitialState, string templateFinalState)
        {
            string fetchedKerbalName = _liveProcesses[registeredInitialState].kerbalName;

            InitializeHMM(templateFinalState, kerbalName: fetchedKerbalName);
            RemoveHMM(registeredInitialState);

            if (fetchedKerbalName != "")
            {
                UpdateProductiveStateOf(fetchedKerbalName, templateFinalState);
            }
        }

        public HiddenState GetRoleHMM(PersonnelFile crewmember)
        {
            foreach (KeyValuePair<string, HiddenState> kvp in _liveProcesses)
            {
                if (kvp.Value.kerbalName == crewmember.UniqueName())
                {
                    if (kvp.Value.templateStateName.StartsWith("role_"))
                    {
                        return kvp.Value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Generate a nearly normal random number of days for a HMM triggering event. Box Muller transform
        /// taken from https://stackoverflow.com/questions/218060/random-gaussian-variables
        /// Guarantees that the period is at least 1/20 of the baseValue.
        /// Assumes a standard deviation of 1/3 of the mean.
        /// </summary>
        /// <param name="meanValue">The base value specified in the config node of a HMM</param>
        /// <returns></returns>
        private double GeneratePeriod(double meanValue, bool decay = false)
        {
            double modifier = 1;
            if (decay)
            {
                modifier = attentionSpanFactor;
            }
            double stdDev = meanValue / 3;
            double u1 = 1.0 - storytellerRand.NextDouble();
            double u2 = 1.0 - storytellerRand.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                                   Math.Sin(2.0 * Math.PI * u2);
            double returnedVal = meanValue + stdDev * randStdNormal;
            double floorVal = meanValue / 10;
            double outValue = Math.Max(returnedVal * modifier, floorVal);

            // Convert to seconds based on hardcoded period
            return outValue * _assumedPeriod;
        }

        /// <summary>
        /// Easy access to live processes by name
        /// </summary>
        /// <param name="processName">name as registered</param>
        /// <returns></returns>
        public HiddenState GetProcess(string processName)
        {
            if (_liveProcesses.ContainsKey(processName))
            {
                return _liveProcesses[processName];
            }

            return null;
        }

        #endregion

        #region Scheduling

        /// <summary>
        /// Update the cached value for nextUpdate by finding the smallest value in the scheduler.
        /// </summary>
        private void SchedulerCacheNextTime()
        {
            _nextUpdate = HeadlinesUtil.GetUT();

            List<string> missingCrew = new List<string>();

            double minVal = 60;
            foreach (KeyValuePair<string, double> kvp in _hmmScheduler)
            {
                if (minVal == 60) minVal = kvp.Value;
                else
                {
                    if (kvp.Value < _nextUpdate)
                    {
                        Debug($"HMM {kvp.Key} failed.", "HMM");
                        
                        // Should be deleted?
                        HiddenState hmm = _liveProcesses[kvp.Key];
                        if (hmm.kerbalName != "" && _peopleManager.GetFile(hmm.kerbalName) == null)
                        {
                            missingCrew.Add(hmm.kerbalName);
                        }
                        
                        ReScheduleHMM(kvp.Key, hmm.period);

                    }
                    if (kvp.Value < minVal) minVal = kvp.Value;
                }
            }

            foreach (string name in missingCrew)
            {
                RemoveKerbalHMM(name);
            }

            _nextUpdate = minVal;
        }

        /// <summary>
        /// Set/Reset the next trigger time for Registered state name. Assumes that it is in the system.
        /// </summary>
        /// <param name="registeredStateIdentity">State as registered</param>
        /// <param name="baseTime">Depending on the state itself.</param>
        public void ReScheduleHMM(string registeredStateIdentity, double baseTime)
        {
            double deltaTime = GeneratePeriod(_liveProcesses[registeredStateIdentity].period, registeredStateIdentity.Contains("_decay"));
            
            // pilots during a campaign are at 2X speed (This would be best encoded in the HMM period)
            if (_reputationManager.currentMode == MediaRelationMode.CAMPAIGN &&
                registeredStateIdentity.Contains("role_Pilot"))
            {
                deltaTime /= 2;
            }

            _hmmScheduler[registeredStateIdentity] = HeadlinesUtil.GetUT() + deltaTime;

            // Punt injury inactivation into the future
            if (registeredStateIdentity.Contains("kerbal_injured"))
            {
                _peopleManager.GetFile(_liveProcesses[registeredStateIdentity].kerbalName).SetInactive(deltaTime);
            }
            
            // Same for program managers
            if (registeredStateIdentity.Contains("program_manager"))
            {
                _programManager.InactivatePMasCrewFor(deltaTime);
            }
        }

        /// <summary>
        /// Scans the scheduler and fire everything site to trigger before the current time.
        /// </summary>
        /// <param name="currentTime">Passed on from GetUT() from a previous calls</param>
        private void SchedulerUpdate(double currentTime)
        {
            _peopleManager.MarkEffectivenessCacheDirty();
            
            Debug("Scheduler update");
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

            foreach (string registeredStateName in triggerStates)
            {
                Debug($"Triggering HMM {registeredStateName}");

                // Rogue HMM not deleted in some edge cases
                if (_liveProcesses[registeredStateName].kerbalName != "")
                {
                    if (_peopleManager.GetFile(_liveProcesses[registeredStateName].kerbalName) == null)
                    {
                        RemoveKerbalHMM(_liveProcesses[registeredStateName].kerbalName);
                        continue;
                    }
                }
                
                // Check for validity as it *may* have been removed recently
                if (_liveProcesses.ContainsKey(registeredStateName) == false)
                {
                    continue;
                }
                
                // Makes sure that Emissions are reflecting game state
                ContextualizeHMM(_liveProcesses[registeredStateName]);

                // HMM emission call
                emittedEvent = _liveProcesses[registeredStateName].Emission();
                Debug( $"HMM {registeredStateName} fires {emittedEvent}", "HMM");

                if (emittedEvent != "")
                {
                    if (_liveProcesses[registeredStateName].kerbalName != "")
                    {
                        PersonnelFile personnelFile =
                            _peopleManager.GetFile(_liveProcesses[registeredStateName].kerbalName);
                        if (personnelFile == null)
                        {
                            // We have a ghost (simulation artifact in an edge case)
                            RemoveKerbalHMM(_liveProcesses[registeredStateName].kerbalName);
                            continue;
                        }
                        if (personnelFile.IsInactive()) continue;
                        // Program managers don't generate speciality-specific events.
                        if (registeredStateName.Contains(personnelFile.Specialty()) &
                            personnelFile.UniqueName() == _programManager.ManagerName()) continue;

                        if (personnelFile.coercedTask)
                        {
                            EmitEvent(personnelFile.kerbalTask, personnelFile);
                            KerbalCoercedTask(personnelFile);
                        }
                        else
                        {
                            EmitEvent(emittedEvent, personnelFile);
                        }
                    }
                    else
                    {
                        EmitEvent(emittedEvent);
                    }
                }

                if (_liveProcesses[registeredStateName].kerbalName != "")
                {
                    PersonnelFile pf = _peopleManager.GetFile(_liveProcesses[registeredStateName].kerbalName);
                    if (GetPartCategoriesUnderResearch().Contains(pf.passion))
                    {
                        _liveProcesses[registeredStateName].AdjustTransition("kerbal_inspired", 2);
                    }
                }
                
                // HMM transition determination
                nextTransitionState = _liveProcesses[registeredStateName].Transition();

                if (nextTransitionState != _liveProcesses[registeredStateName].TemplateStateName())
                {
                    TransitionHMM(registeredStateName, nextTransitionState);
                }
                else
                {
                    ReScheduleHMM(registeredStateName, _liveProcesses[registeredStateName].period);
                }
            }
            
            // Remove HMM
            foreach (string registeredKey in _hmmToRemove)
            {
                RemoveHMM(registeredKey);
            }

            SchedulerCacheNextTime();
            CullNewsFeed();
        }


        #endregion

        #region Headlines Events

        /// <summary>
        /// Primary handler for the emission of events.
        /// </summary>
        /// <param name="eventName"></param>
        public void EmitEvent(string eventName)
        {
            //Emissions emitData = new Emissions(eventName);

            switch (eventName)
            {
                case "attention_span_long":
                    AdjustSpaceCraze(1);
                    break;
                case "attention_span_short":
                    AdjustSpaceCraze(-1);
                    break;
                case "hype_boost":
                    AdjustHype(5f);
                    break;
                case "hype_dampened":
                    AdjustHype(-5f);
                    break;
                case "decay_reputation":
                    DecayReputation();
                    break;
                case "reality_check":
                    RealityCheck(true);
                    break;
                case "new_applicant":
                    NewRandomApplicant();
                    break;
                case "withdraw_application":
                    WithdrawRandomApplication();
                    break;
                case "damning_report":
                    InquiryDamningReport();
                    break;
                case "spin_findings":
                    InquirySpinFindings();
                    break;
                case "conclude_inquiry":
                    InquiryConclude();
                    break;
                case "debris_damage":
                    DebrisDamage();
                    break;
                case "debris_spin":
                    DebrisSpin();
                    break;
                case "debris_conclude":
                    DebrisConclude();
                    break;
                case "program_check":
                    ProgramCheck();
                    break;
                default:
                    Debug( $"[Emission] {eventName} is not implemented yet.");
                    break;
            }
        }

        /// <summary>
        /// Emission handler for HMM with a kerbal associated.
        /// </summary>
        /// <param name="eventName">As defined in the config files</param>
        /// <param name="personnelFile">An instance of the personnel file for the correct kerbal</param>
        public void EmitEvent(string eventName, PersonnelFile personnelFile)
        {
            bool shouldCancelInfluence = true;
            
            Emissions emitData = new Emissions(eventName);

            if (emitData.IsOngoingTask() == true)
            {
                // Can only go forward if active
                if (personnelFile.IsInactive()) return;

                // Indicates a shift in focus over time
                personnelFile.TrackCurrentActivity(eventName);
            }

            switch (eventName)
            {
                case "impact":
                    shouldCancelInfluence = false;
                    KerbalImpact(personnelFile, emitData);
                    break;
                case "legacy_impact":
                    shouldCancelInfluence = false;
                    KerbalImpact(personnelFile, emitData, true);
                    break;
                case "accelerate_research":
                    shouldCancelInfluence = false;
                    KerbalAccelerate(personnelFile, emitData);
                    break;
                case "accelerate_assembly":
                    shouldCancelInfluence = false;
                    KerbalAccelerate(personnelFile, emitData);
                    break;
                case "media_blitz":
                    KerbalMediaBlitz(personnelFile, emitData);
                    break;
                case "study_leave":
                    KerbalStudyLeave(personnelFile, emitData);
                    break;
                case "media_training":
                    // Don't do anything special except ensures that only pilot do that.
                    if (personnelFile.Specialty() == "Pilot") KerbalStudyLeave(personnelFile, emitData);
                    break;
                case "quit":
                    KerbalConsiderResignation(personnelFile, emitData);
                    shouldCancelInfluence = false;
                    break;
                case "synergy":
                    shouldCancelInfluence = false;
                    KerbalSynergy(personnelFile, emitData);
                    break;
                case "feud":
                    shouldCancelInfluence = false;
                    KerbalFeud(personnelFile, emitData);
                    break;
                case "reconcile":
                    shouldCancelInfluence = false;
                    KerbalReconcile(personnelFile, emitData);
                    break;
                case "fundraise":
                    KerbalFundRaise(personnelFile, emitData);
                    break;
                case "scout_talent":
                    KerbalScoutTalent(personnelFile, emitData);
                    break;
                case "bring_visiting_scholar":
                    KerbalBringScholar(personnelFile, emitData);
                    break;
                case "mentor_peer":
                    shouldCancelInfluence = false;
                    KerbalMentorPeer(personnelFile, emitData);
                    break;
                case "accident":
                    KerbalAccident(personnelFile, emitData);
                    break;
                default:
                    Debug( $"[Emission] Event {eventName} is not implemented yet.");
                    break;
            }
            
            if (shouldCancelInfluence) CancelInfluence(personnelFile);
        }

        /// <summary>
        /// Provide message UI and file away in the queue.
        /// </summary>
        /// <param name="newsStory"></param>
        public void FileHeadline(NewsStory newsStory, bool fileMessage = true)
        {
            if (!logDebug && newsStory.scope == HeadlineScope.DEBUG) return;
            
            HeadlinesUtil.Report(newsStory, notificationThreshold, fileMessage);
            if (newsStory.scope != HeadlineScope.DEBUG)
            {
                headlines.Enqueue(newsStory);
            }
        }

        public void Debug(string message, string label = "")
        {
            if (!logDebug) return;
            FileHeadline(new NewsStory(HeadlineScope.DEBUG, label, message));
        }
        
        public void PrintScreen(string message)
        {
            if (!logDebug) return;
            FileHeadline(new NewsStory(HeadlineScope.SCREEN, Story:message));
        }

        /// <summary>
        /// Adjust the time it takes to trigger Reputation Decay. The golden ratio here is about just right
        /// for the purpose. This attention span doesn't not affect the current state we're in, but *may*
        /// affect the next iteration. 
        /// </summary>
        /// <remarks>Likely should be split into distinct effects.</remarks>
        /// <param name="increment">either -1 or 1</param>
        public void AdjustSpaceCraze(double increment)
        {
            double power = 1.618;
            if (increment < 0)
            {
                power = 1 / power;
            }

            attentionSpanFactor *= power;

            // Clamp this factor within reasonable boundaries
            attentionSpanFactor = Math.Max(Math.Pow(power, -5), attentionSpanFactor); // min is 0.47
            attentionSpanFactor = Math.Min(Math.Pow(power, 3), attentionSpanFactor); // max is 1.56

            // Let player know without spamming the screen
            if (increment > 0 && attentionSpanFactor > 1.61)
            {
                Emissions em = new Emissions("attention_span_long");
                NewsStory ns = new NewsStory(em);
                ns.headline = "Pressure lowers on you";
                ns.AddToStory(em.GenerateStory());
                FileHeadline(ns);
            }
            else if (attentionSpanFactor <= 1)
            {
                Emissions em = new Emissions("attention_span_short");
                NewsStory ns = new NewsStory(em);
                ns.headline = "Space craze is high";
                ns.AddToStory(em.GenerateStory());
                FileHeadline(ns);
            }
        }

        /// <summary>
        /// Adjust hype in either direction. Hype cannot go under 0 as there is no such thing
        /// as negative hype.
        /// </summary>
        /// <param name="increment">(float)the number of increment unit to apply.</param>
        public double AdjustHype(float increment)
        {
            return _reputationManager.AdjustHype(increment);
        }

        /// <summary>
        /// Degrades reputation of the program. 
        /// </summary>
        public void DecayReputation()
        {
            double decayScalar = _reputationManager.GetReputationDecay(_peopleManager.ProgramProfile());

            // reputation tends to program profile if too low.
            if (decayScalar > 0)
            {
                _reputationManager.AdjustCredibility(decayScalar);
                return;
            }
            
            // Fame for each crew member is decaying
            _peopleManager.DecayFame();
            
            // Secret achievements cannot be protected by PR
            foreach (NewsStory ns in _reputationManager.shelvedAchievements)
            {
                ns.reputationValue *= 0.933f;
            }

            if (KerbalProtectReputationDecay())
            {
                HeadlinesUtil.Report(2, "Your crew prevented your reputation from decaying.", "Media relation");
                return;
            }

            double marginOverProfile = _reputationManager.Credibility() - _peopleManager.ProgramProfile();
            if (marginOverProfile / 100 > storytellerRand.NextDouble())
            {
                _reputationManager.AdjustCredibility(decayScalar);
                HeadlinesUtil.Report(2, $"{(int) decayScalar} reputation decayed. New total: {(int) _reputationManager.CurrentReputation()}.");
            }
        }

        /// <summary>
        /// Public reevaluation of the hype around a program versus its actual reputation, and a proportional correction.
        /// </summary>
        public void RealityCheck(bool withStory = true, bool noProtection = false)
        {
            // Reality checks can be prevented during a campaign only
            if (!noProtection && _reputationManager.currentMode == MediaRelationMode.CAMPAIGN && KerbalProtectReputationDecay())
            {
                return;
            }
            double hypeLoss = _reputationManager.RealityCheck();
            if (hypeLoss >= 1)
            {
                if (withStory)
                {
                    Emissions em = new Emissions("reality_check");
                    em.AddStoryElement("delta", ((int) hypeLoss).ToString());
                    FileHeadline(new NewsStory(em, Headline:"Hype deflates", generateStory:true));
                }
            }
        }

        /// <summary>
        /// Heartbeat of the program manager
        /// </summary>
        public void ProgramCheck()
        {
            // In case of a retirement
            RemoveRetirees();
            
            // In ase of a brand new PM
            _programManager.CrewReactToAppointment();

            SkillCheckOutcome outcome = SkillCheck(GetProbabilisticLevel(_programManager.ManagerProfile()), GetProgramComplexity());
            
            if (outcome == SkillCheckOutcome.CRITICAL & _programManager.ControlLevel() != ProgramControlLevel.HIGH)
            {
                NewsStory ns = new NewsStory(HeadlineScope.FEATURE, "KSC in high-gear");
                ns.AddToStory($"Your program is experiencing a golden age of productivity due to the leadership from {_programManager.ManagerName()}.");
                FileHeadline(ns);
            }
            if (outcome == SkillCheckOutcome.FUMBLE & _programManager.ControlLevel() != ProgramControlLevel.CHAOS)
            {
                NewsStory ns = new NewsStory(HeadlineScope.FEATURE, "KSC in chaos");
                ns.AddToStory($"A few blunders from {_programManager.ManagerName()} sends your program into chaos.");
                FileHeadline(ns);
            }
            
            _programManager.RegisterProgramCheck(outcome);
        }

        #region Death

        public void InquiryDamningReport()
        {
            Emissions em = new Emissions("damning_report");
            FileHeadline(new NewsStory(em, Headline: "Public inquiry: damning report", generateStory:true));
            
            if (storytellerRand.NextDouble() < 0.5 | _reputationManager.Hype() == 0) DecayReputation();
            RealityCheck();
        }
        
        public void InquirySpinFindings()
        {
            Emissions em = new Emissions("spin_findings");
            FileHeadline(new NewsStory(em, Headline: "Public inquiry: exoneration", generateStory:true));
            
            AdjustHype(1);
        }
        
        public void InquiryConclude()
        {
            Emissions em = new Emissions("conclude_inquiry");
            FileHeadline(new NewsStory(em, Headline: "Public inquiry: case closed", generateStory:true));
            
            RemoveHMM("death_inquiry");
            ongoingInquiry = false;
        }

        #endregion

        /// <summary>
        /// Add a random applicant at the program's reputation level
        /// </summary>
        public void NewRandomApplicant()
        {
            PersonnelFile pf = _peopleManager.GenerateRandomApplicant(GetValuationLevel());
            
            Emissions emission = new Emissions("new_applicant");
            NewsStory ns = new NewsStory(emission);
            emission.AddStoryElement("name", pf.DisplayName());
            emission.AddStoryElement("effectiveness", _peopleManager.QualitativeEffectiveness(pf.Effectiveness()));
            emission.AddStoryElement("specialty", pf.Specialty());
            ns.AddToStory(emission.GenerateStory());
            if (pf.personality != "")
            {
                ns.AddToStory($"During the interview, the candidate proved to be {pf.personality}.");
            }
            ns.headline = $"New {_peopleManager.QualitativeEffectiveness(pf.Effectiveness())} applicant";
            FileHeadline(ns);
            
            if (_peopleManager.ShouldEndWarp(pf))
            {
                HeadlinesUtil.Report(2, "New Applicant to review");
                TimeWarp.SetRate(0,false);
            }
        }

        /// <summary>
        /// Random select then delete of an applicant. Never let the pool go under 2 kerbals. One isn't enough since the
        /// player may decide to hire them, leaving the pool empty.
        /// </summary>
        /// <remarks>This probably should be split into two: select random applicant, delete applicant.</remarks>
        /// todo Split the two procedure into two methods.
        public void WithdrawRandomApplication()
        {
            while (_peopleManager.applicantFolders.Count <= 1)
            {
                _peopleManager.GenerateRandomApplicant();
            }
            
            if (_peopleManager.applicantFolders.Count > 1)
            {
                PersonnelFile dropOut = (PersonnelFile) null;
                int randomIndex = storytellerRand.Next(0, _peopleManager.applicantFolders.Count);
                foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.applicantFolders)
                {
                    if (randomIndex == 0)
                    {
                        dropOut = kvp.Value;
                        break;
                    }

                    randomIndex--;
                }
                
                Emissions em = new Emissions("withdraw_application");
                em.AddStoryElement("name", dropOut.DisplayName()); 
                
                NewsStory ns = new NewsStory(em, Headline:"Withdrawn application");
                ns.AddToStory(em.GenerateStory());
                FileHeadline(ns);
                
                _peopleManager.RemoveKerbal(dropOut);
            }
        }

        public void EventContractCompleted(Contract contract)
        {
            NewsStory ns = new NewsStory(HeadlineScope.FRONTPAGE, $"{contract.Title}");
            ns.AddToStory($"Multiple reports of the completion of {contract.Title}!");
            if (contract.FundsCompletion != 0)
            {
                ns.AddToStory($" The √{contract.FundsCompletion} will be most welcome to bankroll future endeavours.");
            }

            ns.reputationValue = contract.ReputationCompletion;
            HeadlinesUtil.Report(1, $"Contract {contract.Title} value is {contract.ReputationCompletion}");
            if (_reputationManager.currentMode != MediaRelationMode.LOWPROFILE)
            {
                FileHeadline(ns, false);
                
                // Add fame to any crew in the active vessel
                if (FlightGlobals.ActiveVessel != null)
                {
                    foreach (ProtoCrewMember pcm in FlightGlobals.ActiveVessel.GetVesselCrew())
                    {
                        _peopleManager.GetFile(pcm.name).AddFame(contract.ReputationCompletion);
                    }
                }
            }
            else
            {
                _reputationManager.FilePressRelease(ns);
            }

            if (_reputationManager.mediaContracts.Contains(contract))
            {
                _reputationManager.mediaContracts.Remove(contract);
            }
        }

        public void EventContractAccepted(Contract contract)
        {
            if (contract.AutoAccept) return;

            NewsStory ns = new NewsStory(HeadlineScope.FEATURE, "Challenge Accepted!");
            ns.AddToStory($"Multiple reports covering the pledge to complete {contract.Title} by {KSPUtil.PrintDate(contract.DateExpire, false, false)}.");
            if (contract.FundsAdvance != 0)
            {
                ns.AddToStory($" A sum of √{contract.FundsAdvance} was advanced to help in this commitment.");
            }

            if (contract.ReputationCompletion != 0 & _reputationManager.CurrentReputation() != 0)
            {
                ns.AddToStory($" {Math.Round(100*(contract.ReputationCompletion/_reputationManager.CurrentReputation()), MidpointRounding.AwayFromZero)}% of the program's reputation may be at stake.");
            }
            FileHeadline(ns, false);
            
        }

        #region Debris
        
        /// <summary>
        /// Triggered by self-declaration
        /// </summary>
        /// <param name="populated"></param>
        public void DebrisOverLand(bool populated = false)
        {
            if (populated)
            {
                InitializeHMM("debris_endangerment_populated");
            }
            else
            {
                InitializeHMM("debris_endangerment");
            }
        }

        /// <summary>
        /// Something bad happened because you these debris.
        /// </summary>
        private void DebrisDamage()
        {
            Emissions em = new Emissions("debris_damage");
            NewsStory ns = new NewsStory(em);
            ns.headline = "Debris: Damage report";
            ns.AddToStory(em.GenerateStory());
            FileHeadline(ns);
            DecayReputation();
            RealityCheck(withStory:false);
            DebrisResolve();
        }

        /// <summary>
        /// benine, if not positive effect of media attention
        /// </summary>
        private void DebrisSpin()
        {
            Emissions em = new Emissions("debris_spin");
            NewsStory ns = new NewsStory(em);
            ns.headline = "Debris: It could be worse";
            ns.AddToStory(em.GenerateStory());
            ns.AddToStory("Hype is going up by 5.");
            FileHeadline(ns);
            _reputationManager.AdjustHype(5);
            DebrisResolve();
        }

        /// <summary>
        /// End the sequence of events related to debris on land.
        /// </summary>
        private void DebrisConclude()
        {
            foreach (KeyValuePair<string, HiddenState> kvp in _liveProcesses)
            {
                if (kvp.Key.StartsWith("debris_endangerment"))
                {
                    _hmmToRemove.Add(kvp.Key);
                    break;
                }
            }
        }

        public void DebrisResolve()
        {
            foreach (KeyValuePair<string, HiddenState> kvp in _liveProcesses)
            {
                if (kvp.Key.StartsWith("debris_"))
                {
                    double concludeProb = kvp.Value.GetEmissionProbability("debris_conclude");
                    concludeProb += (1 - concludeProb) / 2;
                    kvp.Value.SpecifyEmission("debris_conclude", (float)concludeProb);
                }
            }
        }

        #endregion

        public void HighDramaEndingWell()
        {
            highDramaReported = true;
            NewsStory ns = new NewsStory(HeadlineScope.FRONTPAGE, "High drama on TV");
            ns.AddToStory($"Dramatic tension for {FlightGlobals.ActiveVessel.name} is captured on live TV.");
            FileHeadline(ns);
            _reputationManager.AdjustHype(_reputationManager.Hype()*0.2);
        }

        public void VisibleShowOverUrban()
        {
            overUrbanReported = true;
            NewsStory ns = new NewsStory(HeadlineScope.FRONTPAGE, "This is not a UFO!");
            ns.AddToStory($"Sightings of {FlightGlobals.ActiveVessel.name} over the city are captured on live TV.");
            FileHeadline(ns);
            _reputationManager.AdjustHype(_reputationManager.Hype()*0.1);
        }

        #endregion

        #region GUIDisplay

        /// <summary>
        /// String to represent the valuation qualitatively, and quantitatively.
        /// </summary>
        /// <returns></returns>
        public string GUIValuation()
        {
            return $"{_reputationManager.QualitativeReputation()} ({(int)_reputationManager.CurrentReputation()})";
        }

        /// <summary>
        /// String to represent the relative over-valuation
        /// </summary>
        /// <returns></returns>
        public string GUIOvervaluation()
        {
            double ovr = Math.Round(100 * _reputationManager.OverRating(),MidpointRounding.AwayFromZero);
            return $"{ovr}%";
        }

        public string GUISpaceCraze()
        {
            if (this.attentionSpanFactor < 0.6) return "High";
            else if (this.attentionSpanFactor > 1.619)
            {
                return "Low";
            }

            return "Nominal";
        }

        public string GUIAverageProfile()
        {
            double averageProfile = _peopleManager.ProgramAverageEffectiveness(determinisitic: true);
            return _peopleManager.QualitativeEffectiveness(averageProfile);
        }

        public string GUIRelativeToPeak()
        {
            double peak = _reputationManager.Peak();

            return $"{Math.Round(100 * peak)}%";
        }

        public double GUIVisitingSciencePercent()
        {
            if (totalScience == 0) return 0;

            double ratio = visitingScienceTally / totalScience;
            ratio *= 100;
            return Math.Round(ratio, 1);
        }

        public double GUIVisitingScience()
        {
            return Math.Round(visitingScienceTally, 1);
        }

        public double GUIFundraised()
        {
            return Math.Floor(this.fundraisingTally);
        }

        public int GUIVABEnhancement()
        {
            return _peopleManager.KSCImpact("Engineer") + _programManager.GetVABInfluence();

        }

        public int GUIRnDEnhancement()
        {
            return _peopleManager.KSCImpact("Scientist") + _programManager.GetRnDInfluence();
        }

        #endregion

        #region InternalLogic

        /// <summary>
        /// Not very readable method to cull the feed when larger than maxStories. It deletes all SCREEN items over 3 months,
        /// NEWSLETTER items over 6 month, FEATURES over 9 month, and HEADLINES over 1 year.
        /// </summary>
        private void CullNewsFeed()
        {
            int maxStories = 100;
            double month = 3600 * 24 * 30;
            double now = HeadlinesUtil.GetUT();

            
            if (headlines.Count > maxStories)
            {
                Queue<NewsStory> nq = new Queue<NewsStory>();
                foreach (NewsStory ns in headlines)
                {
                    if (ns.timestamp > now - ((int)ns.scope * 3 * month))
                    {
                        nq.Enqueue(ns);
                    }
                }
                headlines = nq;
                Debug($"News Feed culled to {headlines.Count} elements","News Feed");
            }
        }

        public int GetNumberNewsAbout(string crewName)
        {
            int output = 0;
            foreach (NewsStory ns in headlines)
            {
                if (ns.HasActor(crewName))
                {
                    output += 1;
                }
            }

            return output;
        }

        /// <summary>
        /// Determines the outcome of a check based on a mashup of Pendragon and VOID.
        /// </summary>
        /// <remarks>Deprecated, will die if the GURPS model makes more sense.</remarks>
        /// <param name="skillLevel">0+ arbitrary unit</param>
        /// <param name="difficulty">0+ arbitrary unit</param>
        /// <returns>FUMBLE|FAILURE|SUCCESS|CRITICAL</returns>
        public static SkillCheckOutcome SkillCheckOutcomeCypher(int skillLevel, int difficulty = 0)
        {
            int upperlimit = 3 * skillLevel;
            int lowerlimit = 3 * difficulty;

            SkillCheckOutcome outcome = SkillCheckOutcome.FAILURE;

            int die = storytellerRand.Next(1, 20);

            if (upperlimit > 20) die += (upperlimit - 20);
            else if (die == 20) outcome = SkillCheckOutcome.FUMBLE;

            if (die == upperlimit || (upperlimit >= 20 && die >= 20)) outcome = SkillCheckOutcome.CRITICAL;
            else if (die >= lowerlimit && die < upperlimit) outcome = SkillCheckOutcome.SUCCESS;

            return outcome;
        }
        
        /// <summary>
        /// Determines the outcome of a check based on GURPS/Cyper mashup. Lowers chances of critical success
        /// and fumbles as the straigh CYPHER system isn't design for a high volume of checks.
        /// </summary>
        /// <param name="skillLevel">0+ arbitrary unit</param>
        /// <param name="difficulty">0+ arbitrary unit</param>
        /// <returns>FUMBLE|FAILURE|SUCCESS|CRITICAL</returns>
        public static SkillCheckOutcome SkillCheck(int skillLevel, int difficulty = 0)
        {
            int upperlimit = 3 * skillLevel;
            int lowerlimit = 3 * difficulty;

            SkillCheckOutcome outcome = SkillCheckOutcome.FAILURE;

            int die = storytellerRand.Next(1, 7) + storytellerRand.Next(1, 7) + storytellerRand.Next(1, 7);
            
            if ((die <= upperlimit & die > lowerlimit) | (upperlimit <= 4 & die <= 4)) outcome = SkillCheckOutcome.SUCCESS; 
            else if (die <= 4 | upperlimit - die >= 10 ) outcome = SkillCheckOutcome.CRITICAL;
            else if (die >= 17 ) outcome = SkillCheckOutcome.FUMBLE;
            
            return outcome;
        }
        
        /// <summary>
        /// Valuation is the sum of reputation and hype: this is what people can see.
        /// </summary>
        /// <returns></returns>
        public double GetValuation()
        {
            return _reputationManager.CurrentReputation();
        }

        public int GetValuationLevel()
        {
            return _reputationManager.GetReputationLevel();
        }

        public PeopleManager GetPeopleManager()
        {
            return _peopleManager;
        }
        
        /// <summary>
        ///  Begin a brand new search process.
        /// </summary>
        public void LaunchSearch(bool headhunt = false, bool nocost = false)
        {
            Debug( "Launch a new search");
            _peopleManager.ClearApplicants();

            double cost = SearchCost(headhunt);

            int generationLevel = GetValuationLevel();
            if (headhunt)
            {
                generationLevel += 2;
            }

            if (!nocost)
            {
                AdjustFunds(-1 * cost);
            }
            
            generationLevel = Math.Min(5, generationLevel);
            
            int poolSize = 4 +
                           (int) Math.Round((double) generationLevel * (_reputationManager.Peak())) +
                           storytellerRand.Next(-1, 2);
            while (poolSize > 0)
            {
                _peopleManager.GenerateRandomApplicant(generationLevel);
                poolSize--;
            }
        }

        /// <summary>
        /// Compute search cost (and headhunt)
        /// </summary>
        /// <param name="headhunt">is this a headhunt?</param>
        /// <returns>Cost in funds</returns>
        public double SearchCost(bool headhunt = false)
        {
            double cost = 1000 * (double)(GetValuationLevel() + 1);
            if (headhunt)
            {
                cost *= 4;
            }

            return cost;
        }

        public void StartMediaCampaign(bool invite, int nDays)
        {
            if (_reputationManager.currentMode == MediaRelationMode.LOWPROFILE & invite)
            {
                double campaignLength = nDays * (3600 * 24);
                _reputationManager.LaunchCampaign(campaignLength);
                
                // Ensures that any crew able will make a media appearance
                foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.personnelFolders)
                {
                    if (kvp.Value.Specialty() == "Pilot")
                    {
                        // Guarantees a media event per pilot during a campaign
                        HiddenState hmm = GetRoleHMM(kvp.Value);
                        double newTime = storytellerRand.NextDouble() * campaignLength + HeadlinesUtil.GetUT();
                        if (_hmmScheduler.ContainsKey(hmm.RegisteredName()))
                        {
                            _hmmScheduler[hmm.RegisteredName()] = Math.Min(_hmmScheduler[hmm.RegisteredName()], newTime);
                        }

                        kvp.Value.OrderTask("media_blitz");
                    }
                }
            }
        }

        /// <summary>
        /// If time expires and the target isn't met, deduce the shortcoming from actual reputation and zero hype.
        /// </summary>
        public void MediaEventUpdate()
        {
            if (_reputationManager.currentMode == MediaRelationMode.LOWPROFILE)
            {
                return;
            }

            if (_reputationManager.currentMode == MediaRelationMode.CAMPAIGN &
                _reputationManager.airTimeEnds < HeadlinesUtil.GetUT())
            {
                _reputationManager.CancelMediaEvent();
                _programManager.AdjustRemainingLaunches(-0.5);
            }

            if (_reputationManager.currentMode == MediaRelationMode.LIVE & _reputationManager.airTimeEnds < HeadlinesUtil.GetUT())
            {
                double credibilityAdjustment = _reputationManager.EndLIVE();
                NewsStory ns = new NewsStory(HeadlineScope.FEATURE);
                if (credibilityAdjustment < 0)
                {
                    HeadlinesUtil.Report(2, $"Coming off live with a credibility loss of {credibilityAdjustment}");
                    ns.AddToStory($"The media crews are leaving disappointed. (Rep: {Math.Round(credibilityAdjustment,2)})");
                    ns.headline = "Media debrief: failure";
                }
                else
                {
                    ns.AddToStory($"The media crews are leaving impressed. (Hype: {Math.Round(_reputationManager.Hype(),2)})");
                    ns.headline = "Media debrief: Success";
                    _programManager.AdjustRemainingLaunches(0.5);
                }
                FileHeadline(ns);
            }
        }

        public void IssuePressRelease(NewsStory ns)
        {
            _reputationManager.IssuePressReleaseFor(ns);
            FileHeadline(ns);
        }

        /// <summary>
        /// Convergence to 35% bonus with 10 visitors, more likely high end is 22% with 3.
        /// </summary>
        /// <returns></returns>
        public float VisitingScienceBonus()
        {
            if (visitingScholarEndTimes.Count != 0)
            {
                while (visitingScholarEndTimes[0] < HeadlinesUtil.GetUT())
                {
                    visitingScholarEndTimes.Remove(visitingScholarEndTimes[0]);
                }
            }
            
            float output = 0;
            for (int i = 0; i < visitingScholarEndTimes.Count; i++)
            {
                output += 0.12f * (1f/(1f+i));
            }
            return output;
        }

        /// <summary>
        /// Determines whether crew on staff are protecting against decay. Pilots are at a disadvantage (-1) in this case.
        /// </summary>
        /// <returns></returns>
        public bool KerbalProtectReputationDecay()
        {
            // Step 1: the program manager
            SkillCheckOutcome outcome = SkillCheck(GetProbabilisticLevel(_programManager.ManagerProfile()) );
            if (outcome == SkillCheckOutcome.SUCCESS || outcome == SkillCheckOutcome.CRITICAL) return true;
            
            // Step 2: Any crew in media_relation mode
            foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.personnelFolders)
            {
                if (kvp.Value.IsInactive() || kvp.Value.isProgramManager) continue;
                if (kvp.Value.kerbalTask != "media_blitz") continue;

                outcome = SkillCheck(kvp.Value.Effectiveness(isMedia: true)-1);
                if (outcome == SkillCheckOutcome.SUCCESS || outcome == SkillCheckOutcome.CRITICAL) return true;
            }

            return false;
        }

        public double HiringRebate()
        {
            return 10000;
        }

        /// <summary>
        /// How difficult it is for the program manager to do their job.
        /// </summary>
        /// <returns>A difficulty for a SkillCheck</returns>
        public int GetProgramComplexity()
        {
            double level = 0;
            level += GetFacilityLevel(SpaceCenterFacility.MissionControl) * 2;
            level += GetFacilityLevel(SpaceCenterFacility.AstronautComplex) * 2;
            level += GetFacilityLevel(SpaceCenterFacility.ResearchAndDevelopment);
            level += GetFacilityLevel(SpaceCenterFacility.VehicleAssemblyBuilding);
            level /= 6;

            return (int) Math.Round(level, MidpointRounding.AwayFromZero);
        }

        public int GetProbabilisticLevel(double level, bool deterministic = true)
        {
            if (deterministic)
            {
                return (int)Math.Round(level, MidpointRounding.AwayFromZero);
            }
            
            int output = (int) Math.Floor(level);
            if (storytellerRand.NextDouble() <= level - Math.Floor(level))
            {
                output += 1;
            }

            return output;
        }

        /// <summary>
        /// Handles both crew and program managers
        /// </summary>
        public void RemoveRetirees()
        {
            string retireeName = _peopleManager.DeleteRetirees();
            if (retireeName != "")
            {
                RemoveKerbalHMM(retireeName);
            }

            if (_programManager.ManagerRemainingLaunches() <= 0)
            {
                string message = $"{_programManager.ManagerName()} retires as PM and is replaced by ";
                _programManager.RevertToDefaultProgramManager();
                message += _programManager.ManagerName();
                NewsStory ns = new NewsStory(HeadlineScope.FRONTPAGE, "PM retires", message);
                FileHeadline(ns);
            }
        }

        /// <summary>
        /// Add 10 days to a live event, against cash and diminishing hype.
        /// </summary>
        public void ExtendLiveEvent()
        {
            _reputationManager.ExtendLiveEvent();
            RealityCheck();
            AdjustFunds(-1 * _reputationManager.MediaCampaignCost(10));

            NewsStory ns = new NewsStory(HeadlineScope.FRONTPAGE, "Keep on watching!");
            ns.AddToStory("The program asks the press to stay posted up to an additional 10 days.");
            FileHeadline(ns);
        }
        
        /// <summary>
        /// Compute the number of guaranteed appearances, and their expected earnings. 
        /// </summary>
        /// <param name="nAppearance"></param>
        /// <param name="expectedHype"></param>
        public void ExpectedCampaignEarnings(ref int nAppearance, ref double expectedHype, double deadline = -1)
        {
            nAppearance = 0;
            expectedHype = 0;
            
            if (deadline == -1)
            {
                deadline = _reputationManager.airTimeEnds;
            }

            PersonnelFile crew;
            int skill = 0;
            double oneEarning = 0;
            double nEvents = 0;
            double nAppDouble = 0;
            foreach (KeyValuePair<string, HiddenState> kvp in _liveProcesses)
            {
                
                if (kvp.Value.RegisteredName().Contains("role_"))
                {
                    crew = _peopleManager.GetFile(kvp.Value.kerbalName);
                    if (_reputationManager.currentMode == MediaRelationMode.CAMPAIGN && crew.coercedTask && crew.kerbalTask == "media_blitz")
                    {
                        if (_hmmScheduler[kvp.Key] <= deadline)
                        {
                            skill = crew.Effectiveness(deterministic: true);
                            oneEarning = 2 * skill * HeadlinesUtil.Pvalue(skill);
                            nEvents = 1 + (deadline - _hmmScheduler[kvp.Key])/ (2 * kvp.Value.period*24*3600);
                            expectedHype += oneEarning * nEvents;
                            nAppDouble += nEvents;
                        }
                    }
                    else if (_reputationManager.currentMode == MediaRelationMode.LOWPROFILE &&
                             crew.Specialty() == "Pilot")
                    {
                        skill = crew.Effectiveness(deterministic: true);
                        oneEarning = 2 * skill * HeadlinesUtil.Pvalue(skill);
                        nEvents = 1 + (deadline - HeadlinesUtil.GetUT())/ (2 * kvp.Value.period*24*3600);
                        expectedHype += oneEarning * nEvents;
                        nAppDouble += nEvents;
                    }
                }
            }

            // Non-linear transform
            expectedHype = _reputationManager.TransformReputation(expectedHype, _reputationManager.CurrentReputation());

            nAppearance = (int)Math.Round(nAppDouble, MidpointRounding.AwayFromZero);
        }
        
        #endregion

        #region RP1

        /// <summary>
        /// Connect to RP1 to obtain points in R&D
        /// </summary>
        /// <returns></returns>
        public int GetRnDPoints()
        {
            return Utilities.GetSpentUpgradesFor(SpaceCenterFacility.ResearchAndDevelopment);
        }

        /// <summary>
        /// Connect to RP1 to obtain points in R&D
        /// </summary>
        /// <returns></returns>
        public int GetVABPoints()
        {
            return Utilities.GetSpentUpgradesFor(SpaceCenterFacility.VehicleAssemblyBuilding);
        }

        public int UpgradeIncrementVAB()
        {
            int purchased = GetVABPoints() - GUIVABEnhancement();
            return Math.Max(1, GetProbabilisticLevel((double)purchased * 0.05));
        }
        
        public int UpgradeIncrementRnD()
        {
            int purchased = GetRnDPoints() - GUIRnDEnhancement();
            return Math.Max(1, GetProbabilisticLevel((double)purchased * 0.05));
        }

        /// <summary>
        /// Change the tally of points in the R&D.
        /// </summary>
        /// <param name="deltaPoint">number of point to change</param>
        public void AdjustRnD(int deltaPoint)
        {
            KSCItem ksc = KCTGameStates.ActiveKSC;
            ksc.RDUpgrades[0] += deltaPoint;
            KCTGameStates.PurchasedUpgrades[0] += deltaPoint;
            Debug( $"Adjust R&D points by {deltaPoint}.");
        }

        /// <summary>
        /// Change the tally of points in the VAB.
        /// </summary>
        /// <param name="deltaPoint">number of point to change</param>
        public void AdjustVAB(int deltaPoint, int line = 0)
        {
            KSCItem ksc = KCTGameStates.ActiveKSC;
            ksc.VABUpgrades[line] += deltaPoint;
            KCTGameStates.PurchasedUpgrades[0] += deltaPoint;
            Debug( $"Adjust VAB points by {deltaPoint}.");
        }

        /// <summary>
        /// Remove the live influence of a kerbal on the KSC.
        /// </summary>
        /// <param name="kerbalFile">The actor</param>
        /// <param name="leaveKSC">This cancellation is the last one.</param>
        private void CancelInfluence(PersonnelFile kerbalFile, bool leaveKSC = false)
        {
            if (kerbalFile.influence != 0)
            {
                switch (kerbalFile.Specialty())
                {
                    case "Scientist":
                        AdjustRnD(-1 * kerbalFile.influence);
                        if (leaveKSC) AdjustRnD(-1 * kerbalFile.teamInfluence);
                        break;
                    case "Engineer":
                        AdjustVAB(-1 * kerbalFile.influence);
                        if (leaveKSC) AdjustVAB(-1 * kerbalFile.teamInfluence);
                        break;
                }

                kerbalFile.influence = 0;
            }
        }

        /// <summary>
        /// When an event causes everything to stop.
        /// </summary>
        private void CancelAllInfluence()
        {
            foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.personnelFolders)
            {
                CancelInfluence(kvp.Value);
            }
        }

        /// <summary>
        /// Catch cases where inactivity is triggered by a course.
        /// </summary>
        private void CancelInfluenceForInactiveCrew()
        {
            foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.personnelFolders.Where(x=>x.Value.IsInactive()))
            {
                CancelInfluence(kvp.Value);
            }
        }

        public int GetFacilityLevel(SpaceCenterFacility facility)
        {
            return Utilities.GetBuildingUpgradeLevel(facility);
        }

        /// <summary>
        /// Returns whatever KCT wants to warp to next, including tech nodes. 
        /// </summary>
        /// <returns></returns>
        public double GetNextLaunchDeltaTime()
        {
            IKCTBuildItem buildItem = Utilities.GetNextThingToFinish();
            double buildTime = buildItem.GetTimeLeft();
            return buildTime;
        }

        /// <summary>
        /// Pull the data structure of all vessels currently in assembly
        /// </summary>
        /// <returns></returns>
        public List<BuildListVessel> GetAssemblyQueue()
        {
            List<BuildListVessel> output = new List<BuildListVessel>();

            foreach (BuildListVessel blv in KCTGameStates.ActiveKSC.VABList)
            {
                output.Add(blv);
            }
            foreach (BuildListVessel blv in KCTGameStates.ActiveKSC.SPHList)
            {
                output.Add(blv);
            }
            
            return output;
        }

        /// <summary>
        /// Pulls all tech nodes from KCT
        /// </summary>
        /// <returns></returns>
        public List<TechItem> GetTechNodes(bool  ongoing = false)
        {
            List<TechItem> output = new List<TechItem>();
            foreach (var techNode in KCTGameStates.TechList)
            {
                if (!ongoing || ongoing && techNode.Progress != 0)
                {
                    output.Add(techNode);
                }
                
                //var x = techNode.ProtoNode.partsPurchased[0];
                //PartLoader.LoadedPartsList.Where(x => x.TechRequired == techNode.TechID);
            }
            
            return output;
        }

        public List<PartCategories> GetPartCategoriesUnderResearch()
        {
            List<PartCategories> output = new List<PartCategories>();
            foreach (TechItem tech in GetTechNodes(true))
            {
                foreach (AvailablePart avp in PartLoader.LoadedPartsList.Where(x => x.TechRequired == tech.TechID))
                {
                    if (!output.Contains(avp.category) && avp.category != PartCategories.none)
                    {
                        output.Add(avp.category);
                    }
                }
            }

            return output;
        }
        

        #endregion
    }
}
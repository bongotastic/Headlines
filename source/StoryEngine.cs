using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;
using Expansions.Missions.Editor;
using FinePrint;
using HiddenMarkovProcess;
using KerbalConstructionTime;
using Renamer;
using RPStoryteller.source;
using RPStoryteller.source.Emissions;
using UnityEngine;
using UnityEngine.Serialization;


namespace RPStoryteller
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

        public static string[] renownLevels = new[] { "underdog", "renowned", "leader", "excellent", "legendary"};

        // Random number generator
        private static System.Random storytellerRand = new System.Random();

        // Mod data structures
        private PeopleManager _peopleManager;
        
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

        // Maximum earning in repuation in a single increase call.
        [KSPField(isPersistant = true)] public float programHype = 10;
        
        // To appeal to the optimizers
        [KSPField(isPersistant = true)] public double headlinesScore = 0;
        [KSPField(isPersistant = true)] public double lastScoreTimestamp = 0;

        // Pledged hype when inviting the public
        [KSPField(isPersistant = true)] public bool mediaSpotlight = false;
        [KSPField(isPersistant = true)] public double endSpotlight = 0;
        [KSPField(isPersistant = true)] public double wageredReputation = 0;
        
        //[KSPField(isPersistant = true)] public Dictionary<System.Guid, double> pledgedContracts = 10;

        // Cache of the last time we manipulated repuation
        [KSPField(isPersistant = true)] public float programLastKnownReputation = 0;

        // Highest reputation achieved (including overvaluation)
        [KSPField(isPersistant = true)] public float programHighestValuation = 0;

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
        
        // New Game flag
        [KSPField(isPersistant = true)] public bool hasnotvisitedAstronautComplex = true;
        public bool inAstronautComplex = false;
        
        // Logging
        [KSPField(isPersistant = true)] public HeadlineScope notificationThreshold = HeadlineScope.FEATURE;
        [KSPField(isPersistant = true)] public HeadlineScope feedThreshold = HeadlineScope.FEATURE;
        [KSPField(isPersistant = true)] public bool logDebug = true;
        public Queue<NewsStory> headlines = new Queue<NewsStory>();
         
        #endregion

        #region UnityStuff

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
            //InitializeHMM("death_inquiry");

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
            GameEvents.Contract.onCompleted.Add(EventContractAccepted);
        }

        /// <summary>
        /// Heartbeat of the Starstruck mod. Using a cached _nextupdate so as to
        /// avoid checking the collection's values all the time.
        /// </summary>
        public void Update()
        {
            // Shameless hack.
            if (updateIndex < 10)
            {
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
            }

            // Unfortunate addition to the Update look to get around weirdness in Event Firing.
            if (newDeath.Count != 0) DeathRoutine();
            if (newLaunch.Count != 0) CrewedLaunchReputation();

            // Another dumb hack
            if (ongoingInquiry && !_liveProcesses.ContainsKey("death_inquiry")) InitializeHMM("death_inquiry");
            
            // End of Media spotlight?
            EndMediaSpotlight();
            
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
            GameEvents.Contract.onCompleted.Remove(EventContractAccepted);
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
                programLastKnownReputation = newReputation;
                UpdatePeakValuation();

                return;
            }

            // Don't grant reputation for losing a vessel
            if (reason == TransactionReasons.VesselLoss)
            {
                Reputation.Instance.SetReputation(programLastKnownReputation, TransactionReasons.None);
                return;
            }
            
            float deltaReputation = newReputation - this.programLastKnownReputation;
            if (deltaReputation == 0) return;
            
            string before =
                $"Rep: {programLastKnownReputation}, New delta rep: {deltaReputation}, Hype: {this.programHype}.";
            string after = "";
            Debug( before, "Reputation");
            if (deltaReputation <= programHype)
            {
                this.programHype -= deltaReputation;
                this.programLastKnownReputation = newReputation;
                after = $"Final Rep: {programLastKnownReputation}, Net delta: {deltaReputation}, Hype: {programHype}.";
            }
            else
            {
                // Retroactively cap the reputation gain to the active hype
                float realDelta = programHype;
                if (mediaSpotlight)
                {
                    realDelta = Math.Min(deltaReputation, programHype*2f);
                    if (realDelta > programHype)
                    {
                        PrintScreen(
                            $"Thanks to the media invitation, you capture the public's imagination.");
                    }
                }
                else
                {
                    float percent =  programHype / deltaReputation;
                    if (percent < 1f)
                    {
                        PrintScreen(
                            $"Underrated! Your achievement's impact is limited.\n({percent.ToString("P1")})");
                    }
                }
                
                // Integrate as reputation X time in year
                headlinesScore += programLastKnownReputation * ((HeadlinesUtil.GetUT() - lastScoreTimestamp)/(31536000));
                lastScoreTimestamp = HeadlinesUtil.GetUT();
                
                // Surplus reputation goes as new hype
                programHype = deltaReputation - programHype;
                programLastKnownReputation += realDelta;
                Reputation.Instance.SetReputation(programLastKnownReputation, TransactionReasons.None);
                after = $"Final Rep: {programLastKnownReputation}, Net delta: {realDelta}, Hype: {programHype}.";
                Debug( after, "Reputation");
            }
            UpdatePeakValuation();
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
            PersonnelFile newCrew = _peopleManager.GetFile(pcm.name);
            _peopleManager.HireApplicant(newCrew);
            InitializeCrewHMM(newCrew);
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
                
                HeadlinesUtil.Report(3, $"Death inquiry for {crewName} launched.", "Public Inquiry");

                PersonnelFile personnelFile = _peopleManager.GetFile(crewName);
                if (personnelFile == null) PrintScreen("null pfile");
            
                // Make crew members a bit more discontent
                _peopleManager.OperationalDeathShock(crewName);
            
                // inquiry
                InitializeHMM("death_inquiry");
                ongoingInquiry = true;

                // Remove influence
                PrintScreen( "Canceling influence");
                CancelInfluence(personnelFile, leaveKSC: true);

                // HMMs
                RemoveHMM(personnelFile);

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
                newLaunch.Add(ev.host);
            }
        }

        /// <summary>
        /// Reputation and hype gained by launching a kerbal. 
        /// </summary>
        public void CrewedLaunchReputation()
        {
            foreach (Vessel vessel in newLaunch)
            {
                // get crew
                List<ProtoCrewMember> inFlight = vessel.GetVesselCrew();

                float onboardHype = 0f;
            
                PersonnelFile pf;
                foreach (ProtoCrewMember pcm in inFlight)
                {
                    pf = _peopleManager.GetFile(pcm.name);
                    onboardHype += pf.Effectiveness();
                }

                if (onboardHype != 0)
                {
                    HeadlinesUtil.Report(3, $"Hype and rep increased by {onboardHype} due to the crew.", "Crew in Flight");
                    AdjustHype(onboardHype);
                    Reputation.Instance.AddReputation(onboardHype, TransactionReasons.Vessels);
                }
            }
            
            newLaunch.Clear();
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

            SkillCheckOutcome successLevel = SkillCheck(kerbalFile.Effectiveness(isMedia));

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
                adjective = ", however, innefectively ";
                deltaHype = 1f;
            }

            Emissions em = new Emissions("media_blitz");
            NewsStory ns = new NewsStory(em);
            ns.headline = "Media appearance";
            ns.SpecifyMainActor(kerbalFile.DisplayName(), em);
            ns.AddToStory(em.GenerateStory());
            ns.AddToStory($"They are{adjective}in the public eye. Hype gain is {deltaHype}.");
            FileHeadline(ns);
            AdjustHype(deltaHype );
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
                    default:
                        Debug( $"This should never happen {impactType}");
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
                    default:
                        Debug( $"This should never happen {impactType}");
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
            
            NewsStory ns = new NewsStory(emitData,$"Study leave: {personnelFile.UniqueName()}", true);
            ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
            
            // A leave is always good for the soul.
            personnelFile.AdjustDiscontent(-1);

            SkillCheckOutcome outcome = SkillCheck(5, personnelFile.Effectiveness());

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

            Debug( $"{personnelFile.DisplayName()}'s discontent is {personnelFile.GetDiscontent()}.");
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
            
            if (personnelFile.IsFeuding(collaborator))
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
            RemoveHMM(personnelFile);

            // Make it happen
            _peopleManager.RemoveKerbal(personnelFile);
        }

        public void KerbalSacked(PersonnelFile personnelFile)
        {
            // Remove influence
            CancelInfluence(personnelFile, leaveKSC: true);

            // HMMs
            RemoveHMM(personnelFile);

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
            PersonnelFile newApplicant = _peopleManager.GenerateRandomApplicant(GetValuationLevel() + 2);

            NewsStory ns = new NewsStory(emitData);
            ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
            emitData.Add("recruit_name", newApplicant.DisplayName());
            ns.AddToStory(emitData.GenerateStory());
            ns.AddToStory($"{newApplicant.DisplayName()} is a  {_peopleManager.QualitativeEffectiveness(newApplicant.Effectiveness())} {newApplicant.Specialty()}.");
            if (newApplicant.personality != "")
            {
                ns.AddToStory($"They are reported to be somewhat {newApplicant.personality}.");
            }
            FileHeadline(ns);
            
            if (_peopleManager.EndWarp(newApplicant))
            {
                TimeWarp.SetRate(1,false);
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
            
            visitingScholarName = CrewGenerator.GetRandomName(gender);

            NewsStory ns = new NewsStory(emitData);
            ns.SpecifyMainActor(personnelFile.DisplayName(), emitData);
            emitData.Add("visiting_name", visitingScholarName);
            ns.AddToStory(emitData.GenerateStory());
            ns.AddToStory($" {visitingScholarName} is expected to be in-residence until {KSPUtil.PrintDate(expiryDate,false, false)}.");
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
            if (storytellerRand.NextDouble() < 0.5 * stubbornFactor)
            {
                message += "one last time ";
                personnelFile.coercedTask = false;
            }

            if (storytellerRand.NextDouble() < 0.20 * stubbornFactor)
            {
                message += "and isn't happy";
                personnelFile.AdjustDiscontent(1);
            }

            NewsStory ns = new NewsStory(HeadlineScope.SCREEN, Story: message + ".");
            FileHeadline(ns);
            
            EmitEvent(personnelFile.kerbalTask, personnelFile);
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
            Debug($"Initializing {registeredStateIdentity} for '{kerbalName}'");
            string templateStateIdentity = registeredStateIdentity;

            // Split template and kerbal parts when initialized from a save node
            int splitter = registeredStateIdentity.IndexOf("@");
            if (splitter != -1)
            {
                templateStateIdentity = registeredStateIdentity.Substring(splitter + 1);
                kerbalName = registeredStateIdentity.Substring(0, splitter);
            }
            Debug( $"Initializing {templateStateIdentity} for {kerbalName}");

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

        public void ApplyPersonality(HiddenState newState)
        {
            // People manager may not be loaded
            if (_peopleManager.applicantFolders.Count + _peopleManager.personnelFolders.Count == 0) return;
            
            newState._personalityApplied = true;
            // Personality
            if (newState.kerbalName != "")
            {
                PersonnelFile pf = _peopleManager.GetFile(newState.kerbalName);
                float tempVal = 0;
                bool recompute = false;
                
                // This crew member is a social butterfly
                if (pf.HasAttribute("genial"))
                {
                    tempVal = newState.GetEmissionProbability("synergy");
                    if (tempVal != 0) newState.SpecifyEmission("synergy", tempVal*1.5f);
                    
                    tempVal = newState.GetEmissionProbability("reconcile");
                    if (tempVal != 0) newState.SpecifyEmission("reconcile", tempVal*1.5f);
                    
                    tempVal = newState.GetEmissionProbability("feud");
                    if (tempVal != 0) newState.SpecifyEmission("feud", tempVal*0.5f);

                    recompute = true;
                }

                // This one is not
                if (pf.HasAttribute("scrapper"))
                {
                    tempVal = newState.GetEmissionProbability("reconcile");
                    if (tempVal != 0) newState.SpecifyEmission("reconcile", tempVal*0.75f);
                    
                    tempVal = newState.GetEmissionProbability("feud");
                    if (tempVal != 0) newState.SpecifyEmission("feud", tempVal*1.5f);
                    
                    recompute = true;
                }
                
                if (recompute) newState.Recompute();
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
        /// Safely disable a HMM.
        /// </summary>
        /// <param name="registeredStateIdentity">The identifier for this HMM</param>
        private void RemoveHMM(string registeredStateIdentity)
        {
            Debug( $"Removing HMM {registeredStateIdentity}", "HMM");
            if (_hmmScheduler.ContainsKey(registeredStateIdentity)) _hmmScheduler.Remove(registeredStateIdentity);
            if (_liveProcesses.ContainsKey(registeredStateIdentity)) _liveProcesses.Remove(registeredStateIdentity);
        }

        /// <summary>
        /// Removes HMMs associated with a specific file
        /// </summary>
        /// <param name="personnelFile">the file of a crew member</param>
        private void RemoveHMM(PersonnelFile personnelFile)
        {
            Debug( $"Removing HMM for {personnelFile.UniqueName()}", "HMM");
            List<string> choppingBlock = new List<string>();
            foreach (KeyValuePair<string, HiddenState> kvp in _liveProcesses)
            {
                if (kvp.Value.kerbalName == personnelFile.UniqueName())
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

        #endregion

        #region Scheduling

        /// <summary>
        /// Update the cached value for nextUpdate by finding the smallest value in the scheduler.
        /// </summary>
        private void SchedulerCacheNextTime()
        {
            _nextUpdate = HeadlinesUtil.GetUT();

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
        /// <param name="registeredStateIdentity">State as registered</param>
        /// <param name="baseTime">Depending on the state itself.</param>
        public void ReScheduleHMM(string registeredStateIdentity, double baseTime)
        {
            double deltaTime = GeneratePeriod(_liveProcesses[registeredStateIdentity].period, registeredStateIdentity.Contains("_decay"));
           
            _hmmScheduler[registeredStateIdentity] = HeadlinesUtil.GetUT() + deltaTime;
            Debug( $"Rescheduling HMM {registeredStateIdentity} to +{deltaTime}", "HMM");

            // Punt injury inactivation into the future
            if (registeredStateIdentity.Contains("kerbal_injured"))
            {
                _peopleManager.GetFile(_liveProcesses[registeredStateIdentity].kerbalName).IncurInjury(deltaTime);
            }
        }

        /// <summary>
        /// Scans the scheduler and fire everything site to trigger before the current time.
        /// </summary>
        /// <param name="currentTime">Passed on from GetUT() from a previous calls</param>
        private void SchedulerUpdate(double currentTime)
        {
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
                // Check for validity as it *may* have been removed recently
                if (_liveProcesses.ContainsKey(registeredStateName) == false)
                {
                    Debug($"{registeredStateName} schedule, but doesn't exist");
                    continue;
                }

                // HMM emission call
                emittedEvent = _liveProcesses[registeredStateName].Emission();
                Debug( $"HMM {registeredStateName} fires {emittedEvent}", "HMM");

                if (emittedEvent != "")
                {
                    if (_liveProcesses[registeredStateName].kerbalName != "")
                    {
                        PersonnelFile personnelFile =
                            _peopleManager.GetFile(_liveProcesses[registeredStateName].kerbalName);
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
                
                _liveProcesses[registeredStateName].PrintHMM();
                // HMM transition determination
                nextTransitionState = _liveProcesses[registeredStateName].Transition();
                Debug( $"HMM {registeredStateName} transitions to {nextTransitionState}", "HMM");

                if (nextTransitionState != _liveProcesses[registeredStateName].TemplateStateName())
                {
                    TransitionHMM(registeredStateName, nextTransitionState);
                }
                else
                {
                    ReScheduleHMM(registeredStateName, _liveProcesses[registeredStateName].period);
                }
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
            Debug( $"[Emission] {eventName} at time {KSPUtil.PrintDate(GetUT(), true, false)}");

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
                    RealityCheck();
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

            if (emitData.OngoingTask() == true)
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
        public void FileHeadline(NewsStory newsStory)
        {
            if (!logDebug && newsStory.scope == HeadlineScope.DEBUG) return;
            
            HeadlinesUtil.Report(newsStory, notificationThreshold);
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
            programHype *= (float)power;

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
                ns.headline = "Space craze is low";
                ns.AddToStory(em.GenerateStory());
                FileHeadline(ns);
            }
        }

        /// <summary>
        /// Adjust hype in either direction. Hype cannot go under 0 as there is no such thing
        /// as negative hype.
        /// </summary>
        /// <param name="increment">(float)the number of increment unit to apply.</param>
        public void AdjustHype(float increment)
        {
            // Simplistic model ignoring reasonable targets
            this.programHype += increment;
            this.programHype = Math.Max(0f, this.programHype);

            Debug( $"Hype on the program changed by {increment} to now be {this.programHype}.");
            if (increment > 0)
            {
                PrintScreen($"Space craze is heating up in the press.");
            }
            else
            {
                PrintScreen($"Space craze is cooling down.");
            }

            PrintScreen($"Program Hype: {string.Format("{0:0}", this.programHype)}");

            if (programHype + Reputation.CurrentRep > programHighestValuation)
            {
                programHighestValuation = programHype + Reputation.CurrentRep;
            }

        }

        /// <summary>
        /// Degrades reputation of the program. 
        /// </summary>
        public void DecayReputation()
        {
            Reputation repInterface = Reputation.Instance;

            // Get current reputation
            double currentReputation = repInterface.reputation;

            // Get the program's reputation floor
            double programProfile = _peopleManager.ProgramProfile();

            // margin over profile (losable reputation)
            double marginOverProfile = Math.Max(0, currentReputation - programProfile);

            // reputation tends to program profile if too low.
            if (marginOverProfile == 0f)
            {
                repInterface.AddReputation((float) (0.5 * (programProfile - currentReputation)),
                    TransactionReasons.None);
                Debug( $"Reputation increased by {0.5 * (programProfile - currentReputation)} to approach program profile.");
                return;
            }

            // Calculate the magic loss 0.9330 (1/2 life of 10 iterations, or baseline 1 year of doing nothing)
            double decayReputation = marginOverProfile * (1 - 0.933);

            if (storytellerRand.NextDouble() <= (marginOverProfile / 100))
            {
                repInterface.AddReputation((float) (-1 * decayReputation), TransactionReasons.None);
                PrintScreen(
                    $"{(int) decayReputation} reputation decayed. New total: {(int) repInterface.reputation}.");
            }
        }

        /// <summary>
        /// Public reevaluation of the hype around a program versus its actual reputation, and a proportional correction.
        /// </summary>
        public void RealityCheck(bool withStory = true)
        {
            // No check if there is no hype
            if (programHype == 0) return;
            
            Reputation repInstance = Reputation.Instance;

            if (repInstance.reputation > 0)
            {
                float overrating = (this.programHype + repInstance.reputation) / repInstance.reputation;
                this.programHype /= overrating;

                if (withStory)
                {
                    Emissions em = new Emissions("reality_check");
                    em.Add("delta", ((int) this.programHype).ToString());
                    FileHeadline(new NewsStory(em, Headline:"Hype deflates", generateStory:true));
                }
            }
            else
            {
                // Edge case where there is no reputation (start game), be nice
                programHype *= 0.9f;
            }

        }

        #region Death

        public void InquiryDamningReport()
        {
            Emissions em = new Emissions("damning_report");
            FileHeadline(new NewsStory(em, Headline: "Public inquiry: damning report", generateStory:true));
            
            if (storytellerRand.NextDouble() < 0.5 | programHype == 0) DecayReputation();
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
            emission.Add("name", pf.DisplayName());
            emission.Add("effectiveness", _peopleManager.QualitativeEffectiveness(pf.Effectiveness()));
            emission.Add("specialty", pf.Specialty());
            ns.AddToStory(emission.GenerateStory());
            if (pf.personality != "")
            {
                ns.AddToStory($"During the interview, the candidate proved to be {pf.personality}.");
            }
            ns.headline = $"New {_peopleManager.QualitativeEffectiveness(pf.Effectiveness())} applicant";
            FileHeadline(ns);
            
            if (_peopleManager.ShouldNotify(pf.Specialty()))
            {
                TimeWarp.SetRate(1,false);
            }
        }

        /// <summary>
        /// Random select then delete of an applicant.
        /// </summary>
        /// <remarks>This probably should be split into two: select random applicant, delete applicant.</remarks>
        /// todo Split the two procedure into two methods.
        public void WithdrawRandomApplication()
        {
            if (_peopleManager.applicantFolders.Count > 0)
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
                em.Add("name", dropOut.DisplayName()); 
                
                NewsStory ns = new NewsStory(em, Headline:"Withdrawn application");
                ns.AddToStory(em.GenerateStory());
                FileHeadline(ns);
                
                _peopleManager.RemoveKerbal(dropOut);
            }
        }

        public void EventContractCompleted(Contract contract)
        {

        }

        public void EventContractAccepted(Contract contract)
        {

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
            programHype += 5;
            DebrisResolve();
        }

        /// <summary>
        /// End the sequence of events related to debris on land.
        /// </summary>
        private void DebrisConclude()
        {
            string stateToRemove = "";
            foreach (KeyValuePair<string, HiddenState> kvp in _liveProcesses)
            {
                if (kvp.Key.StartsWith("debris_endangerment"))
                {
                    stateToRemove = kvp.Key;
                    break;
                }
            }

            if (stateToRemove != "")
            {
                RemoveHMM(stateToRemove);
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
        

        #endregion

        #region GUIDisplay

        /// <summary>
        /// String to represent the valuation qualitatively, and quantitatively.
        /// </summary>
        /// <returns></returns>
        public string GUIValuation()
        {
            double reputation = GetValuation();
            string output = StoryEngine.renownLevels[GetValuationLevel()];
            return output + $" ({(int) Math.Floor(reputation)})";
        }

        /// <summary>
        /// String to represent the relative over-valuation
        /// </summary>
        /// <returns></returns>
        public string GUIOvervaluation()
        {
            float reputation = Reputation.CurrentRep;

            if (reputation + programHype == 0f)
            {
                return "0%";
            }

            return
                $"{(int) Math.Floor(100f * Math.Floor(this.programHype) / Math.Floor(reputation + this.programHype))}%";
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
            double valuation = GetValuation();
            if (valuation > programHighestValuation)
            {
                programHighestValuation = (float) valuation;
            }
            
            // in case of a really bad mishap
            valuation = Math.Max(0f, valuation);

            return $"{Math.Round(100 * (valuation / this.programHighestValuation))}%";
        }

        public double GUIVisitingSciencePercent()
        {
            if (totalScience == 0) return 0;

            double ratio = visitingScienceTally / totalScience;
            ratio *= 100;
            return Math.Round(ratio, 1);
        }

        public double GUIFundraised()
        {
            return Math.Floor(this.fundraisingTally);
        }

        public int GUIVABEnhancement()
        {
            return _peopleManager.KSCImpact("Engineer");

        }

        public int GUIRnDEnhancement()
        {
            return _peopleManager.KSCImpact("Scientist");
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
                headlines = new Queue<NewsStory>(headlines.Where(x => x.timestamp < now - ((int)x.scope * 3 * month)));
                Debug($"News Feed culled to {headlines.Count} elements","News Feed");
            }
            else
            {
                Debug($"News Feed at {headlines.Count} elements","News Feed");
            }
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
            return this.programLastKnownReputation + this.programHype;
        }

        public void UpdatePeakValuation()
        {
            if (GetValuation() > programHighestValuation)
            {
                programHighestValuation = (float)GetValuation();
            }
        }

        public double GetReputation()
        {
            return programLastKnownReputation;
        }

        public int GetValuationLevel()
        {
            double valuation = GetValuation();

            if (valuation <= 50) return 0;
            if (valuation <= 150) return 1;
            if (valuation <= 350) return 2;
            if (valuation <= 600) return 3;
            return 4;

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

            double cost = 2000 * (double)(GetValuationLevel() + 1);

            int generationLevel = GetValuationLevel();
            if (headhunt)
            {
                generationLevel += 2;
                cost *= 5;
            }

            if (!nocost)
            {
                AdjustFunds(-1 * cost);
            }
            
            generationLevel = Math.Min(5, generationLevel);
            
            int poolSize = 4 +
                           (int) Math.Round((double) generationLevel * (2 * GetValuation() / programHighestValuation + 1)) +
                           storytellerRand.Next(-1, 2);
            while (poolSize > 0)
            {
                _peopleManager.GenerateRandomApplicant(generationLevel);
                poolSize--;
            }
        }

        public void InvitePress(bool invite)
        {
            if (!mediaSpotlight & invite)
            {
                mediaSpotlight = true;
                endSpotlight = HeadlinesUtil.GetUT() + (3600*24*2);
                wageredReputation = Reputation.CurrentRep + programHype;
                Debug( $"Media Spotlight started with end at {KSPUtil.PrintDateCompact(endSpotlight,true,true)}");
            }
        }

        /// <summary>
        /// If time expires and the target isn't met, deduce the shortcoming from actual reputation and zero hype.
        /// </summary>
        public void EndMediaSpotlight()
        {
            if (!mediaSpotlight)
            {
                return;
            }

            if (endSpotlight < HeadlinesUtil.GetUT())
            {
                Debug( $"Media invite expires {wageredReputation} wagered, {Reputation.CurrentRep} actual");
                double shortcoming = wageredReputation - Reputation.CurrentRep;
                if (shortcoming > 0)
                {
                    shortcoming *= -1;
                    Reputation.Instance.AddReputation((float)shortcoming, TransactionReasons.None);
                    programHype = 0;
                    wageredReputation = 0;
                    HeadlinesUtil.Report(3,$"The media crews are leaving disappointed. (Rep: {Math.Round(shortcoming,2)})", "Media debrief: failure");
                }
                else
                {
                    wageredReputation = 0;
                    programHype *= 2;
                    HeadlinesUtil.Report(3,$"The media crews are leaving impressed. (Hype: {programHype})", "Media Debrief: success");
                }

                mediaSpotlight = false;
            }
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
        
        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Expansions.Missions.Editor;
using FinePrint;
using HiddenMarkovProcess;
using RPStoryteller.source;
using RPStoryteller.source.Emissions;
using UnityEngine;


namespace RPStoryteller
{
    public enum ImpactType
    {
        NEGATIVE,
        NONE,
        TRANSIENT,
        LASTING
    }

    public enum SkillCheckOutcome
    {
        FUMBLE, FAILURE, SUCCESS, CRITICAL
    }
    
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames, GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class StoryEngine : ScenarioModule
    {
        #region Declarations
        public static StoryEngine Instance { get; private set; }
        
        // Random number generator
        private static System.Random storytellerRand = new System.Random();
        
        // Mod data structures
        private PeopleManager _peopleManager;

        // HMM data structures and parameters
        private Dictionary<string, HiddenState> _liveProcesses = new Dictionary<string, HiddenState>();
        private Dictionary<string, double> _hmmScheduler = new Dictionary<string, double>();

        // Cached value for the next trigger so the Scheduler doesn't have to scan constantly
        private double _nextUpdate = -1;
        
        // Master switch of the mod's tempo in second. Should be 36 days for a real run. 
        private double _assumedPeriod = 60;
        //private double _assumedPeriod = 3600 * 24 * 36;
        
        // Multiplier to the _assumedPeriod when it comes to HMM triggering
        [KSPField(isPersistant = true)] public double attentionSpanFactor = 1;
        
        // Maximum earning in repuation in a single increase call.
        [KSPField(isPersistant = true)] public float programHype = 1;
        
        // Cache of the last time we manipulated repuation
        [KSPField(isPersistant = true)] public float programLastKnownReputation = 0;
        
        // Antagonized potential capital campaign donors
        [KSPField(isPersistant = true)] public bool fundraisingBlackout = false;
        
        // Visiting scholars
        [KSPField(isPersistant = true)] public bool visitingScholar = false;
        [KSPField(isPersistant = true)] public float programLastKnownScience = 0;
        
        #endregion
        
        #region UnityStuff
        
        /// <summary>
        /// Unity method with some basics stuff that needs to run once inside a the scene.
        /// </summary>
        public void Start()
        {
            HeadlinesUtil.Report(1,"Initializing Starstruck");
            
            // Default HMM
            InitializeHMM("space_craze");
            InitializeHMM("reputation_decay");

            InitializePeopleManager();
            
            SchedulerCacheNextTime();
            
            // Event Catching
            GameEvents.OnReputationChanged.Add(ReputationChanged);
            GameEvents.OnScienceChanged.Add(ScienceChanged);
        }
        
        /// <summary>
        /// Heartbeat of the Starstruck mod. Using a cached _nextupdate so as to
        /// avoid checking the collection's values all the time.
        /// </summary>
        public void Update()
        {
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
            return HeadlinesUtil.GetUT();
        }

        /// <summary>
        /// Highjacks all increase in reputation to ensure that they are capped at the program hype level.
        /// </summary>
        /// <param name="newReputation">The new, unmodified reputation</param>
        /// <param name="reason">Transactionreason item</param>
        private void ReputationChanged(float newReputation, TransactionReasons reason)
        {
            float deltaReputation = newReputation - this.programLastKnownReputation;

            // Avoid processing recursively the adjustment
            if (reason == TransactionReasons.None)
            {
                this.programLastKnownReputation = newReputation;
                return;
            }
            
            HeadlinesUtil.Report(1,$"New delta: {deltaReputation} whilst hype is {this.programHype}.");
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
                HeadlinesUtil.Report(1,$"Reputation was capped at {this.programHype} due to insufficient hype.");
                HeadlinesUtil.ScreenMessage($"Underrated! Your achievement's impact is limited.\n({(this.programHype/deltaReputation).ToString("P1")})");
                
                // Surplus reputation goes as additional hype
                this.programHype = deltaReputation - this.programHype;
            }
            HeadlinesUtil.Report(1,$"Program hype is now {this.programHype}.");
            
        }

        /// <summary>
        /// Can do so much more interesting things than this, but this is a stub where visiting scholars are boosting science
        /// output of experiments by 20%. Maybe a visiting scholar could be a "tourist" or a Scientist.
        /// </summary>
        /// <param name="newScience"></param>
        /// <param name="reason"></param>
        private void ScienceChanged(float newScience, TransactionReasons reason)
        {
            if (this.visitingScholar)
            {
                float deltaScience = (newScience - this.programLastKnownScience) * 0.2f;
                ResearchAndDevelopment.Instance.CheatAddScience(deltaScience);
                this.visitingScholar = false;
            }

            this.programLastKnownScience = newScience;
        }
        #endregion

        #region kerbals

        /// <summary>
        /// Register all kerbal HMM into event generation structures.
        /// </summary>
        private void InitializePeopleManager()
        {
            _peopleManager = new PeopleManager();
            _peopleManager.RefreshPersonnelFolder();

            foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.personnelFolders)
            {
                // Productivity 
                InitializeHMM("kerbal_" + kvp.Value.kerbalProductiveState, kerbalName: kvp.Value.UniqueName());

                // Task
                InitializeHMM("role_" + kvp.Value.Specialty(), kerbalName: kvp.Value.UniqueName());
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
            HeadlinesUtil.Report(1, $"Failed attempt to fetch productive state of {kerbalFile.DisplayName()}.");
            return "";
        }
        /// <summary>
        /// Determines the outcome of a check based on a mashup of Pendragon and VOID.
        /// </summary>
        /// <param name="skillLevel">0+ arbitrary unit</param>
        /// <param name="difficulty">0+ arbitrary unit</param>
        /// <returns>FUMBLE|FAILURE|SUCCESS|CRITICAL</returns>
        public static SkillCheckOutcome SkillCheck(int skillLevel, int difficulty = 0)
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
        /// Kerbal staff will alter the state of the KSC or have an effect on a media_blitz.
        /// </summary>
        /// <param name="kerbalFile">the actor</param>
        /// <param name="emitData">the event</param>
        /// <param name="legacyMode">legacy impact possible</param>
        /// <param name="isMedia">Indicate a media task for non-pilot</param>
        public void KerbalImpact(PersonnelFile kerbalFile, Emissions emitData, bool legacyMode = false, bool isMedia = false)
        {
            // Check for valid activities
            List<string> validTask = new List<string>() { "media_blitz", "accelerate_research", "accelerate_assembly" };
            if (validTask.Contains(kerbalFile.kerbalTask) == false)
            {
                return;
            }
            
            SkillCheckOutcome successLevel = SkillCheck(kerbalFile.Effectiveness(isMedia));
            if (successLevel == SkillCheckOutcome.FAILURE)
            {
                return;
            }
            
            ImpactType impactType = ImpactType.TRANSIENT;
            switch (successLevel)
            {
                case SkillCheckOutcome.FUMBLE:
                    impactType = ImpactType.NEGATIVE;
                    break;
                case SkillCheckOutcome.CRITICAL:
                    impactType = ImpactType.LASTING;
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
                    multiplier *= -1;
                    break;
                case ImpactType.LASTING:
                    multiplier *= 2;
                    break;
            }

            string adjective = "";
            int effectiveness = kerbalFile.Effectiveness();
            if (effectiveness == 0)
            {
                adjective = "inneffectively ";
            }
            HeadlinesUtil.Report(2,$"{kerbalFile.DisplayName()} {adjective}in the limelight.");
            AdjustHype( effectiveness * multiplier );
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
            
            // Define the magnitude of the change.
            // TODO Get from RP1 the total number of points in R&D
            int pointsRandD = GetRnDPoints();
            
            // 2% of R&D or 1 point
            int deltaRandD = (int)Math.Ceiling((double)pointsRandD * 0.02);
            if (deltaRandD == 0) return;

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
                        break;
                    case ImpactType.LASTING:
                        kerbalFile.teamInfluence += deltaRandD;
                        message = $"{kerbalFile.DisplayName()}'s team is taking it to the next level.";
                        break;
                    case ImpactType.TRANSIENT:
                        kerbalFile.influence += deltaRandD;
                        message = $"{kerbalFile.DisplayName()} earns their pay at the R&D complex.";
                        break;
                    default:
                        HeadlinesUtil.Report(1, $"This should never happen {impactType}");
                        break;
                }
            }
            
            AdjustRnD(deltaRandD);

            HeadlinesUtil.Report(2, message);
            HeadlinesUtil.Report(1, $"{message} ({deltaRandD})");
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
            
            // Define the magnitude of the change.
            // TODO Get from RP1 the total number of points in R&D
            int pointsVAB = GetVABPoints();
            
            // 2% of R&D or 1 point
            int deltaVAB = (int)Math.Ceiling((double)pointsVAB * 0.02);
            if (deltaVAB == 0) return;
            
            HeadlinesUtil.Report(1,$"Adjustment by {deltaVAB}");

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
                        break;
                    case ImpactType.LASTING:
                        kerbalFile.teamInfluence += deltaVAB;
                        message = $"{kerbalFile.DisplayName()}'s team is taking it to the next level.";
                        break;
                    case ImpactType.TRANSIENT:
                        kerbalFile.influence += deltaVAB;
                        message = $"{kerbalFile.DisplayName()} earns their pay at the VAB.";
                        break;
                    default:
                        HeadlinesUtil.Report(1, $"This should never happen {impactType}");
                        break;
                }
            }

            AdjustVAB(deltaVAB);

            HeadlinesUtil.Report(2, message);
            HeadlinesUtil.Report(1, $"{message} ({deltaVAB})");
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

            SkillCheckOutcome outcome = SkillCheck(5,personnelFile.Effectiveness());

            if (outcome == SkillCheckOutcome.SUCCESS)
            {
                personnelFile.trainingLevel += 1;
                HeadlinesUtil.Report(2,$"{personnelFile.DisplayName()} matures.");
            }
            else if (outcome == SkillCheckOutcome.CRITICAL)
            {
                personnelFile.trainingLevel += 2;
                personnelFile.AdjustDiscontent(-1);
                HeadlinesUtil.Report(2,$"{personnelFile.DisplayName()} has a breakthrough.");
            }
            else if (outcome == SkillCheckOutcome.FUMBLE)
            {
                personnelFile.trainingLevel -= 1;
                personnelFile.AdjustDiscontent(1);
                HeadlinesUtil.Report(2,$"{personnelFile.DisplayName()} goes down a misguided rabbit hole.");
            }

        }

        /// <summary>
        /// Determines whether a Kerbal decides to walk away.
        /// </summary>
        /// <param name="personnelFile"></param>
        /// <param name="emitData"></param>
        public void KerbalConsiderResignation(PersonnelFile personnelFile, Emissions emitData)
        {
            // Assert resignation
            double effectiveness = Math.Floor((double)personnelFile.Effectiveness());
            double peer_effectiveness = Math.Floor( _peopleManager.ProgramAverageEffectiveness());
            int netDiscontent = (int) effectiveness - (int) peer_effectiveness + personnelFile.discontent;
            
            SkillCheckOutcome outcome = SkillCheck(netDiscontent);
            switch (outcome)
            {
                case SkillCheckOutcome.FUMBLE:
                    personnelFile.AdjustDiscontent(-1 * personnelFile.discontent);
                    break;
                case SkillCheckOutcome.FAILURE:
                    personnelFile.AdjustDiscontent(1);
                    break;
                default:
                    KerbalResignation(personnelFile, emitData);
                    return;
            }
            
            HeadlinesUtil.Report(1,$"{personnelFile.DisplayName()}'s discontent is {personnelFile.discontent}.");
        }

        /// <summary>
        /// Two kerbals are discovering new ways to work together than enhances their productivity.
        /// </summary>
        /// <param name="personnelFile">subject triggering the event</param>
        /// <param name="emitData"></param>
        public void KerbalSynergy(PersonnelFile personnelFile, Emissions emitData)
        {
            PersonnelFile collaborator = _peopleManager.GetRandomKerbal(personnelFile);
            if (collaborator == null)
            {
                return;
            }
            
            if (personnelFile.IsFeuding(collaborator))
            {
                if (personnelFile.UnsetFeuding(collaborator))
                {
                    collaborator.UnsetFeuding(personnelFile);
                    HeadlinesUtil.Report(3,$"{personnelFile.DisplayName()} and {collaborator.DisplayName()} have found a way to make peace, somehow.", 
                        "Reconciliation");
                }
            }
            else if (personnelFile.IsCollaborator(collaborator) == false)
            {
                if (personnelFile.SetCollaborator(collaborator))
                {
                    collaborator.SetCollaborator(personnelFile);
                    HeadlinesUtil.Report(3,$"{personnelFile.DisplayName()} and {collaborator.DisplayName()} have entered a new and productive collaboration", 
                        "New collaboration");
                }
            }
        }

        public void KerbalFeud(PersonnelFile personnelFile, Emissions emitData)
        {
            PersonnelFile candidate = _peopleManager.GetRandomKerbal(personnelFile);
            if (candidate == null) return;

            if (personnelFile.SetFeuding(candidate))
            {
                candidate.SetFeuding(personnelFile);
                HeadlinesUtil.Report(3,$"{personnelFile.DisplayName()} and {candidate.DisplayName()} are engaged in a destructive feud.", 
                    "Feud in the KSC");
            }
        }
        
        public void KerbalReconcile(PersonnelFile personnelFile, Emissions emitData)
        {
            PersonnelFile candidate = _peopleManager.GetRandomKerbal(personnelFile, personnelFile.feuds);
            if (candidate == null) return;
            else if (personnelFile.UnsetFeuding(candidate))
            {
                candidate.UnsetFeuding(personnelFile);
                HeadlinesUtil.Report(3,$"{personnelFile.DisplayName()} and {candidate.DisplayName()} have found a way to make peace, somehow.", 
                    "Reconciliation");
            }
        }
        
        /// <summary>
        /// Execute a resignation from the space program
        /// </summary>
        /// <param name="personnelFile">resiging kerbal</param>
        /// <param name="emitData"></param>
        public void KerbalResignation(PersonnelFile personnelFile, Emissions emitData, bool trajedy = false)
        {
            // Message
            if (!trajedy)
            {
                HeadlinesUtil.Report(3,$"{personnelFile.DisplayName()} has resigned to spend more time with their family.",
                    $"{personnelFile.DisplayName()} resigns!");
            }
            else
            {
                HeadlinesUtil.Report(3,$"{personnelFile.DisplayName()} has died of a dumb accident.",
                    $"{personnelFile.DisplayName()} trajedy!");
            }
            
            
            // Remove influence
            CancelInfluence(personnelFile, leaveKSC:true);
            
            // HMMs
            RemoveHMM(personnelFile);
            
            // Make it happen
            _peopleManager.Remove(personnelFile);
        }

        /// <summary>
        /// Helps a peer to gain training with a probability related to the difference in training level. There is a
        /// very low probability that a less trained peer can somehow make a difference.
        /// </summary>
        /// <param name="personnelFile">the actor</param>
        /// <param name="emitData">the event</param>
        public void KerbalMentorPeer(PersonnelFile personnelFile, Emissions emitData)
        {
            List<string> excludeList = new List<string>() { personnelFile.UniqueName() };
            foreach (string feudingbuddy in personnelFile.feuds)
            {
                excludeList.Add(feudingbuddy);
            }

            PersonnelFile peer = _peopleManager.GetRandomKerbal(excludeList);

            if (peer != null)
            {
                int deltaSkill = personnelFile.trainingLevel - peer.trainingLevel;
                string message = "";
                if (deltaSkill < 0) message = "Although unlikely, ";

                SkillCheckOutcome outcome = SkillCheck((int)Math.Max(1,deltaSkill));

                switch (outcome)
                {
                    case SkillCheckOutcome.FUMBLE:
                        message += $"{personnelFile.DisplayName()} introduces superstitious ideas taken up by {peer.DisplayName()}.";
                        peer.trainingLevel -= 1;
                        break;
                    case SkillCheckOutcome.SUCCESS:
                        message += $"{personnelFile.DisplayName()} mentors {peer.DisplayName()}.";
                        peer.trainingLevel += 1;
                        break;
                    case SkillCheckOutcome.CRITICAL:
                        message += $"{personnelFile.DisplayName()} and {peer.DisplayName()} mutually benefits from each other's company.";
                        peer.trainingLevel += 1;
                        personnelFile.trainingLevel += 1;
                        break;
                    default:
                        return;
                }
                
                HeadlinesUtil.Report(3, message, "Mentorship");
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
            if (fundraisingBlackout == true)
            {
                HeadlinesUtil.Report(3,"Relationship mended with potential capital campaign donors. Fundraising is possible again.", "Fundraising again");
                fundraisingBlackout = false;
                return;
            }
            
            SkillCheckOutcome outcome = SkillCheck(personnelFile.Effectiveness());

            double funds = 0;

            string message = $"{personnelFile.DisplayName()} ";
            
            switch (outcome)
            {
                case SkillCheckOutcome.SUCCESS:
                    funds = 20000;
                    break;
                case SkillCheckOutcome.CRITICAL:
                    funds = 100000;
                    break;
                case SkillCheckOutcome.FUMBLE:
                    fundraisingBlackout = true;
                    message += "commits a blunder and offends potential donors. The program enters damage control.";
                    break;
            }

            if (funds > 0)
            {
                message += $"raises ${(int)(funds/1000)}K from a private foundation.";
                Funding.Instance.AddFunds(funds, TransactionReasons.Any);
                HeadlinesUtil.Report(3, message, "Fundraising success");
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
            stupidity = Math.Max(4, stupidity);

            SkillCheckOutcome outcome = SkillCheck((int) stupidity);
            switch (outcome)
            {
                case SkillCheckOutcome.CRITICAL:
                    KerbalResignation(personnelFile, emitData, trajedy:true);
                    break;
                case SkillCheckOutcome.SUCCESS:
                    HeadlinesUtil.Report(3,$"{personnelFile.DisplayName()} is injured in a dumb accident. They will be off productive work for a few weeks.", $"{personnelFile.DisplayName()} injured.");
                    // TODO inactivate the kerbal
                    TransitionHMM(KerbalStateOf(personnelFile),"kerbal_injured");
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
            // TODO implement Scout Talent
        }
        
        /// <summary>
        /// A visiting scholar allows to do more with the science units that are in the bank. 
        /// </summary>
        /// <param name="personnelFile"></param>
        /// <param name="emitData"></param>
        public void KerbalBringScholar(PersonnelFile personnelFile, Emissions emitData)
        {
            this.visitingScholar = true;
            HeadlinesUtil.Report(3,$"A visiting scholar brought by {personnelFile.DisplayName()} get clearance to work at the R&D complex.","Visiting scholar");
        }
        #endregion
        
        #region HMM Logic

        /// <summary>
        /// Create a HMM and place it to be triggered at a later time.
        /// </summary>
        /// <param name="registeredStateIdentity">The identifier as registered in the scheduler</param>
        private void InitializeHMM(string registeredStateIdentity, double timestamp = 0, string kerbalName = "")
        {
            // TODO clean up the mess here when confirmed to be unnecessary 
            string templateStateIdentity = registeredStateIdentity;
            
            // Split template and kerbal parts when initialized from a save node
            int splitter = registeredStateIdentity.IndexOf("@");
            if (splitter != -1)
            {
                templateStateIdentity = registeredStateIdentity.Substring(splitter + 1);
                kerbalName = registeredStateIdentity.Substring(0, splitter);
            }
            
            HiddenState newState = new HiddenState(templateStateIdentity, kerbalName);

            // Avoid duplications
            if (_liveProcesses.ContainsKey(newState.RegisteredName()) == false)
            {
                _liveProcesses.Add(newState.RegisteredName(), newState);
            }
            
            timestamp = timestamp != 0 ? timestamp : HeadlinesUtil.GetUT() + GeneratePeriod(newState.period);
            
            if (_hmmScheduler.ContainsKey(newState.RegisteredName()) == false)
            {
                _hmmScheduler.Add(newState.RegisteredName(), timestamp);
            }
            else
            {
                _hmmScheduler[newState.RegisteredName()] = timestamp;
            }
            
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
        /// Removes HMMs associated with a specific file
        /// </summary>
        /// <param name="personnelFile">the file of a crew member</param>
        private void RemoveHMM(PersonnelFile personnelFile)
        {
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

            InitializeHMM(templateFinalState, kerbalName:fetchedKerbalName);
            RemoveHMM(registeredInitialState);

            if (fetchedKerbalName != "")
            {
                UpdateProductiveStateOf(fetchedKerbalName, templateFinalState);
            }
        }

        /// <summary>
        /// Generate a nearly normal random number of days for a HMM triggering event. Box Muller transform
        /// taken from https://stackoverflow.com/questions/218060/random-gaussian-variables
        /// Guarantees that the period is at least 1/20 of the baseValue.
        /// Assumes a standard deviation of 1/3 of the mean.
        /// </summary>
        /// <param name="meanValue">The base value specified in the config node of a HMM</param>
        /// <returns></returns>
        private double GeneratePeriod(double meanValue)
        {
            double stdDev = meanValue / 3;
            double u1 = 1.0- storytellerRand.NextDouble(); 
            double u2 = 1.0- storytellerRand.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                                   Math.Sin(2.0 * Math.PI * u2);
            double returnedVal = meanValue + stdDev * randStdNormal;
            double floorVal = meanValue / 20;
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
            double deltaTime = GeneratePeriod(_liveProcesses[registeredStateIdentity].period);
            _hmmScheduler[registeredStateIdentity] = HeadlinesUtil.GetUT() + deltaTime;
            
            // Punt injury inactivation into the future
            if (registeredStateIdentity.Contains("kerbal_injured"))
            {
                _peopleManager.GetFile( _liveProcesses[registeredStateIdentity].kerbalName ).IncurInjury(deltaTime);
            }
        }

        /// <summary>
        /// Scans the scheduler and fire everything site to trigger before the current time.
        /// </summary>
        /// <param name="currentTime">Passed on from GetUT() from a previous calls</param>
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
            
            foreach (string registeredStateName in triggerStates)
            {
                // Check for validity as it *may* have been removed recently
                if (_liveProcesses.ContainsKey(registeredStateName) == false) continue;
                
                // HMM emission call
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
            HeadlinesUtil.Report(1,$"[Emission] {eventName} at time { KSPUtil.PrintDate(GetUT(), true, false) }");

            Emissions emitData = new Emissions(eventName);

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
                case "decay_reputation":
                    DecayReputation();
                    break;
                case "reality_check":
                    RealityCheck();
                    break;
                default:
                    HeadlinesUtil.Report(1,$"[Emission] {eventName} is not implemented yet.");
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
                    KerbalImpact(personnelFile, emitData);
                    break;
                case "legacy_impact":
                    KerbalImpact(personnelFile, emitData, true);
                    break;
                case "accelerate_research":
                    KerbalAccelerate(personnelFile, emitData);
                    break;
                case "accelerate_assembly":
                    KerbalAccelerate(personnelFile, emitData);
                    break;
                case "media_blitz":
                    CancelInfluence(personnelFile);
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
                    break;
                case "synergy":
                    KerbalSynergy(personnelFile, emitData);
                    break;
                case "feud":
                    KerbalFeud(personnelFile, emitData);
                    break;
                case "reconcile":
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
                default:
                    HeadlinesUtil.Report(1,$"[Emission] Event {eventName} is not implemented yet.");
                    break;
            }
        }

        /// <summary>
        /// Adjust the time it takes to trigger Reputation Decay. The golden ratio here is about just right
        /// for the purpose. This attention span doesn't not affect the current state we're in, but *may*
        /// affect the next iteration. 
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
            attentionSpanFactor = Math.Max(Math.Pow(power, -5), attentionSpanFactor); // min is 0.47
            attentionSpanFactor = Math.Min(Math.Pow(power, 3), attentionSpanFactor); // max is 1.56
            
            // Let player know without spamming the screen
            if (increment > 0 && attentionSpanFactor > 1.61)
            {
                HeadlinesUtil.ScreenMessage($"People understand that space exploration takes time.");
            }
            else if (attentionSpanFactor <= 1)
            {
                HeadlinesUtil.ScreenMessage($"Public grow impatient. Act soon!");
            }
        }

        /// <summary>
        /// Adjust hype in either direction in increments of 5. Cannot go under 0 as there is no such thing
        /// as negative hype.
        /// </summary>
        /// <param name="increment">(float)the number of increment unit to apply.</param>
        public void AdjustHype(float increment)
        {
            // Simplistic model ignoring reasonable targets
            this.programHype += increment * 5f;
            this.programHype = Math.Max(0f, this.programHype);
            
            HeadlinesUtil.Report(1,$"Hype on the program changed by {increment*5f} to now be {this.programHype}.");
            if (increment > 0)
            {
                HeadlinesUtil.ScreenMessage($"Space craze is heating up in the press.");
            }
            else
            {
                HeadlinesUtil.ScreenMessage($"Space craze is cooling down.");
            }
            HeadlinesUtil.ScreenMessage($"Program Hype: {string.Format("{0:0}", this.programHype)}");
            
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

            // Calculate the magic loss 0.9330 (1/2 life of 10 iterations, or baseline 1 year of doing nothing)
            double decayReputation = marginOverProfile * (1 - 0.933);

            if (storytellerRand.NextDouble() <= (marginOverProfile / 100))
            {
                repInterface.AddReputation((float)(-1 * decayReputation), TransactionReasons.None);
                HeadlinesUtil.Report(2,$"{(int)decayReputation} reputation lost. New total: {(int)repInterface.reputation}.");
            }
        }

        /// <summary>
        /// Public reevaluation of the hype around a program versus its actual reputation, and a proportional correction.
        /// </summary>
        public void RealityCheck()
        {
            Reputation repInstance = Reputation.Instance;

            if (repInstance.reputation > 0)
            {
                float overrating = (this.programHype + repInstance.reputation) / repInstance.reputation;
                this.programHype /= overrating;
            
                HeadlinesUtil.Report(2,$"Public hype correction over your program ({(int)this.programHype}).");
            }
            
        }
        
        #endregion

        #region RP1

        /// <summary>
        /// Connect to RP1 to obtain points in R&D
        /// </summary>
        /// <returns></returns>
        public int GetRnDPoints()
        {
            return 50;
        }
        
        /// <summary>
        /// Connect to RP1 to obtain points in R&D
        /// </summary>
        /// <returns></returns>
        public int GetVABPoints()
        {
            return 50;
        }
        
        /// <summary>
        /// Change the tally of points in the R&D.
        /// </summary>
        /// <param name="deltaPoint">number of point to change</param>
        public void AdjustRnD(int deltaPoint)
        {
            HeadlinesUtil.Report(1,$"Adjust R&D points by {deltaPoint}.");
        }
        
        /// <summary>
        /// Change the tally of points in the VAB.
        /// </summary>
        /// <param name="deltaPoint">number of point to change</param>
        public void AdjustVAB(int deltaPoint)
        {
            HeadlinesUtil.Report(1,$"Adjust VAB points by {deltaPoint}.");
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

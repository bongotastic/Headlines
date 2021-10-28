using System;
using System.Collections.Generic;
using CommNet.Network;
using Expansions.Missions;
using HiddenMarkovProcess;
using Renamer;
using RP0.Crew;
using Smooth.Collections;

namespace Headlines.source
{
    /// <summary>
    /// Four levels of control over the program
    /// </summary>
    public enum ProgramControlLevel { CHAOS, WEAK, NOMINAL, HIGH}

    /// <summary>
    /// Possible priority management states
    /// </summary>
    public enum ProgramPriority { NONE, REPUTATION, PRODUCTION, CAPACITY }
    
    /// <summary>
    /// Data structure to store and serialize the data related to program managers. Since not all PM are KSP crew members, it is best to keep this
    /// data separate.
    /// </summary>
    public class ProgramManagerRecord
    {
        public string name, background, personality;
        public int launches = 0;
        public double managerSkill;
        public bool isNPC = true;
        public double lastSeenRetirementDate = 0;
        public double remainingLaunches = 30;
        public double initialCredibility = 0;
        public bool reactionRecorded = false;
        public double timeOfAppointment = 0;

        public ProgramManagerRecord(string _name = "", string _background = "Neutral", string _personality = "", double initialCred = 0)
        {
            name = _name;
            background = _background;
            managerSkill = 4;
            personality = _personality;
            remainingLaunches = 10 + (double)HeadlinesUtil.Threed6();
            initialCredibility = initialCred;
        }

        public ProgramManagerRecord(PersonnelFile crewMember, double initialCred = 0)
        {
            name = crewMember.UniqueName();
            background = crewMember.Specialty();
            managerSkill = crewMember.Effectiveness(deterministic:true);
            personality = crewMember.personality;
            isNPC = false;
            lastSeenRetirementDate = CrewHandler.Instance.KerbalRetireTimes[crewMember.UniqueName()];
            remainingLaunches = 10 + (double)HeadlinesUtil.Threed6();
            initialCredibility = initialCred;
        }

        public ProgramManagerRecord(ConfigNode node)
        {
            FromConfigNode(node);
        }

        public ConfigNode AsConfigNode()
        {
            ConfigNode output = new ConfigNode("PMRECORD");
    
            output.AddValue("name", name);
            output.AddValue("background", background);
            output.AddValue("personality", personality);
            output.AddValue("launches", launches);
            output.AddValue("managerSkill", managerSkill);
            output.AddValue("isNPC", isNPC);
            output.AddValue("lastSeenRetirementDate" , lastSeenRetirementDate);
            output.AddValue("initialCredibility", initialCredibility);
            output.AddValue("remainingLaunches", remainingLaunches);
            output.AddValue("reactionRecorded", reactionRecorded);
            output.AddValue("timeOfAppointment", timeOfAppointment);
            
            return output;
        }

        public void FromConfigNode(ConfigNode node)
        {
            HeadlinesUtil.SafeString("name", ref name, node);
            HeadlinesUtil.SafeString("background", ref background, node);
            HeadlinesUtil.SafeString("personality", ref personality, node);
            HeadlinesUtil.SafeInt("launches", ref launches, node);
            HeadlinesUtil.SafeDouble("managerSkill", ref managerSkill, node);
            HeadlinesUtil.SafeBool("isNPC", ref isNPC, node);
            HeadlinesUtil.SafeDouble("lastSeenRetirementDate", ref lastSeenRetirementDate, node);
            HeadlinesUtil.SafeDouble("remainingLaunches", ref remainingLaunches, node);
            HeadlinesUtil.SafeDouble("initialCredibility", ref initialCredibility, node);
            HeadlinesUtil.SafeBool("reactionRecorded", ref reactionRecorded, node);
            HeadlinesUtil.SafeDouble("timeOfAppointment", ref timeOfAppointment, node);
            
            // compatibility <-0.8.2
            if (timeOfAppointment == 0)
            {
                timeOfAppointment = HeadlinesUtil.GetUT();
            }
        }
    }
    
    /// <summary>
    /// Routines related to global control of the program. Ths class is about making decision, and automating processes.
    /// </summary>
    public class ProgramManager
    {
        private string managerKey = "";
        public Dictionary<string, ProgramManagerRecord> _record = new Dictionary<string, ProgramManagerRecord>();
        
        int influenceVAB = 0;
        int influenceRnD = 0;

        /// <summary>
        /// The internal state of this program.
        /// </summary>
        private ProgramControlLevel controlLevel = ProgramControlLevel.NOMINAL;

        private ProgramPriority programPriority = ProgramPriority.NONE;
        
        /// <summary>
        /// Instances to work with
        /// </summary>
        private StoryEngine _storyEngine;
        private PeopleManager _peopleManager;

        public ProgramManager()
        {
            KSPLog.print("instanciating a new ProgramManager");
            controlLevel = ProgramControlLevel.WEAK;
            programPriority = ProgramPriority.NONE;
        }

        public void SetStoryEngine(StoryEngine storyEngine)
        {
            _storyEngine = storyEngine;
            _peopleManager = _storyEngine.GetPeopleManager();
        }

        public int GetVABInfluence()
        {
            return influenceVAB;
        }

        public int GetRnDInfluence()
        {
            return influenceRnD;
        }
        
        #region serialization

        public void FromConfigNode(ConfigNode node)
        {
            HeadlinesUtil.Report(1, $"Loading PROGRAMMANAGER");
            HeadlinesUtil.SafeString("managerKey", ref managerKey, node);

            if (node.HasValue("controlLevel"))
            {
                controlLevel = (ProgramControlLevel) int.Parse(node.GetValue("controlLevel"));
            }
            if (node.HasValue("programPriority"))
            {
                programPriority = (ProgramPriority)int.Parse(node.GetValue("programPriority"));
            }

            HeadlinesUtil.SafeInt("influenceVAB", ref influenceVAB, node);
            HeadlinesUtil.SafeInt("influenceRnD", ref influenceRnD, node);

            _record.Clear();
            foreach (ConfigNode pmnode in node.GetNodes("PMRECORD"))
            {
                ProgramManagerRecord pmRecord = new ProgramManagerRecord(pmnode);
                _record.Add(pmRecord.name, pmRecord);
            }
            KSPLog.print("Done reading");

        }

        public ConfigNode AsConfigNode()
        {
            ConfigNode node = new ConfigNode("PROGRAMMANAGER");

            node.AddValue("controlLevel", (int)controlLevel);
            node.AddValue("programPriority", (int)programPriority);
            node.AddValue("managerKey", managerKey);
            node.AddValue("influenceVAB", influenceVAB);
            node.AddValue("influenceRnD", influenceRnD);

            foreach (KeyValuePair<string, ProgramManagerRecord> kvp in _record)
            {
                node.AddNode(kvp.Value.AsConfigNode());
            }
            
            return node;
        }

        #endregion

        #region Priority

        public ProgramPriority GetPriority()
        {
            return programPriority;
        }

        public string GetPriorityAsString()
        {
            switch (programPriority)
            {
                case ProgramPriority.REPUTATION:
                    return "Reputation";
                case ProgramPriority.PRODUCTION:
                    return "Production";
                case ProgramPriority.CAPACITY:
                    return "Growth";
                default:
                    return "Balanced";
            }
        }
        
        

        public void OrderNewPriority(ProgramPriority newPriority)
        {
            if (newPriority == programPriority) return;
            programPriority = newPriority;
        }

        #endregion
        
        #region Control

        /// <summary>
        /// Obtain the internal state related to control level
        /// </summary>
        /// <returns></returns>
        public ProgramControlLevel ControlLevel()
        {
            return controlLevel;
        }

        /// <summary>
        /// Convenience method to get a human readable control level for the UI.
        /// </summary>
        /// <returns></returns>
        public string ControlLevelQualitative()
        {
            switch (controlLevel)
            {
                case ProgramControlLevel.CHAOS:
                    return "In chaos";
                case ProgramControlLevel.WEAK:
                    return "Underperforming";
                case ProgramControlLevel.NOMINAL:
                    return "Nominal";
                case ProgramControlLevel.HIGH:
                    return "Inspired";
            }

            return "";
        }

        /// <summary>
        /// Cancel all influence accrued by the program manager.
        /// </summary>
        public void CancelInfluence()
        {
            _storyEngine.AdjustVAB(-1 * influenceVAB);
            influenceVAB = 0;
            _storyEngine.AdjustRnD(-1 * influenceRnD);
            influenceRnD = 0;
        }

        /// <summary>
        /// Standard impact of a program manager on a program when in control.
        /// </summary>
        public void ApplyInfluence()
        {
            int VAB = 0;
            int RnD = 0;

            // Balancing the KSC
            if (_storyEngine.GetVABPoints() * 2 < _storyEngine.GetRnDPoints())
            {
                VAB += 1;
                RnD -= 1;
            }

            if (_storyEngine.GetVABPoints() > _storyEngine.GetRnDPoints())
            {
                VAB -= 1;
                RnD += 1;
            }
            
            HeadlinesUtil.Report(1, $"Applying PM influence as a {GetProgramManagerRecord().background}","PM");
            switch (GetProgramManagerRecord().background)
            {
                case "Neutral":
                    ApplyInfluence(0 + VAB,0 + RnD);
                    break;
                case "Pilot":
                    ApplyInfluence(2 + VAB, -1 + RnD);
                    break;
                case "Engineer":
                    ApplyInfluence(1 + VAB, 0 + RnD);
                    break;
                case "Scientist":
                    ApplyInfluence(0 + VAB,1 + RnD);
                    break;
            }
        }

        /// <summary>
        /// Generic method to affect influence for the program manager
        /// </summary>
        private void ApplyInfluence(int VAB, int RnD)
        {
            CancelInfluence();
            influenceVAB = VAB * _storyEngine.UpgradeIncrementVAB();
            _storyEngine.AdjustVAB(influenceVAB);

            influenceRnD = RnD * _storyEngine.UpgradeIncrementRnD();
            _storyEngine.AdjustRnD(influenceRnD);
        }
        
        #endregion

        #region Processes

        /// <summary>
        /// Routine to process a new launch
        /// </summary>
        /// <param name="vessel"></param>
        public void RegisterLaunch(Vessel vessel)
        {
            GetProgramManagerRecord().launches++;
        }
        
        /// <summary>
        /// Handles the dispatching based on a Program manager skill check outcome.
        /// </summary>
        /// <param name="outcome"></param>
        public void RegisterProgramCheck(SkillCheckOutcome outcome)
        {
            switch (outcome)
            {
                case SkillCheckOutcome.FUMBLE:
                    RegisterProgramCheckFumble();
                    break;
                case SkillCheckOutcome.FAILURE:
                    RegisterProgramCheckFailure();
                    break;
                case SkillCheckOutcome.SUCCESS:
                    RegisterProgramCheckSuccess();
                    break;
                case SkillCheckOutcome.CRITICAL:
                    RegisterProgramCheckCritical();
                    break;
            }
        }

        private void RegisterProgramCheckFumble()
        {
            CancelInfluence();
            controlLevel = ProgramControlLevel.CHAOS;
        }

        private void RegisterProgramCheckFailure()
        {
            if (controlLevel == ProgramControlLevel.CHAOS) return;
            CancelInfluence();
            controlLevel = ProgramControlLevel.WEAK;
        }

        private void RegisterProgramCheckSuccess()
        {
            if (controlLevel == ProgramControlLevel.CHAOS)
            {
                controlLevel = ProgramControlLevel.WEAK;
                RegisterProgramCheckFailure();
                return;
            }

            if (programPriority == ProgramPriority.CAPACITY)
            {
                _storyEngine.programPayrollRebate =
                    (_storyEngine.programPayrollRebate == 0) ? 1 : _storyEngine.programPayrollRebate;
            }
            CancelInfluence();
            ApplyInfluence();
            controlLevel = ProgramControlLevel.NOMINAL;
        }

        private void RegisterProgramCheckCritical()
        {
            if (controlLevel == ProgramControlLevel.CHAOS)
            {
                RegisterProgramCheckSuccess();
                return;
            }
            ApplyInfluence();
            
            if (programPriority == ProgramPriority.CAPACITY)
            {
                _storyEngine.programPayrollRebate += 1;
            }
            controlLevel = ProgramControlLevel.HIGH;
        }

        #endregion

        #region ManagerRole

        public void AssignProgramManager(string pmName, double initialCred)
        {
            if (!GetProgramManagerRecord().isNPC)
            {
                PersonnelFile oldManager = _peopleManager.GetFile(managerKey);
                oldManager.isProgramManager = false;
            }
            
            managerKey = pmName;
            
            // Remove from flight ops
            if (!GetProgramManagerRecord().isNPC)
            {
                PersonnelFile newManager = _peopleManager.GetFile(managerKey);
                newManager.SetInactive(HeadlinesUtil.GetUT() + (3600*24*30));
            }
            
            // How was the program when they got there
            if (GetProgramManagerRecord().initialCredibility < initialCred)
            {
                GetProgramManagerRecord().initialCredibility = initialCred;
            }
            
            // Length of appointment
            if (GetProgramManagerRecord().remainingLaunches <= 6)
            {
                GetProgramManagerRecord().remainingLaunches += (double)HeadlinesUtil.randomGenerator.Next(1, 7);
            }
            HeadlinesUtil.Report(1, $"Assigning {managerKey} as PM.");

            PerformIntegrityCheckonRecord();
        }

        public void AssignProgramManager(PersonnelFile crew, double initialCred)
        {
            ProgramManagerRecord newRecord;
            if (!_record.ContainsKey(crew.UniqueName()))
            {
                newRecord = new ProgramManagerRecord(crew, initialCred);
                _record.Add(crew.UniqueName(), newRecord);
                HeadlinesUtil.Report(1, $"Adding {newRecord.name} to PM records.");
            }
            AssignProgramManager(crew.UniqueName(), initialCred);
        }
        
        public string ManagerName()
        {
            return GetProgramManagerRecord().name;
        }

        /// <summary>
        /// Compute the skill level of the manager
        /// </summary>
        /// <returns></returns>
        public double ManagerProfile(bool deterministic = false)
        {
            if (_peopleManager == null) _storyEngine.GetPeopleManager();
            
            ProgramManagerRecord pmRecord = GetProgramManagerRecord();

            if (pmRecord == null)
            {
                return 0;
            }
            
            double output = pmRecord.managerSkill;
            
            // Check for post-retirement extension
            if (!pmRecord.isNPC && HeadlinesUtil.GetUT() <= pmRecord.lastSeenRetirementDate)
            {
                    // Get Rid of old default PM
                    RemoveDefaultProgramManager();
                    
                    // Set as NPC
                    pmRecord.isNPC = true;
            }
            

            if (!pmRecord.isNPC)
            {
                PersonnelFile crew = _peopleManager.GetFile(managerKey);
                if (crew == null)
                {
                    HeadlinesUtil.Report(1,"PM not retrieved from PeopleManager.");
                    pmRecord.isNPC = true;
                }
                else
                {
                    output = crew.Effectiveness(deterministic);
                }
            }
            
            if (pmRecord.personality == "inspiring")
            {
                output++;
            }
            if (pmRecord.personality == "bland")
            {
                output--;
            }
            
            if (pmRecord.launches <= 2)
            {
                output--;
            }

            if (pmRecord.launches >= 8)
            {
                output++;
            }

            // Morale due to successes
            double cr =  StoryEngine.Instance._reputationManager.CurrentReputation();
            if (pmRecord.initialCredibility / cr <= 0.8) output--;
            else if (cr - pmRecord.initialCredibility >= 50) output++;

            return output;
        }

        public string ManagerBackground()
        {
            return GetProgramManagerRecord().background;
        }

        public string ManagerPersonality()
        {
            return GetProgramManagerRecord().personality;
        }

        public int ManagerLaunches()
        {
            return GetProgramManagerRecord().launches;
        }

        public double ManagerInitialCredibility()
        {
            return GetProgramManagerRecord().initialCredibility;
        }

        public double ManagerRemainingLaunches()
        {
            return GetProgramManagerRecord().remainingLaunches;
        }
        
        public bool ManagerIsTired()
        {
            return HeadlinesUtil.randomGenerator.NextDouble() <
                    (HeadlinesUtil.GetUT() - GetProgramManagerRecord().timeOfAppointment) / (HeadlinesUtil.OneYear * 3);
        }

        /// <summary>
        /// Retrieves the data structure defined by managerkey. If there is no PM, it creates one.
        /// </summary>
        /// <returns>The PM record of the active PM.</returns>
        private ProgramManagerRecord GetProgramManagerRecord()
        {
            if (_record.Count == 0)
            {
                GenerateDefaultProgramManager();
            }

            if (_record.ContainsKey(managerKey))
            {
                return _record[managerKey];
            }
            else
            {
                HeadlinesUtil.Report(1, $"[PROGRAMMANAGER] PM key not found: {managerKey}");

                return GetDefaultProgramManagerRecord();
            }
            
        }

        /// <summary>
        /// Seek the NPC program manager. If there are none, generate a new one.
        /// </summary>
        public void RevertToDefaultProgramManager()
        {
            // Prevent being stuck with only one PM
            if (_record.Count == 1)
            {
                _record.Clear();
            }
            
            AssignProgramManager(GetDefaultProgramManagerRecord().name, _storyEngine._reputationManager.CurrentReputation());
        }

        public bool CanBeAppointed(string name)
        {
            if (_record.ContainsKey(name))
            {
                if (_record[name].remainingLaunches <= 0) return false;
            }

            return true;
        }

        /// <summary>
        /// Scan _record for the first and only isNPC PM.
        /// </summary>
        /// <returns></returns>
        private ProgramManagerRecord GetDefaultProgramManagerRecord()
        {

            foreach (KeyValuePair<string, ProgramManagerRecord> pmr in _record)
            {
                if (pmr.Value.isNPC) return pmr.Value;
            }
            
            // Something weird is going on
            GenerateDefaultProgramManager();
            return GetDefaultProgramManagerRecord();
        }

        /// <summary>
        /// Random generation of a non-staff PM and add it to _record.
        /// </summary>
        private void GenerateDefaultProgramManager()
        {
            Random rnd = new Random();
            string name = "Leslie Kerman";
            string cultureName = "Unknown";
            ProtoCrewMember.Gender _gender = rnd.Next()%2 == 0 ? ProtoCrewMember.Gender.Female : ProtoCrewMember.Gender.Male;
            
            KerbalRenamer.RandomName(_gender ,ref cultureName, ref name);
            
            string background = new List<string>() {"Neutral", "Pilot", "Engineer", "Scientist"}[rnd.Next(3)];
            
            ProgramManagerRecord pmRecord = new ProgramManagerRecord(name, background, PersonnelFile.GetRandomPersonality());
            if (_storyEngine == null)
            {
                _storyEngine = StoryEngine.Instance;
            }
            pmRecord.managerSkill = _storyEngine.GetProgramComplexity() + 4;
            _record.Add(pmRecord.name, pmRecord);
            AssignProgramManager(pmRecord.name, _storyEngine._reputationManager.CurrentReputation());
        }

        /// <summary>
        /// Removes the default PM unless they are appointed.
        /// </summary>
        public void RemoveDefaultProgramManager()
        {
            string pmName = null;
            int found = 0;
            foreach (KeyValuePair<string, ProgramManagerRecord> kvp in _record)
            {
                if (kvp.Value.isNPC & ManagerName() != kvp.Value.name)
                {
                    pmName = kvp.Key;
                    found += 1;
                }
            }

            if (pmName != null)
            {
                _record.Remove(pmName);
            }
            
            // Just in case that there is more than one
            if (found > 1) RemoveDefaultProgramManager();
        }

        // Ensures that there is only 1 default PM, and remove staff PM that no longer exists.
        public void PerformIntegrityCheckonRecord()
        {
            List<string> NPCs = new List<string>();
            List<string> delPMs = new List<string>();

            foreach (KeyValuePair<string, ProgramManagerRecord> kvp in _record)
            {
                if (kvp.Value.isNPC)
                {
                    NPCs.Add(kvp.Key);
                    if (kvp.Value.remainingLaunches <= 0)
                    {
                        delPMs.Add(kvp.Key);
                    }
                }
                else
                {
                    // Check to see if valid still
                    if (!_peopleManager.personnelFolders.ContainsKey(kvp.Key))
                    {
                        delPMs.Add(kvp.Key);
                    }
                }
            }

            if (NPCs.Count > 1)
            {
                if (GetProgramManagerRecord().isNPC)
                {
                    foreach (string pmName in NPCs)
                    {
                        if (pmName != managerKey)
                        {
                            _record.Remove(pmName);
                        }
                    }
                }
                else
                {
                    while (NPCs.Count > 1)
                    {
                        _record.Remove(NPCs[0]);
                        NPCs.Remove(NPCs[0]);
                    }
                }
            }
            
            foreach (string name in delPMs)
            {
                _record.Remove(name);
            }

            if (!_record.ContainsKey(managerKey))
            {
                RevertToDefaultProgramManager();
            }
        }

        /// <summary>
        /// Ensures that a Program manager from crew can't be assigned to flight operations.
        /// </summary>
        /// <param name="newTime"></param>
        public void InactivatePMasCrewFor(double newTime)
        {
            if (!GetProgramManagerRecord().isNPC)
            {
                _peopleManager.GetFile(GetProgramManagerRecord().name).SetInactive(newTime);
            }
        }

        /// <summary>
        /// Modify the number of launches left.
        /// </summary>
        /// <param name="delta"></param>
        public void AdjustRemainingLaunches(double delta)
        {
            GetProgramManagerRecord().remainingLaunches += delta;
        }

        public void SetInitialReputation(double cred)
        {
            foreach (KeyValuePair<string, ProgramManagerRecord> kvp in _record)
            {
                if (kvp.Value.initialCredibility == 0) kvp.Value.initialCredibility = cred;
            }
        }
        #endregion

        #region HMM modifiers

        public void ModifyEmissionMediaMode(HiddenState hmm, MediaRelationMode mediaMode)
        {
            if (mediaMode == MediaRelationMode.LOWPROFILE)
            {
                hmm.AdjustEmission("media_blitz", 0.2f);
            }
        }

        public void ModifyEmissionProgramManager(HiddenState hmm)
        {
            // todo implement 
        }
        
        /// <summary>
        /// Priority-independent effect that is based on control
        /// </summary>
        /// <param name="hmm"></param>
        public void ModifyEmissionControl(HiddenState hmm)
        {
            if (controlLevel >= ProgramControlLevel.NOMINAL)
            {
                if (GetProgramManagerRecord().personality == "genial")
                {
                    hmm.AdjustEmission("synergy", 1.5f);
                }
            }
            else
            {
                if (GetProgramManagerRecord().personality == "scrapper")
                {
                    hmm.AdjustEmission("feud", 1.5f);
                }
            }
        }
        
        public void ModifyEmissionPriority(HiddenState hmm)
        {
            if (controlLevel >= ProgramControlLevel.NOMINAL)
            {
                if (programPriority == ProgramPriority.REPUTATION)
                {
                    hmm.AdjustEmission("media_blitz", 2f);
                    hmm.AdjustEmission("accelerate_research", 0.5f);
                    hmm.AdjustEmission("accelerate_assembly", 0.5f);
                }
                
                if (programPriority == ProgramPriority.PRODUCTION)
                {
                    hmm.AdjustEmission("accelerate_research", 2f);
                    hmm.AdjustEmission("accelerate_assembly", 2f);
                    hmm.AdjustEmission("media_training", 2f);
                }

                if (programPriority == ProgramPriority.CAPACITY)
                {
                    hmm.AdjustEmission("mentor_peer", 2f);
                    hmm.AdjustEmission("study_leave", 2f);
                    hmm.AdjustEmission("scout_talent", 2f);
                    hmm.AdjustEmission("fundraise", 2f);
                    hmm.AdjustEmission("media_blitz", 0.2f);
                    if (controlLevel == ProgramControlLevel.HIGH)
                    {
                        hmm.AdjustEmission("legacy_impact", 1.5f);
                    }
                }
            }
        }

        #endregion

        #region People

        /// <summary>
        /// Cycle through the staff to react to the newly appointment PM
        /// </summary>
        public void CrewReactToAppointment()
        {
            // react only once
            if (GetProgramManagerRecord().reactionRecorded) return;
            GetProgramManagerRecord().reactionRecorded = true;
            
            int reaction;
            foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.applicantFolders)
            {
                if (kvp.Value.UniqueName() == managerKey) continue;
                
                // People don't like change 
                reaction = (GetProgramManagerRecord().personality == "inspiring") ? 0 : 1;
                
                if (kvp.Value.Specialty() == GetProgramManagerRecord().background)
                {
                    reaction -= 2;
                }

                if (kvp.Value.IsCollaborator(GetProgramManagerRecord().name)) reaction -= 2;
                if (kvp.Value.IsFeuding(GetProgramManagerRecord().name)) reaction += 2;
                
                kvp.Value.AdjustDiscontent(reaction);
            }
        }
        

        #endregion
    }
}

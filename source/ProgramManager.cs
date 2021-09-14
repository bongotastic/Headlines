using System;
using System.Collections.Generic;
using CommNet.Network;
using Expansions.Missions;
using HiddenMarkovProcess;
using Smooth.Collections;

namespace RPStoryteller.source
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

        public ProgramManagerRecord(string _name = "", string _background = "Neutral", string _personality = "")
        {
            name = _name;
            background = _background;
            managerSkill = 4;
            personality = _personality;
        }

        public ProgramManagerRecord(PersonnelFile crewMember)
        {
            name = crewMember.UniqueName();
            background = crewMember.Specialty();
            managerSkill = crewMember.Effectiveness(deterministic:true);
            personality = crewMember.personality;
            isNPC = false;
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
            
            return output;
        }

        public void FromConfigNode(ConfigNode node)
        {
            name = node.GetValue("name");
            background = node.GetValue("background");
            personality = node.GetValue("personality");
            launches = int.Parse(node.GetValue("launches"));
            managerSkill = double.Parse(node.GetValue("managerSkill"));
            isNPC = bool.Parse(node.GetValue("isNPC"));
        }
    }
    
    /// <summary>
    /// Routines related to global control of the program. Ths class is about making decision, and automating processes.
    /// </summary>
    public class ProgramManager
    {
        private string managerKey;
        private Dictionary<string, ProgramManagerRecord> _record = new Dictionary<string, ProgramManagerRecord>();
        
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
            KSPLog.print($"reading {node}");
            controlLevel = (ProgramControlLevel) int.Parse(node.GetValue("controlLevel"));
            programPriority = (ProgramPriority) int.Parse(node.GetValue("programPriority"));
            managerKey = node.GetValue("managerKey");
            
            influenceVAB = int.Parse(node.GetValue("influenceVAB"));
            influenceRnD = int.Parse(node.GetValue("influenceRnD"));

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
            switch (GetProgramManagerRecord().background)
            {
                case "Neutral":
                    ApplyInfluence(0,0);
                    break;
                case "Pilot":
                    ApplyInfluence(2, -1);
                    break;
                case "Engineer":
                    ApplyInfluence(1, 0);
                    break;
                case "Scientist":
                    ApplyInfluence(0,1);
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
            if (controlLevel >= ProgramControlLevel.NOMINAL) return;
            if (controlLevel == ProgramControlLevel.CHAOS)
            {
                controlLevel = ProgramControlLevel.WEAK;
                RegisterProgramCheckFailure();
                return;
            }
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
            controlLevel = ProgramControlLevel.HIGH;
        }

        #endregion

        #region ManagerRole

        public void AssignProgramManager(string pmName)
        {
            managerKey = pmName;
            CrewReactToAppointment();
        }

        public void AssignProgramManager(PersonnelFile crew)
        {
            if (!_record.ContainsKey(crew.UniqueName()))
            {
                ProgramManagerRecord newRecord = new ProgramManagerRecord(crew);
                _record.Add(crew.UniqueName(), newRecord);
            }
            AssignProgramManager(crew.UniqueName());
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
            
            double output = pmRecord.managerSkill;
            
            if (!pmRecord.isNPC)
            {
                PersonnelFile crew = _peopleManager.GetProgramManager();
                output = crew.Effectiveness(deterministic);
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

        private ProgramManagerRecord GetProgramManagerRecord()
        {
            if (_record.Count == 0)
            {
                GenerateDefaultProgramManager();
            }
            return _record[managerKey];
        }

        private void GenerateDefaultProgramManager()
        {
            string name = "Leslie Kerman";
            Random rnd = new Random();
            string background = new List<string>() {"Neutral", "Pilot", "Engineer", "Scientist"}[rnd.Next(3)];
            
            ProgramManagerRecord pmRecord = new ProgramManagerRecord(name, background, PersonnelFile.GetRandomPersonality());
            _record.Add(pmRecord.name, pmRecord);
            AssignProgramManager(pmRecord.name);
        }

        #endregion

        #region HMM modifiers

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
                    hmm.AdjustEmission("scout_talents", 2f);
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
            int reaction;
            foreach (KeyValuePair<string, PersonnelFile> kvp in _peopleManager.applicantFolders)
            {
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
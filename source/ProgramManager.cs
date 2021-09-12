using System;
using System.Collections.Generic;
using CommNet.Network;
using Expansions.Missions;
using Smooth.Collections;

namespace RPStoryteller.source
{
    /// <summary>
    /// Four levels of control over the program
    /// </summary>
    public enum ProgramControlLevel { CHAOS, WEAK, NOMINAL, HIGH}

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
        
        /// <summary>
        /// Instances to work with
        /// </summary>
        private StoryEngine _storyEngine;
        private PeopleManager _peopleManager;

        public ProgramManager()
        {
            KSPLog.print("instanciating a new ProgramManager");
            controlLevel = ProgramControlLevel.WEAK;
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
        
        #region Control

        public ProgramControlLevel ControlLevel()
        {
            return controlLevel;
        }

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

        public void CancelInfluence()
        {
            _storyEngine.AdjustVAB(-1 * influenceVAB);
            influenceVAB = 0;
            _storyEngine.AdjustRnD(-1 * influenceRnD);
            influenceRnD = 0;
        }

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
        /// Pilots are looking for a maximal launch tempo at the detriment to research and development.
        /// </summary>
        public void ApplyInfluence(int VAB, int RnD)
        {
            CancelInfluence();
            influenceVAB = VAB * _storyEngine.UpgradeIncrementVAB();
            _storyEngine.AdjustVAB(influenceVAB);

            influenceRnD = RnD * _storyEngine.UpgradeIncrementRnD();
            _storyEngine.AdjustRnD(influenceRnD);
        }
        
        #endregion

        #region Processes

        public void RegisterLaunch(Vessel vessel)
        {
            GetProgramManagerRecord().launches++;
        }
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
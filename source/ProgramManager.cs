using System;
using System.Collections.Generic;
using CommNet.Network;
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
        public int launches;
        public double managerSkill;
        public bool isNPC = true;

        public ProgramManagerRecord(string _name = "", string _background = "Neutral", string _personality = "")
        {
            name = _name;
            background = _background;
            launches = 0;
            managerSkill = 4;
            personality = _personality;
        }

        public ProgramManagerRecord(PersonnelFile crewMember)
        {
            name = crewMember.UniqueName();
            background = crewMember.Specialty();
            launches = 0;
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

        /// <summary>
        /// The internal state of this program.
        /// </summary>
        private ProgramControlLevel controlLevel = ProgramControlLevel.NOMINAL;
        
        /// <summary>
        /// Instances to work with
        /// </summary>
        private StoryEngine _storyEngine;
        private PeopleManager _peopleManager;

        public ProgramManager(StoryEngine storyEngine, PeopleManager peopleManager)
        {
            _storyEngine = storyEngine;
            _peopleManager = peopleManager;

            controlLevel = ProgramControlLevel.NOMINAL;

            GenerateDefaultProgramManager();
        }

        #region serialization

        public void FromConfigNode(ConfigNode node)
        {
            controlLevel = (ProgramControlLevel) int.Parse(node.GetValue("controlLevel"));
            managerKey = node.GetValue("managerKey");

            _record.Clear();
            foreach (ConfigNode pmnode in node.GetNodes("PMRECORD"))
            {
                ProgramManagerRecord pmRecord = new ProgramManagerRecord(pmnode);
                _record.Add(pmRecord.name, pmRecord);
            }

        }

        public ConfigNode AsConfigNode()
        {
            ConfigNode node = new ConfigNode("PROGRAMMANAGER");

            node.AddValue("controlLevel", (int)controlLevel);
            node.AddValue("managerKey", managerKey);

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

        #endregion

        #region ManagerRole

        public void AssignProgramManager(string pmName)
        {
            managerKey = pmName;
        }
        
        public string ManagerName()
        {
            return managerKey;
        }

        /// <summary>
        /// Compute the skill level of the manager
        /// </summary>
        /// <returns></returns>
        public double ManagerProfile(bool deterministic = false)
        {
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
            return _record[managerKey];
        }

        private void GenerateDefaultProgramManager()
        {
            _record.Clear();
            managerKey = "";

            string name = "Larry Kerman";
            Random rnd = new Random();
            string background = new List<string>() {"Neutral", "Pilot", "Engineer", "Scientist"}[rnd.Next(3)];
            
            ProgramManagerRecord pmRecord = new ProgramManagerRecord(name, background, PersonnelFile.GetRandomPersonality());
            _record.Add(pmRecord.name, pmRecord);
            AssignProgramManager(pmRecord.name);
        }

        #endregion
    }
}
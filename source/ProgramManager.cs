using CommNet.Network;

namespace RPStoryteller.source
{
    /// <summary>
    /// Four levels of control over the program
    /// </summary>
    public enum ProgramControlLevel { CHAOS, WEAK, NOMINAL, HIGH}
    
    /// <summary>
    /// Routines related to global control of the program. Ths class is about making decision, and automating processes.
    /// </summary>
    public class ProgramManager
    {
        private PersonnelFile managerRole;
        private string npcManagerRoleName = "Laura Kerman";
        private int npcManagerProfile = 4;

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

            managerRole = _peopleManager.GetProgramManager();

            controlLevel = ProgramControlLevel.NOMINAL;
        }

        #region serialization

        public void FromConfigNode(ConfigNode node)
        {
            controlLevel = (ProgramControlLevel) int.Parse(node.GetValue("controlLevel"));
            
            if (node.HasValue("ManagerRole"))
            {
                managerRole = _peopleManager.GetFile(node.GetValue("ManagerRole"));
            }
            else
            {
                managerRole = null;
            }
            
        }

        public ConfigNode AsConfigNode()
        {
            ConfigNode node = new ConfigNode("PROGRAMMANAGER");

            node.AddValue("controlLevel", (int)controlLevel);
            
            if (managerRole != null)
            {
                node.AddValue("ManagerRole", managerRole.UniqueName());    
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

        public string ManagerName()
        {
            return (managerRole != null) ? managerRole.DisplayName() : npcManagerRoleName;
        }

        /// <summary>
        /// Compute the skill level of the manager
        /// </summary>
        /// <returns></returns>
        public double ManagerProfile()
        {
            int output = npcManagerProfile;
            
            if (managerRole != null)
            {
                output = managerRole.Effectiveness();
                if (managerRole.HasAttribute("inspiring"))
                {
                    output++;
                }
                if (managerRole.HasAttribute("bland"))
                {
                    output--;
                }
            }

            return output;
        }

        #endregion
    }
}
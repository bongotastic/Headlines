using System;
using System.Collections.Generic;
using CommNet.Network;
using RPStoryteller.source;
using Smooth.Collections;

namespace RPStoryteller
{
    /// <summary>
    /// This class manages the Kerbal interface of the Storyteller mod: a mix of HR and the PR department.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames,
        GameScenes.SPACECENTER)]
    public class RPPeopleManager : ScenarioModule
    {
        // Binds KSP crew and Starstruck data
        public Dictionary<string, PersonnelFile> personnelFolders = new Dictionary<string, PersonnelFile>();

        #region Kitchen Sink

        public RPPeopleManager()
        {
            RefreshPersonnelFolder();
        }
        
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            
            // Save personnel files
            ConfigNode folder = new ConfigNode();
            
            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                folder.AddNode("File", kvp.Value.AsConfigNode());
            }

            node.AddNode("PERSONNELFILES", folder);
            
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            
            // Load personnel files
            ConfigNode folder = node.GetNode("PERSONNELFILES");
            if (folder != null)
            {
                PersonnelFile temporaryFile;
            
                foreach (ConfigNode kerbalFile in folder.GetNodes())
                {
                    temporaryFile = new PersonnelFile(kerbalFile);
                    personnelFolders.Add(temporaryFile.UniqueName(), temporaryFile);
                }
            }
            
        }
        
        #endregion
        
        #region KSP

        /// <summary>
        /// Ensures that all kerbals have a personnel file into RPPeopleManager
        /// </summary>
        public void RefreshPersonnelFolder()
        {
            foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Crew)
            {
                if (personnelFolders.ContainsKey(pcm.name) == false)
                {
                    PersonnelFile newKerbal = new PersonnelFile(pcm);
                    personnelFolders.Add( pcm.name, newKerbal);
                }
            }
        }
        

        #endregion

        #region Logic

        /// <summary>
        /// Fetch one file from the folder.
        /// </summary>
        /// <param name="kerbalName">kerbal name as unique ID</param>
        /// <returns>Instance of the file</returns>
        public PersonnelFile GetFile(string kerbalName)
        {
            if (personnelFolders.ContainsKey(kerbalName)) return personnelFolders[kerbalName];
            return null;
        }

        #endregion
    }

    public class PersonnelFile
    {
        private static System.Random randomNG = new System.Random();
        
        public int trainingLevel = 0;
        
        //public int influence = 0;
        //public int legacy = 0;

        // Store HMM
        public string kerbalProductiveState;
        public string kerbalTask;

        private ProtoCrewMember pcm;
        
        /// <summary>
        /// Constructor used to generate a brand new file from a protocrewember
        /// </summary>
        /// <param name="pcm"></param>
        public PersonnelFile(ProtoCrewMember pcm)
        {
            this.pcm = pcm;
            
            // Default HMM state and task
            this.kerbalProductiveState = "productive";

            switch (pcm.trait)
            {
                case "Pilot":
                    this.kerbalTask = "media_blitz";
                    break;
                case "Scientist":
                    this.kerbalTask = "accelerate_research";
                    break;
                case "Engineer":
                    this.kerbalTask = "accelerate_assembly";
                    break;
                default:
                    this.kerbalTask = "idle";
                    break;
            }
        }

        /// <summary>
        /// COnstructor used when loading from a save file
        /// </summary>
        /// <param name="node"></param>
        public PersonnelFile(ConfigNode node)
        {
            FromConfigNode(node);
        }

        #region Unity stuff

        public void FromConfigNode(ConfigNode node)
        {
            this.kerbalProductiveState = node.GetValue("kerbalState");
            this.kerbalTask = node.GetValue("kerbalTask");
            this.trainingLevel = int.Parse(node.GetValue("trainingLevel"));

            this.pcm = HighLogic.CurrentGame.CrewRoster[node.GetValue("kerbalName")];
        }

        public ConfigNode AsConfigNode()
        {
            ConfigNode outputNode = new ConfigNode();

            outputNode.AddValue("kerbalName", pcm.name);
            outputNode.AddValue("kerbalState", this.kerbalProductiveState);
            outputNode.AddValue("kerbalTask", this.kerbalTask);
            outputNode.AddValue("trainingLevel", this.trainingLevel);
            
            return outputNode;
        }
        

        #endregion

        #region Getter

        /// <summary>
        /// Profile is a key metric in evaluating the impact of a kerbal. People value courage and extremes in stupidity.
        /// </summary>
        /// <returns>Zero-bound value</returns>
        public double Profile()
        {
            double outputProfile = 2 * (pcm.courage * (2 * Math.Abs(0.5 - pcm.stupidity)));

            outputProfile += (double)pcm.experience;
            
            return outputProfile;
        }

        /// <summary>
        /// Computes the skill level of a kerbal. This method is non-deterministic as it treats partial profile as
        /// a probability. 
        /// </summary>
        /// <returns>effectiveness</returns>
        public int Effectiveness()
        {
            int effectiveness = 0;
            
            // Profile and experience with probability for fractional points
            double tempProfile = Profile();
            effectiveness += (int) tempProfile;

            // Treat partial profile point as probabilities
            if (randomNG.NextDouble() <= tempProfile - (double) effectiveness) effectiveness += 1;
            
            // experience Level
            effectiveness += ExperienceProfileIncrements();
            
            // training
            effectiveness += this.trainingLevel;
            
            // slump/inspired
            switch (this.kerbalProductiveState)
            {
               case "kerbal_slump":
                   effectiveness -= 1;
                   break;
               case "kerbal_inspired":
                   effectiveness += 1;
                   break;
            }

            return effectiveness;
        }

        /// <summary>
        /// Create a custom level system to reward early career a bit more, and cap impact on effectiveness in the
        /// upper range.
        /// </summary>
        /// <returns>Starstruck levels</returns>
        private int ExperienceProfileIncrements()
        {
            float xp = pcm.experience;

            if (xp <= 2) return (int) xp;
            else if (xp <= 4)
            {
                return 3;
            }
            else return 4;
        }

        /// <summary>
        /// Get User-readable name from pcm
        /// </summary>
        /// <returns>printable name</returns>
        public string DisplayName()
        {
            return pcm.displayName;
        }

        /// <summary>
        /// Returns the key for a kerbal
        /// </summary>
        /// <returns>unique name</returns>
        public string UniqueName()
        {
            return pcm.name;
        }

        /// <summary>
        /// Returns the trait of a kerbal
        /// </summary>
        /// <returns>Pilot|Engineer|Scientist</returns>
        public string Specialty()
        {
            return pcm.trait;
        }

        #endregion

        #region Setters

        /// <summary>
        /// Keeps track of the current productivity state. 
        /// </summary>
        /// <param name="templateStateIdentity">the template name of the state</param>
        public void EnterNewState(string templateStateIdentity)
        {
            // ignore specialty as it is a given
            if (templateStateIdentity.IndexOf(Specialty()) != -1)
            {
                // Wild assumption that all states begin with kerbal_
                this.kerbalProductiveState = templateStateIdentity.Substring(7);
            }
        }
        
        /// <summary>
        /// Keeps track of the last emitted event in the kerbal's role. 
        /// </summary>
        /// <param name="newTaskName">the template name of the state</param>
        public void TrackCurrentActivity(string newTaskName)
        {
            this.kerbalTask = newTaskName;
        }

        #endregion
        
    }
}
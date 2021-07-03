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
        public Dictionary<string, PersonelFile> personelFolders = new Dictionary<string, PersonelFile>();

        #region Kitchen Sink

        public RPPeopleManager()
        {
            RefreshPersonelFolder();
        }

        public RPPeopleManager GetInstance()
        {
            return this;
        }
        
        #endregion
        
        #region KSP

        /// <summary>
        /// Ensures that all kerbals have a personnel file into RPPeopleManager
        /// </summary>
        public void RefreshPersonelFolder()
        {
            StarStruckUtil.Report(1, $"Inside RefreshPersonnelFolder");
            foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Crew)
            {
                StarStruckUtil.Report(1,$"Considering {pcm.name}");
                if (personelFolders.ContainsKey(pcm.name) == false)
                {
                    PersonelFile newKerbal = new PersonelFile(pcm);
                    personelFolders.Add( pcm.name, newKerbal);
                    StarStruckUtil.Report(1,$"New kerbal: {pcm.displayName} with profile {newKerbal.Profile()}.");
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
        public PersonelFile GetFile(string kerbalName)
        {
            if (personelFolders.ContainsKey(kerbalName)) return personelFolders[kerbalName];
            return null;
        }

        #endregion
    }

    public class PersonelFile
    {
        public string skill_level = "NAIVE";
        
        public int influence = 0;
        public int legacy = 0;

        // Store HMM
        public string kerbalState;
        public string kerbalTask;

        private ProtoCrewMember pcm;
        

        public PersonelFile(ProtoCrewMember pcm)
        {
            this.pcm = pcm;
            
            // Default HMM state and task
            this.kerbalState = "productive";

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

        #region Unity stuff

        public void FromConfigNode(ConfigNode node)
        {
            this.kerbalState = node.GetValue("kerbalState");
            this.kerbalTask = node.GetValue("kerbalTask");

            this.pcm = HighLogic.CurrentGame.CrewRoster[node.GetValue("kerbalName")];
        }

        public ConfigNode AsConfigNode()
        {
            ConfigNode outputNode = new ConfigNode();

            outputNode.AddValue("kerbalName", pcm.name);
            outputNode.AddValue("kerbalState", this.kerbalState);
            outputNode.AddValue("kerbalTask", this.kerbalTask);
            
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

        public string DisplayName()
        {
            return pcm.displayName;
        }

        public string UniqueName()
        {
            return pcm.name;
        }

        public string Specialty()
        {
            return pcm.trait;
        }

        #endregion

        #region Setters

        public void EnterNewState(string newState)
        {
            // ignore specialty as it is a given
            if (newState.IndexOf(Specialty()) != -1)
            {
                // Wild assumption that all states begin with kerbal_
                this.kerbalState = newState.Substring(7);
            }
        }

        #endregion
        
    }
}
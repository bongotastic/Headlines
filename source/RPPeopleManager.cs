using System;
using System.Collections.Generic;
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
        
        public static RPPeopleManager Instance { get; private set; } 
        
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

        public void RefreshPersonelFolder()
        {
            foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Crew)
            {
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

        

        #endregion
    }

    public class PersonelFile
    {
        public string skill_level = "NAIVE";
        
        public int influence = 0;
        public int legacy = 0;

        private ProtoCrewMember pcm;

        public PersonelFile(ProtoCrewMember pcm)
        {
            this.pcm = pcm;
        }

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

    }
}
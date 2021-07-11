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
    public class PeopleManager : ScenarioModule
    {
        private static System.Random randomNG = new System.Random();
        
        // Binds KSP crew and Starstruck data
        public Dictionary<string, PersonnelFile> personnelFolders = new Dictionary<string, PersonnelFile>();

        #region Kitchen Sink

        public PeopleManager()
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
                    if (personnelFolders.ContainsKey(kerbalFile.GetValue("kerbalName")) == false)
                    {
                        temporaryFile = new PersonnelFile(kerbalFile);
                        personnelFolders.Add(temporaryFile.UniqueName(), temporaryFile);
                    }
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

        /// <summary>
        /// Delete the personnel file and remove the pcm from the game.
        /// </summary>
        /// <param name="personnelFile"></param>
        public void Remove(PersonnelFile personnelFile)
        {
            personnelFile.Remove();
            personnelFolders.Remove(personnelFile.UniqueName());
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
            else
            {
                // Possible when loading a save file...
                ProtoCrewMember temppcm = HighLogic.CurrentGame.CrewRoster[kerbalName];
                if (temppcm != null)
                {
                    personnelFolders.Add(kerbalName, new PersonnelFile(temppcm));
                    return personnelFolders[kerbalName];
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the unique name of a random kerbal on staff. Will not return anyone on the exclude list. Meant to be
        /// usable for many purposes. The two parameter should not be used together.
        /// </summary>
        /// <param name="exclude">identifiers of kerbal to exclude from selection</param>
        /// <param name="subset">restrict selection to this subset</param>
        /// <returns>null or a PersonnelFile</returns>
        public PersonnelFile GetRandomKerbal(List<string> exclude = null, List<string> subset = null)
        {
            if (exclude == null) exclude = new List<string>();

            if (subset == null)
            {
                subset = new List<string>();
                foreach (KeyValuePair<string,PersonnelFile> kvp in personnelFolders)
                {
                    if (exclude.Contains(kvp.Key)) continue;
                    if (subset.Contains(kvp.Key) == false) subset.Add(kvp.Key);
                }
            }
            else
            {
                List<string> copySubset = new List<string>(subset);
                foreach (string kerbalName in copySubset)
                {
                    if (exclude.Contains(kerbalName)) subset.Remove(kerbalName);
                }
            }
            
            if (subset.Count == 0) return null;

            return GetFile(subset[randomNG.Next() % subset.Count]);
        }

        /// <summary>
        /// Overloading to simplify when exclude is a single kerbal.
        /// </summary>
        /// <param name="exclude">the personnel file of the kerbal to exclude</param>
        /// <param name="subset">the range of kerbal to pick from</param>
        /// <returns></returns>
        public PersonnelFile GetRandomKerbal(PersonnelFile exclude, List<string> subset = null)
        {
            List<string> excludeme = new List<string>() { exclude.UniqueName() };
            return GetRandomKerbal(excludeme, subset);
        }
        
        
        /// <summary>
        /// Used by soul-searching kerbals wondering if they are being dragged down by their peers.
        /// </summary>
        /// <returns>Average program effectiveness</returns>
        public double ProgramAverageEffectiveness(double exclusion = 0)
        {
            if (personnelFolders.Count > 0)
            {
                return (ProgramProfile() - exclusion) / (double)personnelFolders.Count;
            }

            return 0;
        }
        
        /// <summary>
        /// Sums all staff effectiveness as a program profile extimate.
        /// </summary>
        /// <returns></returns>
        public double ProgramProfile()
        {
            double programProfile = 0;

            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                programProfile += kvp.Value.Effectiveness();
            }

            return programProfile;
        }
        
        

        #endregion
    }

    public class PersonnelFile
    {
        private static System.Random randomNG = new System.Random();
        
        // Getting better through professional development
        public int trainingLevel = 0;
        // Affects the odds of leaving the program
        public int discontent = 1;
        
        // While sustaining (transient)
        public int influence = 0;
        // Passive until retirement
        public int teamInfluence = 0;
        // Will never be removed
        public int legacy = 0;

        // Store HMM
        public string kerbalProductiveState;
        public string kerbalTask;
        
        // relationships
        public List<string> collaborators = new List<string>();
        public List<string> feuds = new List<string>();

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
            this.influence = int.Parse(node.GetValue("influence"));
            this.teamInfluence = int.Parse(node.GetValue("teamInfluence"));
            this.legacy = int.Parse(node.GetValue("legacy"));
            this.discontent = int.Parse(node.GetValue("discontent"));

            ConfigNode people = node.GetNode("people");
            if (people != null)
            {
                foreach (ConfigNode.Value kerbal in people.values)
                {
                    if (kerbal.value == "feud" && feuds.Contains(kerbal.name) == false) feuds.Add(kerbal.name);
                    else if (kerbal.value == "collaborator" && collaborators.Contains(kerbal.name) == false) collaborators.Add(kerbal.name);
                }
            }

            this.pcm = HighLogic.CurrentGame.CrewRoster[node.GetValue("kerbalName")];
        }

        public ConfigNode AsConfigNode()
        {
            ConfigNode outputNode = new ConfigNode();

            outputNode.AddValue("kerbalName", pcm.name);
            outputNode.AddValue("kerbalState", this.kerbalProductiveState);
            outputNode.AddValue("kerbalTask", this.kerbalTask);
            outputNode.AddValue("trainingLevel", this.trainingLevel);
            outputNode.AddValue("influence", this.influence);
            outputNode.AddValue("teamInfluence", this.teamInfluence);
            outputNode.AddValue("legacy", this.legacy);
            outputNode.AddValue("discontent", this.discontent);

            ConfigNode people = new ConfigNode();
            foreach (string kerbalName in collaborators)
            {
                people.AddValue(kerbalName, "collaborator");
            }
            foreach (string kerbalName in feuds)
            {
                people.AddValue(kerbalName, "feud");
            }
            outputNode.AddNode("people", people);
            
            return outputNode;
        }
        

        #endregion

        #region KSP

        /// <summary>
        /// Remove pcm from the game.
        /// </summary>
        public void Remove()
        {
            HighLogic.CurrentGame.CrewRoster.Remove(this.pcm);
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
        /// <param name="isMedia">Indicate a media task when Kerbal is not a pilot</param>
        /// <returns>effectiveness</returns>
        public int Effectiveness(bool isMedia = false)
        {
            int effectiveness = 0;
            
            // Profile and experience with probability for fractional points
            double tempProfile = Profile();
            int wholePartProfile = (int) Math.Floor(tempProfile);
            effectiveness += wholePartProfile;

            // Treat partial profile point as probabilities
            if (randomNG.NextDouble() <= tempProfile - (double) wholePartProfile) effectiveness += 1;
            
            // experience Level (untrained if media for non-pilot
            if (!(isMedia && Specialty() != "Pilot"))
            {
                effectiveness += ExperienceProfileIncrements();
            }

            // training
            effectiveness += this.trainingLevel;
            
            // feuds and collaborations
            int relationships = collaborators.Count - feuds.Count;
            relationships = (int)Math.Min(-2, relationships);
            relationships = (int)Math.Max(2, relationships);
            
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

            return (int)Math.Max(0, effectiveness);
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

        public bool IsCollaborator(PersonnelFile candidate)
        {
            return collaborators.Contains(candidate.UniqueName());
        }

        public bool IsInactive()
        {
            return pcm.inactive;
        }
        
        public bool IsFeuding(PersonnelFile candidate)
        {
            return feuds.Contains(candidate.UniqueName()) ;
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

        /// <summary>
        /// Access stupidity in the pcm object.
        /// </summary>
        /// <returns></returns>
        public double Stupidity()
        {
            return (double) pcm.stupidity;
        }
        #endregion

        #region Setters

        public void UpdateProductiveState(string templateName)
        {
            if (templateName.StartsWith("kerbal_") == true)
            {
                this.kerbalProductiveState = templateName.Substring(7);
            }
        }
        
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
        /// General purpose to add and remove two types of relationships
        /// </summary>
        /// <param name="candidate">the other kerbal</param>
        /// <param name="collaboration">false for feuds</param>
        /// <param name="unset">true to unset</param>
        /// <returns></returns>
        private bool SetRelationship(PersonnelFile candidate, bool collaboration = true, bool unset = false)
        {
            List<string> mycollection;
            List<string> othercollection;
            
            if (collaboration)
            {
                mycollection = collaborators;
                othercollection = feuds;
            }
            else
            {
                mycollection = feuds;
                othercollection = collaborators;
            }
            
            if (unset == false)
            {
                if (mycollection.Contains(candidate.UniqueName()) == false)
                {
                    mycollection.Add(candidate.UniqueName());
                    if (othercollection.Contains((candidate.UniqueName())))
                    {
                        othercollection.Remove(candidate.UniqueName());
                        
                    }
                    return true;
                }
            }
            else
            {
                if (mycollection.Contains(candidate.UniqueName()) == true)
                {
                    mycollection.Remove(candidate.UniqueName());
                    if (othercollection.Contains((candidate.UniqueName())))
                    {
                        othercollection.Remove(candidate.UniqueName());
                        
                    }
                    return true;
                }
            }

            return false;
        }
        
        public bool SetCollaborator(PersonnelFile candidate, bool unset = false)
        {
            return SetRelationship(candidate);
        }

        public bool UnsetCollaborator(PersonnelFile candidate)
        {
            return SetRelationship(candidate, true, false);
        }
        
        /// <summary>
        /// Add/Remove candidate as feuding counterpart.
        /// </summary>
        /// <param name="candidate">the other kerbal</param>
        /// <param name="unset">unset operation</param>
        /// <returns></returns
        public bool SetFeuding(PersonnelFile candidate, bool unset = false)
        {
            return SetRelationship(candidate, false, false);
        }

        /// <summary>
        ///  Cleaner API to unsetting while keeping things DRY.
        /// </summary>
        /// <param name="candidate">the other kerbal</param>
        /// <returns>whether it worked</returns>
        public bool UnsetFeuding(PersonnelFile candidate)
        {
            return SetFeuding(candidate, true);
        }
        
        /// <summary>
        /// Keeps track of the last emitted event in the role_. 
        /// </summary>
        /// <param name="newTaskName">the template name of the state</param>
        public void TrackCurrentActivity(string newTaskName)
        {
            this.kerbalTask = newTaskName;
        }

        /// <summary>
        /// Ensures that discontent is bounded from 0 to 5.
        /// </summary>
        /// <param name="increment"></param>
        public void AdjustDiscontent(int increment)
        {
            this.discontent += increment;
            this.discontent = Math.Max(this.discontent, 0);
            this.discontent = Math.Min(this.discontent, 5);
        }

        public void IncurInjury(double endTime)
        {
            pcm.SetInactive(endTime, false);
        }

        #endregion
        
    }
}
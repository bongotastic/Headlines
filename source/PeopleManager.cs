using System;
using System.Collections.Generic;
using CommNet.Network;
using KerbalConstructionTime;
using Headlines.source;
using Smooth.Collections;
using UniLinq;
using Enumerable = System.Linq.Enumerable;

namespace Headlines
{

    /// <summary>
    /// This class manages the Kerbal interface of the Storyteller mod: a mix of HR and the PR department.
    /// </summary>
    //[KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames, GameScenes.SPACECENTER)]
    [KSPScenario(ScenarioCreationOptions.AddToAllGames,
        new GameScenes[] { GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION })]
    public class PeopleManager : ScenarioModule
    {
        private static System.Random randomNG = new System.Random();

        public static PeopleManager Instance = null;
        public bool initialized = false;

        [KSPField(isPersistant = true)] public int generationLevel = 3;

        // Binds KSP crew and Starstruck data
        public Dictionary<string, PersonnelFile> personnelFolders = new Dictionary<string, PersonnelFile>();

        // Prospective crew members
        public Dictionary<string, PersonnelFile> applicantFolders = new Dictionary<string, PersonnelFile>();

        // Program manager(s)
        public Dictionary<string, PersonnelFile> managerFolders = new Dictionary<string, PersonnelFile>();

        // Job Search
        [KSPField(isPersistant = true)] public bool seekingPilot = false;
        [KSPField(isPersistant = true)] public bool seekingScientist = false;
        [KSPField(isPersistant = true)] public bool seekingEngineer = false;

        #region Kitchen Sink

        public PeopleManager()
        {
            Instance = this;

            GameEvents.OnCrewmemberHired.Add(HiringEventHandler);
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            // Save personnel files
            ConfigNode personnelfolder = new ConfigNode();

            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                personnelfolder.AddNode("File", kvp.Value.AsConfigNode());
            }

            node.AddNode("PERSONNELFILES", personnelfolder);

            ConfigNode appfolders = new ConfigNode();
            foreach (KeyValuePair<string, PersonnelFile> kvp in applicantFolders)
            {
                appfolders.AddNode("File", kvp.Value.AsConfigNode());
            }

            node.AddNode("APPLICANTFILES", appfolders);

            ConfigNode mngrfolders = new ConfigNode();
            foreach (KeyValuePair<string, PersonnelFile> kvp in managerFolders)
            {
                mngrfolders.AddNode("File", kvp.Value.AsConfigNode());
            }

            node.AddNode("MANAGERFILES", mngrfolders);
        }

        public override void OnLoad(ConfigNode node)
        {
            initialized = true;
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
                        AddCrew(temporaryFile);
                    }
                }
            }

            folder = node.GetNode("APPLICANTFILES");
            if (folder != null)
            {
                PersonnelFile temporaryFile;

                foreach (ConfigNode kerbalFile in folder.GetNodes())
                {
                    if (applicantFolders.ContainsKey(kerbalFile.GetValue("kerbalName")) == false)
                    {
                        temporaryFile = new PersonnelFile(kerbalFile);
                        applicantFolders.Add(temporaryFile.UniqueName(), temporaryFile);
                    }
                }
            }

            folder = node.GetNode("MANAGERFILES");
            if (folder != null)
            {
                PersonnelFile temporaryFile;

                foreach (ConfigNode kerbalFile in folder.GetNodes())
                {
                    if (managerFolders.ContainsKey(kerbalFile.GetValue("kerbalName")) == false)
                    {
                        temporaryFile = new PersonnelFile(kerbalFile);
                        managerFolders.Add(temporaryFile.UniqueName(), temporaryFile);
                    }
                }
            }
        }

        public void OnDestroy()
        {
            GameEvents.OnCrewmemberHired.Remove(HiringEventHandler);
        }

        #endregion

        #region KSP

        /// <summary>
        /// Ensures that all kerbals have a personnel file into RPPeopleManager
        /// </summary>
        public void RefreshPersonnelFolder()
        {
            // relevant only once
            foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Crew)
            {
                if (personnelFolders.ContainsKey(pcm.name) == false)
                {
                    // Create and file properly
                    GetFile(pcm.name);
                }
            }

            // remove all applicants not generated by Headlines
            List<string> toDelete = new List<string>();
            List<string> missingApplicants = applicantFolders.Keys.ToList();
            foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Kerbals(ProtoCrewMember.KerbalType
                .Applicant))
            {
                if (applicantFolders.ContainsKey(pcm.name) == false)
                {
                    toDelete.Add(pcm.name);
                }
                else
                {
                    missingApplicants.Remove(pcm.name);
                }
            }

            foreach (string name in toDelete)
            {
                HighLogic.CurrentGame.CrewRoster.Remove(name);
            }

            // put back applicants removed by KSP without Headlines' knowledge
            foreach (string name in missingApplicants)
            {
                HighLogic.CurrentGame.CrewRoster.AddCrewMember(applicantFolders[name].GetKSPData());
            }
        }

        /// <summary>
        /// Manual Add of a crew to the roster (through hiring)
        /// </summary>
        /// <param name="newCrew"></param>
        public void AddCrew(PersonnelFile newCrew)
        {
            personnelFolders.Add(newCrew.UniqueName(), newCrew);
        }

        public void HireApplicant(PersonnelFile pf)
        {
            if (applicantFolders.ContainsKey(pf.UniqueName())) applicantFolders.Remove(pf.UniqueName());
            AddCrew(pf);
        }

        /// <summary>
        /// Delete the personnel file and remove the pcm from the game.
        /// </summary>
        /// <param name="personnelFile"></param>
        public void RemoveKerbal(PersonnelFile personnelFile)
        {
            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                kvp.Value.NotifyOfRemoval(personnelFile.UniqueName());
            }

            if (personnelFolders.ContainsKey(personnelFile.UniqueName()))
            {
                personnelFolders.Remove(personnelFile.UniqueName());
            }
            else
            {
                applicantFolders.Remove(personnelFile.UniqueName());
            }
            
            personnelFile.Remove();
        }

        public void ReturnToApplicantPool(PersonnelFile personnelFile)
        {
            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                kvp.Value.NotifyOfRemoval(personnelFile.UniqueName());
            }
            if (personnelFolders.ContainsKey(personnelFile.UniqueName()))
            {
                applicantFolders.Add(personnelFile.UniqueName(), personnelFile);
                personnelFolders.Remove(personnelFile.UniqueName());
            }
        }

        /// <summary>
        /// Remove all applicants from the pool
        /// </summary>
        public void ClearApplicants()
        {
            List<ProtoCrewMember> toDelete = new List<ProtoCrewMember>();
            foreach (ProtoCrewMember apcm in HighLogic.CurrentGame.CrewRoster.Applicants)
            {
                toDelete.Add(apcm);
            }

            List<string> test = applicantFolders.Keys.ToList();
            
            foreach (ProtoCrewMember apcm in toDelete)
            {
                HighLogic.CurrentGame.CrewRoster.Remove(apcm);
            }

            foreach (string kerbalName in applicantFolders.Keys.ToList())
            {
                applicantFolders.Remove(kerbalName);
            }
        }

        /// <summary>
        /// Generate an applicant and its file with a baseline around a program level.
        /// </summary>
        /// <param name="level">level of a program's reputation</param>
        /// <returns></returns>
        public PersonnelFile GenerateRandomApplicant(int level = 0)
        {
            ProtoCrewMember newpcm = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Applicant);
            PersonnelFile newFile = GetFile(newpcm.name);
            newFile.Randomize(level);
            return newFile;
        }

        /// <summary>
        /// Create a non-flight manager to run the place in absence of a high profile Project Manager
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        public PersonnelFile GenerateDefaultProgramManager(int level = 0)
        {
            ProtoCrewMember newpcm = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Unowned);
            PersonnelFile newFile = GetFile(newpcm.name);
            newFile.Randomize(level);
            return newFile;
        }

        /// <summary>
        /// Moves from applicants to personnel or create the file
        /// </summary>
        /// <param name="apcm">PCM from event</param>
        /// <param name="n">some integer</param>
        private void HiringEventHandler(ProtoCrewMember apcm, int n)
        {
            PersonnelFile personnelFile;
            if (applicantFolders.ContainsKey(apcm.name))
            {
                personnelFile = GetFile(apcm.name);
                applicantFolders.Remove(apcm.name);
                AddCrew(personnelFile);
            }
            else
            {
                // Ensures the file exists
                GetFile(apcm.name);
            }
        }

        /// <summary>
        ///  So there is variability in each new career
        /// </summary>
        public void RandomizeStartingCrew()
        {
            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                kvp.Value.RandomizeType();
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
            if (applicantFolders.ContainsKey(kerbalName)) return applicantFolders[kerbalName];
 
            // Possible when loading a save file...
            ProtoCrewMember temppcm = HighLogic.CurrentGame.CrewRoster[kerbalName];
            if (temppcm == null) return (PersonnelFile) null;
            
            PersonnelFile pf = new PersonnelFile(temppcm);
            pf.Randomize(generationLevel);
            
            // Ensures a 3, 2, 1 for the first three kerbals ever generated.
            generationLevel = Math.Max(0, generationLevel - 1);
            
            if (temppcm.type == ProtoCrewMember.KerbalType.Crew)
            {
                AddCrew(pf);
                return personnelFolders[kerbalName];
                } 
            if (temppcm.type == ProtoCrewMember.KerbalType.Applicant)
            {
                if (!applicantFolders.ContainsKey(kerbalName))
                {
                    applicantFolders.Add(kerbalName, pf);
                }
                return applicantFolders[kerbalName];
            }
            
            // if we get there, we have a rogue kerbal that doesn't exit
            return null;
        }

        public void AssignProgramManager(PersonnelFile newManager)
        {
            managerFolders.Clear();
            managerFolders.Add(newManager.UniqueName(), newManager);
        }
        
        /// <summary>
        /// Returns the program manager or null
        /// </summary>
        /// <returns></returns>
        public PersonnelFile GetProgramManager()
        {
            HeadlinesUtil.Report(1, $"managerFolder length {managerFolders.Count}.");
            if (managerFolders.Count == 0)
            {
                return null;
            }

            foreach (KeyValuePair<string, PersonnelFile> kvp in managerFolders)
            {
                return kvp.Value;
            }

            return null;
        }
        
        /// <summary>
        /// Needed by the GUI to build a roster selector
        /// </summary>
        /// <returns></returns>
        public List<string> GetDisplayNameRoster()
        {
            List<string> output = new List<string>();
            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                output.Add(kvp.Value.DisplayName());
            }

            return output;
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

        public void OperationalDeathShock(string deceased)
        {
            int shock;
            // Everyone gets a discontent increment
            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                if (deceased == kvp.Value.UniqueName()) continue;
                // Scrappers are unfazed by other people's death
                shock = 1;
                if (kvp.Value.HasAttribute("scrapper"))
                {
                    shock -= 1;
                }
                if (kvp.Value.IsCollaborator(deceased))
                {
                    shock += 1;
                }
                HeadlinesUtil.Report(2, $"{kvp.Value.DisplayName()} suffers {shock} shock.");
                kvp.Value.AdjustDiscontent(shock);
            }
        }
        
        /// <summary>
        /// Used by soul-searching kerbals wondering if they are being dragged down by their peers.
        /// </summary>
        /// <returns>Average program effectiveness</returns>
        public double ProgramAverageEffectiveness(double exclusion = 0, bool determinisitic = false)
        {
            if (personnelFolders.Count > 0)
            {
                return (ProgramProfile(determinisitic) - exclusion) / (double)personnelFolders.Count;
            }

            return 0;
        }

        /// <summary>
        /// Provide a qualifier to translate point into an English adjective.
        /// </summary>
        /// <param name="value">Effectiveness</param>
        /// <returns></returns>
        public string QualitativeEffectiveness(double value)
        {
            if (value <= 1) return "inept";
            else if (value <= 2)
            {
                return "naive";
            }
            else if (value <= 4)
            {
                return "junior";
            }
            else if (value <= 6)
            {
                return "competent";
            }
            else if (value <= 10)
            {
                return "excellent";
            }
            else return "legendary";
        }

        /// <summary>
        /// Determined is Warp shoudl be killed when a new applicant spawns
        /// </summary>
        /// <param name="applicantRole">Speciality</param>
        /// <returns></returns>
        public bool ShouldNotify(string applicantSpecialty)
        {
            if (seekingEngineer & applicantSpecialty == "Engineer") return true;
            if (seekingPilot & applicantSpecialty == "Pilot") return true;
            if (seekingScientist & applicantSpecialty == "Scientist") return true;
            return false;
        }
        
        /// <summary>
        /// Sums all staff effectiveness as a program profile extimate.
        /// </summary>
        /// <returns></returns>
        public double ProgramProfile(bool deterministic = false)
        {
            double programProfile = 0;

            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                programProfile += kvp.Value.Effectiveness(deterministic:deterministic);
            }

            return programProfile;
        }

        /// <summary>
        /// Tallies all impact on a building by all kerbals at this moment
        /// </summary>
        /// <param name="asA">Either Scientist or Engineer</param>
        /// <returns>Point value</returns>
        public int KSCImpact(string asA = "Engineer")
        {
            int output = 0;
            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                if (kvp.Value.Specialty() == asA)
                {
                    output += kvp.Value.influence + kvp.Value.teamInfluence + kvp.Value.legacy;
                }
            }

            return output;
        }

        public bool EndWarp(PersonnelFile applicant)
        {
            if (applicant.Specialty() == "Pilot" && !seekingPilot) return false;
            if (applicant.Specialty() == "Engineer" && !seekingEngineer) return false;
            if (applicant.Specialty() == "Scientist" && !seekingScientist) return false;
            
            // Insert minimal effectiveness logic here

            return true;
        }

        public PersonnelFile TopCrewMembers(string specialtyFilter = "")
        {
            PersonnelFile output = null;
            double tempEffectiveness = 0;
            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                if (specialtyFilter != "")
                {
                    if (specialtyFilter != kvp.Value.Specialty()) continue;
                }
                if (output == null)
                {
                    output = kvp.Value;
                    tempEffectiveness = output.Effectiveness(deterministic: true);
                }
                else if (kvp.Value.Effectiveness(deterministic: true) > tempEffectiveness)
                {
                    output = kvp.Value;
                    tempEffectiveness = kvp.Value.Effectiveness(deterministic: true);
                }
            }

            return output;
        }

        /// <summary>
        /// Returns a list of names ordered by effectiveness. 
        /// </summary>
        /// <remarks>This could be done in one step, but is fighting me rn.</remarks>
        /// <returns></returns>
        public List<string> RankCrewMembers()
        {
            List<PersonnelFile> temp =new List<PersonnelFile>();

            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                temp.Add(kvp.Value);    
            }

            List<string> output = new List<string>();
            
            // to do Cannot use cached as it causes flickering when PM is not a NPC (?)
            foreach (PersonnelFile pf in temp.OrderByDescending(o=>o.Effectiveness(deterministic:true, quickDirty:false)))
            {
                output.Add(pf.UniqueName());
            }

            return output;
        }

        public void MarkEffectivenessCacheDirty()
        {
            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                kvp.Value.ResetEffectivenessCache();
            }
        }

        /// <summary>
        /// Decay fame by the magic factor.
        /// </summary>
        public void DecayFame()
        {
            foreach (KeyValuePair<string, PersonnelFile> kvp in personnelFolders)
            {
                kvp.Value.DecayFame();
            }
        }

        #endregion
    }

    public class PersonnelFile
    {
        #region fields
        
        private static System.Random randomNG = new System.Random();
        
        public static List<string>attributes = new List<string>() {"stubborn","genial","inspiring","charming","scrapper","bland"};
        
        private static List<PartCategories> validCategoriesList = new List<PartCategories>()
        {
            PartCategories.Aero, PartCategories.Cargo, PartCategories.Communication, PartCategories.Control,
            PartCategories.Coupling, PartCategories.Electrical,
            PartCategories.Engine, PartCategories.Ground, PartCategories.Payload, PartCategories.Propulsion,
            PartCategories.Science, PartCategories.Structural,
            PartCategories.Thermal, PartCategories.Utility, PartCategories.FuelTank
        };
        
        
        // Getting better through professional development
        public int trainingLevel = 0;

        /// <summary>
        /// Track how past visibility affects profile
        /// </summary>
        public double fame = 0;

        // Whether the crew has affinity to a certain category
        public PartCategories passion = PartCategories.none;
        
        // Affects the odds of leaving the program
        [KSPField(isPersistant = true)] private int discontent = 1;
        
        // Personality
        [KSPField(isPersistant = true)] public string personality = "";

        [KSPField(isPersistant = true)] public string nationality = "";

        // Indicate that the current task was ordered by the player
        public bool coercedTask = false;
        
        // While sustaining (transient)
        public int influence = 0;
        // Passive until retirement
        public int teamInfluence = 0;
        // Will never be removed
        public int legacy = 0;
        
        // Pilot related performance
        public int numberScout = 0;
        public int fundRaised = 0;
        public int lifetimeHype = 0;

        // Store HMM
        public string kerbalProductiveState;
        public string kerbalTask;
        
        // Program Manager
        public bool isProgramManager = false;
        
        // relationships
        public List<string> collaborators = new List<string>();
        public List<string> feuds = new List<string>();
        
        // Personality
        //private string personalityTrait = "";

        private ProtoCrewMember pcm;

        private int _cachedEffectiveness = 0; 
        
        #endregion
        
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
            kerbalProductiveState = node.GetValue("kerbalState");
            kerbalTask = node.GetValue("kerbalTask");
            trainingLevel = int.Parse(node.GetValue("trainingLevel"));
            influence = int.Parse(node.GetValue("influence"));
            teamInfluence = int.Parse(node.GetValue("teamInfluence"));
            legacy = int.Parse(node.GetValue("legacy"));
            discontent = int.Parse(node.GetValue("discontent"));
            lifetimeHype = int.Parse(node.GetValue("lifetimeHype"));
            personality = node.GetValue("personality");
            isProgramManager = Boolean.Parse(node.GetValue("isProgramManager"));
            nationality = node.GetValue("nationality");
            if (node.HasValue("passion"))
            {
                passion = (PartCategories)int.Parse(node.GetValue("passion"));
            }

            if (node.HasValue("fame"))
            {
                fame = double.Parse(node.GetValue("fame"));
            }
            
            
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
            outputNode.AddValue("lifetimeHype", lifetimeHype);
            outputNode.AddValue("personality", personality);
            outputNode.AddValue("isProgramManager", isProgramManager);
            outputNode.AddValue("nationality", nationality);
            outputNode.AddValue("passion", (int)passion);
            outputNode.AddValue("fame", fame);

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

        public void NotifyOfRemoval(string kerbalName)
        {
            if (feuds.Contains(kerbalName)) feuds.Remove(kerbalName);
            if (collaborators.Contains(kerbalName)) collaborators.Remove(kerbalName);
        }

        #endregion

        #region Getter

        public ProtoCrewMember GetKSPData()
        {
            return pcm;
        }

        /// <summary>
        /// Asserts whether a crew member has a given attribute.
        /// </summary>
        /// <param name="attribute"></param>
        /// <returns></returns>
        public bool HasAttribute(string attribute)
        {
            if (personality == attribute) return true;
            return false;
        }
        
        /// <summary>
        /// Profile is a key metric in evaluating the impact of a kerbal. People value courage and extremes in stupidity.
        /// </summary>
        /// <returns>Zero-bound value</returns>
        public double Charisma()
        {
            double outputProfile = 2 * (pcm.courage + (2 * Math.Abs(0.5 - pcm.stupidity)));

            outputProfile += (double)pcm.experience;
            
            return outputProfile;
        }

        #region effectiveness

        /// <summary>
        /// Computes the skill level of a kerbal. This method is non-deterministic as it treats partial profile as
        /// a probability. 
        /// </summary>
        /// <param name="isMedia">Indicate a media task when Kerbal is not a pilot</param>
        /// <returns>effectiveness</returns>
        public int Effectiveness(bool isMedia = false, bool deterministic = false, bool quickDirty= false)
        {
            if (quickDirty && _cachedEffectiveness != 0)
            {
                return _cachedEffectiveness;
            }
            int effectiveness = EffectivenessLikability(deterministic);

            // experience Level (untrained if media for non-pilot
            if (!(isMedia && Specialty() != "Pilot"))
            {
                effectiveness += EffectivenessFame();
            }
            
            // Charm and personality
            effectiveness += EffectivenessPersonality(isMedia);

            // training
            effectiveness += this.trainingLevel;

            // Discontentment, collaborations and feuds
            effectiveness += EffectivenessHumanFactors(isMedia);
            
            // slump/inspired
            effectiveness += EffectivenessMood();

            _cachedEffectiveness = effectiveness;
            
            return (int)Math.Max(0, effectiveness);
        }

        public int EffectivenessLikability(bool deterministic = false)
        {
            int effectiveness = 0;
            // Profile and experience with probability for fractional points
            double tempProfile = Charisma();
            int wholePartProfile = (int) Math.Floor(tempProfile);
            effectiveness += wholePartProfile;

            // Treat partial profile point as probabilities
            double partialProfile = tempProfile - (double) wholePartProfile;
            if (!deterministic && randomNG.NextDouble() <= partialProfile)
            {
                effectiveness += 1;
            }
            else
            {
                effectiveness += (int)Math.Round(partialProfile,MidpointRounding.AwayFromZero);
            }

            return effectiveness;
        }
        
        /// <summary>
        /// Impact of personality tags on effectiveness
        /// </summary>
        /// <param name="isMedia"></param>
        /// <returns></returns>
        public int EffectivenessPersonality(bool isMedia = false)
        {
            int effectiveness = 0;
            if (isMedia | Specialty() == "Pilot")
            {
                if (HasAttribute("charming")) effectiveness++;
                if (HasAttribute("bland")) effectiveness--;
            }

            return effectiveness;
        }
        
        /// <summary>
        /// Effect of productivity hidden state
        /// </summary>
        /// <returns>Partial effectiveness</returns>
        public int EffectivenessMood()
        {
            int effectiveness = -1 * discontent;

            switch (kerbalProductiveState)
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
        /// Accounts for the effect of discontentment, collaborations and feuds.
        /// </summary>
        /// <param name="isMedia"></param>
        /// <returns>Partial effectiveness</returns>
        public int EffectivenessHumanFactors(bool isMedia = false)
        {
            int effectiveness = collaborators.Count;

            if (!(isMedia || Specialty() == "Pilot"))
            {
                // Feuds can't be ignored when working at the KSC
                effectiveness -= feuds.Count;
            }

            return effectiveness;
        }

        /// <summary>
        /// Create a custom level system to reward early career a bit more, and cap impact on effectiveness in the
        /// upper range.
        /// </summary>
        /// <returns>Starstruck levels</returns>
        public int EffectivenessFame()
        {
            int output = (int)Math.Round(fame, MidpointRounding.AwayFromZero);
            float xp = pcm.experience;

            if (xp <= 2) output += (int)xp;
            else if (xp <= 4)
            {
                output += 3;
            }
            else output += 4;

            return output;
        }

        #endregion
        

        public bool IsCollaborator(string candidate)
        {
            return collaborators.Contains(candidate);
        }

        public bool IsInactive()
        {
            return pcm.inactive;
        }

        public double InactiveDeadline()
        {
            return pcm.inactiveTimeEnd;
        }
        
        public bool IsFeuding(string candidate)
        {
            return feuds.Contains(candidate) ;
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

        public double Courage()
        {
            return (double) pcm.courage;
        }

        public int GetDiscontent()
        {
            int output = discontent - collaborators.Count + feuds.Count;
            return Math.Max(0, Math.Min(5,output));
        }

        /// <summary>
        /// This isn't working.
        /// </summary>
        /// <returns></returns>
        public string GetCulture()
        {
            if (nationality != "") return nationality;
            
            string output = "Unknown";
            for (int i = 0; i < pcm.flightLog.Count; i++)
            {
                FlightLog.Entry e = pcm.flightLog[i];
                if (e.type.ToString() == "Nationality")
                {
                    output = e.target.ToString();
                }
            }

            output = output.Replace("_or_", "/");
            output = output.Replace("_", " ");
            nationality = output;
            return output;
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
            return SetRelationship(candidate, false, unset);
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

        public void SetInactive(double endTime)
        {
            pcm.SetInactive(endTime, false);
        }

        /// <summary>
        /// This is triggered by the UI when a player changes the stored kerbaltask
        /// </summary>
        /// <param name="newtask">the new task</param>
        public void OrderTask(string newtask)
        {
            kerbalTask = newtask;
            coercedTask = true;
        }
        
        public void ResetEffectivenessCache()
        {
            _cachedEffectiveness = 0;
        }

        public void AddLifetimeHype(int moreHype)
        {
            lifetimeHype += moreHype;
            AddFame((double)moreHype/4);
        }

        public void DecayFame()
        {
            fame *= 0.933;
        }

        public void AddFame(double newFame)
        {
            fame += Math.Log(newFame, 4);
        }

        #endregion

        #region logic

        /// <summary>
        /// Shake things up so not all new crew members are the same.
        /// </summary>
        public void Randomize(int level = 0)
        {
            // Add some wiggle room within a level
            level += randomNG.Next(-1, 2);
            level = Math.Min(5, level);
            level = Math.Max(0, level);

            discontent = randomNG.Next(0, 2);
            int difference = level - Effectiveness();

            if (difference > 0)
            {
                // Experience for free as we assume most have piloting license
                KerbalRoster.SetExperienceLevel(pcm, 1);
                difference -= 1;
                trainingLevel = difference;
            }
            else
            {
                discontent = Math.Max(0, discontent + difference);
            }

            personality = GetRandomPersonality();
            
            RandomizePassion();
        }

        public static string GetRandomPersonality()
        {
            if (randomNG.NextDouble() < 0.5)
            {
                int attributeIndex = randomNG.Next(0, attributes.Count);
               return attributes[attributeIndex];
            }

            return "";
        }

        public void RandomizeType()
        {
            // Only the first starting 4 will be randomized as crew
            if (pcm.type == ProtoCrewMember.KerbalType.Crew)
            {
                string[] newTypes = new[] {"Pilot", "Engineer", "Scientist"};
                string newType = newTypes[randomNG.Next(0,4)];
                KerbalRoster.SetExperienceTrait(pcm, newType);
            }
        }

        /// <summary>
        /// Assign a passion (or unassign)
        /// </summary>
        public void RandomizePassion()
        {
            double p = 0.5;
            if (passion != PartCategories.none)
            {
                p /= 5;
            }
            if (randomNG.NextDouble() >= p)
            {
                passion = GetRandomPassion();
                return;
            }

            if (randomNG.NextDouble() <= 0.1)
            {
                passion = PartCategories.none;
            }
        }
        public PartCategories GetRandomPassion()
        {
            int rndInt = randomNG.Next() % validCategoriesList.Count;
            return validCategoriesList[rndInt];
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using Contracts;
using HiddenMarkovProcess;
using KSP.UI.Screens;
using UnityEngine;


namespace RPStoryteller.source.GUI
{
    [KSPAddon(KSPAddon.Startup.FlightEditorAndKSC, false)]
    public class HeadlinesGUIManager : MonoBehaviour
    {
        private StoryEngine storyEngine;
        private static ApplicationLauncherButton stockButton;

        public bool _isDisplayed = false;
        public bool _showAutoAcceptedContracts = false;
        
        private string _activeTab = "program";
        private int _selectedCrew = 0;
        private int _currentActivity = 0;

        private PeopleManager peopleManager;
        private List<string> crewRoster;
        private List<string> applicantRoster;
        
        //private List<string> tabLabels;
        private List<string> activityLabels;

        // location of the Window
        public Rect position;

        #region Unity stuff
        
        protected void Awake()
        {
            try
            {
                GameEvents.onGUIApplicationLauncherReady.Add(OnGuiAppLauncherReady);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Headlines] failed to register UIHolder.OnGuiAppLauncherReady");
                Debug.LogException(ex);
            }
        }
        
        public void Start()
        {
            storyEngine = StoryEngine.Instance;
            position = new Rect(100f, 100f, 400f, 200f);
        }

        protected void OnDestroy()
        {
            try
            {
                GameEvents.onGUIApplicationLauncherReady.Remove(OnGuiAppLauncherReady);
                GameEvents.onGameSceneLoadRequested.Remove(OnSceneChange);
                GameEvents.onGameSceneSwitchRequested.Remove(OnSceneChange);

                if (stockButton != null)
                    ApplicationLauncher.Instance.RemoveModApplication(stockButton);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
        
        private void OnGuiAppLauncherReady()
        {
            if (stockButton == null)
            {
                stockButton = ApplicationLauncher.Instance.AddModApplication(
                    ShowWindow,
                    HideWindow,
                    null,
                    null,
                    null,
                    null,
                    ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH,
                    GameDatabase.Instance.GetTexture("Headlines/artwork/icons/crowdwatching28mask2", false)
                );
                //ApplicationLauncher.Instance.AddOnHideCallback(HideButton);
                GameEvents.onGameSceneLoadRequested.Add(OnSceneChange);
                GameEvents.onGameSceneSwitchRequested.Add(OnSceneChange);
            }
        }
        
        private void OnSceneChange(GameScenes s)
        {
            if (s == GameScenes.FLIGHT)
                HideWindow();
        }

        private void OnSceneChange(GameEvents.FromToAction<GameScenes, GameScenes> ev)
        {
            if (ev.from == GameScenes.SPACECENTER)
            {
                HideWindow();
            }
        }

        private void BuildActivityLabels(string role)
        {
            HiddenState hmmprocess = new HiddenState("role_" + role);
            activityLabels = hmmprocess.ListEmissions();
        }

        private void ShowWindow()
        {
            if (storyEngine == null)
            {
                storyEngine = StoryEngine.Instance;
            }
            _isDisplayed = true;
        }
        
        private void HideWindow()
        {
            _isDisplayed = false;
        }

        public void OnGUI()
        {
            if (_isDisplayed)
            {
                position = GUILayout.Window(GetInstanceID(), position, DrawWindow, "Headlines");
            }
        }
        
        #endregion

        #region Drawing

        /// <summary>
        /// Store the tab to display
        /// </summary>
        /// <param name="activeTab"></param>
        private void SwitchTab(string activeTab)
        {
            _activeTab = activeTab;
        }
        
        /// <summary>
        /// Top-level UI for Headlines
        /// </summary>
        /// <param name="windowID"></param>
        public void DrawWindow(int windowID)
        {
            // Tab area
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Program"))
            {
                SwitchTab("program");
            }
            else if (GUILayout.Button("Personnel"))
            {
                SwitchTab("personnel");
            }
            else if (GUILayout.Button("Recruitment"))
            {
                SwitchTab("recruitment");
            }
            else if (GUILayout.Button("GM"))
            {
                SwitchTab("gm");
            }
            GUILayout.EndHorizontal();

            switch (_activeTab)
            {
                case "program":
                    DrawProgramDashboard(windowID);
                    break;
                case "personnel":
                    DrawPersonelPanel();
                    break;
                case "recruitment":
                    DrawRecruitmentPanel();
                    break;
                case "gm":
                    DrawGMPanel();
                    break;
            }

            if (GUILayout.Button("Close"))
            {
                HideWindow();
            }
            
            UnityEngine.GUI.DragWindow();
        }

        /// <summary>
        /// Display program information and (eventually) the pledging mechanism 
        /// </summary>
        /// <param name="windowID"></param>
        public void DrawProgramDashboard(int windowID)
        {
            storyEngine = StoryEngine.Instance;
            
            GUILayout.BeginVertical();
            
            GUILayout.Box($"Program Dashboard (Staff: {storyEngine.GUIAverageProfile()})");

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Reputation:", GUILayout.Width(100));
            GUILayout.Label($"{storyEngine.GUIValuation()}");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Overvaluation:", GUILayout.Width(100));
            GUILayout.Label($"{storyEngine.GUIOvervaluation()} (Hype: {Math.Round(storyEngine.programHype, MidpointRounding.ToEven)})");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Peak:", GUILayout.Width(100));
            GUILayout.Label($"{storyEngine.GUIRelativeToPeak()}");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Space Craze:", GUILayout.Width(100));
            GUILayout.Label($"{storyEngine.GUISpaceCraze()}");
            GUILayout.EndHorizontal();
            if (storyEngine.ongoingInquiry)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Public inquiry:", GUILayout.Width(100));
                GUILayout.Label($"{storyEngine.ongoingInquiry}");
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Score:", GUILayout.Width(100));
            GUILayout.Label($"{(int)storyEngine.headlinesScore} Rep * year");
            GUILayout.EndHorizontal();
            GUILayout.Space(20);
            
            DrawContracts();

            GUILayout.Box("Impact");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Capital Funding: {storyEngine.GUIFundraised()}");
            GUILayout.Label($"Science Data   : {storyEngine.GUIVisitingSciencePercent()}%");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"VAB Boost: {storyEngine.GUIVABEnhancement()}");
            GUILayout.Label($"R&D Boost: {storyEngine.GUIRnDEnhancement()}");
            GUILayout.EndHorizontal();
            if (storyEngine.visitingScholar)
            {
                GUILayout.Label($"Visiting scholar {storyEngine.visitingScholarName} will help with next science haul.");
            }
            
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Contract view and controls for the program view.
        /// </summary>
        public void DrawContracts()
        {
            float ratio = 0f;
            bool hasheader = false;

            Color originalColor = UnityEngine.GUI.contentColor;

            foreach (Contract myContract in ContractSystem.Instance.GetCurrentContracts<Contract>())
            {
                if (!hasheader)
                {
                    GUILayout.Box("Contracts (% hyped)");
                    hasheader = true;
                }
                if (myContract.ContractState == Contract.State.Active)
                {
                    // Skip autoaccepted contracts 
                    if (myContract.AutoAccept & !_showAutoAcceptedContracts) continue;
                    
                    if (myContract.ReputationCompletion > 0)
                    {
                        ratio = storyEngine.programHype / myContract.ReputationCompletion;
                    }
                    else
                    {
                        ratio = 1f;
                    }

                    if (ratio >= 1f)
                    {
                        UnityEngine.GUI.contentColor = Color.green;
                    }

                    else if (ratio >= 0.5f)
                    {
                        UnityEngine.GUI.contentColor = Color.yellow;
                    }
                    else UnityEngine.GUI.contentColor = Color.red;
                    
                    GUILayout.BeginHorizontal();
                    //GUILayout.Button("Pledge");
                    GUILayout.Label($"{myContract.Title} ({(int)Math.Ceiling(100f*ratio)}%)" );
                    GUILayout.EndHorizontal();
                }
                
            }
            
            UnityEngine.GUI.contentColor = originalColor;
            _showAutoAcceptedContracts = GUILayout.Toggle(_showAutoAcceptedContracts, "Show all contracts");

            GUILayout.Space(20);
            if (storyEngine.endSpotlight < HeadlinesUtil.GetUT())
            {
                storyEngine.InvitePress(GUILayout.Button("Invite Press"));
            }
            else
            {
                GUILayout.Box($"Media spotlight for {KSPUtil.PrintDateDeltaCompact(storyEngine.endSpotlight - HeadlinesUtil.GetUT(), true, true)}");
            }
            GUILayout.Space(10);
        }

        /// <summary>
        /// Top-level UI for the Crew panel of the main UI
        /// </summary>
        public void DrawPersonelPanel()
        {
            RefreshRoster();

            if (crewRoster.Count == 0) return;
            
            GUILayout.BeginVertical();
            GUILayout.Box("Active crew");
            
            _selectedCrew = GUILayout.SelectionGrid(_selectedCrew, crewRoster.ToArray(), 3);
            if (_selectedCrew >= crewRoster.Count)
            {
                _selectedCrew = 0;
            }
            
            GUILayout.Space(10);
            DrawCrew();
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// Draw the crew UI assuming that _selectedCrew is set to the index of a a crew. Assumes that crewRoster is built.
        /// </summary>
        /// //todo should be refactored into components
        public void DrawCrew()
        {
            string crewName = crewRoster[_selectedCrew];
            PersonnelFile focusCrew = peopleManager.GetFile(crewName);
            
            string personality = "";
            if (focusCrew.personality != "")
            {
                personality = $" ({focusCrew.personality})";
            }
            
            GUILayout.Box($"{peopleManager.QualitativeEffectiveness(focusCrew.Effectiveness(deterministic:true))} {focusCrew.Specialty().ToLower()}{personality}");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Net Score: {focusCrew.Effectiveness(deterministic:true)}");
            GUILayout.Label($"training: {focusCrew.trainingLevel}");
            GUILayout.Label($"Discontent: {focusCrew.GetDiscontent()}");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Profile: {Math.Round(focusCrew.Profile(),2)}");
            GUILayout.Label($"Stupidity: {Math.Round(focusCrew.Stupidity(), 2)}");
            GUILayout.Label($"Courage: {Math.Round(focusCrew.Courage(),2)}");
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            
            // If untrained, offers to reassign
            if (focusCrew.trainingLevel == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Retrain as: ");
                if (focusCrew.Specialty() != "Pilot")
                {
                    if (GUILayout.Button("Pilot"))
                    {
                        storyEngine.RetrainKerbal(focusCrew, "Pilot");
                    }
                }
                if (focusCrew.Specialty() != "Scientist")
                {
                    if (GUILayout.Button("Scientist"))
                    {
                        storyEngine.RetrainKerbal(focusCrew, "Scientist");
                    }
                }
                if (focusCrew.Specialty() != "Engineer")
                {
                    if (GUILayout.Button("Engineer"))
                    {
                        storyEngine.RetrainKerbal(focusCrew, "Engineer");
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(10);
            
            // Impact
            if (focusCrew.Specialty() != "Pilot")
            {
                string location = "VAB";
                if (focusCrew.Specialty() == "Scientist") location = "R&D complex";
                GUILayout.Box($"Impact on {location} ({focusCrew.influence+focusCrew.teamInfluence+focusCrew.legacy} pts)");
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Immediate: {focusCrew.influence}");
                GUILayout.Label($"Lasting: {focusCrew.teamInfluence}");
                GUILayout.Label($"Legacy: {focusCrew.legacy}");
                GUILayout.EndHorizontal();
                GUILayout.Space(20);
            }
            
            // Relationships
            if (focusCrew.feuds.Count + focusCrew.collaborators.Count != 0)
            {
                GUILayout.Box($"Relationships");
                foreach (string otherCrew in focusCrew.collaborators)
                {
                    DrawRelationship(peopleManager.GetFile(otherCrew), isFeud:false);
                }
                foreach (string otherCrew in focusCrew.feuds)
                {
                    DrawRelationship(peopleManager.GetFile(otherCrew), isFeud:true);
                }
                GUILayout.Space(20);
            }
            
            // Activity controls
            BuildActivityLabels(focusCrew.Specialty()); // inefficient
            GUILayout.Box($"Activity ({focusCrew.kerbalProductiveState})");
            _currentActivity = activityLabels.IndexOf(focusCrew.kerbalTask);
            _currentActivity = GUILayout.SelectionGrid(_currentActivity, activityLabels.ToArray(), 2);
            if (_currentActivity != activityLabels.IndexOf(focusCrew.kerbalTask))
            {
                focusCrew.OrderTask(activityLabels[_currentActivity]);
            }

            if (focusCrew.coercedTask)
            {
                focusCrew.coercedTask = GUILayout.Toggle(focusCrew.coercedTask, "Told what to do");
            }

        }

        /// <summary>
        /// This section of the Crew UI displays one entry of the interpersonal relationships involving the selected crewMember 
        /// </summary>
        /// <param name="crewMember">Name of the "other" crew member</param>
        /// <param name="isFeud"></param>
        public void DrawRelationship(PersonnelFile crewMember, bool isFeud = false)
        {
            GUILayout.BeginHorizontal();
            Color oldColor = UnityEngine.GUI.contentColor;
            if (isFeud == true)
            {
                UnityEngine.GUI.contentColor = Color.red;
                GUILayout.Label("[FEUD]", GUILayout.Width(50));
            }
            else
            {
                UnityEngine.GUI.contentColor = Color.green;
                GUILayout.Label("[COLL]", GUILayout.Width(50));
            }
            UnityEngine.GUI.contentColor = oldColor;
            
            GUILayout.Label($"{crewMember.DisplayName()}", GUILayout.Width(160));
            GUILayout.Label($"{crewMember.Specialty()} ({peopleManager.QualitativeEffectiveness(crewMember.Effectiveness(deterministic:true))})", GUILayout.Width(120));
            
            GUILayout.EndHorizontal();
        }

        public void DrawRecruitmentPanel()
        {
            peopleManager = PeopleManager.Instance;
            
            GUILayout.BeginVertical();

            double searchCost = 2000 + 2000 * (double) storyEngine.GetValuationLevel();
            if (storyEngine.GetFunds() > searchCost)
            {
                if (GUILayout.Button($"Open new search (${searchCost})"))
                {
                    storyEngine.LaunchSearch(false);
                }

                searchCost *= 5;
                if (storyEngine.GetFunds() > searchCost)
                {
                    if (GUILayout.Button($"Contract a headHunter (${searchCost})"))
                    {
                        storyEngine.LaunchSearch(true);
                    }
                }
                else
                {
                    GUILayout.Label($"Hiring a headhunter costs ${searchCost}");
                }
            }
            else
            {
                GUILayout.Label($"Open a search for ${searchCost}");
            }
            
            
            GUILayout.Box($"Applicant Pool ({peopleManager.applicantFolders.Count})");

            int nApplicant = 0;
            List<string> toDelete = new List<string>();
            foreach (KeyValuePair<string, PersonnelFile> kvp in peopleManager.applicantFolders)
            {
                GUILayout.BeginHorizontal();
                if (kvp.Key != kvp.Value.UniqueName())
                {
                    toDelete.Add(kvp.Key);
                }
                else
                {
                    nApplicant++;
                    GUILayout.Label($"{kvp.Value.DisplayName()}", GUILayout.Width(150)); 
                    GUILayout.Label($"{peopleManager.QualitativeEffectiveness(kvp.Value.Effectiveness(deterministic:true))} {kvp.Value.Specialty()}", GUILayout.Width(120));
                    GUILayout.Label($"{kvp.Value.personality}", GUILayout.Width(60));
                }

                GUILayout.EndHorizontal();

            }

            if (nApplicant == 0)
            {
                GUILayout.Space(10);
                if (storyEngine.hasnotvisitedAstronautComplex)
                {
                    if (!storyEngine.inAstronautComplex)
                    {
                        GUILayout.Label("Toby, your HR staff, has misplaced his key to the astronaut complex. Please enter then exit the complex so that he may interview the applicants waiting in line.");
                    }
                    else
                    {
                        GUILayout.Label("You may leave the complex while Toby does his job.");
                    }
                
                }
                else
                {
                    GUILayout.Label("There are no applicants on file. You will have to rely on chance walk-ins or spend money to find prospects.");
                }
            }
            
            foreach (var oldKey in toDelete)
            {
                peopleManager.applicantFolders.Remove(oldKey);
            }
            
            GUILayout.Space(10);
            
            GUILayout.Box("Walk-in applicants notifications");
            GUILayout.BeginHorizontal();
            peopleManager.seekingPilot = GUILayout.Toggle(peopleManager.seekingPilot, "Pilots");
            peopleManager.seekingScientist = GUILayout.Toggle(peopleManager.seekingScientist, "Scientists");
            peopleManager.seekingEngineer = GUILayout.Toggle(peopleManager.seekingEngineer, "Engineers");
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            
            GUILayout.EndVertical();
        }

        public void DrawGMPanel()
        {
            double clock = HeadlinesUtil.GetUT();
            
            GUILayout.BeginVertical();
            GUILayout.Box("Cheats");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add 5 Hype"))
            {
                storyEngine.programHype += 5;
            }
            if (GUILayout.Button("Add 5 Reputation"))
            {
                Reputation.Instance.AddReputation(5, TransactionReasons.None);
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Trigger Decay"))
            {
                storyEngine.DecayReputation();
            }
            if (GUILayout.Button("Reality Check"))
            {
                storyEngine.RealityCheck();
            }
            if (GUILayout.Button("New Applicant"))
            {
                storyEngine.NewRandomApplicant();
            }
            GUILayout.Space(10);
            GUILayout.Box("HMM");
            foreach (KeyValuePair<string, double> kvp in storyEngine._hmmScheduler)
            {
                GUILayout.Label($"{KSPUtil.PrintDateDeltaCompact(kvp.Value-clock, true, false)} - {kvp.Key}");
            }
            GUILayout.EndVertical();
        }
        #endregion

        #region Logic
        

        /// <summary>
        /// Pulls the data from PeopleManager and stores names to be used for drawing buttons and passing to DrawCrew.
        /// </summary>
        public void RefreshRoster()
        {
            peopleManager = storyEngine.GetPeopleManager();

            crewRoster = new List<string>();
            foreach (KeyValuePair<string, PersonnelFile> kvp in peopleManager.personnelFolders)
            {
                crewRoster.Add(kvp.Value.UniqueName());
            }

            applicantRoster = new List<string>();
            foreach (KeyValuePair<string, PersonnelFile> kvp in peopleManager.applicantFolders)
            {
                applicantRoster.Add(kvp.Value.UniqueName());
            }
        }

        /// <summary>
        /// I believe that this awkward duck belongs to the PeopleManager Class Refactor.
        /// </summary>
        /// todo Refactor to shift to PeopleManager.
        /// <param name="displayName"></param>
        /// <returns></returns>
        public PersonnelFile GetFileFromDisplay(string displayName)
        {
            foreach (KeyValuePair<string, PersonnelFile> kvp in peopleManager.personnelFolders)
            {
                if (kvp.Value.DisplayName() == displayName) return kvp.Value;
            }
            
            foreach (KeyValuePair<string, PersonnelFile> kvp in peopleManager.applicantFolders)
            {
                if (kvp.Value.DisplayName() == displayName) return kvp.Value;
            }

            return null;
        }
        
        #endregion

        #region UIcontrols
        

        #endregion
    }
}
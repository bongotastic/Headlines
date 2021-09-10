using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;
using HiddenMarkovProcess;
using KSP.UI.Screens;
using RPStoryteller.source.Emissions;
using UnityEngine;
using Enumerable = UniLinq.Enumerable;


namespace RPStoryteller.source.GUI
{
    [KSPAddon(KSPAddon.Startup.FlightEditorAndKSC, false)]
    public class HeadlinesGUIManager : MonoBehaviour
    {
        private StoryEngine storyEngine;
        private ReputationManager RepMgr;
        private ProgramManager PrgMgr;
        private static ApplicationLauncherButton stockButton;

        public bool _isDisplayed = false;
        public bool _showAutoAcceptedContracts = false;
        public bool _reducedMessage = false;
        
        private int _activeTabIndex = 0;
        private int _selectedCrew = 0;
        private int _currentActivity = 0;

        private PeopleManager peopleManager;
        private List<string> crewRoster;
        private List<string> applicantRoster;
        
        //private List<string> tabLabels;
        private List<string> activityLabels;

        // location of the Window
        public Rect position;
        private bool resizePosition = true;

        private Vector2 scrollFeedView = new Vector2(0,0);
        private Vector2 scrollHMMView = new Vector2(0,0);
        

        //private bool feedChatter = true;
        private int feedThreshold = 1;
        private string feedFilterLabel = "";

        private static string[] feedFilter = new[] { "All", "Chatter", "Feature stories", "Headlines"};
        private static string[] tabs = new[] { "Program", "Media", "Feed", "Personnel", "Recruit","Story"};
        
        private int mediaInvitationDelay = 1;

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
            position = new Rect(100f, 150f, 400f, 575f);
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
                if (resizePosition)
                {
                    position.height = 100f;
                    resizePosition = false;
                }
                position = GUILayout.Window(GetInstanceID(), position, DrawWindow, $"Headlines -- {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()}");
            }
        }
        
        #endregion

        #region Drawing

        /// <summary>
        /// Store the tab to display
        /// </summary>
        /// <param name="activeTab"></param>
        private void SwitchTab(int selectedActiveTab)
        {
            if (selectedActiveTab != _activeTabIndex)
            {
                resizePosition = true;
                _activeTabIndex = selectedActiveTab;
            }
        }
        
        private void SwitchCrew(int selectedCrew)
        {
            if (selectedCrew != _selectedCrew)
            {
                resizePosition = true;
                _selectedCrew = selectedCrew;
            }
        }
        /// <summary>
        /// Top-level UI for Headlines
        /// </summary>
        /// <param name="windowID"></param>
        public void DrawWindow(int windowID)
        {
            SwitchTab(GUILayout.SelectionGrid(_activeTabIndex, tabs, 3, GUILayout.Width(400)));

            switch (_activeTabIndex)
            {
                case 0:
                    DrawProgramDashboard(windowID);
                    break;
                case 3:
                    DrawPersonelPanel();
                    break;
                case 4:
                    DrawRecruitmentPanel();
                    break;
                case 2:
                    DrawProgramFeed();
                    break;
                case 5:
                    DrawStoryPanel();
                    break;
                case 1:
                    DrawPressRoom();
                    break;
            }

            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Close"))
            {
                //HideWindow();
                stockButton.SetFalse();
            }
            
            UnityEngine.GUI.DragWindow();
        }

        #region Program Panel
        
        /// <summary>
        /// Display program information and (eventually) the pledging mechanism 
        /// </summary>
        /// <param name="windowID"></param>
        public void DrawProgramDashboard(int windowID)
        {
            storyEngine = StoryEngine.Instance;
            RepMgr = storyEngine._reputationManager;
            PrgMgr = storyEngine._programManager;
            
            GUILayout.BeginVertical();
            DrawProgramManager();
            DrawProgramStats();
            DrawImpact();
            GUILayout.EndVertical();
        }

        private void DrawProgramManager()
        {
            peopleManager = storyEngine.GetPeopleManager();
            
            GUILayout.Box($"Program status: {PrgMgr.ControlLevelQualitative()}");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Manager: {PrgMgr.ManagerName()}", GUILayout.Width(200));
            GUILayout.Label($"Profile: {peopleManager.QualitativeEffectiveness(PrgMgr.ManagerProfile()).Substring(1)}", GUILayout.Width(200));
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawProgramStats()
        {
            GUILayout.Box($"Program Dashboard (Staff: {storyEngine.GUIAverageProfile()})");

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Credibility:", GUILayout.Width(100));
            GUILayout.Label($"{storyEngine.GUIValuation()}", GUILayout.Width(100));
            GUILayout.Label($"Overvaluation:", GUILayout.Width(100));
            GUILayout.Label($"{storyEngine.GUIOvervaluation()} (Hype: {Math.Round(storyEngine._reputationManager.Hype(), MidpointRounding.ToEven)})", GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Peak:", GUILayout.Width(100));
            GUILayout.Label($"{storyEngine.GUIRelativeToPeak()}", GUILayout.Width(100));
            GUILayout.Label($"Space Craze:", GUILayout.Width(100));
            GUILayout.Label($"{storyEngine.GUISpaceCraze()}", GUILayout.Width(100));
            GUILayout.EndHorizontal();
            if (storyEngine.ongoingInquiry)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Public inquiry:", GUILayout.Width(100));
                GUILayout.Label($"{storyEngine.ongoingInquiry}");
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Headlines score:", GUILayout.Width(200));
            GUILayout.Label($"{Math.Round(storyEngine._reputationManager.GetScore(),2)} Rep * year");
            GUILayout.EndHorizontal();
            GUILayout.Space(20);
        }

        /// <summary>
        /// Contract view and controls for the program view.
        /// </summary>
        public void DrawContracts()
        {
            float ratio = 0f;

            Color originalColor = UnityEngine.GUI.contentColor;

            GUILayout.Box("Contracts (% hyped)");
            
            foreach (Contract myContract in ContractSystem.Instance.GetCurrentContracts<Contract>().OrderBy(x=>x.ReputationCompletion))
            {
                if (myContract.ContractState == Contract.State.Active)
                {
                    // Skip autoaccepted contracts 
                    if (myContract.AutoAccept & !_showAutoAcceptedContracts) continue;
                    
                    if (myContract.ReputationCompletion > 0)
                    {
                        ratio = (float)storyEngine._reputationManager.Hype() / myContract.ReputationCompletion;
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
                    GUILayout.Label($"{myContract.Title} (Cred: {myContract.ReputationCompletion}, {(int)Math.Ceiling(100f*ratio)}%)" );
                    GUILayout.EndHorizontal();
                }
                
            }
            
            UnityEngine.GUI.contentColor = originalColor;
            _showAutoAcceptedContracts = GUILayout.Toggle(_showAutoAcceptedContracts, "Show all contracts");

            GUILayout.Space(20);
        }

        public void DrawPressGallery()
        {
            switch (RepMgr.currentMode)
            {
                case MediaRelationMode.LOWPROFILE:
                    DrawPressGalleryLowProfile();
                    break;
                case MediaRelationMode.CAMPAIGN:
                    DrawPressGalleryCampaign();
                    break;
                case MediaRelationMode.LIVE:
                    DrawPressGalleryLive();
                    break;
            }
            
            GUILayout.Space(20);
        }

        public void DrawPressGalleryLowProfile()
        {
            GUILayout.Box("Media relation");
            if ((int)RepMgr.Hype() >= (int)RepMgr.MinimumHypeForInvite())
            {
                double cost = RepMgr.MediaCampaignCost(mediaInvitationDelay);
                
                GUILayout.BeginHorizontal();
                if (cost > Funding.Instance.Funds)
                {
                    mediaInvitationDelay -= 1;
                    GUILayout.Button($"Insufficient fund for ", GUILayout.Width(200));
                }
                else
                {
                    storyEngine.InvitePress(GUILayout.Button($"Invite Press (√{cost})", GUILayout.Width(200)), mediaInvitationDelay);
                }
                GUILayout.Label("  in ", GUILayout.Width(25));
                mediaInvitationDelay = Int32.Parse(GUILayout.TextField($"{mediaInvitationDelay}", GUILayout.Width(40)));
                GUILayout.Label("  days");
                GUILayout.EndHorizontal();
                GUILayout.Label($"  NB: Invite the press if you expect to exceed earnings of {Math.Round(RepMgr.Hype(),MidpointRounding.AwayFromZero)} on that day. They will report negatively otherwise.");
            }
            else
            {
                GUILayout.Label($"Right now, even if lunch is provided, no outlet cares enough to come.\nPress will come only if your hype is at least {(int)RepMgr.MinimumHypeForInvite()}.");
            }
        }
        
        public void DrawPressGalleryCampaign()
        {
            GUILayout.Box("Media campaign ongoing");
            double timeToLive = RepMgr.airTimeStarts - HeadlinesUtil.GetUT();
            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(10));
            GUILayout.Label("Public Event in", GUILayout.Width(100));
            GUILayout.Box($"{KSPUtil.PrintDateDeltaCompact(timeToLive, true, true)}", GUILayout.Width(150));
            GUILayout.Label($"    Hype:{Math.Round(RepMgr.CampaignHype(), MidpointRounding.AwayFromZero)}");
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Cancel Media Event"))
            {
                RepMgr.CancelMediaEvent();
            }

        }
        
        public void DrawPressGalleryLive()
        {
            GUILayout.Box("Media relation: We're live!");
            double timeToLive = RepMgr.airTimeEnds - HeadlinesUtil.GetUT();
            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(10));
            GUILayout.Label("Live for ", GUILayout.Width(100));
            GUILayout.Box($"{KSPUtil.PrintDateDeltaCompact(timeToLive, true, true)}", GUILayout.Width(150));
            GUILayout.Label($"  Cred. Target:{Math.Round(RepMgr.WageredCredibilityToGo(), MidpointRounding.AwayFromZero)}");
            GUILayout.EndHorizontal();
            
            if (RepMgr.EventSuccess())
            {
                if (GUILayout.Button("Call successful media debrief"))
                {
                    RepMgr.CallMediaDebrief();
                    //storyEngine.MediaEventUpdate();
                }
            }
            else
            {
                GUILayout.Label($"Awaiting {Math.Round(RepMgr.WageredCredibilityToGo(),MidpointRounding.AwayFromZero) } additional reputation points to be satisfied.", GUILayout.Width(380));
                if (GUILayout.Button("Dismiss the press gallery in shame"))
                {
                    RepMgr.CallMediaDebrief();
                    //storyEngine.MediaEventUpdate();
                }
            }
        }

        public void DrawImpact()
        {
            GUILayout.Box("Impact");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Capital Funding: {storyEngine.GUIFundraised()}", GUILayout.Width(200));
            GUILayout.Label($"Science Data   : {storyEngine.GUIVisitingSciencePercent()}%");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"VAB Boost: {storyEngine.GUIVABEnhancement()}", GUILayout.Width(200));
            GUILayout.Label($"R&D Boost: {storyEngine.GUIRnDEnhancement()}");
            GUILayout.EndHorizontal();
            if (storyEngine.visitingScholarEndTimes.Count != 0)
            {
                GUILayout.Label($"There are {storyEngine.visitingScholarEndTimes.Count} visiting scholar(s) in residence providing a science bonus of {Math.Round(storyEngine.VisitingScienceBonus()*100f)}% on new science data.");
            }
            GUILayout.Space(20);
        }
        
        #endregion

        /// <summary>
        /// Draw panel to manage media and reputation
        /// </summary>
        public void DrawPressRoom()
        {
            DrawContracts();
            DrawPressGallery();
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
            
            SwitchCrew(GUILayout.SelectionGrid(_selectedCrew, crewRoster.ToArray(), 3));
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
            GUILayout.Label($"Charisma: {focusCrew.EffectivenessLikability(true)}", GUILayout.Width(133));
            GUILayout.Label($"Training: {focusCrew.trainingLevel}", GUILayout.Width(133));
            GUILayout.Label($"Experience: {focusCrew.EffectivenessExperience()}", GUILayout.Width(133));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Personality: {focusCrew.EffectivenessPersonality()}", GUILayout.Width(133));
            GUILayout.Label($"Peers: {focusCrew.EffectivenessHumanFactors()}", GUILayout.Width(133));
            GUILayout.Label($"Mood: {focusCrew.EffectivenessMood()}", GUILayout.Width(133));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Net: {focusCrew.Effectiveness(deterministic:true)}", GUILayout.Width(133));
            GUILayout.Label($"Nation: {focusCrew.GetCulture()}");
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            
            // If untrained, offers to reassign
            if (focusCrew.trainingLevel + focusCrew.EffectivenessExperience() == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Reassign as: ");
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
                GUILayout.Label($"Immediate: {focusCrew.influence}", GUILayout.Width(133));
                GUILayout.Label($"Lasting: {focusCrew.teamInfluence}", GUILayout.Width(133));
                GUILayout.Label($"Legacy: {focusCrew.legacy}", GUILayout.Width(133));
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Box($"Impact on Space Program");
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Hype: {focusCrew.lifetimeHype}", GUILayout.Width(133));
            GUILayout.Label($"Scout: {focusCrew.numberScout}", GUILayout.Width(133));
            GUILayout.Label($"Funds: {focusCrew.fundRaised/1000}K", GUILayout.Width(133));
            GUILayout.EndHorizontal();
            GUILayout.Space(20);
            
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
            if (focusCrew.IsInactive())
            {
                double deltaTime = focusCrew.InactiveDeadline() - HeadlinesUtil.GetUT();
                GUILayout.Box($"Activity (inactive)");
                GUILayout.Label($"Earliest possible return: {KSPUtil.PrintDateDelta(deltaTime,false, false)}");
            }
            else
            {
                BuildActivityLabels(focusCrew.Specialty()); // inefficient
                GUILayout.Box($"Activity ({focusCrew.kerbalProductiveState})");
                _currentActivity = activityLabels.IndexOf(focusCrew.kerbalTask);
                _currentActivity = GUILayout.SelectionGrid(_currentActivity, activityLabels.ToArray(), 2);
                if (_currentActivity != activityLabels.IndexOf(focusCrew.kerbalTask))
                {
                    storyEngine.KerbalOrderTask(focusCrew, activityLabels[_currentActivity]);
                }

                focusCrew.coercedTask = GUILayout.Toggle(focusCrew.coercedTask, "Told what to do");
            }
            
            GUILayout.Space(10);
            GUILayout.Box("News feed");
            DrawFeedSection(true);
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

            // Avoid launching a search that get overwritten at the start of a career
            if(!storyEngine.hasnotvisitedAstronautComplex)
            {
                double searchCost = storyEngine.SearchCost();
                if (storyEngine.GetFunds() > searchCost)
                {
                    if (GUILayout.Button($"Open new search (${searchCost})"))
                    {
                        storyEngine.LaunchSearch(false);
                    }

                    searchCost = storyEngine.SearchCost(true);
                    if (storyEngine.GetFunds() > searchCost)
                    {
                        if (GUILayout.Button($"Contract a head hunter firm (${searchCost})"))
                        {
                            storyEngine.LaunchSearch(true);
                        }
                    }
                    else
                    {
                        GUILayout.Label($"Hiring a head hunter firm costs ${searchCost}");
                    }
                }
                else
                {
                    GUILayout.Label($"Open a search for ${searchCost}");
                }
            }
            
            if (storyEngine.programPayrollRebate > 0)
            {
                GUILayout.Box($"Hiring vouchers: {storyEngine.programPayrollRebate} X {storyEngine.HiringRebate()/1000},000 funds.");
                GUILayout.Space(5);
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

        public void DrawStoryPanel()
        {
            double clock = HeadlinesUtil.GetUT();
            
            GUILayout.BeginVertical();
            GUILayout.Box("Story Elements");
            if (GUILayout.Button("Possible debris falling in populated area"))
            {
                storyEngine.DebrisOverLand(true);
            }
            if (GUILayout.Button("Possible debris fallout over land"))
            {
                storyEngine.DebrisOverLand();
            }
            GUILayout.Space(10);
            
            GUILayout.Box("Beta testing");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add 5 Hype"))
            {
                storyEngine._reputationManager.AdjustHype(5);
            }
            if (GUILayout.Button("Add 5 Reputation"))
            {
                RepMgr.AdjustCredibility(5, reason:TransactionReasons.None);
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
            GUILayout.Box("Random processes");
            scrollHMMView = GUILayout.BeginScrollView(scrollHMMView, GUILayout.Width(400), GUILayout.Height(300));
            foreach (KeyValuePair<string, double> kvp in storyEngine._hmmScheduler)
            {
                GUILayout.Label($"{KSPUtil.PrintDateDeltaCompact(kvp.Value-clock, true, false)} - {kvp.Key}");
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        public void DrawProgramFeed()
        {
            FeedThreshold(GUILayout.SelectionGrid(feedThreshold, feedFilter, 4, GUILayout.Width(400)));

            DrawFeedSection();
            /*
            scrollFeedView = GUILayout.BeginScrollView(scrollFeedView, GUILayout.Width(400), GUILayout.Height(430));
            if (storyEngine.headlines.Count == 0)
            {
                GUILayout.Label("This is soon to become a busy feed. Enjoy the silence while it lasts.");
            }
            foreach (NewsStory ns in storyEngine.headlines.Reverse())
            {
                if ((int)ns.scope < feedThreshold + 1) continue;
                DrawHeadline(ns);
                GUILayout.Space(5);
            }
            GUILayout.EndScrollView();
            */
            GUILayout.Space(10);

            _reducedMessage = GUILayout.Toggle(storyEngine.notificationThreshold != HeadlineScope.NEWSLETTER,
                "Fewer messages");
            if (_reducedMessage) storyEngine.notificationThreshold = HeadlineScope.FEATURE;
            else storyEngine.notificationThreshold = HeadlineScope.NEWSLETTER;
            GUILayout.Space(20);
        }

        private void DrawFeedSection(bool crewSpecific = false)
        {
            int height = crewSpecific ? 100 : 430;
            scrollFeedView = GUILayout.BeginScrollView(scrollFeedView, GUILayout.Width(400), GUILayout.Height(height));
            if (storyEngine.headlines.Count == 0)
            {
                GUILayout.Label("This is soon to become a busy feed. Enjoy the silence while it lasts.");
            }
            foreach (NewsStory ns in storyEngine.headlines.Reverse())
            {
                if (crewSpecific)
                {
                    if (ns.HasActor(crewRoster[_selectedCrew]))
                    {
                        DrawHeadline(ns);
                    }
                }
                else
                {
                    if ((int)ns.scope < feedThreshold + 1) continue;
                    DrawHeadline(ns);
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawHeadline(NewsStory ns)
        {
            if (ns.headline == "")
            {
                GUILayout.Label($"{KSPUtil.PrintDate(ns.timestamp, false, false)} {ns.story}",GUILayout.Width(370));
            }
            else
            {
                GUILayout.Label($"{KSPUtil.PrintDate(ns.timestamp, false, false)} {ns.headline}");
                GUILayout.BeginHorizontal();
                GUILayout.Label("", GUILayout.Width(10));
                GUILayout.TextArea(ns.story, GUILayout.Width(360));
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(5);
        }
        #endregion

        #region Logic
        

        /// <summary>
        /// Pulls the data from PeopleManager and stores names to be used for drawing buttons and passing to DrawCrew.
        /// </summary>
        public void RefreshRoster()
        {
            peopleManager = storyEngine.GetPeopleManager();

            crewRoster = peopleManager.RankCrewMembers();
            /*
            crewRoster = new List<string>();
            foreach (KeyValuePair<string, PersonnelFile> kvp in peopleManager.personnelFolders)
            {
                crewRoster.Add(kvp.Value.UniqueName());
            }
            */
            
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

        private void FeedThreshold(int level)
        {
            feedThreshold = level;
            //storyEngine.feedThreshold = (HeadlineScope) (level + 1);
            feedFilterLabel = feedFilter[level];
            //SetFeedFilterLabel(feedThreshold);
        }

        private void SetFeedFilterLabel(int level)
        {
            switch (level)
            {
                case 1:
                    feedFilterLabel = "Everything";
                    break;
                case 2:
                    feedFilterLabel = "Newsletter";
                    break;
                case 3:
                    feedFilterLabel = "Feature stories";
                    break;
                case 4:
                    feedFilterLabel = "Headlines";
                    break;
            }
        }

        #endregion
    }
}
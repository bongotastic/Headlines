using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;
using HiddenMarkovProcess;
using KSP.UI.Screens;
using Headlines.source.Emissions;
using Smooth.Collections;
using UnityEngine;
using Enumerable = UniLinq.Enumerable;


namespace Headlines.source.GUI
{
    [KSPAddon(KSPAddon.Startup.FlightEditorAndKSC, false)]
    public class HeadlinesGUIManager : MonoBehaviour
    {
        #region declarations

        internal StoryEngine storyEngine;
        internal ReputationManager RepMgr;
        internal ProgramManager PrgMgr;
        private static ApplicationLauncherButton stockButton;

        public bool _isDisplayed = false;
        public bool _showAutoAcceptedContracts = false;
        public bool _reducedMessage = false;
        public bool _showDebug = false;
        
        private int _activeTabIndex = 0;
        private int _selectedCrew = 0;
        private int _currentActivity = 0;
        public int _priority = 0;

        private PeopleManager peopleManager;
        public List<string> crewRoster;
        private List<string> applicantRoster;
        
        //private List<string> tabLabels;
        private List<string> activityLabels;

        // location of the Window
        public Rect position;
        public bool resizePosition = true;

        private Vector2 scrollFeedView = new Vector2(0,0);
        private Vector2 scrollHMMView = new Vector2(0,0);
        private Vector2 scrollReleases = new Vector2(0, 0);
        private Vector2 scrollRelationships = new Vector2(0, 0);
        
        
        private int feedThreshold = 1;
        private string feedFilterLabel = "";

        private static string[] feedFilter = new[] { "All", "Chatter", "Feature stories", "Headlines"};
        private static string[] tabs = new[] { "Program", "Media", "Feed", "Personnel", "Recruit","Story"};
        private static string[] flightTabs = new[] { "Program", "Media", "Story"};
        public static string[] priorities = new[] { "Balanced", "Reputation", "Production", "Growth"};

        public int mediaInvitationDelay = 1;

        /// <summary>
        /// UISections
        /// </summary>
        private Dictionary<string, UIBox> _section = new Dictionary<string, UIBox>();
        
        #endregion

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
            position = new Rect(100f, 150f, 300f, 575f);
            
            _section.Add("ProgramCredibility", new UISectionProgramCredibility(this));
            _section.Add("ProgramManagement", new UISectionProgramManagement(this));
            _section.Add("ProgramPriority", new UISectionProgramPriority(this));
            _section.Add("ProgramImpact", new UISectionProgramImpact(this));
            _section.Add("MediaEvent", new UISectionMediaEvent(this));
            _section.Add("MediaContracts", new UISectionMediaContracts(this));
            _section.Add("MediaSecret", new UISectionMediaSecret(this));
            _section.Add("PersonnelProfile", new UISectionPersonnelProfile(this));
            _section.Add("PersonnelImpact", new UISectionPersonnelImpact(this));
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
            if (stockButton == null && HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
                stockButton = ApplicationLauncher.Instance.AddModApplication(
                    ShowWindow,
                    HideWindow,
                    null,
                    null,
                    null,
                    null,
                    ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.FLIGHT,
                    GameDatabase.Instance.GetTexture("Headlines/artwork/icons/crowdwatching28mask2", false)
                );

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

        #region Styling

        public readonly int widthUI = 400;
        public readonly int widthMargin = 15;

        public GUILayoutOption FullWidth() => GUILayout.Width(widthUI);
        public GUILayoutOption ThirdWidth() => GUILayout.Width(widthUI/3);
        public void GUIPad() => GUILayout.Space(5);

        public void Indent()
        {
            GUILayout.Label("", GUILayout.Width(widthMargin));
        }

        #endregion
        
        #region Panels
        
        public void DrawSection(string name)
        {
            if (_section.ContainsKey(name)) _section[name].Draw();
        }

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
                mediaInvitationDelay = (int)Math.Ceiling(storyEngine.GetNextLaunchDeltaTime() / (3600*24));
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
            if (!HighLogic.LoadedSceneIsFlight)
            {
                SwitchTab(GUILayout.SelectionGrid(_activeTabIndex, tabs, 3, GUILayout.Width(400)));
                switch (_activeTabIndex)
                {
                    case 0:
                        DrawProgramDashboard(windowID);
                        break;
                    case 1:
                        DrawPressRoom();
                        break;
                    case 2:
                        DrawProgramFeed();
                        break;
                    case 3:
                        DrawPersonelPanel();
                        break;
                    case 4:
                        DrawRecruitmentPanel();
                        break;
                    case 5:
                        DrawStoryPanel();
                        break;
                }
            }
            else
            {
                SwitchTab(GUILayout.SelectionGrid(_activeTabIndex, flightTabs, 3, GUILayout.Width(400)));
                switch (_activeTabIndex)
                {
                    case 0:
                        DrawProgramDashboard(windowID);
                        break;
                    case 1:
                        DrawPressRoom();
                        break;
                    case 2:
                        DrawStoryPanel();
                        break;
                }
            }

            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Close", FullWidth()))
            {
                //HideWindow();
                stockButton.SetFalse();
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
            RepMgr = storyEngine._reputationManager;
            if (PrgMgr == null)
            {
                PrgMgr = storyEngine._programManager;
                InitializePriority();
            }
            
            
            GUILayout.BeginVertical();
            DrawSection("ProgramCredibility");
            
            if (!HighLogic.LoadedSceneIsFlight)
            {
                DrawSection("ProgramManagement");
                DrawSection("ProgramPriority");
                DrawSection("ProgramImpact");
            }
            GUILayout.EndVertical();
        }
        

        #region Media tab

        
        
        /// <summary>
        /// Draw panel to manage media and reputation
        /// </summary>
        public void DrawPressRoom()
        {
            DrawSection("MediaEvent");
            if (RepMgr.shelvedAchievements.Count != 0)
            {
                DrawSection("MediaSecret");
            }
            DrawSection("MediaContracts");
            
        }



        #endregion

        #region Personnel

        /// <summary>
        /// Top-level UI for the Crew panel of the main UI
        /// </summary>
        public void DrawPersonelPanel()
        {
            RefreshRoster();

            if (crewRoster.Count == 0) return;
            
            GUILayout.BeginVertical();
            GUILayout.Box("Active crew");
            
            SwitchCrew(GUILayout.SelectionGrid(_selectedCrew, crewRoster.ToArray(), 3, FullWidth()));
            if (_selectedCrew >= crewRoster.Count)
            {
                _selectedCrew = 0;
            }
            
            GUIPad();
            //DrawCrew();
            DrawSection("PersonnelProfile");
            DrawSection("PersonnelImpact");
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

            // If untrained, offers to reassign
            if (focusCrew.trainingLevel + focusCrew.EffectivenessFame() == 0)
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
            else
            {
                if (PrgMgr.ManagerName() != focusCrew.UniqueName())
                {
                    if (focusCrew.Effectiveness(deterministic: true) > PrgMgr.ManagerProfile(deterministic:true))
                    {
                        GUILayout.BeginHorizontal();
                        Indent();
                        if (GUILayout.Button("Promote to Program Manager", GUILayout.Width(380)))
                        {
                            storyEngine.KerbalAppointProgramManager(focusCrew);
                        }
                        GUILayout.EndHorizontal();
                    }
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    Indent();
                    if (GUILayout.Button("Dismiss as Program Manager", GUILayout.Width(380)))
                    {
                        storyEngine.KerbalAppointProgramManager(null);
                    }
                    GUILayout.EndHorizontal();
                }
                
            }
            GUIPad();
            
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
            GUIPad();
            
            // Relationships
            int nLines = focusCrew.feuds.Count + focusCrew.collaborators.Count;
            if (nLines != 0)
            {
                GUILayout.Box($"Relationships");
                if (nLines >= 3)
                {
                    scrollRelationships = GUILayout.BeginScrollView(scrollRelationships, GUILayout.Width(400), GUILayout.Height(100));
                }
                foreach (string otherCrew in focusCrew.collaborators)
                {
                    DrawRelationship(peopleManager.GetFile(otherCrew), isFeud:false);
                }
                foreach (string otherCrew in focusCrew.feuds)
                {
                    DrawRelationship(peopleManager.GetFile(otherCrew), isFeud:true);
                }

                if (nLines >= 3)
                {
                    GUILayout.EndScrollView();
                }
                GUIPad();
            }
            
            // Activity controls
            if (focusCrew.UniqueName() != PrgMgr.ManagerName())
            {
                if (focusCrew.IsInactive())
                {
                    double deltaTime = focusCrew.InactiveDeadline() - HeadlinesUtil.GetUT();
                    GUILayout.Box($"Activity (inactive)");
                    GUILayout.Label($"Earliest possible return: {KSPUtil.PrintDateDelta(deltaTime,false, false)}");
                }
                else if (PrgMgr.ControlLevel() >= ProgramControlLevel.NOMINAL)
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
                else
                {
                    GUILayout.Box($"Activity ({focusCrew.kerbalProductiveState})");
                    GUILayout.Label($"Busy with {focusCrew.kerbalTask}. {PrgMgr.ManagerName()} needs to have at least nominal control to micromanage crew.", FullWidth());
                }
            }

            GUIPad();
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
            GUILayout.Label($"{peopleManager.QualitativeEffectiveness(crewMember.Effectiveness(deterministic:true))} {crewMember.Specialty()}", GUILayout.Width(190));
            
            GUILayout.EndHorizontal();
        }
        
        #endregion
        
        #region Feed

        public void DrawProgramFeed()
        {
            FeedThreshold(GUILayout.SelectionGrid(feedThreshold, feedFilter, 4, GUILayout.Width(400)));
            DrawFeedSection();
            GUIPad();

            _reducedMessage = GUILayout.Toggle(storyEngine.notificationThreshold != HeadlineScope.NEWSLETTER,
                "Fewer messages");
            if (_reducedMessage) storyEngine.notificationThreshold = HeadlineScope.FEATURE;
            else storyEngine.notificationThreshold = HeadlineScope.NEWSLETTER;
            GUIPad();
        }

        private void DrawFeedSection(bool crewSpecific = false)
        {
            bool drawfeed = false;
            int height = crewSpecific ? 100 : 430;
            if (storyEngine.headlines.Count == 0 & !crewSpecific)
            {
                GUILayout.Label("This is soon to become a busy feed. Enjoy the silence while it lasts.");
            }
            foreach (NewsStory ns in storyEngine.headlines.Reverse())
            {
                if (crewSpecific)
                {
                    if (ns.HasActor(crewRoster[_selectedCrew]))
                    {
                        if (!drawfeed)
                        {
                            GUILayout.Box("News feed");
                            scrollFeedView = GUILayout.BeginScrollView(scrollFeedView, GUILayout.Width(400), GUILayout.Height(height));
                            drawfeed = true;
                        }
                        DrawHeadline(ns);
                    }
                }
                else
                {
                    if ((int)ns.scope < feedThreshold + 1) continue;
                    if (!drawfeed)
                    {
                        scrollFeedView = GUILayout.BeginScrollView(scrollFeedView, GUILayout.Width(400), GUILayout.Height(height));
                        drawfeed = true;
                    }
                    DrawHeadline(ns);
                }
            }

            if (drawfeed)
            {
                GUILayout.EndScrollView();
            }
            
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
                    try
                    {
                        // There seems to be a concurency issue in some case when new applicants are added. Better to bail for one frame.
                        GUILayout.Label($"{kvp.Value.DisplayName()}", GUILayout.Width(150)); 
                        GUILayout.Label($"{peopleManager.QualitativeEffectiveness(kvp.Value.Effectiveness(deterministic:true))} {kvp.Value.Specialty()}", GUILayout.Width(120));
                        GUILayout.Label($"{kvp.Value.personality}", GUILayout.Width(60));
                    }
                    catch (Exception e)
                    {
                        HeadlinesUtil.Report(1, $"Unable to write the labels for applicant {kvp.Value.DisplayName()}");
                        Console.WriteLine(e);
                        throw;
                    }
                    
                }

                GUILayout.EndHorizontal();

            }

            if (nApplicant == 0)
            {
                GUIPad();
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
            
            GUIPad();
            
            GUILayout.Box("Walk-in applicants notifications");
            GUILayout.BeginHorizontal();
            peopleManager.seekingPilot = GUILayout.Toggle(peopleManager.seekingPilot, "Pilots");
            peopleManager.seekingScientist = GUILayout.Toggle(peopleManager.seekingScientist, "Scientists");
            peopleManager.seekingEngineer = GUILayout.Toggle(peopleManager.seekingEngineer, "Engineers");
            GUILayout.EndHorizontal();
            GUIPad();
            
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

            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                if (GUILayout.Button("High drama ending not badly") && RepMgr.currentMode == MediaRelationMode.LIVE && !storyEngine.highDramaReported)
                {
                    storyEngine.HighDramaEndingWell();
                }
                
                if (GUILayout.Button("Naked-eye sightings over urban area") && RepMgr.currentMode != MediaRelationMode.LOWPROFILE && !storyEngine.overUrbanReported)
                {
                    storyEngine.VisibleShowOverUrban();
                }
            }

            GUIPad();
            if (HighLogic.LoadedSceneIsFlight)
            {
                GUILayout.EndVertical();
                return;
            }
            
            if (_showDebug)
            {
                
                GUILayout.Box("Beta testing");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Add 5 Hype"))
                {
                    storyEngine._reputationManager.AdjustHype(5);
                }

                if (GUILayout.Button("Add 5 Reputation"))
                {
                    RepMgr.AdjustCredibility(5, reason: TransactionReasons.None);
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
                
                if (GUILayout.Button("Dump part categories"))
                {
                    foreach (PartCategories pc in storyEngine.GetPartCategoriesUnderResearch())
                    {
                        HeadlinesUtil.Report(1,$"{pc.ToString()}");
                    }
                }

                GUIPad();
                GUILayout.Box("Random processes");
                scrollHMMView = GUILayout.BeginScrollView(scrollHMMView, GUILayout.Width(400), GUILayout.Height(200));
                foreach (KeyValuePair<string, double> kvp in storyEngine._hmmScheduler)
                {
                    GUILayout.Label($"{KSPUtil.PrintDateDeltaCompact(kvp.Value - clock, true, false)} - {kvp.Key}");
                }

                GUILayout.EndScrollView();
            }

            bool temp = false;
            temp = GUILayout.Toggle(_showDebug, "Show debug controls");
            if (temp != _showDebug)
            {
                _showDebug = temp;
                resizePosition = true;
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

        public void OrderNewPriority(int buttonClicked)
        {
            if (buttonClicked == _priority) return;
            _priority = buttonClicked;
            ProgramPriority pp = ProgramPriority.NONE;
            
            switch (buttonClicked)
            {
                case 1:
                    pp = ProgramPriority.REPUTATION;
                    break;
                case 2:
                    pp = ProgramPriority.PRODUCTION;
                    break;
                case 3:
                    pp = ProgramPriority.CAPACITY;
                    break;
            }
            PrgMgr.OrderNewPriority(pp);
        }

        private void InitializePriority()
        {
            switch (PrgMgr.GetPriority())
            {
                case ProgramPriority.NONE:
                    _priority = 0;
                    break;
                case ProgramPriority.REPUTATION:
                    _priority = 1;
                    break;
                case ProgramPriority.PRODUCTION:
                    _priority = 2;
                    break;
                case ProgramPriority.CAPACITY:
                    _priority = 3;
                    break;
            }
        }

        public PersonnelFile GetFocusCrew()
        {
            return peopleManager.GetFile(crewRoster[_selectedCrew]);
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

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
        
        public static HeadlinesGUIManager Instance = null;

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
        public int _currentActivity = 0;
        public int _priority = 0;

        private PeopleManager peopleManager;
        public List<string> crewRoster;
        private List<string> applicantRoster;
        
        //private List<string> tabLabels;
        public List<string> activityLabels;

        // location of the Window
        public Rect position;
        public bool resizePosition = true;
        public bool stateRead = false;

        private Vector2 scrollFeedView = new Vector2(0,0);
        private Vector2 scrollHMMView = new Vector2(0,0);
        private Vector2 scrollReleases = new Vector2(0, 0);
        public Vector2 scrollRelationships = new Vector2(0, 0);
        
        
        private int feedThreshold = 1;
        private string feedFilterLabel = "";

        private static string[] feedFilter = new[] { "All", "Chatter", "Feature stories", "Headlines"};
        private static string[] tabs = new[] { "Program", "Media", "Feed", "Personnel", "Recruit","Story"};
        private static string[] flightTabs = new[] { "Program", "Media", "Story"};
        public static string[] priorities = new[] { "Balanced", "Reputation", "Production", "Growth"};

        public int mediaInvitationDelay = 1;
        public int mediaCampaignLength = 1;

        /// <summary>
        /// UISections
        /// </summary>
        private Dictionary<string, UISection> _section = new Dictionary<string, UISection>();
        
        private double  _debugRepDelta = 0;
        
        #endregion

        #region Unity stuff
        
        protected void Awake()
        {
            Instance = this;
            
            try
            {
                GameEvents.onGUIApplicationLauncherReady.Add(OnGuiAppLauncherReady);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Headlines] failed to register UIHolder.OnGuiAppLauncherReady");
                Debug.LogException(ex);
            }
            position = new Rect(100f, 150f, 300f, 575f);
        }
        
        public void Start()
        {
            storyEngine = StoryEngine.Instance;

            _section.Add("ProgramCredibility", new UISectionProgramCredibility(this));
            _section.Add("ProgramManagement", new UISectionProgramManagement(this));
            _section.Add("ProgramPriority", new UISectionProgramPriority(this));
            _section.Add("ProgramImpact", new UISectionProgramImpact(this));
            _section.Add("MediaEvent", new UISectionMediaEvent(this));
            _section.Add("MediaContracts", new UISectionMediaContracts(this));
            _section.Add("MediaSecret", new UISectionMediaSecret(this));
            _section.Add("PersonnelProfile", new UISectionPersonnelProfile(this));
            _section.Add("PersonnelImpact", new UISectionPersonnelImpact(this));
            _section.Add("PersonnelActivity", new UISectionPersonnelActivity(this));
            _section.Add("PersonnelRelationships", new UISectionPersonnelRelationships(this));
            _section.Add("PersonnelNewsFeed", new UISectionPersonnelNewsFeed(this));
            
            ReadStates();
        }

        protected void OnDestroy()
        {
            WriteState();
            try
            {
                GameEvents.onGUIApplicationLauncherReady.Remove(OnGuiAppLauncherReady);
                GameEvents.onGameSceneLoadRequested.Remove(OnSceneChange);
                GameEvents.onGameSceneSwitchRequested.Remove(OnSceneChange);
                GameEvents.onHideUI.Remove(HideWindow);
                GameEvents.onShowUI.Remove(ShowWindow);

                if (stockButton != null)
                    ApplicationLauncher.Instance.RemoveModApplication(stockButton);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        protected void OnMouseUp()
        {
            WriteState();
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
                GameEvents.onHideUI.Add(HideWindow);
                GameEvents.onShowUI.Add(ShowWindow);
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

        public void BuildActivityLabels(string role)
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
                if (!stateRead) ReadStates();
                
                position = GUILayout.Window(GetInstanceID(), position, DrawWindow, $"Headlines -- {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()}");
            }
        }

        private void ReadStates()
        {
            if (storyEngine.UIStates != null)
            {
                stateRead = true;
                foreach (ConfigNode.Value section in storyEngine.UIStates.values)
                {
                    if (_section.ContainsKey(section.name))
                    {
                        _section[section.name]._state = (UIBoxState)int.Parse(section.value);
                    }
                }
                
                // Window position
                float uiX = 200;
                float uiY = 200;
                HeadlinesUtil.SafeFloat("windowX", ref uiX, storyEngine.UIStates);
                HeadlinesUtil.SafeFloat("windowY", ref uiY, storyEngine.UIStates);
                if (uiX != 200 && uiY != 200)
                {
                    position.x = uiX;
                    position.y = uiY;
                }
            }
        }

        public void WriteState()
        {
            if (storyEngine == null)
            {
                storyEngine = StoryEngine.Instance;
            }
            
            storyEngine.UIStates = new ConfigNode("UIStates");
            foreach (KeyValuePair<string, UISection> kvp in _section)
            {
                storyEngine.UIStates.AddValue(kvp.Key, (int)kvp.Value._state);
            }
            
            // window position
            storyEngine.UIStates.AddValue("windowX", position.x);
            storyEngine.UIStates.AddValue("windowY", position.y);
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
                // Time to launch
                mediaInvitationDelay = (int)Math.Ceiling(storyEngine.GetNextLaunchDeltaTime() / (3600*24));
                // Assume 100% of time is for campaign
                mediaCampaignLength = mediaInvitationDelay;
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
                        DrawPersonnelPanel();
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

            if (!HighLogic.LoadedSceneIsFlight)
            {
                DrawSection("ProgramManagement");
                DrawSection("ProgramPriority");
                DrawSection("ProgramImpact");
            }
            else
            {
                DrawSection("ProgramManagement");
            }
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// Draw panel to manage media and reputation
        /// </summary>
        public void DrawPressRoom()
        {
            DrawSection("ProgramCredibility");
            DrawSection("MediaEvent");
            if (RepMgr.shelvedAchievements.Count != 0)
            {
                DrawSection("MediaSecret");
            }
            DrawSection("MediaContracts");
            
        }

        /// <summary>
        /// Top-level UI for the Crew panel of the main UI
        /// </summary>
        public void DrawPersonnelPanel()
        {
            RefreshRoster();

            if (crewRoster.Count == 0) return;
            
            // Crew member selector
            GUILayout.BeginVertical();
            GUILayout.Box("Active crew");
            
            SwitchCrew(GUILayout.SelectionGrid(_selectedCrew, crewRoster.ToArray(), 3, FullWidth()));
            if (_selectedCrew >= crewRoster.Count)
            {
                _selectedCrew = 0;
            }
            GUIPad();
            
            // Crew information part
            DrawSection("PersonnelProfile");
            DrawSection("PersonnelImpact");
            DrawSection("PersonnelActivity");
            if (GetFocusCrew().collaborators.Count + GetFocusCrew().feuds.Count != 0)
            {
                DrawSection("PersonnelRelationships");
            }

            if (storyEngine.GetNumberNewsAbout(crewRoster[_selectedCrew]) != 0)
            {
                DrawSection("PersonnelNewsFeed");
            }
            
            GUILayout.EndVertical();
     
        }

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

        public void DrawFeedSection(bool crewSpecific = false)
        {
            int width = crewSpecific ? widthUI - widthMargin - 10 :  widthUI;
            
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
                            scrollFeedView = GUILayout.BeginScrollView(scrollFeedView, GUILayout.Width(width), GUILayout.Height(height));
                            drawfeed = true;
                        }
                        DrawHeadline(ns, width);
                    }
                }
                else
                {
                    if ((int)ns.scope < feedThreshold + 1) continue;
                    if (!drawfeed)
                    {
                        scrollFeedView = GUILayout.BeginScrollView(scrollFeedView, GUILayout.Width(width), GUILayout.Height(height));
                        drawfeed = true;
                    }
                    DrawHeadline(ns, width);
                }
            }

            if (drawfeed)
            {
                GUILayout.EndScrollView();
            }
            
        }

        private void DrawHeadline(NewsStory ns, int width)
        {
            if (ns.headline == "")
            {
                GUILayout.Label($"{KSPUtil.PrintDate(ns.timestamp, false, false)} {ns.story}",GUILayout.Width(width-30));
            }
            else
            {
                GUILayout.Label($"{KSPUtil.PrintDate(ns.timestamp, false, false)} {ns.headline}");
                GUILayout.BeginHorizontal();
                GUILayout.Label("", GUILayout.Width(10));
                GUILayout.TextArea(ns.story, GUILayout.Width(width - 40));
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
                    GUILayout.Box("Recruitment activities", FullWidth());
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
                GUILayout.Box($"Hiring vouchers: {storyEngine.programPayrollRebate} X {storyEngine.HiringRebate()/1000},000 funds.", FullWidth());
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
                        Indent();
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
                        GUILayout.Label("Toby, your HR staff, has misplaced his key to the astronaut complex. Please enter then exit the complex so that he may interview the applicants waiting in line.", FullWidth());
                    }
                    else
                    {
                        GUILayout.Label("You may leave the complex while Toby does his job.", FullWidth());
                    }
                
                }
                else
                {
                    GUILayout.Label("There are no applicants on file. You will have to rely on chance walk-ins or spend money to find prospects.", FullWidth());
                }
            }
            
            foreach (var oldKey in toDelete)
            {
                peopleManager.applicantFolders.Remove(oldKey);
            }
            
            GUIPad();
            
            GUILayout.Box("Walk-in applicants notifications");
            GUILayout.BeginHorizontal();
            Indent();
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

            // Overall stack
            GUILayout.BeginVertical();
            GUILayout.Box("Story Elements");
            
            // Indent the buttons
            GUILayout.BeginHorizontal();
            Indent();
            
            // Button stack
            GUILayout.BeginVertical();
            
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
            GUILayout.EndVertical(); // end of button stack
            GUILayout.EndHorizontal(); // end of indentation
            
            GUIPad();
            if (!HighLogic.LoadedSceneIsFlight)
            {

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

                    if (GUILayout.Button("Remove long inactivity periods"))
                    {
                        foreach (var kvp in storyEngine.GetPeopleManager().personnelFolders)
                        {
                            if ((kvp.Value.InactiveDeadline() - HeadlinesUtil.GetUT()) / HeadlinesUtil.OneDay > 100)
                            {
                                kvp.Value.SetInactive(0);
                            }
                        }
                    }
                    
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Simulate Rep change"))
                    {
                        HeadlinesUtil.Report(2, $"Once {_debugRepDelta}, now {RepMgr.TransformReputation(_debugRepDelta)}");
                    }
                    if (GUILayout.Button("Simulate hype change"))
                    {
                        HeadlinesUtil.Report(2, $"Once {_debugRepDelta}, now {RepMgr.TransformReputation(_debugRepDelta, RepMgr.CurrentReputation())}");
                    }

                    _debugRepDelta = double.Parse(GUILayout.TextField($"{_debugRepDelta}"));
                    GUILayout.EndHorizontal();

                    GUIPad();
                    GUILayout.Box("Random processes");
                    scrollHMMView =
                        GUILayout.BeginScrollView(scrollHMMView, GUILayout.Width(400), GUILayout.Height(200));
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
            }

            GUILayout.EndVertical(); // primary end of the stack
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

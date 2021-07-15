using System;
using KSP.UI.Screens;
using UnityEngine;


namespace RPStoryteller.source.GUI
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class HeadlinesGUIManager : MonoBehaviour
    {
        private StoryEngine storyEngine;
        private ApplicationLauncherButton stockButton;
        private HeadlinesGUIRenderer guiRenderer;

        public bool _isDisplayed = false;

        public Rect position;
        private GUIStyle windowGUIStyle;


        public void Start()
        {
            HeadlinesUtil.Report(1,"Headlines GUI loading...");
            guiRenderer = new HeadlinesGUIRenderer(this);
            UpdateToolbarStock();
            
            storyEngine = StoryEngine.Instance;

            position = new Rect(100f, 100f, 200f, 200f);

            //windowGUIStyle = new GUIStyle();
            //windowGUIStyle.alignment = TextAnchor.UpperLeft;
        }

        private void UpdateToolbarStock()
        {
            HeadlinesUtil.Report(1,"Headlines GUI loading...");
            stockButton = ApplicationLauncher.Instance.AddModApplication(
                OpenWindow,
                CloseWindow,
                null,
                null,
                null,
                null,
                ApplicationLauncher.AppScenes.SPACECENTER,
                GameDatabase.Instance.GetTexture("Headlines/artwork/icons/crowdwatching2 ", false)
            );
            HeadlinesUtil.Report(1,"Headlines GUI loaded.");
            if (_isDisplayed)
                stockButton.SetTrue();
        }

        public void OpenWindow()
        {
            if (storyEngine == null)
            {
                storyEngine = StoryEngine.Instance;
                HeadlinesUtil.Report(1, $"Set storyEngine {storyEngine != null}");
            }
            
                if (_isDisplayed == false)
            {
                HeadlinesUtil.Report(1,"Button set to True", "GUI");
                storyEngine = StoryEngine.Instance;
                _isDisplayed = true;
                UpdateButtonIcon();
            }
        }

        public void CloseWindow()
        {
            if (_isDisplayed == true)
            {
                HeadlinesUtil.Report(1,"Button set to False", "GUI");
                _isDisplayed = false;
                UpdateButtonIcon();
            }
        }        
        
        private void UpdateButtonIcon()
        {
            if (stockButton != null)
            {
                if (_isDisplayed)
                {
                    stockButton.SetTrue();
                }
                else
                {
                    stockButton.SetFalse();
                }
            }
        }

        public void OnGUI()
        {
            if (_isDisplayed)
            {
                position = GUILayout.Window(GetInstanceID(), position, DrawWindow, "Headlines");
            }
        }

        public void DrawWindow(int windowID)
        {
            DrawProgramDashboard(windowID);
        }

        public void DrawProgramDashboard(int windowID)
        {
            GUILayout.BeginVertical();
            
            GUILayout.Box("Program Dashboard");

            GUILayout.Label($"   Reputation: {storyEngine.GUIReputation()}");
            GUILayout.Label($"Overvaluation: {storyEngine.GUIOvervaluation()} (Hype: {Math.Round(storyEngine.programHype, MidpointRounding.ToEven)})");
            GUILayout.Label($"         Peak: {storyEngine.GUIRelativeToPeak()}");
            GUILayout.Label($"  Space Craze: {storyEngine.GUISpaceCraze()}");
            GUILayout.Space(10);
            
            GUILayout.Box("Contracts");
            GUILayout.Space(10);
            
            GUILayout.Box("Impact");
            GUILayout.Space(10);
            
            GUILayout.EndVertical();
        }
    }
}
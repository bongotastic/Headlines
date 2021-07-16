using System;
using Contracts;
using KSP.UI.Screens;
using UnityEngine;


namespace RPStoryteller.source.GUI
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class HeadlinesGUIManager : MonoBehaviour
    {
        private StoryEngine storyEngine;
        private static ApplicationLauncherButton stockButton;

        public bool _isDisplayed = false;

        public Rect position;



        public void Start()
        {
            UpdateToolbarStock();
            
            storyEngine = StoryEngine.Instance;

            position = new Rect(100f, 100f, 200f, 200f);

        }

        private void UpdateToolbarStock()
        {
            if (stockButton == null)
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
                ApplicationLauncher.Instance.AddOnHideCallback(HideButton);
                HeadlinesUtil.Report(1,"Headlines GUI loaded.");
                if (_isDisplayed)
                    stockButton.SetTrue();
            }
            else
            {
                stockButton.SetTrue();
            }
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
            else
            {
                _isDisplayed = false;
            }
        }        
        
        public void HideButton()
        {
            CloseWindow();
            ApplicationLauncher.Instance.RemoveModApplication(stockButton);
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
            
            GUILayout.Box($"Program Dashboard ({storyEngine.GUIAverageProfile()})");

            GUILayout.Label($"   Reputation: {storyEngine.GUIReputation()}");
            GUILayout.Label($"Overvaluation: {storyEngine.GUIOvervaluation()} (Hype: {Math.Round(storyEngine.programHype, MidpointRounding.ToEven)})");
            GUILayout.Label($"         Peak: {storyEngine.GUIRelativeToPeak()}");
            GUILayout.Label($"  Space Craze: {storyEngine.GUISpaceCraze()}");
            GUILayout.Space(10);
            
            GUILayout.Box("Contracts (% hyped)");
            DrawContracts();
            GUILayout.Space(10);
            
            GUILayout.Box("Impact");
            GUILayout.Label($"Capital Funding: {storyEngine.GUIFundraised()}");
            GUILayout.Label($"Science Data   : {storyEngine.GUIVisitingSciencePercent()}%");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"VAB Boost: {storyEngine.GUIVABEnhancement()}");
            GUILayout.Label($"R&D Boost: {storyEngine.GUIRnDEnhancement()}");
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        public void DrawContracts()
        {
            float ratio = 0f;

            HeadlinesUtil.Report(1, "DrawContract");
            
            foreach (Contract myContract in ContractSystem.Instance.GetCurrentContracts<Contract>())
            {
                HeadlinesUtil.Report(1, $"{myContract.ToString()}");
                if (myContract.ContractState == Contract.State.Active)
                {
                    ratio = storyEngine.programHype / myContract.ReputationCompletion;
                    /*
                    if (ratio >= 1f)
                    {
                        myStyle.font.material.color = Color.green;
                    }
                    else if (ratio >= 0.5f)
                    {
                        myStyle.font.material.color = Color.yellow;
                    }
                    else myStyle.font.material.color = Color.red;
                    */
                    GUILayout.BeginHorizontal();
                    GUILayout.Button("Pledge");
                    GUILayout.Label($"{myContract.Title} ({(int)Math.Ceiling(100f*ratio)}%)" );
                    GUILayout.EndHorizontal();
                }
                
            }
            
            // pledge Clock (if applicable) and total tally 
        }
    }
}
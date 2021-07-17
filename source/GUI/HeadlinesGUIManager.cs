using System;
using System.Collections.Generic;
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
        private bool _programTab = true;
        private bool _crewTab = false;
        private int _selectedCrew = 0;

        private PeopleManager peopleManager;
        private List<string> crewRoster;
        private List<string> tabLabels;

        public Rect position;



        public void Start()
        {
            UpdateToolbarStock();
            
            storyEngine = StoryEngine.Instance;

            position = new Rect(100f, 100f, 300f, 200f);

        }

        private void UpdateToolbarStock()
        {
            if (stockButton == null)
            {
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
            }
            
            if (_isDisplayed == false)
            {
                storyEngine = StoryEngine.Instance;
                _isDisplayed = true;
                UpdateButtonIcon();
            }
        }

        public void CloseWindow()
        {
            if (_isDisplayed == true)
            {
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
            // Tab area
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Program"))
            {
                _programTab = true;
                _crewTab = false;
            }
            else if (GUILayout.Button("Personnel"))
            {
                _programTab = false;
                _crewTab = true;
            }
            GUILayout.EndHorizontal();

            if (_programTab)
            {
                DrawProgramDashboard(windowID);
            }
            else if (_crewTab)
            {
                DrawPersonelPanel();
            }
            
            
            
        }

        /// <summary>
        /// Display program information and (eventually) the pledging mechanism 
        /// </summary>
        /// <param name="windowID"></param>
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

        /// <summary>
        /// Contract view and controls for the program view.
        /// </summary>
        public void DrawContracts()
        {
            float ratio = 0f;
            

            Color originalColor = UnityEngine.GUI.contentColor;

            foreach (Contract myContract in ContractSystem.Instance.GetCurrentContracts<Contract>())
            {
                if (myContract.ContractState == Contract.State.Active)
                {
                    ratio = storyEngine.programHype / myContract.ReputationCompletion;
                    
                    if (ratio >= 1f)
                    {
                        UnityEngine.GUI.contentColor = Color.green;
                    }
                    else if (ratio >= 0.5f)
                    {
                        UnityEngine.GUI.contentColor = Color.gray;
                    }
                    else UnityEngine.GUI.contentColor = Color.black;
                    
                    GUILayout.BeginHorizontal();
                    //GUILayout.Button("Pledge");
                    GUILayout.Label($"{myContract.Title} ({(int)Math.Ceiling(100f*ratio)}%)" );
                    GUILayout.EndHorizontal();
                }
                
            }

            UnityEngine.GUI.contentColor = originalColor;

            // pledge Clock (if applicable) and total tally 
        }

        public void DrawPersonelPanel()
        {
            RefreshRoster();
            
            GUILayout.BeginVertical();
            _selectedCrew = GUILayout.SelectionGrid(_selectedCrew, crewRoster.ToArray(), 3);
            DrawCrew();
            GUILayout.EndVertical();
        }

        public void RefreshRoster()
        {
            peopleManager = storyEngine.GetPeopleManager();

            crewRoster = new List<string>();
            foreach (KeyValuePair<string, PersonnelFile> kvp in peopleManager.personnelFolders)
            {
                crewRoster.Add(kvp.Value.DisplayName());
            }
        }

        public PersonnelFile GetFileFromDisplay(string displayName)
        {
            foreach (KeyValuePair<string, PersonnelFile> kvp in peopleManager.personnelFolders)
            {
                if (kvp.Value.DisplayName() == displayName) return kvp.Value;
            }

            return null;
        }

        public void DrawCrew()
        {
            string crewName = crewRoster[_selectedCrew];
            PersonnelFile focusCrew = GetFileFromDisplay(crewName);
            GUILayout.Box(focusCrew.DisplayName());
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Effectiveness: {peopleManager.QualitativeEffectiveness(focusCrew.Effectiveness(deterministic:true))}");
            GUILayout.Label($"training: {focusCrew.trainingLevel}");
            GUILayout.Label($"Discontent: {focusCrew.discontent}");
            GUILayout.EndHorizontal();
            
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
            }
            
            // Activity controls
            GUILayout.Box($"Activity ({focusCrew.kerbalProductiveState})");
        }

        public void DrawRelationship(PersonnelFile crewMember, bool isFeud = false)
        {
            
            
            GUILayout.BeginHorizontal();
            Color oldColor = UnityEngine.GUI.contentColor;
            if (isFeud == true)
            {
                UnityEngine.GUI.contentColor = Color.red;
                GUILayout.Label("[FEUD]");
            }
            else
            {
                UnityEngine.GUI.contentColor = Color.green;
                GUILayout.Label("[COLL]");
            }
            UnityEngine.GUI.contentColor = oldColor;
            
            GUILayout.Label($"{crewMember.DisplayName()}");
            GUILayout.Label($"{crewMember.Specialty()} ( ({peopleManager.QualitativeEffectiveness(crewMember.Effectiveness(deterministic:true))})");
            
            GUILayout.EndHorizontal();

            
        }
    }
}
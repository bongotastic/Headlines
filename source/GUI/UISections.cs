using System;
using CommNet.Network;
using Headlines.source.GUI;
using UnityEngine;

namespace Headlines.source.GUI
{
    /// <summary>
    /// Draws details about credibility, hype and the space race.
    /// </summary>
    public class UISectionProgramCredibility : UIBox
    {
        public UISectionProgramCredibility(HeadlinesGUIManager root, bool isFullwidth = false) : base(root, isFullwidth)
        {
            hasCompact = true;
            hasExtended = true;
            hasHelp = true;
        }

        protected override string HeadString()
        {
            return $"Program Dashboard (Staff: {_root.storyEngine.GUIAverageProfile()})";
        }

        protected override void DrawCompact()
        {
            GUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Credibility:", GUILayout.Width(80));
            GUILayout.Label($"{_root.storyEngine.GUIValuation()}", GUILayout.Width(100));
            GUILayout.Label($"Overvaluation:", GUILayout.Width(85));
            GUILayout.Label($"{_root.storyEngine.GUIOvervaluation()} (Hype: {Math.Round(_root.storyEngine._reputationManager.Hype(), MidpointRounding.ToEven)})", GUILayout.Width(95));
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        protected override void DrawExtended()
        {
            GUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Credibility:", GUILayout.Width(80));
            GUILayout.Label($"{_root.storyEngine.GUIValuation()}", GUILayout.Width(100));
            GUILayout.Label($"Overvaluation:", GUILayout.Width(85));
            GUILayout.Label($"{_root.storyEngine.GUIOvervaluation()} (Hype: {Math.Round(_root.storyEngine._reputationManager.Hype(), MidpointRounding.ToEven)})", GUILayout.Width(95));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Peak:", GUILayout.Width(80));
            GUILayout.Label($"{_root.storyEngine.GUIRelativeToPeak()}", GUILayout.Width(100));
            GUILayout.Label($"Space Craze:", GUILayout.Width(80));
            GUILayout.Label($"{_root.storyEngine.GUISpaceCraze()}", GUILayout.Width(100));
            GUILayout.EndHorizontal();
            if (_root.storyEngine.ongoingInquiry)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Public inquiry:", GUILayout.Width(100));
                GUILayout.Label($"{_root.storyEngine.ongoingInquiry}");
                GUILayout.EndHorizontal();
            }
            _root.GUIPad();
            GUILayout.EndVertical();
        }
        
        protected override void DrawHelp()
        {
            GUILayout.Label("Hype is more volatile than real reputation when your overvaluation is large.", GUILayout.Width(_root.widthUI - _root.widthMargin));
        }
    }
    
    /// <summary>
    /// Template for deriving a class
    /// </summary>
    public class UISectionProgramManagement : UIBox
    {
        public UISectionProgramManagement(HeadlinesGUIManager root, bool isFullwidth = false) : base(root, isFullwidth)
        {
            hasCompact = true;
            hasExtended = true;
            hasHelp = false;

            _state = UIBoxState.COMPACT;
        }

        protected override string HeadString()
        {
            return $"Program status: {_root.PrgMgr.ControlLevelQualitative()}";
        }

        protected override void DrawCompact()
        {
            GUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Manager: {_root.PrgMgr.ManagerName()}", GUILayout.Width(sectionWidth/2));
            GUILayout.Label($"Suitability: {_root.storyEngine.GetPeopleManager().QualitativeEffectiveness(_root.PrgMgr.ManagerProfile()-_root.storyEngine.GetProgramComplexity())}", GUILayout.Width(sectionWidth/2));
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        protected override void DrawExtended()
        {
            peopleManager = storyEngine.GetPeopleManager();
            GUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Manager: {PrgMgr.ManagerName()}", GUILayout.Width(sectionWidth/2));
            GUILayout.Label($"Profile: {peopleManager.QualitativeEffectiveness(PrgMgr.ManagerProfile())}", GUILayout.Width(sectionWidth/2));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Suitability: {peopleManager.QualitativeEffectiveness(PrgMgr.ManagerProfile()-storyEngine.GetProgramComplexity())}", GUILayout.Width(sectionWidth/2));
            string qualifier = PrgMgr.ManagerLaunches() <= 2 ? "[GREEN]" : PrgMgr.ManagerLaunches() >= 8 ? "[VETERAN]" : "";
            GUILayout.Label($"Launches: {PrgMgr.ManagerLaunches()} {qualifier}", GUILayout.Width(sectionWidth/2));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            if (PrgMgr.ManagerPersonality() != "")
            {
                GUILayout.Label($"Trait: {PrgMgr.ManagerPersonality()}", GUILayout.Width(sectionWidth/2));
            }
            GUILayout.Label($"Background: {PrgMgr.ManagerBackground()}", GUILayout.Width(sectionWidth/2));
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
            
            _root.GUIPad();
        }
        
        protected override void DrawHelp()
        {
            
        }
    }
    
    /// <summary>
    /// Template for deriving a class
    /// </summary>
    public class UISectionProgramPriority : UIBox
    {
        public UISectionProgramPriority(HeadlinesGUIManager root, bool isFullwidth = false) : base(root, isFullwidth)
        {
            hasCompact = true;
            hasExtended = true;
            hasHelp = false;

            _state = UIBoxState.COMPACT;
        }

        protected override string HeadString()
        {
            return "Program Priority";
        }

        protected override void DrawCompact()
        {
            GUILayout.BeginVertical();
            
            _root.OrderNewPriority(GUILayout.SelectionGrid(_root._priority, HeadlinesGUIManager.priorities,4 , GUILayout.Width(sectionWidth)));
            
            GUILayout.EndVertical();
        }

        protected override void DrawExtended()
        {
            GUILayout.BeginVertical();
            _root.OrderNewPriority(GUILayout.SelectionGrid(_root._priority, HeadlinesGUIManager.priorities,4 , FullWidth()));
            
            string UIhint = "";
            string verb = "is";
            if (PrgMgr.ControlLevel() <= ProgramControlLevel.WEAK)
            {
                UIhint = $"{PrgMgr.ManagerName()} lacks focus and the program is running as balanced. ";
                verb = "should be";
            }

            switch (PrgMgr.GetPriority())
            {
                case ProgramPriority.NONE:
                    UIhint += "A balanced program requires everyone to pursue their personal goals and do their best.";
                    break;
                case ProgramPriority.REPUTATION:
                    UIhint += $"The focus {verb} on building reputation at the expense of other KSC activities.";
                    break;
                case ProgramPriority.PRODUCTION:
                    UIhint += $"The focus {verb} on research and vehicle assembly at the expense of medium- and long-term activities..";
                    break;
                case ProgramPriority.CAPACITY:
                    UIhint += $"{PrgMgr.ManagerName()} {verb} focussing on capacity building for the future.";
                    break;
            }
            GUILayout.Label(UIhint, FullWidth());
            _root.GUIPad();
            GUILayout.EndVertical();
        }
        
        protected override void DrawHelp()
        {
            
        }
    }
    
    
    /// <summary>
    /// Template for deriving a class
    /// </summary>
    public class UISectionProgramImpact : UIBox
    {
        public UISectionProgramImpact(HeadlinesGUIManager root, bool isFullwidth = false) : base(root, isFullwidth)
        {
            hasCompact = true;
            hasExtended = true;
            hasHelp = false;

            _state = UIBoxState.COMPACT;
        }

        protected override string HeadString()
        {
            return $"Impact on Career";
        }

        protected override void DrawCompact()
        {
            GUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"VAB Boost: {storyEngine.GUIVABEnhancement()}", GUILayout.Width(sectionWidth/2));
            GUILayout.Label($"R&D Boost: {storyEngine.GUIRnDEnhancement()}", GUILayout.Width(sectionWidth/2));
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        protected override void DrawExtended()
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Capital Funding: {storyEngine.GUIFundraised()}", GUILayout.Width(sectionWidth/2));
            GUILayout.Label($"Science Data   : {storyEngine.GUIVisitingSciencePercent()}%", GUILayout.Width(sectionWidth/2));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"VAB Boost: {storyEngine.GUIVABEnhancement()}", GUILayout.Width(sectionWidth/2));
            GUILayout.Label($"R&D Boost: {storyEngine.GUIRnDEnhancement()}", GUILayout.Width(sectionWidth/2));
            GUILayout.EndHorizontal();
            
            if (storyEngine.visitingScholarEndTimes.Count != 0)
            {
                GUILayout.Label($"There are {storyEngine.visitingScholarEndTimes.Count} visiting scholar(s) in residence providing a science bonus of {Math.Round(storyEngine.VisitingScienceBonus()*100f)}% on new science data.", FullWidth());
            }
            _root.GUIPad();
            GUILayout.EndVertical();
        }
        
        protected override void DrawHelp()
        {
            
        }
    }

    
    /// <summary>
    /// Template for deriving a class
    /// </summary>
    public class UISectionTemplate : UIBox
    {
        public UISectionTemplate(HeadlinesGUIManager root, bool isFullwidth = false) : base(root, isFullwidth)
        {
            hasCompact = false;
            hasExtended = false;
            hasHelp = false;

            _state = UIBoxState.COMPACT;
        }

        protected override string HeadString()
        {
            return $"";
        }

        protected override void DrawCompact()
        {
            GUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();
            
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        protected override void DrawExtended()
        {
            GUILayout.BeginVertical();
            GUILayout.EndVertical();
        }
        
        protected override void DrawHelp()
        {
            
        }
    }
}
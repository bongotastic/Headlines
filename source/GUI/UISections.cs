﻿using System;
using CommNet.Network;
using Contracts;
using Headlines.source.Emissions;
using Headlines.source.GUI;
using UniLinq;
using UnityEngine;

namespace Headlines.source.GUI
{
    #region Program UI
    
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
    #endregion

    #region Media UI

    public class UISectionMediaEvent : UIBox
    {
        public UISectionMediaEvent(HeadlinesGUIManager root, bool isFullwidth = false) : base(root, isFullwidth)
        {
            hasCompact = false;
            hasExtended = true;
            hasHelp = false;

            _state = UIBoxState.EXTENDED;
        }

        protected override string HeadString()
        {
            switch (RepMgr.currentMode)
            {
                case MediaRelationMode.LOWPROFILE:
                    return "Plan a media campaign";
                case MediaRelationMode.CAMPAIGN:
                    return "Executing a media campaign";
                case MediaRelationMode.LIVE:
                    return "We are live!";
            }

            return "Media";
        }

        protected override void DrawCompact()
        {
        }

        protected override void DrawExtended()
        {
            GUILayout.BeginVertical();
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
            DrawPressGalleryContractList();
            
            _root.GUIPad();
            GUILayout.EndVertical();
        }
        
        protected override void DrawHelp()
        {
            
        }
        
        public void DrawPressGalleryLowProfile()
        {
            double cost = RepMgr.MediaCampaignCost(_root.mediaInvitationDelay);
            
            GUILayout.BeginHorizontal();
            if (cost > Funding.Instance.Funds)
            {
                _root.mediaInvitationDelay -= 1;
                GUILayout.Button($"Insufficient fund for ", GUILayout.Width(200));
            }
            else
            {
                storyEngine.InvitePress(GUILayout.Button($"Invite Press (√{cost})", GUILayout.Width(200)), _root.mediaInvitationDelay);
            }
            GUILayout.Label("  in ", GUILayout.Width(25));
            _root.mediaInvitationDelay = Math.Max(Int32.Parse(GUILayout.TextField($"{_root.mediaInvitationDelay}", GUILayout.Width(40))), 1);
            GUILayout.Label("  days");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"NB: Invite the press if you expect to exceed earnings of {Math.Round(RepMgr.GetMediaEventWager(),MidpointRounding.AwayFromZero)} on that day. They will report negatively otherwise.", GUILayout.Width(sectionWidth-20));
            GUILayout.EndHorizontal();
            
        }
        
        public void DrawPressGalleryCampaign()
        {
            double now = HeadlinesUtil.GetUT();
            
            double timeToLive = RepMgr.airTimeStarts - HeadlinesUtil.GetUT();
            GUILayout.BeginHorizontal();

            if (now <= RepMgr.airTimeStarts)
            {
                GUILayout.Label("Earliest event:", GUILayout.Width(100));
            }
            else
            {
                GUILayout.Label("Latest event:", GUILayout.Width(100));
                timeToLive = RepMgr.airTimeEnds - HeadlinesUtil.GetUT();
            }
            GUILayout.Box($"{KSPUtil.PrintDateDeltaCompact(timeToLive, true, true)}", GUILayout.Width(135));
            GUILayout.Label($"    Hype:{Math.Round(RepMgr.CampaignHype(), MidpointRounding.AwayFromZero)}", GUILayout.Width(120));
            GUILayout.EndHorizontal();
            
            if (GUILayout.Button("Cancel Media Event", FullWidth()))
            {
                
                RepMgr.CancelMediaEvent();
            }

            if (now >= RepMgr.airTimeStarts)
            {

                if (GUILayout.Button("Manual Kickoff of Media Event", FullWidth()))
                {
                    RepMgr.GoLIVE();
                }
            }
            _root.GUIPad();
        }
        
        public void DrawPressGalleryLive()
        {
            GUILayout.Box("Media relation: We're live!");
            double timeToLive = RepMgr.airTimeEnds - HeadlinesUtil.GetUT();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Live for ", GUILayout.Width(100));
            GUILayout.Box($"{KSPUtil.PrintDateDeltaCompact(timeToLive, true, false)}", GUILayout.Width(135));
            if (RepMgr.WageredCredibilityToGo() > 0)
            {
                GUILayout.Label($"  Cred. Target:{Math.Round(RepMgr.WageredCredibilityToGo(), MidpointRounding.AwayFromZero)}", GUILayout.Width(120));
            }
            else
            {
                GUILayout.Label($"  Exceeded by:{Math.Round(RepMgr.WageredCredibilityToGo()*-1, MidpointRounding.AwayFromZero)}", GUILayout.Width(120));
            }
            GUILayout.EndHorizontal();
            
            if (RepMgr.EventSuccess())
            {
                if (GUILayout.Button("Call successful media debrief", GUILayout.Width(sectionWidth-20)))
                {
                    RepMgr.CallMediaDebrief();
                }
            }
            else
            {
                GUILayout.Label($"Awaiting {Math.Round(RepMgr.WageredCredibilityToGo(),MidpointRounding.AwayFromZero) } additional reputation points to be satisfied.", GUILayout.Width(sectionWidth-20));
                if (GUILayout.Button("Dismiss the press gallery in shame"))
                {
                    RepMgr.CallMediaDebrief();

                }
            }
        }

        public void DrawPressGalleryContractList()
        {
            
            if (RepMgr.mediaContracts.Count == 0) return;
            
            GUILayout.Box($"Pledged objectives: {RepMgr.GetMediaEventWager()} reputation");
            foreach (Contract contract in RepMgr.mediaContracts.OrderByDescending(c=>c.ReputationCompletion))
            {
                DrawPressGalleryContractItem(contract);
            }
            
        }
        public void DrawPressGalleryContractItem(Contract contract)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Toggle(contract.ReputationCompletion <= RepMgr.Hype(), $"   {contract.Title} ({contract.ReputationCompletion})", GUILayout.Width(sectionWidth));
            GUILayout.EndHorizontal();
        }
    }
    
    public class UISectionMediaContracts : UIBox
    {
        public UISectionMediaContracts(HeadlinesGUIManager root, bool isFullwidth = false) : base(root, isFullwidth)
        {
            hasCompact = true;
            hasExtended = true;
            hasHelp = false;

            _state = UIBoxState.COMPACT;
        }

        protected override string HeadString()
        {
            return "Contracts (% hyped)";
        }

        protected override void DrawCompact()
        {
            GUILayout.BeginVertical();
            
            DrawContracts(true);
            
            GUILayout.EndVertical();
        }

        protected override void DrawExtended()
        {
            GUILayout.BeginVertical();
            DrawContracts(false);
            GUILayout.EndVertical();
        }
        
        protected override void DrawHelp()
        {
            
        }
        
        public void DrawContracts(bool isCompact)
        {
            float ratio = 0f;
            
            Color originalColor = UnityEngine.GUI.contentColor;

            foreach (Contract myContract in ContractSystem.Instance.GetCurrentContracts<Contract>().OrderBy(x=>x.ReputationCompletion))
            {
                if (myContract.ContractState == Contract.State.Active)
                {
                    
                    // Do no show pledged contracts
                    if (RepMgr.currentMode != MediaRelationMode.LOWPROFILE & RepMgr.mediaContracts.Contains(myContract)) continue;
                    
                    // Skip autoaccepted contracts 
                    if (myContract.AutoAccept & isCompact) continue;

                    GUILayout.BeginHorizontal();
                    if (RepMgr.currentMode == MediaRelationMode.LOWPROFILE)
                    {
                        if (RepMgr.mediaContracts.Contains(myContract))
                        {
                            if (GUILayout.Button("-", GUILayout.Width(20)))
                            {
                                RepMgr.WithdrawContractFromMediaEvent(myContract);
                            }
                        }
                        else
                        {
                            if (GUILayout.Button("+", GUILayout.Width(20)))
                            {
                                RepMgr.AttachContractToMediaEvent(myContract);
                            }
                        }
                    }
                    
                    if (myContract.ReputationCompletion > 0)
                    {
                        ratio = (float)storyEngine._reputationManager.Hype() / myContract.ReputationCompletion;
                    }
                    else
                    {
                        ratio = 1f;
                    }

                    string ratioString = $"({(int)Math.Ceiling(100f*ratio)}%)";
                    if (ratio >= 1f)
                    {
                        UnityEngine.GUI.contentColor = Color.green;
                        ratioString = $"({Math.Round(ratio, (ratio > 5f) ? 0 : 1, MidpointRounding.AwayFromZero)}X)";
                    }

                    else if (ratio >= 0.5f)
                    {
                        UnityEngine.GUI.contentColor = Color.yellow;
                    }
                    else UnityEngine.GUI.contentColor = Color.red;
                    
                    GUILayout.Label($"{myContract.Title} (Cred: {myContract.ReputationCompletion}, {ratioString}" , GUILayout.Width(sectionWidth-20));
                    UnityEngine.GUI.contentColor = originalColor;
                    
                    GUILayout.EndHorizontal();
                }
                
            }

            _root.GUIPad();
        }
    }
    
    public class UISectionMediaSecret : UIBox
    {
        public UISectionMediaSecret(HeadlinesGUIManager root, bool isFullwidth = false) : base(root, isFullwidth)
        {
            hasCompact = false;
            hasExtended = true;
            hasHelp = false;

            _state = UIBoxState.EXTENDED;
        }

        protected override string HeadString()
        {
            return "Secret achievements (rep. value)";
        }

        protected override void DrawCompact()
        {
        }

        protected override void DrawExtended()
        {
            GUILayout.BeginVertical();
            foreach (NewsStory ns in RepMgr.shelvedAchievements.OrderByDescending(x=>x.reputationValue))
            {
                DrawUnreleasedNews(ns);
            }
            GUILayout.EndVertical();
        }
        
        public void DrawUnreleasedNews(NewsStory ns)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Release", GUILayout.Width(60)))
            {
                storyEngine.IssuePressRelease(ns);
            }
            GUILayout.Label($"{ns.headline} ({Math.Round(ns.reputationValue,1,MidpointRounding.AwayFromZero)})", GUILayout.Width(sectionWidth-60));
            GUILayout.EndHorizontal();
        }
        
        protected override void DrawHelp()
        {
            
        }
    }

    #endregion

    #region Personnel UI

    /// <summary>
    /// Top block in Personnel UI panel breaking down profile and come vital statistics.
    /// </summary>
    public class UISectionPersonnelProfile : UIBox
    {
        public PersonnelFile focusCrew;
        
        public UISectionPersonnelProfile(HeadlinesGUIManager root, bool isFullwidth = false) : base(root, isFullwidth)
        {
            hasCompact = true;
            hasExtended = true;
            hasHelp = false;

            _state = UIBoxState.EXTENDED;
        }

        protected override string HeadString()
        {
            focusCrew = _root.GetFocusCrew();
            string personality = "";
            if (focusCrew.personality != "")
            {
                personality = $", {focusCrew.personality}";
            }

            double effectiveness = focusCrew.Effectiveness(deterministic: true);
            if (focusCrew.UniqueName() == PrgMgr.ManagerName())
            {
                effectiveness -= storyEngine.GetProgramComplexity();
                return $"{peopleManager.QualitativeEffectiveness(effectiveness)} Program Manager ({focusCrew.Specialty()}{personality})";
            }
            
            return $"{peopleManager.QualitativeEffectiveness(effectiveness)} {focusCrew.Specialty().ToLower()}{personality}";
            
        }

        protected override void DrawCompact()
        {
            GUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Net: {focusCrew.Effectiveness(deterministic:true)}", GUILayout.Width(125));
            GUILayout.Label($"Nation: {focusCrew.GetCulture()}", GUILayout.Width(125));
            if (focusCrew.Specialty() == "Scientist" && focusCrew.passion != PartCategories.none)
            {
                GUILayout.Label($"Passion: {focusCrew.passion.ToString()}", GUILayout.Width(125));
            }
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        protected override void DrawExtended()
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Charisma: {focusCrew.EffectivenessLikability(true)}", GUILayout.Width(125));
            GUILayout.Label($"Training: {focusCrew.trainingLevel}", GUILayout.Width(125));
            GUILayout.Label($"Fame: {focusCrew.EffectivenessFame()}", GUILayout.Width(125));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Personality: {focusCrew.EffectivenessPersonality()}", GUILayout.Width(125));
            GUILayout.Label($"Peers: {focusCrew.EffectivenessHumanFactors()}", GUILayout.Width(125));
            GUILayout.Label($"Mood: {focusCrew.EffectivenessMood()}", GUILayout.Width(125));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Net: {focusCrew.Effectiveness(deterministic:true)}", GUILayout.Width(125));
            GUILayout.Label($"Nation: {focusCrew.GetCulture()}", GUILayout.Width(125));
            if (focusCrew.Specialty() == "Scientist" && focusCrew.passion != PartCategories.none)
            {
                GUILayout.Label($"Passion: {focusCrew.passion.ToString()}", GUILayout.Width(125));
            }
            GUILayout.EndHorizontal();
            _root.GUIPad();
            GUILayout.EndVertical();
        }
        
        protected override void DrawHelp()
        {
            
        }
    }
    
    public class UISectionPersonnelImpact : UIBox
    {
        public PersonnelFile focusCrew; 
        
        public UISectionPersonnelImpact(HeadlinesGUIManager root, bool isFullwidth = false) : base(root, isFullwidth)
        {
            hasCompact = true;
            hasExtended = true;
            hasHelp = false;

            _state = UIBoxState.COMPACT;
        }

        protected override string HeadString()
        {
            focusCrew = _root.GetFocusCrew();
            if (focusCrew.Specialty() != "Pilot")
            {
                string location = focusCrew.Specialty() == "Engineed" ? "VAB": "R&C complex";
                return $"Impact on {location} ({focusCrew.influence + focusCrew.teamInfluence + focusCrew.legacy} pts)";
            }
            return "Impact on space program";
        }

        protected override void DrawCompact()
        {
            string toprint = "";
            if (focusCrew.influence != 0) toprint += $"Imp: {focusCrew.influence}, ";
            if (focusCrew.teamInfluence != 0) toprint += $"Team: {focusCrew.teamInfluence}, ";
            if (focusCrew.legacy != 0) toprint += $"Imp: {focusCrew.legacy}, ";
            if (focusCrew.lifetimeHype != 0) toprint += $"Hype: {focusCrew.lifetimeHype}, ";
            if (toprint.Length != 0)
            {
                toprint = toprint.Substring(0, toprint.Length - 2);
            }
            else
            {
                toprint = $"{focusCrew.DisplayName()} mainly eats snack in the lobby.";
            }
            GUILayout.Label(toprint, FullWidth());
            _root.GUIPad();
        }

        protected override void DrawExtended()
        {
            GUILayout.BeginVertical();
            if (focusCrew.Specialty() != "Pilot")
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Immediate: {focusCrew.influence}", GUILayout.Width(sectionWidth/3));
                GUILayout.Label($"Lasting: {focusCrew.teamInfluence}", GUILayout.Width(sectionWidth/3));
                GUILayout.Label($"Legacy: {focusCrew.legacy}", GUILayout.Width(sectionWidth/3));
                GUILayout.EndHorizontal();
            }
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Hype: {focusCrew.lifetimeHype}", GUILayout.Width(sectionWidth/3));
            GUILayout.Label($"Scout: {focusCrew.numberScout}", GUILayout.Width(sectionWidth/3));
            GUILayout.Label($"Funds: {focusCrew.fundRaised/1000}K", GUILayout.Width(sectionWidth/3));
            GUILayout.EndHorizontal();
            _root.GUIPad();
            GUILayout.EndVertical();
        }
        
        protected override void DrawHelp()
        {
            
        }
    }

    #endregion

    #region Recruit UI


    #endregion

    #region Story UI

    

    #endregion
    
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
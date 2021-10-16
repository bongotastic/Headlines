using System;
using System.Collections.Generic;
using Contracts;
using KSP.UI;
using RP0;
using Headlines.source.Emissions;
using UnityEngine;

namespace Headlines.source
{
    public enum MediaRelationMode
    {
        LOWPROFILE, CAMPAIGN, LIVE
    }
    
    public class ReputationManager
    {
        public static string[] renownLevels = new[] { "startup", "underdog", "renowned", "leader", "excellent"};
        
        public MediaRelationMode currentMode = MediaRelationMode.LOWPROFILE;
        
        private double programHype = 10;
        private double highestReputation = 0;

        private double headlinesScore = 0;
        private double lastScoreTimeStamp = 0;
        private double lastKnownCredibility = 0;

        // Media event
        public double airTimeStarts = 0;
        public double airTimeEnds = 0;
        private double mediaOpsTarget = 0;
        private double mediaInitialHype = 0;
        public List<Contract> mediaContracts = new List<Contract>();
        private List<string> _contractNames = new List<string>();
        private string airTimeOpenAlarm = "";
        private string airTimeCloseAlarm = "";
        private string airTimeEndAlarm = "";
        
        private bool announcedSuccess = false;

        public List<NewsStory> shelvedAchievements = new List<NewsStory>();
        private int credibilityGainAllowed = 0;

        private double _daylight = 0;
        private double _lastDaylight = 0;

        #region Serialization

        public void FromConfigNode(ConfigNode node)
        {
            if (node.HasValue("currentMode"))
            {
                currentMode = (MediaRelationMode)int.Parse(node.GetValue("currentMode"));
            }

            HeadlinesUtil.SafeDouble("programHype", ref programHype, node);
            HeadlinesUtil.SafeDouble("highestReputation", ref highestReputation, node);
            
            HeadlinesUtil.SafeDouble("headlinesScore", ref headlinesScore, node);
            HeadlinesUtil.SafeDouble( "lastScoreTimeStamp", ref lastScoreTimeStamp, node);
            HeadlinesUtil.SafeDouble("lastKnownCredibility", ref lastKnownCredibility, node);
            
            HeadlinesUtil.SafeDouble("airTimeStarts", ref airTimeStarts, node);
            HeadlinesUtil.SafeDouble("airTimeEnds", ref airTimeEnds, node);
            HeadlinesUtil.SafeDouble("mediaOpsTarget", ref mediaOpsTarget, node);
            HeadlinesUtil.SafeDouble("mediaInitialHype", ref mediaInitialHype, node);
            
            HeadlinesUtil.SafeDouble("_daylight", ref _daylight, node);

            if (node.HasValue("airTimeOpenAlarm"))
            {
                airTimeOpenAlarm = node.GetValue("airTimeOpenAlarm");
                airTimeCloseAlarm = node.GetValue("airTimeCloseAlarm");
                airTimeEndAlarm = node.GetValue("airTimeEndAlarm");
            }
            
            ConfigNode nodePledged = node.GetNode("PLEDGED");
            mediaContracts.Clear();
            foreach (string title in nodePledged.GetValues("item"))
            {
                Contract cnt = GetContractFromTitle(title);
                if (cnt != null)
                {
                    mediaContracts.Add(cnt);
                }
                else
                {
                    // Long sigh of horror... KSP hasn't loaded the contracts yet.
                    _contractNames.Add(title);
                    HeadlinesUtil.Report(1,$"Unable to locate contract {title}");
                }
                
            }

            ConfigNode nodeShelved = node.GetNode("SHELVEDACHIEVEMENTS");
            foreach (ConfigNode ShA in nodeShelved.GetNodes("newsstory"))
            {
                shelvedAchievements.Add(new NewsStory(ShA));
            }
        }

        private Contract GetContractFromTitle(string title)
        {
            foreach (Contract contract in ContractSystem.Instance.GetCurrentContracts<Contract>())
            {
                HeadlinesUtil.Report(1, $"Trying {contract.Title} == {title}");
                if (contract.Title == title) return contract;
            }
            
            return null;
        }

        /// <summary>
        /// Not necessary anymore. Delete?
        /// </summary>
        /// <param name="node"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        private double SafeRead(ConfigNode node, string name)
        {
            string value = node.GetValue(name);
            if (value == "")
            {
                return 0;
            }
            return double.Parse(value);
        }

        public ConfigNode AsConfigNode()
        {
            ConfigNode output = new ConfigNode("REPUTATIONMANAGER");
            
            output.AddValue("currentMode", (int)currentMode);
            
            output.AddValue("programHype", programHype);
            output.AddValue("highestReputation", highestReputation);
            
            output.AddValue("headlinesScore", headlinesScore);
            output.AddValue("lastScoreTimeStamp", lastScoreTimeStamp);
            output.AddValue("lastKnownCredibility", lastKnownCredibility);
            
            output.AddValue("airTimeStarts", airTimeStarts);
            output.AddValue("airTimeEnds", airTimeEnds);
            output.AddValue("mediaOpsTarget", mediaOpsTarget);
            output.AddValue("mediaInitialHype", mediaInitialHype);
            
            output.AddValue("airTimeOpenAlarm", airTimeOpenAlarm);
            output.AddValue("airTimeCloseAlarm", airTimeCloseAlarm);
            output.AddValue("airTimeEndAlarm", airTimeEndAlarm);
            
            output.AddValue("_daylight", _daylight);


            ConfigNode nodePledged = new ConfigNode("PLEDGED");
            foreach (Contract contract in mediaContracts)
            {
                nodePledged.AddValue("item", contract.Title);
            }

            output.AddNode(nodePledged);
            
            
            ConfigNode ShA = new ConfigNode("SHELVEDACHIEVEMENTS");
            foreach (NewsStory ns in shelvedAchievements)
            {
                ShA.AddNode("newsstory", ns.AsConfigNode());
            }

            output.AddNode(ShA);

            return output;
        }

        #endregion
        
        #region Getters

        /// <summary>
        /// Get Headline Reputation as the sum of credibility and hype
        /// </summary>
        /// <returns></returns>
        public double CurrentReputation()
        {
            return Reputation.CurrentRep + Hype();
        }

        /// <summary>
        /// Interface to KSP for reputation.
        /// </summary>
        /// <returns></returns>
        public double Credibility()
        {
            return Reputation.CurrentRep;
        }

        public double Hype()
        {
            return programHype * DaylightAtKSC();
        }

        /// <summary>
        /// Fraction of current Reputation vs highest achieved.
        /// </summary>
        /// <returns></returns>
        public double Peak()
        {
            if (highestReputation != 0)
            {
                return CurrentReputation() / highestReputation;
            }

            return 1;
        }

        /// <summary>
        /// Fraction of the Reputation that is hype
        /// </summary>
        /// <returns></returns>
        public double OverRating()
        {
            if (CurrentReputation() > 0)
            {
                return Hype() / CurrentReputation();
            }

            return 1;
        }

        /// <summary>
        /// Tally of hype accumulated during a campaign
        /// </summary>
        /// <returns></returns>
        public double CampaignHype()
        {
            return Hype() - mediaInitialHype;
        }

        #endregion

        #region Setters

        /// <summary>
        /// Interface to add/remove KSP reputation. 
        /// </summary>
        /// <param name="scalar"></param>
        /// <param name="reason"></param>
        public void AdjustCredibility(double scalar = 0, TransactionReasons reason = TransactionReasons.None)
        {
            if (scalar != 0)
            {
                Reputation.Instance.AddReputation((float) scalar, reason);
                // Avoid moving the goalpost with during a campaign
                if (currentMode == MediaRelationMode.CAMPAIGN)
                {
                    mediaOpsTarget += scalar;
                }
            }

            lastKnownCredibility = Reputation.CurrentRep;
            UpdatePeakReputation();
        }

        /// <summary>
        /// Cancel the last KSP reputation update
        /// </summary>
        public void IgnoreLastCredibilityChange()
        {
            Reputation.Instance.SetReputation((float)lastKnownCredibility, TransactionReasons.None);
        }

        /// <summary>
        /// Public internface to changing Hype
        /// </summary>
        /// <param name="scalar"></param>
        /// <param name="factor"></param>
        /// <returns>the effective delta in hype</returns>
        public double AdjustHype(double scalar = 0, double factor = 1)
        {
            double initHype = programHype;
            // Hype increases are doubled during a campaign
            if (currentMode == MediaRelationMode.CAMPAIGN)
            {
                if (scalar > 0) scalar *= 2;
                if (factor > 1) factor = 1 + (1-factor)*2;
            }
            
            programHype += scalar;
            programHype *= factor;
            programHype = Math.Max(0, programHype);
            
            UpdatePeakReputation();

            return programHype - initHype;
        }

        /// <summary>
        /// Set Hype instead of modifying it.
        /// </summary>
        /// <param name="newHype"></param>
        public void ResetHype(double newHype = 0)
        {
            programHype = newHype;
        }
        
        #endregion

        #region Internal Logic

        /// <summary>
        /// Process reputation earnings with respect to hype (real and media). This is called by the Event handler.
        /// </summary>
        /// <param name="newCredibility">New credibility to be earned</param>
        /// <param name="reason">KSP transaction reason</param>
        public void HighjackCredibility(double newCredibility, TransactionReasons reason)
        {
            KSPLog.print($"Highjacking rep new:{newCredibility}, old:{lastKnownCredibility}");
            
            // LOW Profile mode cause a cancellation of credibility unless allowed through
            if (currentMode == MediaRelationMode.LOWPROFILE)
            {
                HeadlinesUtil.Report(1, $"LOW PROFILE with {credibilityGainAllowed} bypasses.");
                if (credibilityGainAllowed == 0)
                {
                    IgnoreLastCredibilityChange();
                    return;
                }
                else
                {
                    HeadlinesUtil.Report(1, $"And letting it through.");
                    credibilityGainAllowed = Math.Max(0, credibilityGainAllowed - 1);
                }
                
            }
            
            // During a campaign, legit credibility is converted to hype. 
            if (currentMode == MediaRelationMode.CAMPAIGN)
            {
                double timetoLIVE = airTimeStarts - HeadlinesUtil.GetUT();
                HeadlinesUtil.Report(2,$"NEWSFEED: Expect greater things in {KSPUtil.PrintDateDelta(timetoLIVE,false,false)} days. Hype +{newCredibility - lastKnownCredibility}");
                AdjustHype(newCredibility - lastKnownCredibility);
                IgnoreLastCredibilityChange();
                return;
            }
            
            // Useful values
            double deltaReputation = newCredibility - lastKnownCredibility;
            KSPLog.print($"Delta: {deltaReputation}, hype:{Hype()}");
            
            if (deltaReputation <= Hype())
            {
                // Anything less than Hype() doesn't take away from Hype() when LIVE
                if (currentMode != MediaRelationMode.LIVE)
                {
                    AdjustHype(-1*deltaReputation);
                }
            }
            else
            {
                double outstanding = deltaReputation - Hype();
                KSPLog.print($"Excess rep:{outstanding}");
                AdjustCredibility(-1 * outstanding, reason:TransactionReasons.None);
                ResetHype(outstanding);
                if (currentMode == MediaRelationMode.LIVE & Credibility() >= mediaOpsTarget & !announcedSuccess)
                {
                    announcedSuccess = true;
                    HeadlinesUtil.Report(2, $"BREAKING NEWS: Media event is a success!");
                }
            }
        }

        /// <summary>
        /// Decrease hype according to how it overvaluates reputation.
        /// </summary>
        /// <returns>The loss in hype</returns>
        public double RealityCheck()
        {
            double hypeLoss = programHype * OverRating();
            
            if (Credibility() == 0)
            {
                hypeLoss = Hype() * 0.1;
                AdjustHype(factor:0.9);
            }
            else
            {
                AdjustHype(-1*hypeLoss);
            }
            return hypeLoss;
        }

        /// <summary>
        /// Returns the adjustment in reputation. Does NOT apply it as storyteller must
        /// apply out of class logic to this process.
        /// </summary>
        /// <param name="programCredibility">Program credibiilty</param>
        /// <returns></returns>
        public double GetReputationDecay(double programCredibility)
        {
            double margin = programCredibility - Credibility();
            if (margin >= 0)
            {
                return 0.5 * margin;
            }
            
            return (1 - 0.933) * margin;
        }

        /// <summary>
        /// Used for calculation of Peak.
        /// </summary>
        public void UpdatePeakReputation()
        {
            highestReputation = Math.Max(highestReputation, CurrentReputation());
        }

        /// <summary>
        /// Convert reputation into a level
        /// </summary>
        /// <returns></returns>
        public int GetReputationLevel()
        {
            double valuation = CurrentReputation();

            if (valuation <= 50) return 0;
            if (valuation <= 150) return 1;
            if (valuation <= 350) return 2;
            if (valuation <= 600) return 3;
            return 4;
        }

        /// <summary>
        /// Narrative reputation level
        /// </summary>
        /// <returns></returns>
        public string QualitativeReputation()
        {
            return renownLevels[GetReputationLevel()];
        }
        
        /// <summary>
        /// Compute daylight hype modifier at the KSC.
        /// </summary>
        /// <returns></returns>
        public double DaylightAtKSC()
        {
            // Do not bog down high warp with this
            if (TimeWarp.CurrentRateIndex > 5) return _daylight;
            
            // Avoid the very expensive computation most of the time
            if (HeadlinesUtil.GetUT() - _lastDaylight < 360 && _daylight != 0)
            {
                return _daylight;
            }

            if (HighLogic.LoadedSceneIsFlight) return _daylight;
            
            Vector3d kscVectorPosition =
                ((FlagPoleFacility)SpaceCenter.FindObjectOfType(typeof(FlagPoleFacility)))
                .transform
                .position;

            Vector3d earthVectorPosition = Planetarium.fetch.Home.transform.position;
            Vector3d sunVectorPosition = Planetarium.fetch.Sun.transform.position;
            
            
            Vector3d earthKSC = kscVectorPosition - earthVectorPosition;
            Vector3d earthSun = sunVectorPosition - earthVectorPosition;

            double angle = Vector3d.Angle(earthSun, earthKSC);
            double output = 1;
            if (angle <= 70)
            {
                output = 1.2;
            }
            else if (angle <= 80)
            {
                output = 1;
            }
            else if (angle <= 90)
            {
                output = 0.9;
            }
            else
            {
                output = 0.8;
            }

            _daylight = output;
            _lastDaylight = HeadlinesUtil.GetUT();
            return output;
        }


        #region Media Ops

        /// <summary>
        /// begin a media blitz ahead of a live event
        /// </summary>
        /// <param name="lenghtCampaign">lenght of campaign</param>
        public void LaunchCampaign(double lenghtCampaign)
        {
            currentMode = MediaRelationMode.CAMPAIGN;
            mediaOpsTarget = Credibility() + GetMediaEventWager();
            mediaInitialHype = Hype();
            airTimeStarts = HeadlinesUtil.GetUT() + lenghtCampaign * 0.9;
            airTimeEnds = HeadlinesUtil.GetUT() + lenghtCampaign * 1.1;
            KSPLog.print($"[MEDIA] Campaign mode engaged.");
            KSPLog.print($"[MEDIA] Targeting credibility of {mediaOpsTarget}.");
            KSPLog.print($"[MEDIA] Going live at {KSPUtil.PrintDate(airTimeStarts, true, false)}.");
            KSPLog.print($"[MEDIA] Going dark at {KSPUtil.PrintDate(airTimeEnds, true, false)}.");

            if (KACWrapper.APIReady)
            {
                airTimeOpenAlarm = KACWrapper.KAC.CreateAlarm(KACWrapper.KACAPI.AlarmTypeEnum.Raw, "Earliest live launch", airTimeStarts);
                airTimeCloseAlarm = KACWrapper.KAC.CreateAlarm(KACWrapper.KACAPI.AlarmTypeEnum.Raw, "Latest live launch", airTimeEnds);
            }
        }
        
        /// <summary>
        /// End the campaign and start the live event.
        /// </summary>
        public void GoLIVE()
        {
            currentMode = MediaRelationMode.LIVE;
            mediaOpsTarget = Credibility() + GetMediaEventWager();
            HeadlinesUtil.ScreenMessage("Going LIVE now!");
            announcedSuccess = false;
            // Set end time to 48h later
            airTimeEnds = HeadlinesUtil.GetUT() + (48*3600);
            if (KACWrapper.APIReady)
            {
                airTimeEndAlarm = KACWrapper.KAC.CreateAlarm(KACWrapper.KACAPI.AlarmTypeEnum.Raw, "Live event ends", airTimeEnds);
                KACWrapper.KAC.DeleteAlarm(airTimeOpenAlarm);
                KACWrapper.KAC.DeleteAlarm(airTimeCloseAlarm);
            }
        }

        /// <summary>
        /// Close the live event, deal with the reputation wager, and return to low profile.
        /// </summary>
        /// <returns></returns>
        public double EndLIVE()
        {
            currentMode = MediaRelationMode.LOWPROFILE;
            
            if (KACWrapper.APIReady)
            {
                KACWrapper.KAC.DeleteAlarm(airTimeEndAlarm);
            }
            
            if (!EventSuccess())
            {
                double credibilityLoss = Credibility() - mediaOpsTarget;
                AdjustCredibility(credibilityLoss);
                return credibilityLoss;
            }
            
            mediaContracts.Clear();
            
            AdjustHype(5);
            announcedSuccess = false;

            airTimeEnds = HeadlinesUtil.GetUT() - 1;

            return 0;
        }

        /// <summary>
        /// Force the transition for the next frame.
        /// </summary>
        public void CallMediaDebrief()
        {
            airTimeEnds = HeadlinesUtil.GetUT() - 1;
        }

        public bool EventSuccess()
        {
            if (mediaContracts.Count == 0)
            {
                return true;
            }
            return Math.Round(Credibility(),0,MidpointRounding.AwayFromZero)  >= Math.Round(mediaOpsTarget,0,MidpointRounding.AwayFromZero);
        }

        public double WageredCredibilityToGo()
        {
            return mediaOpsTarget - Credibility();
        }

        /// <summary>
        /// Enough of a penalty to make it dangerous to troll the press simply for the campaign hype boost.
        /// </summary>
        public void CancelMediaEvent()
        {
            double penalty = (Hype() - mediaInitialHype)/2;
            AdjustHype(-1*penalty);
            penalty /= 2;
            AdjustCredibility(-1 * penalty);
            currentMode = MediaRelationMode.LOWPROFILE;

            if (KACWrapper.APIReady)
            {
                KACWrapper.KAC.DeleteAlarm(airTimeCloseAlarm);
            }
        }

        /// <summary>
        /// Meant to be 1K for the last 10 days, another 1K for the last 90 days.
        /// </summary>
        /// <param name="nDays"></param>
        /// <returns></returns>
        public double MediaCampaignCost(int nDays)
        {
            double output = 100;
            output += Math.Min(nDays, 10) * 90;
            if (nDays > 10)
            {
                nDays -= 10;
                output += Math.Min(90, nDays) * 9;
            }

            return output;
        }

        public void FilePressRelease(NewsStory ns)
        {
            shelvedAchievements.Add(ns);
        }
        
        public void IssuePressReleaseFor(NewsStory ns)
        {
            credibilityGainAllowed += 1;
            shelvedAchievements.Remove(ns);
            AdjustCredibility(ns.reputationValue, TransactionReasons.Contracts);
        }

        
        public void AttachContractToMediaEvent(Contract contract)
        {
            mediaContracts.Add(contract);
        }

        public void WithdrawContractFromMediaEvent(Contract contract)
        {
            mediaContracts.Remove(contract);
        }

        public float GetMediaEventWager()
        {
            float wager = 0;
            foreach (Contract contract in mediaContracts)
            {
                wager += contract.ReputationCompletion;
            }

            return Math.Max(wager, 1f);
        }
        
        #endregion

        #endregion

        #region backward compatibility

        public void SetScore(double score)
        {
            headlinesScore = score;
        }

        public void SetlastScoreTimeStamp(double x)
        {
            lastScoreTimeStamp = x;
        }

        public void SetLastKnownCredibility(double cred)
        {
            lastKnownCredibility = cred;
        }

        public void SetHighestReputation(double value)
        {
            highestReputation = value;
        }

        #endregion

        public void ReattemptLoadContracts()
        {
            bool success = false;
            foreach (string contractName in _contractNames)
            {
                Contract cnt = GetContractFromTitle(contractName);
                if (cnt != null)
                {
                    mediaContracts.Add(cnt);
                    success = true;
                }
            }
            if (success) _contractNames.Clear();
        }
    }
}

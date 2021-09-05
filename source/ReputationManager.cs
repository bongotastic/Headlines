﻿using System;
using KSP.UI;
using UnityEngine;

namespace RPStoryteller.source
{
    public enum MediaRelationMode
    {
        LOWPROFILE, CAMPAIGN, LIVE
    }
    
    public class ReputationManager
    {
        public static string[] renownLevels = new[] { "underdog", "renowned", "leader", "excellent", "legendary"};
        
        public MediaRelationMode currentMode = MediaRelationMode.LOWPROFILE;
        
        private double programHype = 10;
        private double highestReputation = 0;

        private double headlinesScore = 0;
        private double lastScoreTimeStamp = 0;
        private double lastKnownCredibility = 0;

        public double airTimeStarts = 0;
        public double airTimeEnds = 0;
        private double mediaOpsTarget = 0;
        private double mediaInitialHype = 0;

        #region Serialization

        public void FromConfigNode(ConfigNode node)
        {
            currentMode = (MediaRelationMode)int.Parse(node.GetValue("currentMode"));
            
            programHype = Double.Parse(node.GetValue("programeHype"));
            highestReputation = Double.Parse(node.GetValue("highestReputation"));
            
            headlinesScore = Double.Parse(node.GetValue("headlinesScore"));
            lastScoreTimeStamp = Double.Parse(node.GetValue("lastScoreTimeStamp"));
            lastKnownCredibility = Double.Parse(node.GetValue("lastKnownCredibility"));
            
            airTimeStarts = Double.Parse(node.GetValue("airTimeStarts"));
            airTimeEnds = Double.Parse(node.GetValue("airTimeEnds"));
            mediaOpsTarget = Double.Parse(node.GetValue("mediaOpsTarget"));
            mediaInitialHype = Double.Parse(node.GetValue("mediaInitialHype"));
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

            return output;
        }

        #endregion
        
        #region Getters

        public double CurrentReputation()
        {
            return Reputation.CurrentRep + programHype;
        }

        public double Credibility()
        {
            return Reputation.CurrentRep;
        }

        public double Hype()
        {
            return programHype;
        }

        public double Peak()
        {
            if (highestReputation != 0)
            {
                return CurrentReputation() / highestReputation;
            }

            return 1;
        }

        public double OverRating()
        {
            if (CurrentReputation() > 0)
            {
                return programHype / CurrentReputation();
            }

            return 1;
        }

        public double CampaignHype()
        {
            return Hype() - mediaInitialHype;
        }

        #endregion

        #region Setters

        public void AdjustCredibility(double scalar = 0, double factor = 1, TransactionReasons reason = TransactionReasons.None)
        {
            UpdateHeadlinesScore();
            if (factor != 1)
            {
                double current = Reputation.CurrentRep;
                current *= (factor - 1);
                Reputation.Instance.AddReputation((float)current, reason);
            }
            
            if (scalar != 0)
            {
                Reputation.Instance.AddReputation((float) scalar, reason);
            }

            lastKnownCredibility = Reputation.CurrentRep;
            UpdatePeakReputation();
        }

        public void IgnoreLastCredibilityChange()
        {
            AdjustCredibility( Credibility() - lastKnownCredibility);
        }

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

        public void ResetHype(double newHype = 0)
        {
            programHype = newHype;
        }
        
        #endregion

        #region Internal Logic

        /// <summary>
        /// Process reputation earnings with respect to hype (real and media)
        /// </summary>
        /// <param name="newCredibility">New credibility to be earned</param>
        /// <param name="reason">KSP transaction reason</param>
        public void HighjackCredibility(double newCredibility, TransactionReasons reason)
        {
            KSPLog.print($"Highjacking rep new:{newCredibility}, old:{lastKnownCredibility}");
            // During a campaign, legit credibility is converted to hype. 
            if (currentMode == MediaRelationMode.CAMPAIGN)
            {
                KSPLog.print($"[HEADLINES] Credibility converted to hype during campaign: {newCredibility - lastKnownCredibility}");
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
            }
        }

        public double InferredCredibilityEarnings()
        {
            return Credibility() - lastKnownCredibility;
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
                AdjustHype(factor:1 - OverRating());
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

        public void UpdatePeakReputation()
        {
            highestReputation = Math.Max(highestReputation, CurrentReputation());
        }

        public int GetReputationLevel()
        {
            double valuation = CurrentReputation();

            if (valuation <= 50) return 0;
            if (valuation <= 150) return 1;
            if (valuation <= 350) return 2;
            if (valuation <= 600) return 3;
            return 4;
        }

        public string QualitativeReputation()
        {
            return renownLevels[GetReputationLevel()];
        }

        #region Score

        public double GetScore()
        {
            return headlinesScore;
        }

        private void UpdateHeadlinesScore()
        {
            double timestamp = HeadlinesUtil.GetUT();
            headlinesScore += ((timestamp - lastScoreTimeStamp) / (3600 * 24 * 365)) * lastKnownCredibility;
            lastScoreTimeStamp = timestamp;
        }

        #endregion

        #region Media Ops

        public void LaunchCampaign(double goLiveTime)
        {
            currentMode = MediaRelationMode.CAMPAIGN;
            mediaOpsTarget = CurrentReputation();
            mediaInitialHype = Hype();
            airTimeStarts = goLiveTime;
            airTimeEnds = goLiveTime + (3600*24*2);
            KSPLog.print($"[MEDIA] Campaign mode engaged.");
            KSPLog.print($"[MEDIA] Targeting credibility of {mediaOpsTarget}.");
            KSPLog.print($"[MEDIA] Going live at {KSPUtil.PrintDate(airTimeStarts, true, false)}.");
            KSPLog.print($"[MEDIA] Going dark at {KSPUtil.PrintDate(airTimeEnds, true, false)}.");
        }
        
        public void GoLIVE()
        {
            currentMode = MediaRelationMode.LIVE;
            HeadlinesUtil.ScreenMessage("Going LIVE now!");
        }

        public double EndLIVE()
        {
            currentMode = MediaRelationMode.LOWPROFILE;
            if (!EventSuccess())
            {
                double credibilityLoss = Credibility() - mediaOpsTarget;
                AdjustCredibility(credibilityLoss);
                return credibilityLoss;
            }
            AdjustHype(10);

            airTimeEnds = HeadlinesUtil.GetUT() - 1;
            
            return 0;
        }

        public bool EventSuccess()
        {
            return Credibility() >= mediaOpsTarget;
        }

        public double MinimumHypeForInvite()
        {
            return Math.Max(1, Credibility() * 0.05);
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
    }
}
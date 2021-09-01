using System;
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
        public MediaRelationMode currentMode = MediaRelationMode.LOWPROFILE;
        
        private double programHype = 0;
        private double highestReputation = 0;

        private double headlinesScore = 0;
        private double lastScoreTimeStamp = 0;
        private double lastKnownCredibility = 0;

        public double airTimeStarts = 0;
        public double airTimeEnds = 0;
        private double mediaOpsTarget = 0;

        #region Serialization

        public void FromConfigNode(ConfigNode node)
        {
            currentMode = (MediaRelationMode)int.Parse(node.GetValue("currentMode"));
            programHype = Double.Parse(node.GetValue("programeHype"));
            highestReputation = Double.Parse(node.GetValue("highestReputation"));
            headlinesScore = Double.Parse(node.GetValue("headlinesScore"));
            lastScoreTimeStamp = Double.Parse(node.GetValue("lastScoreTimeStamp"));
            lastKnownCredibility = Double.Parse(node.GetValue("lastKnownCredibility"));
        }

        public ConfigNode AsConfigNode()
        {
            ConfigNode output = new ConfigNode();
            
            output.AddValue("currentMode", (int)currentMode);
            
            output.AddValue("programHype", programHype);
            output.AddValue("highestReputation", highestReputation);
            
            output.AddValue("headlinesScore", headlinesScore);
            output.AddValue("lastScoreTimeStamp", lastScoreTimeStamp);
            output.AddValue("lastKnownCredibility", lastKnownCredibility);
            

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
            return programHype / CurrentReputation();
        }

        #endregion

        #region Setters

        public void AdjustCredibility(double scalar = 0, double factor = 1, TransactionReasons reason = TransactionReasons.None)
        {
            UpdateHeadlinesScore();
            if (scalar != 0)
            {
                Reputation.Instance.AddReputation((float)scalar, reason);
            }

            if (factor != 0)
            {
                double current = Reputation.CurrentRep;
                current *= factor;
                Reputation.Instance.SetReputation((float)current, reason);
            }

            lastKnownCredibility = Reputation.CurrentRep;
            UpdatePeakReputation();
        }

        public void AdjustHype(double scalar = 0, double factor = 1)
        {
            // Hype increases are doubled during a campaign
            if (currentMode == MediaRelationMode.CAMPAIGN)
            {
                if (scalar > 0) scalar *= 2;
                if (factor > 1) factor = 1 + (1-factor)*2;
            }
            programHype += scalar;
            programHype *= factor;
            UpdatePeakReputation();
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
        /// <param name="newReputation"></param>
        public void EarnReputation(double newReputation)
        {
            if (newReputation <= Hype())
            {
                // Anything under Hype() doesn't take away from Hype() when LIVE
                if (currentMode != MediaRelationMode.LIVE)
                {
                    AdjustHype(-1*newReputation);
                }
                AdjustCredibility(newReputation);
            }
            else
            {
                double outstanding = newReputation - Hype();
                AdjustCredibility(Hype());
                ResetHype(outstanding);
            }
        }

        /// <summary>
        /// Decrease hype according to how it overvaluates reputation.
        /// </summary>
        /// <returns>The loss in hype</returns>
        public double RealityCheck()
        {
            double hypeLoss = programHype * OverRating();
            AdjustHype(factor:1 - OverRating());
            return hypeLoss;
        }

        /// <summary>
        /// Returns the adjustment in reputation
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

        private void UpdatePeakReputation()
        {
            highestReputation = Math.Max(highestReputation, CurrentReputation());
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
            airTimeStarts = goLiveTime;
            airTimeEnds = goLiveTime + (3600*24*2);
        }
        
        public void GoLIVE()
        {
            currentMode = MediaRelationMode.LIVE;
        }

        public void MediaDebrief()
        {
            currentMode = MediaRelationMode.LOWPROFILE;
        }

        public bool EventSuccess()
        {
            return Credibility() >= mediaOpsTarget;
        }

        #endregion

        #endregion
    }
}
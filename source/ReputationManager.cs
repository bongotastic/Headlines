using System;
using System.Collections.Generic;
using System.Text;
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
        public double campaignTimeStarts = Double.MaxValue;
        private double mediaOpsTarget = 0;
        private double mediaInitialHype = 0;
        private double mediaFreeLiveHype = 0;
        public List<Contract> mediaContracts = new List<Contract>();
        private List<string> _contractNames = new List<string>();
        public bool mediaLaunchTriggersLive = true;
        private string airTimeOpenAlarm = "";
        private string airTimeCloseAlarm = "";
        private string airTimeEndAlarm = "";
        private string airtimeCampaignAlarm = "";
        
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
            HeadlinesUtil.SafeDouble("campaignTimeStarts", ref campaignTimeStarts, node);
            HeadlinesUtil.SafeDouble("mediaOpsTarget", ref mediaOpsTarget, node);
            HeadlinesUtil.SafeBool("mediaLaunchTriggersLive", ref mediaLaunchTriggersLive, node);
            HeadlinesUtil.SafeDouble("mediaInitialHype", ref mediaInitialHype, node);
            HeadlinesUtil.SafeDouble("mediaFreeLiveHype", ref mediaFreeLiveHype, node);
            
            HeadlinesUtil.SafeDouble("_daylight", ref _daylight, node);

            if (node.HasValue("airTimeOpenAlarm"))
            {
                airTimeOpenAlarm = node.GetValue("airTimeOpenAlarm");
                airTimeCloseAlarm = node.GetValue("airTimeCloseAlarm");
                airTimeEndAlarm = node.GetValue("airTimeEndAlarm");
                airtimeCampaignAlarm = node.GetValue("airtimeCampaignAlarm");
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
            
            //backward compatibility to 0.10 beta
            if (CurrentReputation() > 1000) programHype = 950 - Credibility();
        }

        private Contract GetContractFromTitle(string title)
        {
            foreach (Contract contract in ContractSystem.Instance.GetCurrentContracts<Contract>())
            {
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
            output.AddValue("campaignTimeStarts", campaignTimeStarts);
            output.AddValue("mediaOpsTarget", mediaOpsTarget);
            output.AddValue("mediaInitialHype", mediaInitialHype);
            output.AddValue("mediaFreeLiveHype", mediaFreeLiveHype);
            output.AddValue("mediaLaunchTriggersLive", mediaLaunchTriggersLive);
            
            output.AddValue("airTimeOpenAlarm", airTimeOpenAlarm);
            output.AddValue("airTimeCloseAlarm", airTimeCloseAlarm);
            output.AddValue("airTimeEndAlarm", airTimeEndAlarm);
            output.AddValue("airtimeCampaignAlarm", airtimeCampaignAlarm);
            
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
        public double AdjustHype(double scalar = 0, double factor = 1, bool noTransform = false)
        {
            double initHype = programHype;
            // Hype increases are doubled during a campaign
            if (currentMode == MediaRelationMode.CAMPAIGN)
            {
                if (scalar > 0) scalar *= 2;
                if (factor > 1) factor = 1 + (1-factor)*2;
            }
            
            // turn factor to a scalar
            scalar += (factor * programHype) - programHype;

            if (noTransform)
            {
                programHype += scalar;
            }
            else
            {
                programHype += TransformReputation(scalar, CurrentReputation());
            }
            
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
            if (newHype == 0)
            {
                programHype = 0;
            }
            else
            {
                programHype = TransformReputation(newHype);
            }
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
                if (credibilityGainAllowed == 0)
                {
                    IgnoreLastCredibilityChange();
                    return;
                }
                else
                {
                    credibilityGainAllowed = Math.Max(0, credibilityGainAllowed - 1);
                }
                
            }
            
            // During a campaign, legit credibility is converted to hype. 
            if (currentMode == MediaRelationMode.CAMPAIGN && newCredibility - lastKnownCredibility > 0)
            {
                double timetoLIVE = airTimeStarts - HeadlinesUtil.GetUT();
                HeadlinesUtil.Report(2,$"NEWSFEED: Expect greater things in {KSPUtil.PrintDateDelta(timetoLIVE,false,false)} days. Hype +{newCredibility - lastKnownCredibility}");
                AdjustHype(newCredibility - lastKnownCredibility, noTransform:true);
                IgnoreLastCredibilityChange();
                return;
            }
            
            // Useful values
            double deltaReputation = newCredibility - lastKnownCredibility;
            KSPLog.print($"Delta: {deltaReputation}, hype:{Hype()}");
            
            if (deltaReputation <= Hype() && deltaReputation > 0)
            {
                // Anything less than Hype() doesn't take away from Hype() when LIVE
                if (currentMode != MediaRelationMode.LIVE)
                {
                    AdjustHype(-1*deltaReputation, noTransform:true);
                }
                else
                {
                    mediaFreeLiveHype += deltaReputation;
                }
            }
            else
            {
                double outstanding = deltaReputation - Hype();
                KSPLog.print($"Excess rep:{outstanding}");
                //AdjustCredibility(-1 * outstanding, reason:TransactionReasons.None);
                Reputation.Instance.SetReputation(Reputation.CurrentRep - (float)outstanding, TransactionReasons.None);
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
            double margin = 100 + programCredibility - Credibility();
            if (margin >= 0)
            {
                return 0;
            }
            
            return (1 - GetDecayRatio()) * margin;
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
            
            double output = 1;
            if (SpaceCenter.FindObjectOfType(typeof(FlagPoleFacility)) != null)
            {
                Vector3d kscVectorPosition =
                    ((FlagPoleFacility)SpaceCenter.FindObjectOfType(typeof(FlagPoleFacility)))
                    .transform
                    .position;

                Vector3d earthVectorPosition = Planetarium.fetch.Home.transform.position;
                Vector3d sunVectorPosition = Planetarium.fetch.Sun.transform.position;
            
            
                Vector3d earthKSC = kscVectorPosition - earthVectorPosition;
                Vector3d earthSun = sunVectorPosition - earthVectorPosition;

                double angle = Vector3d.Angle(earthSun, earthKSC);
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
            }
            

            _daylight = output;
            _lastDaylight = HeadlinesUtil.GetUT();
            return output;
        }

        /// <summary>
        /// Voodoo method to scale decay to represent how hard it is to maintain (real) reputation over 500.
        /// </summary>
        /// <returns></returns>
        public double GetDecayRatio()
        {
            double ratio = 0.955;
            if (Credibility() > 500)
            {
                ratio -= ((Credibility() - 500) / 500) * 0.08;
            }
            return ratio;
        }


        #region Media Ops

        /// <summary>
        /// begin a media blitz ahead of a live event
        /// </summary>
        /// <param name="lengthCampaign">lenght of campaign</param>
        /// <param name="secondsToEndCampaign"></param>
        public void LaunchCampaign(double lengthCampaign, double secondsToEndCampaign)
        {
            currentMode = MediaRelationMode.CAMPAIGN;
            mediaOpsTarget = Credibility() + GetMediaEventWager();
            mediaInitialHype = Hype();
            airTimeStarts = HeadlinesUtil.GetUT() + secondsToEndCampaign * 0.9;
            airTimeEnds = HeadlinesUtil.GetUT() + secondsToEndCampaign * 1.1;
            campaignTimeStarts = HeadlinesUtil.GetUT() + secondsToEndCampaign - lengthCampaign;

            if (KACWrapper.APIReady)
            {
                airTimeOpenAlarm = KACWrapper.KAC.CreateAlarm(KACWrapper.KACAPI.AlarmTypeEnum.Raw, "Earliest live launch", airTimeStarts);
                airTimeCloseAlarm = KACWrapper.KAC.CreateAlarm(KACWrapper.KACAPI.AlarmTypeEnum.Raw, "Latest live launch", airTimeEnds);
                if (campaignTimeStarts - HeadlinesUtil.GetUT() > HeadlinesUtil.OneDay)
                {
                    airtimeCampaignAlarm = KACWrapper.KAC.CreateAlarm(KACWrapper.KACAPI.AlarmTypeEnum.Raw, "Campaign Kick-off", campaignTimeStarts);
                }
            }
        }
        
        /// <summary>
        /// End the campaign and start the live event.
        /// </summary>
        public void GoLIVE()
        {
            currentMode = MediaRelationMode.LIVE;
            mediaOpsTarget = Credibility() + GetMediaEventWager();
            mediaFreeLiveHype = 0;
            campaignTimeStarts = Double.MaxValue;
            
            HeadlinesUtil.ScreenMessage("Going LIVE now!");
            announcedSuccess = false;
            
            // Set end time to 4 days later
            airTimeEnds = HeadlinesUtil.GetUT() + (4 * HeadlinesUtil.OneDay);
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
            
            double credibilityLoss = Credibility() - mediaOpsTarget;
            
            if (!EventSuccess())
            {
                AdjustCredibility(credibilityLoss);
                
                // partial rep gains degrade hype
                AdjustHype(-1 * mediaFreeLiveHype, noTransform:true);
                
                StoryEngine.Instance.RealityCheck(false, true);
                StoryEngine.Instance.RealityCheck(false);
                return credibilityLoss;
            }
            else
            {
                // Perform the conversion of free hype to hype loss (albeit delayed as hype can be used more than once in some cases)
                AdjustHype(-1 * mediaFreeLiveHype);
                
                // Success is useful anyhow
                AdjustHype(5);
            }
            
            mediaContracts.Clear();
            
            announcedSuccess = false;
            mediaFreeLiveHype = 0;

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
            return Math.Round(Credibility(), MidpointRounding.AwayFromZero)  >= Math.Round(mediaOpsTarget, MidpointRounding.AwayFromZero);
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
            if (penalty > 0)
            {
                AdjustHype(-1*penalty);
                penalty /= 2;
                AdjustCredibility(-1 * penalty);
            }
            
            currentMode = MediaRelationMode.LOWPROFILE;
            campaignTimeStarts = Double.MaxValue;

            if (KACWrapper.APIReady)
            {
                KACWrapper.KAC.DeleteAlarm(airTimeCloseAlarm);
                KACWrapper.KAC.DeleteAlarm(airtimeCampaignAlarm);
            }
        }

        /// <summary>
        /// Meant to be 1K for the last 10 days, another 1K for the last 90 days.
        /// </summary>
        /// <param name="nDays"></param>
        /// <returns></returns>
        public double MediaCampaignCost(int nDays = -1)
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
            StoryEngine.Instance.FileHeadline(ns);
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

            // Non-linear transform
            wager = (float)TransformReputation(wager, Credibility());

            return Math.Max(wager, 1f);
        }

        /// <summary>
        /// Returns the minimum amount of hype to fully reward an event.
        /// </summary>
        /// <returns></returns>
        public float GetMediaEventHype()
        {
            float maxHype = 0;
            foreach (Contract contract in mediaContracts)
            {
                maxHype = Math.Max(maxHype, contract.ReputationCompletion);
            }

            // Non-linear transform
            maxHype = (float)TransformReputation(maxHype, Credibility());

            return Math.Max(maxHype, 0.1f);
            
        }

        /// <summary>
        /// Add 10 days to a live event
        /// </summary>
        public void ExtendLiveEvent()
        {
            airTimeEnds += 3600 * 24 * 10;
        }

        /// <summary>
        /// Add nDay to the latest time an even must go live
        /// </summary>
        public void PostponeLiveEvent(double nDay)
        {
            // Push back
            airTimeEnds += nDay * HeadlinesUtil.OneDay;
            
            // Affect hype by 2% per day
            AdjustHype(-0.02 * nDay * programHype);
            
            if (KACWrapper.APIReady)
            {
                KACWrapper.KAC.DeleteAlarm(airTimeCloseAlarm);
                airTimeCloseAlarm = KACWrapper.KAC.CreateAlarm(KACWrapper.KACAPI.AlarmTypeEnum.Raw, "Latest live launch", airTimeEnds);
                
            }
        }

        /// <summary>
        /// Is the program paying for campaign effect at this time
        /// </summary>
        /// <returns></returns>
        public bool isCampaignActive()
        {
            return currentMode == MediaRelationMode.CAMPAIGN && HeadlinesUtil.GetUT() > campaignTimeStarts;
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

        #region KSP

        /// <summary>
        /// Emulate KSP's nonlinear reputation.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public double TransformReputation(double value, double fictionalAnchor = -1)
        {
            if (fictionalAnchor == -1)
            {
                fictionalAnchor = Credibility();
            }
            
            int iterator = (int) Math.Abs(value);
            double deltaUnit = 1 * Math.Sign(value);
            double runningTally = 0.0;
            double microBump = 0.0;
            for (int index = 0; index <= iterator; ++index)
            {
                if (index != iterator)
                {
                    microBump = IncrementalReputation(deltaUnit, fictionalAnchor);
                }
                else
                {
                    double residual = Math.Abs(value) - (double)iterator;
                    microBump = IncrementalReputation(deltaUnit * residual, fictionalAnchor);
                }

                runningTally += microBump;
                fictionalAnchor += microBump;
            }

            return runningTally;
        }

        /// <summary>
        ///  Accessory to the non-linear transformation.
        /// </summary>
        /// <param name="deltaRep"></param>
        /// <returns></returns>
        public double IncrementalReputation(double deltaRep, double anchor)
        {
            float interpolationPoint = (float)anchor / Reputation.RepRange;
            if (deltaRep > 0.0)
            {
                return deltaRep * GameVariables.Instance.reputationAddition.Evaluate(interpolationPoint);
            }
            else
            {
                return deltaRep * GameVariables.Instance.reputationSubtraction.Evaluate(interpolationPoint);
            }
        }

        #endregion

        /// <summary>
        /// COntracts are not always loaded by the time we de-serialize this class. So, trying again.
        /// </summary>
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

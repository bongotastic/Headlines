using UnityEngine.Serialization;
using Random = System.Random;

namespace RPStoryteller
{
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames,
        GameScenes.SPACECENTER)]
    public class ReputationDecay : ScenarioModule
    {
       
        private float _deltaTime = 3600f * 24f * 36f;  // Set decay to trigger 10 time per Earth years
        private float _reputationFloor = 20f;          // The level at which it doesn't decay anymore as a natural process
        
        private Reputation _reputation;                // Composition for Reputation object
        private readonly Random _rnd = new Random();

        [KSPField(isPersistant=true)] 
        public double nextdecay = -1d;               // Universal time for the next decay trigger
        
        public void Start()
        {
            _reputation = Reputation.Instance;
            
            // Initialization of the decay trigger for a new career or a recent install of the mod
            if (nextdecay <= 0f)
            {
                nextdecay = Planetarium.GetUniversalTime() + _deltaTime;
            }
            KSPLog.print($"[RPStoryteller][RepDecay] Initializing decay at time {nextdecay}.");
        }

        public void Update()
        {
            if (DecayTrigger())
            {
                // Perform a decay increment.
                _reputation.AddReputation(DecayMagnitude(), TransactionReasons.Any);
                KSPLog.print($"[RPStoryteller][RepDecay] New decayed reputation: {_reputation.reputation}.");
            }
        }

        /// <summary>
        /// Determines whether the reputation decay should take place
        /// </summary>
        /// <returns>true when the threshold was met.</returns>
        private bool DecayTrigger()
        {
            if (nextdecay <= Planetarium.GetUniversalTime())
            {
                // Push the next decay up
                nextdecay += _deltaTime;
                
                // Derive a probability such that 
                double pDecay = (_reputation.reputation - _reputationFloor) / 100f;
                
                if (pDecay >= _rnd.NextDouble()) return true;
            }
            return false;
        }
        
        /// <summary>
        /// Determines the magnitude of the reputation decay. Currently a placeholder for more complex behaviour.
        /// </summary>
        /// <returns>(float) a raw modification to Reputation</returns>
        private float DecayMagnitude()
        {
            return -1f;
        }
    }
}
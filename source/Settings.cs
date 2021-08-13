using ConfigNodeStorage = KSPPluginFramework.ConfigNodeStorage;

namespace RPStoryteller.source
{
    internal class Settings : ConfigNodeStorage
    {
        // Non Persistent stuff
        public string version = "";
        
        // Persistent fields
        
        internal Settings(string filePath) : base(filePath)
        {
            version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }
    }
}
using System;
using UnityEngine;
using ConfigNodeStorage = KSPPluginFramework.ConfigNodeStorage;

namespace RPStoryteller.source
{
    public class Settings : ConfigNodeStorage
    {
        // Non Persistent stuff
        public string version = "";
        public Rect windowPos = new Rect(0,0,400,500);
        
        
        // Persistent fields
        [Persistent] private RectStorage windowRect = new RectStorage();
        
        internal Settings(string filePath) : base(filePath)
        {
            version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            windowPos = windowRect.ToRect();
        }

        public override void OnDecodeFromConfigNode()
        {
            windowPos = windowRect.ToRect();
        }

        public override void OnEncodeToConfigNode()
        {
            windowRect = windowRect.FromRect(windowPos);
        }
    }
    
    public class RectStorage:ConfigNodeStorage
    {
        [Persistent] internal Single x,y,width,height;

        internal Rect ToRect() { return new Rect(x, y, width, height); }
        internal RectStorage FromRect(Rect rectToStore)
        {
            this.x = rectToStore.x;
            this.y = rectToStore.y;
            this.width = rectToStore.width;
            this.height = rectToStore.height;
            return this;
        }
    }
}
namespace RPStoryteller.source.Emissions
{
    public class NewsStory
    {
        public string headline = "";
        public string story = "";
        public double timestamp = 0;
        public PersonnelFile actor;

        public NewsStory(Emissions emitNode)
        {
            timestamp = HeadlinesUtil.GetUT();
        }

    }
}
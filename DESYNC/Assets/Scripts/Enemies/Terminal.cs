namespace Desync.Enemies
{
    /// Story terminal: promptLengthByRoom[room] arrows; success raises AccessGranted(terminalLines[storyIndex]).
    public class Terminal : HackStation
    {
        public int storyIndex;

        protected override int Length
        {
            get
            {
                var l = GameConfig.I.promptLengthByRoom;
                return l == null || l.Length == 0 ? 3 : l[UnityEngine.Mathf.Clamp(room, 0, l.Length - 1)];
            }
        }

        protected override void OnHacked()
        {
            var lines = GameConfig.I.terminalLines;
            string line = lines != null && storyIndex >= 0 && storyIndex < lines.Length ? lines[storyIndex] : "ACCESS GRANTED";
            GameEvents.AccessGranted?.Invoke(line);
        }
    }
}

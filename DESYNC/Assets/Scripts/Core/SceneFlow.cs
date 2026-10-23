using UnityEngine.SceneManagement;

namespace Desync
{
    /// Scene names (Build Settings order: Boot, Title, Room_01..Room_06, Ending) and loaders.
    public static class SceneFlow
    {
        public const string Boot = "Boot", Title = "Title", Ending = "Ending";
        public const int RoomCount = 6;
        public static string Room(int index) => $"Room_{index + 1:00}";

        public static void LoadTitle() => SceneManager.LoadScene(Title);
        public static void LoadEnding() => SceneManager.LoadScene(Ending);
        public static void LoadRoom(int index) => SceneManager.LoadScene(index < RoomCount ? Room(index) : Ending);

        /// Room index of the active scene, or -1.
        public static int ActiveRoomIndex()
        {
            var n = SceneManager.GetActiveScene().name;
            return n.StartsWith("Room_") && int.TryParse(n.Substring(5), out var i) ? i - 1 : -1;
        }
    }
}

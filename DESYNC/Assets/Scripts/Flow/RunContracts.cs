using System;
using System.Collections.Generic;
using Desync.Content;
using Desync.Gen;

namespace Desync.Flow
{
    // CONTRACT (M2): run state + run events shared by the run manager (writer) and the run UI (reader / requester).
    // Signatures frozen; the run manager owns the behaviour.

    public enum RunMode { Classic, Quick, Campaign, Daily, Overclock }
    public enum RunPhase { None, Map, Room, Draft, Market, Cooldown, Results, Over }

    public struct MarketItem
    {
        public string kind;        // "exploit" | "removal" | "backup" | "reroll"
        public string exploitId;   // for "exploit"
        public int price;          // Cycles
        public bool sold;
    }

    public static class RunState
    {
        public static RunMode Mode;
        public static RunPhase Phase;
        public static uint Seed;
        public static RunGraph Graph;
        public static string NodeId;                                      // current node ("a1n0"); null before the first choice
        public static int Act;                                            // 1..3
        public static readonly List<Exploit> Held = new List<Exploit>();
        public static readonly List<Mutator> RoomMutators = new List<Mutator>();
        public static readonly List<Exploit> Offer = new List<Exploit>();   // current draft offer
        public static readonly List<MarketItem> Market = new List<MarketItem>();
        public static readonly string[] CooldownOptions = { "reset_melt", "backup", "reroll" };
        public static int Backups, Cycles, Rerolls, Fragments;           // Fragments earned this run (banked on cash-out/end)
        public static float MeltCarry;                                    // melt carried into the next room (50 %)
        public static float RunTime;
        public static int RunScore;
        public static readonly List<char> Grades = new List<char>();     // per cleared room, in order
        public static bool Won;                                           // run completed (final boss) vs died/cashed out
    }

    public static class RunEvents
    {
        public static Action<RunPhase> PhaseChanged;   // run -> UI
        public static Action<RunMode> StartRun;        // UI (title) -> run
        public static Action<string> ChooseNode;       // UI (map) -> run: node id from the current node's next list
        public static Action<int> PickExploit;         // UI (draft) -> run: index into Offer, -1 = skip
        public static Action RerollOffer;              // UI (draft) -> run: spends a reroll token
        public static Action<int> Buy;                 // UI (market) -> run: index into Market
        public static Action<string> RemoveExploit;    // UI (market removal) -> run: exploit id
        public static Action<int> ChooseCooldown;      // UI (cooldown) -> run: index into CooldownOptions
        public static Action Leave;                    // UI (market / cooldown) -> run: continue to the map
        public static Action CashOut;                  // UI (after a boss) -> run: bank fragments and end
        public static Action GoDeeper;                 // UI (after a boss) -> run: continue to the next act
        public static Action RunEnded;                 // run -> UI: show run results (RunState.Won etc.)
    }
}
